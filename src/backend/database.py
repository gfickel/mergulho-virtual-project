import logging
import os

from config import DEBUG_MODE, FIRESTORE_EMULATOR_HOST, FIRESTORE_EMULATOR_PROJECT

logger = logging.getLogger("backend.debug")

if DEBUG_MODE:
    # FIRESTORE_EMULATOR_HOST must be in os.environ before the firestore client
    # is constructed — that env var is the SDK's documented switch for routing
    # a real google-cloud-firestore client at a local emulator with no creds.
    os.environ.setdefault("FIRESTORE_EMULATOR_HOST", FIRESTORE_EMULATOR_HOST)
    from google.cloud import firestore
    logging.basicConfig(level=logging.INFO)
    logger.warning(
        "BACKEND_DEBUG enabled: Firestore client -> emulator at %s (project=%s)",
        os.environ["FIRESTORE_EMULATOR_HOST"],
        FIRESTORE_EMULATOR_PROJECT,
    )
    db = firestore.Client(project=FIRESTORE_EMULATOR_PROJECT)
else:
    from google.cloud import firestore
    db = firestore.Client.from_service_account_json('./serviceAccountKey.json')
