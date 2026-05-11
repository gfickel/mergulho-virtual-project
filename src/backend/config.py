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

# Firestore emulator coordinates. google-cloud-firestore reads
# FIRESTORE_EMULATOR_HOST off the environment on client construction and skips
# auth when it's set — same client code as prod, just a different endpoint.
FIRESTORE_EMULATOR_HOST = os.getenv("FIRESTORE_EMULATOR_HOST", "localhost:8080")
FIRESTORE_EMULATOR_PROJECT = os.getenv(
    "FIRESTORE_EMULATOR_PROJECT", "mergulho-virtual-debug"
)
