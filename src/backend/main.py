import logging
from contextlib import asynccontextmanager
from typing import Optional

from fastapi import FastAPI, Header, Request
from fastapi.responses import JSONResponse
from fastapi.staticfiles import StaticFiles

from api.api import api_router
from config import DEBUG_MODE, LOCAL_STORAGE_DIR, LOCAL_STORAGE_URL_PREFIX, templates

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

app.include_router(api_router)

@app.get("/")
async def root(
    request: Request,
    format: Optional[str] = None,
    accept: Optional[str] = Header(None),
):
    return_json = (
        format == "json"
        or (accept and "application/json" in accept and "text/html" not in accept)
    )
    if return_json:
        return JSONResponse({"message": "API do Mergulho Virtual", "version": 0.1})
    return templates.TemplateResponse("index.html", {"request": request})
