import asyncio
import logging
from contextlib import asynccontextmanager

from fastapi import FastAPI
from fastapi.staticfiles import StaticFiles

from api.api import admin_router, api_router
from config import (
    DEBUG_MODE,
    INSTAGRAM_CACHE_DIR,
    LOCAL_STORAGE_DIR,
    LOCAL_STORAGE_URL_PREFIX,
    ROUTER_MODE,
)

logger = logging.getLogger("backend.startup")


@asynccontextmanager
async def lifespan(_app: FastAPI):
    if DEBUG_MODE:
        # Seed sample avistamentos so the web UI has data on a fresh emulator.
        # Wrapped so an unreachable emulator surfaces as a warning, not a crash.
        try:
            from scripts.seed_debug_data import seed_avistamentos
            seed_avistamentos()
        except Exception as e:
            logger.warning("Debug seed skipped: %s", e)
        # Same deal for the Instagram widget: a placeholder latest_post so the
        # Unity editor flow renders without real Instagram credentials.
        try:
            from scripts.seed_debug_data import seed_instagram_post
            seed_instagram_post()
        except Exception as e:
            logger.warning("Instagram debug seed skipped: %s", e)

    # Instagram polling + token refresh run as plain asyncio tasks — single
    # process, single uvicorn worker (see CLAUDE.md), so no external scheduler.
    # Only started alongside the api router, which is what serves the data.
    background_tasks: list[asyncio.Task] = []
    if ROUTER_MODE in ("api", "both"):
        from services.instagram import instagram_fetch_loop, instagram_refresh_loop
        background_tasks = [
            asyncio.create_task(instagram_fetch_loop()),
            asyncio.create_task(instagram_refresh_loop()),
        ]

    yield

    for task in background_tasks:
        task.cancel()
    if background_tasks:
        await asyncio.gather(*background_tasks, return_exceptions=True)


app = FastAPI(lifespan=lifespan)

app.mount("/static", StaticFiles(directory="static"), name="static")

# The Instagram image cache is read by /api/v1/latest-post/image via
# FileResponse (not a StaticFiles mount), but the fetch job and the debug seed
# both want the directory to exist up front.
INSTAGRAM_CACHE_DIR.mkdir(parents=True, exist_ok=True)

if DEBUG_MODE:
    # StaticFiles fails at import if the directory doesn't exist yet.
    LOCAL_STORAGE_DIR.mkdir(parents=True, exist_ok=True)
    app.mount(
        LOCAL_STORAGE_URL_PREFIX,
        StaticFiles(directory=str(LOCAL_STORAGE_DIR)),
        name="local_storage",
    )

if ROUTER_MODE in ("api", "both"):
    app.include_router(api_router)
if ROUTER_MODE in ("admin", "both"):
    app.include_router(admin_router)
