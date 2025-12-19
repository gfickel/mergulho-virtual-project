import datetime
from google.cloud import storage
from config import GCP_BUCKET_NAME

def generate_signed_url(blob_name: str, expiration=3600) -> str:
    """
    Generates a v4 signed URL for a blob.
    
    :param blob_name: The name of the blob (file) in the bucket.
    :param expiration: Expiration time in seconds (default 1 hour).
    :return: The signed URL.
    """
    storage_client = storage.Client.from_service_account_json('./serviceAccountKey.json')
    bucket = storage_client.bucket(GCP_BUCKET_NAME)
    blob = bucket.blob(blob_name)

    url = blob.generate_signed_url(
        version="v4",
        # This URL is valid for 1 hour
        expiration=datetime.timedelta(seconds=expiration),
        # Allow GET requests using this URL.
        method="GET",
    )

    return url
