import datetime
import os
from pathlib import Path

from fastapi.templating import Jinja2Templates

# Initialize templates
# Assuming templates directory is in the root of the backend folder
templates = Jinja2Templates(directory="templates")
templates.env.globals["now_year"] = datetime.datetime.now().year

GCP_BUCKET_NAME = "avistamentos"

# Debug mode: skips serviceAccountKey.json and routes Firestore/GCS to local
# substitutes (Firestore emulator + on-disk folder served by FastAPI).
# Enable with BACKEND_DEBUG=1.
DEBUG_MODE = os.getenv("BACKEND_DEBUG", "").lower() in ("1", "true", "yes", "on")

# Local replacement for the GCS bucket in debug mode. Mirrors the prod bucket
# layout (originals/<registro>.<ext>, imagens/<registro>.jpg) so reads work the
# same way; main.py mounts the directory at LOCAL_STORAGE_URL_PREFIX.
LOCAL_STORAGE_DIR = Path(__file__).parent / "local_storage"
LOCAL_STORAGE_URL_PREFIX = "/local_storage"

# Public host the Unity client reaches this backend at. Used to build absolute
# URLs in API responses (e.g. the cached Instagram image). Env-overridable for
# LAN dev against a device build.
PUBLIC_BASE_URL = os.getenv(
    "PUBLIC_BASE_URL",
    "http://localhost:8000" if DEBUG_MODE else "https://mergulhovirtual.dev",
).rstrip("/")

# On-disk cache for the latest Instagram post's image bytes. Instagram CDN
# URLs expire, so the app is only ever served our cached copy (videos are the
# exception — their fresh CDN URL is passed through; regular polling keeps it
# valid). Lives under local_storage/ in debug so it's wiped with the rest of
# the dev state; its own gitignored folder under the backend root in prod.
INSTAGRAM_CACHE_DIR = (
    LOCAL_STORAGE_DIR / "instagram"
    if DEBUG_MODE
    else Path(__file__).parent / "instagram_cache"
)

# Background job cadence (seconds). Fetch = latest-post poll; refresh = how
# often the token-refresh job wakes up to check whether a refresh is due.
INSTAGRAM_FETCH_INTERVAL_SECONDS = int(
    os.getenv("INSTAGRAM_FETCH_INTERVAL_SECONDS", str(30 * 60))
)
INSTAGRAM_REFRESH_CHECK_INTERVAL_SECONDS = int(
    os.getenv("INSTAGRAM_REFRESH_CHECK_INTERVAL_SECONDS", str(24 * 60 * 60))
)

# Firestore emulator coordinates. google-cloud-firestore reads
# FIRESTORE_EMULATOR_HOST off the environment on client construction and skips
# auth when it's set — same client code as prod, just a different endpoint.
FIRESTORE_EMULATOR_HOST = os.getenv("FIRESTORE_EMULATOR_HOST", "localhost:8080")
FIRESTORE_EMULATOR_PROJECT = os.getenv(
    "FIRESTORE_EMULATOR_PROJECT", "mergulho-virtual-debug"
)

# Which router(s) the FastAPI process serves. Prod deploys two Cloud Run
# services from the same image, differing only by this env var:
#   ROUTER_MODE=api    → /api/v1/* only (Unity client, App Check-gated)
#   ROUTER_MODE=admin  → / + /avistamentos/* + /telemetria/* (HTML, IAP-gated)
#   ROUTER_MODE=both   → both, for local dev (single uvicorn)
ROUTER_MODE = os.getenv("ROUTER_MODE", "both").lower()
if ROUTER_MODE not in ("api", "admin", "both"):
    raise ValueError(
        f"ROUTER_MODE must be one of api|admin|both, got {ROUTER_MODE!r}"
    )
