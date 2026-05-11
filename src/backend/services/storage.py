import datetime
import logging

from config import (
    GCP_BUCKET_NAME,
    DEBUG_MODE,
    LOCAL_STORAGE_DIR,
    LOCAL_STORAGE_URL_PREFIX,
)

logger = logging.getLogger("backend.debug")

if not DEBUG_MODE:
    from google.cloud import storage
    _client: "storage.Client | None" = None
else:
    _client = None


def _get_bucket():
    """Lazy GCS client init.

    Uses ADC — on Cloud Run the runtime service account is auto-detected. For
    `generate_signed_url(version="v4")` to work without a private key, that SA
    needs `roles/iam.serviceAccountTokenCreator` on itself so the library can
    sign via the IAM Credentials API.
    """
    global _client
    if _client is None:
        _client = storage.Client()
    return _client.bucket(GCP_BUCKET_NAME)


def _local_path(blob_name: str):
    return LOCAL_STORAGE_DIR / blob_name


def upload_bytes(blob_name: str, data: bytes, content_type: str = "application/octet-stream") -> str:
    """
    Upload raw bytes to the configured GCP bucket (or local folder in debug mode).

    :param blob_name: Object key inside the bucket (e.g. "imagens/abc.jpg").
    :param data: Raw bytes to upload.
    :param content_type: MIME type recorded on the object's metadata.
    :return: gs:// URI in prod, or a relative /local_storage/... URL in debug.
    """
    if DEBUG_MODE:
        path = _local_path(blob_name)
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_bytes(data)
        logger.info(
            "[DEBUG] local storage write blob=%s size=%d content_type=%s -> %s",
            blob_name, len(data), content_type, path,
        )
        return f"{LOCAL_STORAGE_URL_PREFIX}/{blob_name}"
    blob = _get_bucket().blob(blob_name)
    blob.upload_from_string(data, content_type=content_type)
    return f"gs://{GCP_BUCKET_NAME}/{blob_name}"


def generate_signed_url(blob_name: str, expiration=3600) -> str:
    """
    Generates a v4 signed URL for a blob (or a local /local_storage/... URL in debug mode).

    :param blob_name: The name of the blob (file) in the bucket.
    :param expiration: Expiration time in seconds (default 1 hour). Ignored in debug mode.
    :return: The browser-loadable URL.
    """
    if DEBUG_MODE:
        return f"{LOCAL_STORAGE_URL_PREFIX}/{blob_name}"
    blob = _get_bucket().blob(blob_name)

    url = blob.generate_signed_url(
        version="v4",
        # This URL is valid for 1 hour
        expiration=datetime.timedelta(seconds=expiration),
        # Allow GET requests using this URL.
        method="GET",
    )

    return url
