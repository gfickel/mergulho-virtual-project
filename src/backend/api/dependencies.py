"""FastAPI dependencies wired by the api router.

`verify_app_check` is attached to every route under /api/v1 so the Unity
client must carry a valid Firebase App Check token to call the backend. Tokens
are short-lived (default 1h) and bound to a real install of the signed APK on a
non-tampered device — App Check is the canonical defense for *unauthenticated*
public mobile apps, where any static API key baked into the APK would be
extracted within minutes.

In debug mode (BACKEND_DEBUG=1) this short-circuits to allow the LAN dev
backend to keep accepting unauthenticated requests from the Unity editor and
test client — Firebase Admin SDK is never initialized.
"""

import logging
import threading
from typing import Optional

from fastapi import Header, HTTPException, status

import config  # live-readable so tests can monkey-patch DEBUG_MODE

logger = logging.getLogger("backend.auth")

_init_lock = threading.Lock()
_initialized = False


def _ensure_firebase_initialized() -> None:
    """Initialize firebase_admin lazily on first verify call.

    On Cloud Run the runtime service account is auto-detected via ADC, so no
    credentials file or env var is required. Locally outside debug mode you
    would need GOOGLE_APPLICATION_CREDENTIALS set to a key file.
    """
    global _initialized
    if _initialized:
        return
    with _init_lock:
        if _initialized:
            return
        import firebase_admin
        try:
            firebase_admin.get_app()
        except ValueError:
            firebase_admin.initialize_app()
        _initialized = True


async def verify_app_check(
    x_firebase_appcheck: Optional[str] = Header(default=None, alias="X-Firebase-AppCheck"),
) -> None:
    """Reject the request unless it carries a valid App Check token."""
    if config.DEBUG_MODE:
        return

    if not x_firebase_appcheck:
        raise HTTPException(
            status_code=status.HTTP_401_UNAUTHORIZED,
            detail="Missing X-Firebase-AppCheck header",
        )

    _ensure_firebase_initialized()
    from firebase_admin import app_check

    try:
        app_check.verify_token(x_firebase_appcheck)
    except Exception as e:
        # Never log the token itself — only the exception class.
        logger.warning("App Check verification failed: %s", type(e).__name__)
        raise HTTPException(
            status_code=status.HTTP_401_UNAUTHORIZED,
            detail="Invalid App Check token",
        )
