import os

from fastapi.templating import Jinja2Templates

# Initialize templates
# Assuming templates directory is in the root of the backend folder
templates = Jinja2Templates(directory="templates")

GCP_BUCKET_NAME = "avistamentos"

# Debug mode: skips serviceAccountKey.json, never writes to Firestore or GCS,
# logs incoming payloads instead. Enable with BACKEND_DEBUG=1.
DEBUG_MODE = os.getenv("BACKEND_DEBUG", "").lower() in ("1", "true", "yes", "on")
