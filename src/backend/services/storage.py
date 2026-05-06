import datetime
import logging

from config import GCP_BUCKET_NAME, DEBUG_MODE

logger = logging.getLogger("backend.debug")

if not DEBUG_MODE:
    from google.cloud import storage
    _client: "storage.Client | None" = None
else:
    _client = None


def _get_bucket():
    global _client
    if _client is None:
        _client = storage.Client.from_service_account_json('./serviceAccountKey.json')
    return _client.bucket(GCP_BUCKET_NAME)


def upload_bytes(blob_name: str, data: bytes, content_type: str = "application/octet-stream") -> str:
    """
    Upload raw bytes to the configured GCP bucket.

    :param blob_name: Object key inside the bucket (e.g. "imagens/abc.jpg").
    :param data: Raw bytes to upload.
    :param content_type: MIME type recorded on the object's metadata.
    :return: gs:// URI of the uploaded object.
    """
    if DEBUG_MODE:
        logger.info(
            "[DEBUG] gcs.upload_bytes bucket=%s blob=%s content_type=%s size=%d",
            GCP_BUCKET_NAME, blob_name, content_type, len(data),
        )
        return f"gs://{GCP_BUCKET_NAME}/{blob_name}"
    blob = _get_bucket().blob(blob_name)
    blob.upload_from_string(data, content_type=content_type)
    return f"gs://{GCP_BUCKET_NAME}/{blob_name}"


def generate_signed_url(blob_name: str, expiration=3600) -> str:
    """
    Generates a v4 signed URL for a blob.

    :param blob_name: The name of the blob (file) in the bucket.
    :param expiration: Expiration time in seconds (default 1 hour).
    :return: The signed URL.
    """
    if DEBUG_MODE:
        logger.info("[DEBUG] gcs.generate_signed_url blob=%s", blob_name)
        return f"https://debug.local/{GCP_BUCKET_NAME}/{blob_name}"
    blob = _get_bucket().blob(blob_name)

    url = blob.generate_signed_url(
        version="v4",
        # This URL is valid for 1 hour
        expiration=datetime.timedelta(seconds=expiration),
        # Allow GET requests using this URL.
        method="GET",
    )

    return url
