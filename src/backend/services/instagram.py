"""Instagram "latest post" integration (InstagramPlan.md, Phase 3).

State lives in Firestore (collection ``instagram``) so the same code works
against prod ADC Firestore and the local emulator:
  - doc ``auth``: access_token, user_id, username, obtained_at, refreshed_at,
    expires_at (ISO strings). Written by scripts/instagram_token_exchange.py
    and rotated by the refresh job below.
  - doc ``latest_post``: the normalized latest-post metadata plus media_id and
    fetched_at. Only ever replaced on a fully successful fetch cycle, so the
    last known good post keeps being served through any Instagram hiccup.

Image bytes are cached on local disk (config.INSTAGRAM_CACHE_DIR, one
``<media_id>.jpg``) and served by /api/v1/latest-post/image — Instagram CDN
URLs expire, so the app never sees them for images. Videos are NOT proxied:
the fresh CDN media_url is passed through and kept valid by regular polling.

The two loops at the bottom are plain asyncio tasks started from main.py's
lifespan (single process, single uvicorn worker — no celery/cron). Endpoints
never call Instagram; they only read Firestore + disk.
"""

from __future__ import annotations

import asyncio
import datetime
import logging
from pathlib import Path
from typing import Any, Dict, Optional

import httpx

import config  # attribute access kept live so tests can monkey-patch paths/URLs
from database import db

logger = logging.getLogger("backend.instagram")

GRAPH_BASE_URL = "https://graph.instagram.com"
HTTP_TIMEOUT_SECONDS = 15.0

MEDIA_FIELDS = "id,caption,media_type,media_url,thumbnail_url,permalink,timestamp"

# Refresh the long-lived token once its last refresh is older than this.
# Tokens last 60 days; a weekly refresh keeps a huge safety margin.
REFRESH_AFTER_DAYS = 7
# If expiry ever gets this close, the refresh loop is broken — scream in the
# logs (log-based alerting per the plan).
EXPIRY_WARN_DAYS = 10


def _auth_ref():
    return db.collection("instagram").document("auth")


def _latest_post_ref():
    return db.collection("instagram").document("latest_post")


def _utcnow() -> datetime.datetime:
    return datetime.datetime.now(datetime.timezone.utc)


def _parse_iso(value: Optional[str]) -> Optional[datetime.datetime]:
    if not value:
        return None
    try:
        dt = datetime.datetime.fromisoformat(value.replace("Z", "+00:00"))
    except ValueError:
        return None
    if dt.tzinfo is None:
        dt = dt.replace(tzinfo=datetime.timezone.utc)
    return dt


def read_auth() -> Optional[Dict[str, Any]]:
    """The stored Instagram credentials, or None if the token exchange script
    was never run against this Firestore."""
    snap = _auth_ref().get()
    return snap.to_dict() if snap.exists else None


def get_latest_post() -> Optional[Dict[str, Any]]:
    """The stored latest-post doc (internal shape, incl. media_id/fetched_at)."""
    snap = _latest_post_ref().get()
    return snap.to_dict() if snap.exists else None


def cached_image_path(media_id: str) -> Path:
    return config.INSTAGRAM_CACHE_DIR / f"{media_id}.jpg"


def normalize_media(media: Dict[str, Any]) -> Dict[str, str]:
    """Normalize one Graph API media object into the app's shape.

    ``image_source_url`` is the (expiring) Instagram CDN URL to download the
    display image from — callers cache those bytes locally and never expose it
    to the app. ``video_url`` IS exposed as-is (videos are not proxied).
      - IMAGE          → image = media_url
      - VIDEO (Reels)  → image = thumbnail_url, video = media_url
      - CAROUSEL_ALBUM → image = cover media_url, no video
    """
    media_type = media.get("media_type", "") or ""
    media_url = media.get("media_url", "") or ""
    thumbnail_url = media.get("thumbnail_url", "") or ""

    if media_type == "VIDEO":
        image_source = thumbnail_url
        video_url = media_url
    else:
        # IMAGE and CAROUSEL_ALBUM (cover); unknown types fall back best-effort.
        image_source = media_url or thumbnail_url
        video_url = ""

    return {
        "media_id": media.get("id", "") or "",
        "media_type": media_type,
        "image_source_url": image_source,
        "video_url": video_url,
        "caption": media.get("caption", "") or "",
        "permalink": media.get("permalink", "") or "",
        "timestamp": media.get("timestamp", "") or "",
    }


