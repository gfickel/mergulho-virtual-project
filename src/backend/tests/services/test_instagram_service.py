"""Unit tests for services.instagram — normalization, fetch, and refresh.

All Instagram HTTP goes through httpx.MockTransport (no network); Firestore is
a MagicMock patched at services.instagram.db, with per-doc wiring so the auth
and latest_post docs can be shaped independently.
"""

import datetime
from unittest.mock import MagicMock, patch

import httpx
import pytest

import config
from services import instagram


# ---------------------------------------------------------------------------
# helpers
# ---------------------------------------------------------------------------

def _fs(mock_db, docs):
    """Wire the mock Firestore so instagram/<name> reads serve docs[name]
    (absent key or None = doc doesn't exist). Returns the refs by name so
    tests can assert on writes (ref.set)."""
    refs = {}
    for name, data in docs.items():
        ref = MagicMock()
        snap = ref.get.return_value
        snap.exists = data is not None
        snap.to_dict.return_value = data
        refs[name] = ref

    mock_db.collection.return_value.document.side_effect = (
        lambda name: refs.setdefault(name, MagicMock())
    )
    return refs


def _client(handler):
    return httpx.AsyncClient(
        transport=httpx.MockTransport(handler), timeout=instagram.HTTP_TIMEOUT_SECONDS
    )


def _iso_days_ago(days: float) -> str:
    return (
        datetime.datetime.now(datetime.timezone.utc) - datetime.timedelta(days=days)
    ).isoformat()


def _iso_days_ahead(days: float) -> str:
    return (
        datetime.datetime.now(datetime.timezone.utc) + datetime.timedelta(days=days)
    ).isoformat()


AUTH_DOC = {
    "access_token": "LONG_LIVED_TOKEN",
    "user_id": "17841400000000000",
    "username": "mergulhovirtual",
    "obtained_at": _iso_days_ago(30),
    "refreshed_at": _iso_days_ago(1),
    "expires_at": _iso_days_ahead(50),
}


@pytest.fixture
def mock_ig_db():
    with patch("services.instagram.db") as mock:
        yield mock


@pytest.fixture
def cache_dir(tmp_path, monkeypatch):
    monkeypatch.setattr(config, "INSTAGRAM_CACHE_DIR", tmp_path)
    return tmp_path


# ---------------------------------------------------------------------------
# normalization
# ---------------------------------------------------------------------------

def test_normalize_image():
    norm = instagram.normalize_media(
        {
            "id": "111",
            "media_type": "IMAGE",
            "media_url": "https://cdn.example/img.jpg",
            "caption": "legenda",
            "permalink": "https://www.instagram.com/p/abc/",
            "timestamp": "2026-07-01T12:00:00+0000",
        }
    )
    assert norm == {
        "media_id": "111",
        "media_type": "IMAGE",
        "image_source_url": "https://cdn.example/img.jpg",
        "video_url": "",
        "caption": "legenda",
        "permalink": "https://www.instagram.com/p/abc/",
        "timestamp": "2026-07-01T12:00:00+0000",
    }


def test_normalize_video_uses_thumbnail_and_passes_video_through():
    norm = instagram.normalize_media(
        {
            "id": "222",
            "media_type": "VIDEO",
            "media_url": "https://cdn.example/video.mp4",
            "thumbnail_url": "https://cdn.example/thumb.jpg",
            "permalink": "https://www.instagram.com/reel/xyz/",
            "timestamp": "2026-07-02T08:00:00+0000",
        }
    )
    assert norm["image_source_url"] == "https://cdn.example/thumb.jpg"
    assert norm["video_url"] == "https://cdn.example/video.mp4"
    assert norm["media_type"] == "VIDEO"
    # caption absent on the Graph object → empty string, never None
    assert norm["caption"] == ""


def test_normalize_carousel_uses_cover_image_no_video():
    norm = instagram.normalize_media(
        {
            "id": "333",
            "media_type": "CAROUSEL_ALBUM",
            "media_url": "https://cdn.example/cover.jpg",
            "caption": None,  # Graph can send explicit nulls
            "permalink": "https://www.instagram.com/p/car/",
            "timestamp": "2026-07-03T10:00:00+0000",
        }
    )
    assert norm["image_source_url"] == "https://cdn.example/cover.jpg"
    assert norm["video_url"] == ""
    assert norm["caption"] == ""


# ---------------------------------------------------------------------------
# fetch job
# ---------------------------------------------------------------------------

MEDIA_IMAGE = {
    "id": "18000000000000001",
    "media_type": "IMAGE",
    "media_url": "https://cdn.example/full.jpg",
    "caption": "novo post",
    "permalink": "https://www.instagram.com/p/new/",
    "timestamp": "2026-07-16T15:00:00+0000",
}


@pytest.mark.asyncio
async def test_fetch_success_caches_image_updates_doc_and_cleans_stale(
    mock_ig_db, cache_dir
):
    refs = _fs(mock_ig_db, {"auth": dict(AUTH_DOC), "latest_post": None})
    stale = cache_dir / "old-media-id.jpg"
    stale.write_bytes(b"stale")

    def handler(request: httpx.Request) -> httpx.Response:
        if request.url.path == "/me/media":
            assert request.url.params["access_token"] == "LONG_LIVED_TOKEN"
            return httpx.Response(200, json={"data": [MEDIA_IMAGE]})
        assert str(request.url) == MEDIA_IMAGE["media_url"]
        return httpx.Response(200, content=b"jpeg-bytes")

    async with _client(handler) as client:
        assert await instagram.fetch_latest_post_once(client) is True

    cached = cache_dir / f"{MEDIA_IMAGE['id']}.jpg"
    assert cached.read_bytes() == b"jpeg-bytes"
    assert not stale.exists()  # old media files cleaned up

    written = refs["latest_post"].set.call_args[0][0]
    assert written["media_id"] == MEDIA_IMAGE["id"]
    assert written["media_type"] == "IMAGE"
    assert written["video_url"] == ""
    assert written["caption"] == "novo post"
    assert written["permalink"] == MEDIA_IMAGE["permalink"]
    assert written["timestamp"] == MEDIA_IMAGE["timestamp"]
    assert written["fetched_at"]  # ISO stamp present


