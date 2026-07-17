"""Unity-bound Instagram latest-post endpoints.

Mounted under /api/v1 by api.api, so both routes inherit the router-level
App Check gate. Neither endpoint ever calls Instagram — the background jobs in
services.instagram keep Firestore + the on-disk image cache fresh, and these
just read them (instant responses, per the plan's "no Instagram call in the
request path" rule).
"""

from fastapi import APIRouter, HTTPException
from fastapi.responses import FileResponse, JSONResponse

from services.instagram import build_public_post, cached_image_path, get_latest_post


router = APIRouter()

# Post rotates at most every fetch interval (30 min) — 5 min of client/edge
# caching is safe and keeps Cloudflare from hammering the VM.
_CACHE_HEADERS = {"Cache-Control": "public, max-age=300"}


@router.get("/latest-post")
async def latest_post():
    """Latest Instagram post metadata (image served from our own cache)."""
    stored = get_latest_post()
    if not stored:
        raise HTTPException(status_code=404, detail="no Instagram post cached yet")
    return JSONResponse(build_public_post(stored), headers=_CACHE_HEADERS)


@router.get("/latest-post/image")
async def latest_post_image():
    """Cached image bytes for the latest post (what image_url points at)."""
    stored = get_latest_post()
    path = cached_image_path(stored["media_id"]) if stored and stored.get("media_id") else None
    if path is None or not path.is_file():
        raise HTTPException(status_code=404, detail="no cached Instagram image")
    return FileResponse(path, media_type="image/jpeg", headers=_CACHE_HEADERS)