def build_public_post(stored: Dict[str, Any]) -> Dict[str, str]:
    """Firestore latest_post doc → the exact JSON the Unity client parses.

    Absent fields are empty strings, never null — JsonUtility on the app side
    can't represent null strings.
    """
    return {
        "media_type": stored.get("media_type", "") or "",
        "image_url": f"{config.PUBLIC_BASE_URL}/api/v1/latest-post/image",
        "video_url": stored.get("video_url", "") or "",
        "caption": stored.get("caption", "") or "",
        "permalink": stored.get("permalink", "") or "",
        "timestamp": stored.get("timestamp", "") or "",
    }


def _cleanup_cache_except(keep_name: str) -> None:
    """Drop stale cached media files, keeping only the current one."""
    try:
        for path in config.INSTAGRAM_CACHE_DIR.iterdir():
            if path.is_file() and path.name != keep_name:
                path.unlink()
    except OSError as e:
        logger.warning("[instagram] cache cleanup failed: %s", e)


async def fetch_latest_post_once(client: Optional[httpx.AsyncClient] = None) -> bool:
    """One fetch cycle. Returns True iff the latest_post doc was updated.

    The Firestore write happens only after everything else succeeded (Graph
    call, normalize, image download, disk write), so any failure leaves the
    last known good post — doc and cached image — fully intact.
    """
    auth = read_auth()
    if not auth or not auth.get("access_token"):
        logger.warning(
            "[instagram] no stored token — skipping fetch "
            "(run scripts/instagram_token_exchange.py once)"
        )
        return False

    own_client = client is None
    if own_client:
        client = httpx.AsyncClient(timeout=HTTP_TIMEOUT_SECONDS)
    try:
        resp = await client.get(
            f"{GRAPH_BASE_URL}/me/media",
            params={
                "fields": MEDIA_FIELDS,
                "limit": 1,
                "access_token": auth["access_token"],
            },
        )
        resp.raise_for_status()
        items = resp.json().get("data", [])
        if not items:
            logger.warning("[instagram] account has no media yet — nothing to cache")
            return False

        norm = normalize_media(items[0])
        if not norm["media_id"] or not norm["image_source_url"]:
            logger.warning(
                "[instagram] media %r has no id or image URL — keeping last good post",
                norm["media_id"],
            )
            return False

        img_resp = await client.get(norm["image_source_url"])
        img_resp.raise_for_status()
        if not img_resp.content:
            logger.warning(
                "[instagram] empty image body for media %s — keeping last good post",
                norm["media_id"],
            )
            return False
    except httpx.HTTPError as e:
        logger.warning(
            "[instagram] fetch failed (%s: %s) — last good post keeps serving",
            type(e).__name__, e,
        )
        return False
    finally:
        if own_client:
            await client.aclose()

    config.INSTAGRAM_CACHE_DIR.mkdir(parents=True, exist_ok=True)
    image_path = cached_image_path(norm["media_id"])
    tmp_path = image_path.with_name(image_path.name + ".tmp")
    tmp_path.write_bytes(img_resp.content)
    tmp_path.replace(image_path)  # readers see the old file or the new, never half

    # Full success — publish the new doc, then drop stale cache files.
    _latest_post_ref().set(
        {
            "media_id": norm["media_id"],
            "media_type": norm["media_type"],
            "video_url": norm["video_url"],
            "caption": norm["caption"],
            "permalink": norm["permalink"],
            "timestamp": norm["timestamp"],
            "fetched_at": _utcnow().isoformat(),
        }
    )
    _cleanup_cache_except(image_path.name)
    logger.info(
        "[instagram] cached latest post %s (%s)", norm["media_id"], norm["media_type"]
    )
    return True