@pytest.mark.asyncio
async def test_fetch_failure_keeps_last_good(mock_ig_db, cache_dir):
    refs = _fs(mock_ig_db, {"auth": dict(AUTH_DOC), "latest_post": None})
    last_good = cache_dir / "last-good.jpg"
    last_good.write_bytes(b"last-good")

    def handler(request: httpx.Request) -> httpx.Response:
        return httpx.Response(500, json={"error": "instagram hiccup"})

    async with _client(handler) as client:
        assert await instagram.fetch_latest_post_once(client) is False

    refs["latest_post"].set.assert_not_called()
    assert last_good.read_bytes() == b"last-good"  # cache untouched


@pytest.mark.asyncio
async def test_fetch_image_download_failure_keeps_last_good(mock_ig_db, cache_dir):
    refs = _fs(mock_ig_db, {"auth": dict(AUTH_DOC), "latest_post": None})

    def handler(request: httpx.Request) -> httpx.Response:
        if request.url.path == "/me/media":
            return httpx.Response(200, json={"data": [MEDIA_IMAGE]})
        return httpx.Response(403, content=b"expired CDN url")

    async with _client(handler) as client:
        assert await instagram.fetch_latest_post_once(client) is False

    refs["latest_post"].set.assert_not_called()
    assert not (cache_dir / f"{MEDIA_IMAGE['id']}.jpg").exists()


@pytest.mark.asyncio
async def test_fetch_noops_gracefully_without_auth_doc(mock_ig_db, cache_dir, caplog):
    _fs(mock_ig_db, {"auth": None, "latest_post": None})

    def handler(request: httpx.Request) -> httpx.Response:
        raise AssertionError("must not call Instagram without a stored token")

    async with _client(handler) as client:
        with caplog.at_level("WARNING", logger="backend.instagram"):
            assert await instagram.fetch_latest_post_once(client) is False
    assert any("no stored token" in r.message for r in caplog.records)


# ---------------------------------------------------------------------------
# refresh job
# ---------------------------------------------------------------------------

@pytest.mark.asyncio
async def test_refresh_replaces_token_when_due(mock_ig_db):
    auth = dict(AUTH_DOC, refreshed_at=_iso_days_ago(8))
    refs = _fs(mock_ig_db, {"auth": auth})

    def handler(request: httpx.Request) -> httpx.Response:
        assert request.url.path == "/refresh_access_token"
        assert request.url.params["grant_type"] == "ig_refresh_token"
        assert request.url.params["access_token"] == "LONG_LIVED_TOKEN"
        return httpx.Response(
            200,
            json={
                "access_token": "NEW_TOKEN",
                "token_type": "bearer",
                "expires_in": 60 * 24 * 3600,
            },
        )

    async with _client(handler) as client:
        assert await instagram.refresh_token_once(client) is True

    written = refs["auth"].set.call_args[0][0]
    assert written["access_token"] == "NEW_TOKEN"
    assert written["username"] == auth["username"]  # identity fields carried over
    assert written["refreshed_at"] > auth["refreshed_at"]
    assert instagram._parse_iso(written["expires_at"]) > instagram._utcnow() + datetime.timedelta(days=55)


@pytest.mark.asyncio
async def test_refresh_skipped_when_recently_refreshed(mock_ig_db):
    refs = _fs(mock_ig_db, {"auth": dict(AUTH_DOC, refreshed_at=_iso_days_ago(1))})

    def handler(request: httpx.Request) -> httpx.Response:
        raise AssertionError("refresh must not be called when not due")

    async with _client(handler) as client:
        assert await instagram.refresh_token_once(client) is False
    refs["auth"].set.assert_not_called()


@pytest.mark.asyncio
async def test_refresh_failure_logs_error_and_keeps_token(mock_ig_db, caplog):
    refs = _fs(mock_ig_db, {"auth": dict(AUTH_DOC, refreshed_at=_iso_days_ago(8))})

    def handler(request: httpx.Request) -> httpx.Response:
        return httpx.Response(400, json={"error": "bad token"})

    async with _client(handler) as client:
        with caplog.at_level("ERROR", logger="backend.instagram"):
            assert await instagram.refresh_token_once(client) is False

    refs["auth"].set.assert_not_called()
    assert any("refresh FAILED" in r.message for r in caplog.records)


@pytest.mark.asyncio
async def test_refresh_warns_loudly_when_expiry_is_near(mock_ig_db, caplog):
    # Not due for refresh, but expiring in 5 days → must still log at ERROR.
    _fs(
        mock_ig_db,
        {"auth": dict(AUTH_DOC, refreshed_at=_iso_days_ago(1), expires_at=_iso_days_ahead(5))},
    )

    def handler(request: httpx.Request) -> httpx.Response:
        raise AssertionError("refresh must not be called when not due")

    async with _client(handler) as client:
        with caplog.at_level("ERROR", logger="backend.instagram"):
            await instagram.refresh_token_once(client)
    assert any("token expires at" in r.message for r in caplog.records)
