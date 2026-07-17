"""Tests for the Unity-facing /api/v1/latest-post endpoints.

The endpoints only read Firestore (patched at services.instagram.db — the
endpoint module calls straight into the service) and the on-disk image cache
(redirected to tmp_path via config.INSTAGRAM_CACHE_DIR).
"""

from unittest.mock import MagicMock, patch

import pytest
from httpx import AsyncClient

import config


STORED_POST = {
    "media_id": "18000000000000001",
    "media_type": "VIDEO",
    "video_url": "https://cdn.example/video.mp4",
    "caption": "um reel",
    "permalink": "https://www.instagram.com/reel/xyz/",
    "timestamp": "2026-07-16T15:00:00+0000",
    "fetched_at": "2026-07-17T10:00:00+00:00",
}


@pytest.fixture
def mock_ig_db():
    with patch("services.instagram.db") as mock:
        yield mock


@pytest.fixture
def cache_dir(tmp_path, monkeypatch):
    monkeypatch.setattr(config, "INSTAGRAM_CACHE_DIR", tmp_path)
    return tmp_path


def _set_latest_post(mock_db, data):
    snap = mock_db.collection.return_value.document.return_value.get.return_value
    snap.exists = data is not None
    snap.to_dict.return_value = data


@pytest.mark.asyncio
async def test_latest_post_returns_cached_post(async_client: AsyncClient, mock_ig_db):
    _set_latest_post(mock_ig_db, dict(STORED_POST))

    response = await async_client.get("/api/v1/latest-post")

    assert response.status_code == 200
    assert response.headers["cache-control"] == "public, max-age=300"
    # Exact contract shape the Unity JsonUtility parser is built against —
    # internal fields (media_id, fetched_at) must NOT leak.
    assert response.json() == {
        "media_type": "VIDEO",
        "image_url": f"{config.PUBLIC_BASE_URL}/api/v1/latest-post/image",
        "video_url": "https://cdn.example/video.mp4",
        "caption": "um reel",
        "permalink": "https://www.instagram.com/reel/xyz/",
        "timestamp": "2026-07-16T15:00:00+0000",
    }


@pytest.mark.asyncio
async def test_latest_post_absent_fields_are_empty_strings(
    async_client: AsyncClient, mock_ig_db
):
    _set_latest_post(mock_ig_db, {"media_id": "1", "media_type": "IMAGE"})

    response = await async_client.get("/api/v1/latest-post")

    assert response.status_code == 200
    body = response.json()
    for field in ("video_url", "caption", "permalink", "timestamp"):
        assert body[field] == ""  # JsonUtility can't take null


@pytest.mark.asyncio
async def test_latest_post_404_when_nothing_cached(
    async_client: AsyncClient, mock_ig_db
):
    _set_latest_post(mock_ig_db, None)

    response = await async_client.get("/api/v1/latest-post")

    assert response.status_code == 404


@pytest.mark.asyncio
async def test_latest_post_image_serves_cached_bytes(
    async_client: AsyncClient, mock_ig_db, cache_dir
):
    _set_latest_post(mock_ig_db, dict(STORED_POST))
    (cache_dir / f"{STORED_POST['media_id']}.jpg").write_bytes(b"jpeg-bytes")

    response = await async_client.get("/api/v1/latest-post/image")

    assert response.status_code == 200
    assert response.headers["content-type"] == "image/jpeg"
    assert response.headers["cache-control"] == "public, max-age=300"
    assert response.content == b"jpeg-bytes"


@pytest.mark.asyncio
async def test_latest_post_image_404_when_file_missing(
    async_client: AsyncClient, mock_ig_db, cache_dir
):
    _set_latest_post(mock_ig_db, dict(STORED_POST))  # doc exists, file doesn't

    response = await async_client.get("/api/v1/latest-post/image")

    assert response.status_code == 404


@pytest.mark.asyncio
async def test_latest_post_image_404_when_no_doc(
    async_client: AsyncClient, mock_ig_db, cache_dir
):
    _set_latest_post(mock_ig_db, None)

    response = await async_client.get("/api/v1/latest-post/image")

    assert response.status_code == 404