async def refresh_token_once(client: Optional[httpx.AsyncClient] = None) -> bool:
    """One refresh-check cycle. Returns True iff the token was replaced.

    Refreshes when the stored token's last refresh is > REFRESH_AFTER_DAYS old;
    logs at ERROR level on any refresh failure AND whenever expiry is within
    EXPIRY_WARN_DAYS, so log-based alerting catches a dying token early.
    """
    auth = read_auth()
    if not auth or not auth.get("access_token"):
        logger.warning(
            "[instagram] no stored token — skipping refresh "
            "(run scripts/instagram_token_exchange.py once)"
        )
        return False

    now = _utcnow()

    expires_at = _parse_iso(auth.get("expires_at"))
    if expires_at is not None and expires_at - now < datetime.timedelta(days=EXPIRY_WARN_DAYS):
        logger.error(
            "[instagram] token expires at %s (< %d days away) — if refresh keeps "
            "failing, re-run the manual token exchange before then",
            expires_at.isoformat(), EXPIRY_WARN_DAYS,
        )

    last_refresh = _parse_iso(auth.get("refreshed_at")) or _parse_iso(auth.get("obtained_at"))
    if last_refresh is not None and now - last_refresh < datetime.timedelta(days=REFRESH_AFTER_DAYS):
        return False

    own_client = client is None
    if own_client:
        client = httpx.AsyncClient(timeout=HTTP_TIMEOUT_SECONDS)
    try:
        resp = await client.get(
            f"{GRAPH_BASE_URL}/refresh_access_token",
            params={
                "grant_type": "ig_refresh_token",
                "access_token": auth["access_token"],
            },
        )
        resp.raise_for_status()
        payload = resp.json()
    except httpx.HTTPError as e:
        logger.error(
            "[instagram] token refresh FAILED (%s: %s) — token still expires at %s",
            type(e).__name__, e, auth.get("expires_at"),
        )
        return False
    finally:
        if own_client:
            await client.aclose()

    new_token = payload.get("access_token")
    if not new_token:
        logger.error("[instagram] refresh response carried no access_token: %r", payload)
        return False

    expires_in = int(payload.get("expires_in", 60 * 24 * 3600))
    updated = dict(auth)
    updated.update(
        {
            "access_token": new_token,
            "refreshed_at": now.isoformat(),
            "expires_at": (now + datetime.timedelta(seconds=expires_in)).isoformat(),
        }
    )
    # Single-doc set is atomic in Firestore — readers see old or new, never a mix.
    _auth_ref().set(updated)
    logger.info(
        "[instagram] token refreshed; new expiry %s", updated["expires_at"]
    )
    return True


async def instagram_fetch_loop() -> None:
    """Poll the latest post every INSTAGRAM_FETCH_INTERVAL_SECONDS, forever.

    Each cycle is wrapped so nothing here can ever crash the server; a failed
    cycle just leaves the last good post being served until the next tick.
    """
    while True:
        try:
            await fetch_latest_post_once()
        except asyncio.CancelledError:
            raise
        except Exception as e:
            logger.warning(
                "[instagram] fetch cycle crashed (%s: %s) — retrying next cycle",
                type(e).__name__, e,
            )
        await asyncio.sleep(config.INSTAGRAM_FETCH_INTERVAL_SECONDS)


async def instagram_refresh_loop() -> None:
    """Wake every INSTAGRAM_REFRESH_CHECK_INTERVAL_SECONDS and refresh the
    token if it's due (see refresh_token_once). Never crashes the server."""
    while True:
        try:
            await refresh_token_once()
        except asyncio.CancelledError:
            raise
        except Exception as e:
            logger.error(
                "[instagram] refresh cycle crashed (%s: %s) — retrying next cycle",
                type(e).__name__, e,
            )
        await asyncio.sleep(config.INSTAGRAM_REFRESH_CHECK_INTERVAL_SECONDS)
