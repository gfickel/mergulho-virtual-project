import logging
from contextlib import asynccontextmanager

from fastapi import FastAPI
from fastapi.staticfiles import StaticFiles

from api.api import admin_router, api_router
from config import (
    DEBUG_MODE,
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
    yield


app = FastAPI(lifespan=lifespan)

app.mount("/static", StaticFiles(directory="static"), name="static")

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
