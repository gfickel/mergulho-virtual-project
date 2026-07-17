"""One-time exchange of a short-lived Instagram token for a long-lived one.

Phase 2 of InstagramPlan.md. Run this once after generating a short-lived
token in the Meta dashboard (*Instagram → API Setup with Instagram Login →
Generate access tokens → Generate token*, copied on the spot — it's shown once):

    cd src/backend
    # against the local debug stack (writes to the Firestore emulator):
    BACKEND_DEBUG=1 python -m scripts.instagram_token_exchange \
        --short-lived-token IGQ... --app-secret <APP_SECRET>

    # on the VM (writes real Firestore via the attached service account / ADC):
    python -m scripts.instagram_token_exchange --short-lived-token IGQ...
    # with the secret coming from the environment instead of argv:
    #   export INSTAGRAM_APP_SECRET=<APP_SECRET>

Steps performed:
  1. GET /access_token (grant_type=ig_exchange_token) → long-lived token (~60 d)
  2. GET /me?fields=id,username with the new token → verifies it + captures ids
  3. Writes Firestore doc instagram/auth (access_token, user_id, username,
     obtained_at, refreshed_at, expires_at as ISO strings)

The app secret is used only for the exchange call and is never persisted
anywhere. After this runs once, the backend's refresh job keeps the token
alive (refreshes weekly, well within the 60-day window).
"""

from __future__ import annotations

import argparse
import datetime
import logging
import os
import sys

import httpx

# Allow running as `python scripts/instagram_token_exchange.py` from backend/ —
# when invoked that way Python only adds `scripts/` to sys.path, not `backend/`,
# so the `from database import db` below would fail without this insert.
if __name__ == "__main__" and __package__ in (None, ""):
    sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))

GRAPH_BASE_URL = "https://graph.instagram.com"
HTTP_TIMEOUT_SECONDS = 15.0


def main() -> int:
    parser = argparse.ArgumentParser(
        description="Exchange a short-lived Instagram token for a long-lived one "
        "and store it in Firestore (instagram/auth)."
    )
    parser.add_argument(
        "--short-lived-token",
        required=True,
        help="Short-lived token from the Meta dashboard (shown once at generation).",
    )
    parser.add_argument(
        "--app-secret",
        default=os.getenv("INSTAGRAM_APP_SECRET"),
        help="Instagram App Secret (Meta dashboard → API Setup). "
        "Defaults to the INSTAGRAM_APP_SECRET env var. Never persisted.",
    )
    parser.add_argument(
        "--already-long-lived",
        action="store_true",
        help="Skip the exchange and store the given token directly. Use this when "
        "the dashboard's Generate-token button issued a long-lived (60-day) token "
        "— exchanging one of those fails with OAuthException 190 'Failed to decrypt'. "
        "Only /me verification is performed; no app secret needed.",
    )
    args = parser.parse_args()

    if not args.already_long_lived and not args.app_secret:
        print(
            "ERROR: no app secret — pass --app-secret or set INSTAGRAM_APP_SECRET.",
            file=sys.stderr,
        )
        return 1

    with httpx.Client(timeout=HTTP_TIMEOUT_SECONDS) as client:
        if args.already_long_lived:
            # Dashboard-issued long-lived token: nothing to exchange. The API gives
            # no way to read its expiry, so assume the documented 60-day window.
            long_lived_token = args.short_lived_token
            expires_in = 0
        else:
            # 1. Short-lived → long-lived exchange.
            resp = client.get(
                f"{GRAPH_BASE_URL}/access_token",
                params={
                    "grant_type": "ig_exchange_token",
                    "client_secret": args.app_secret,
                    "access_token": args.short_lived_token,
                },
            )
            if resp.status_code != 200:
                print(
                    f"ERROR: token exchange failed (HTTP {resp.status_code}): {resp.text}\n"
                    "Likely causes:\n"
                    "  - OAuthException 190 'Failed to decrypt': the dashboard token is "
                    "usually ALREADY long-lived — rerun with --already-long-lived; or the "
                    "secret is the Meta app's general App Secret (Settings → Basic) instead "
                    "of the Instagram App Secret on the API-Setup-with-Instagram-Login page.\n"
                    "  - Session expired: short-lived tokens last ~1 hour — generate a "
                    "fresh one in the dashboard and retry.",
                    file=sys.stderr,
                )
                return 1
            payload = resp.json()
            long_lived_token = payload.get("access_token")
            expires_in = int(payload.get("expires_in", 0) or 0)
            if not long_lived_token:
                print(
                    f"ERROR: exchange response carried no access_token: {payload}",
                    file=sys.stderr,
                )
                return 1

        # 2. Verify the long-lived token and capture the account identity.
        me = client.get(
            f"{GRAPH_BASE_URL}/me",
            params={"fields": "id,username", "access_token": long_lived_token},
        )
        if me.status_code != 200:
            print(
                f"ERROR: /me verification failed (HTTP {me.status_code}): {me.text}",
                file=sys.stderr,
            )
            return 1
        identity = me.json()
        user_id = str(identity.get("id", ""))
        username = identity.get("username", "")
        if not user_id:
            print(f"ERROR: /me response carried no id: {identity}", file=sys.stderr)
            return 1

    now = datetime.datetime.now(datetime.timezone.utc)
    expires_at = now + datetime.timedelta(seconds=expires_in or 60 * 24 * 3600)

    # 3. Store in Firestore. Imported late so exchange errors surface before any
    # GCP credential issues, and so BACKEND_DEBUG decides emulator vs. ADC.
    from database import db  # noqa: E402

    db.collection("instagram").document("auth").set(
        {
            "access_token": long_lived_token,
            "user_id": user_id,
            "username": username,
            "obtained_at": now.isoformat(),
            "refreshed_at": now.isoformat(),
            "expires_at": expires_at.isoformat(),
        }
    )

    days_left = (expires_at - now).days
    print(f"OK: long-lived token stored in Firestore (instagram/auth)")
    print(f"    account:  @{username} (user id {user_id})")
    assumed = " (assumed — dashboard tokens don't report expiry)" if not expires_in else ""
    print(f"    expires:  {expires_at:%Y-%m-%d %H:%M} UTC (~{days_left} days){assumed}")
    print("    the backend's refresh job now keeps it alive automatically")
    return 0


if __name__ == "__main__":
    logging.basicConfig(level=logging.INFO)
    # httpx logs full request URLs at INFO — that would echo client_secret and
    # the access token into the terminal/logs.
    logging.getLogger("httpx").setLevel(logging.WARNING)
    sys.exit(main())
