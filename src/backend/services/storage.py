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
    import google.auth
    from google.auth.transport import requests as _google_requests
    from google.cloud import storage
    _client: "storage.Client | None" = None
    _signing_credentials = None
else:
    _client = None
    _signing_credentials = None


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


def _get_signing_credentials():
    """ADC credentials with a fresh access token, for IAM-API v4 signing.

    On the VM the runtime credentials are Compute Engine credentials that only
    carry a bearer token (no private key), so `generate_signed_url` cannot sign
    locally — it raises "you need a private key to sign credentials". Passing
    `service_account_email` + `access_token` makes the storage library sign via
    the IAM Credentials `signBlob` API instead, which is why the SA needs
    `roles/iam.serviceAccountTokenCreator` on itself. The access token expires
    (~1h), so refresh whenever it's no longer valid.
    """
    global _signing_credentials
    if _signing_credentials is None:
        _signing_credentials, _ = google.auth.default()
    if not _signing_credentials.valid:
        _signing_credentials.refresh(_google_requests.Request())
    return _signing_credentials


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


def find_video_blob(registro: str) -> "str | None":
    """
    Return the blob name of this registro's video (``videos/<registro>.<ext>``), or None.

    Most registros have no video, and the extension varies, so existence is
    probed with a single prefix listing instead of guessing extensions.
    """
    prefix = f"videos/{registro}."
    if DEBUG_MODE:
        directory = LOCAL_STORAGE_DIR / "videos"
        if directory.is_dir():
            for path in sorted(directory.iterdir()):
                if path.name.startswith(f"{registro}."):
                    return f"videos/{path.name}"
        return None
    for blob in _get_bucket().list_blobs(prefix=prefix, max_results=1):
        return blob.name
    return None


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
    creds = _get_signing_credentials()

    url = blob.generate_signed_url(
        version="v4",
        # This URL is valid for 1 hour
        expiration=datetime.timedelta(seconds=expiration),
        # Allow GET requests using this URL.
        method="GET",
        # Sign via the IAM signBlob API (no local private key on the VM).
        service_account_email=creds.service_account_email,
        access_token=creds.token,
    )

    return url
