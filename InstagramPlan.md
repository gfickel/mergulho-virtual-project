# Instagram "Latest Post" Integration — Plan

**Project:** Mergulho Virtual — Unity AR app (Android + iOS)
**Goal:** Display the latest post (image or video) from our own Instagram account inside the Unity app, served through our existing backend.
**API:** Instagram API with Instagram Login (host: `graph.instagram.com`). No Facebook Page required. No Meta App Review required (we only read our own account, connected via the Instagram Tester role, in development mode).

> **Status (2026-07-17): Phases 2–4 implemented and LIVE end-to-end** (token stored, backend polling, widget on the About screen, Reel playback verified on a real Android device). As-built documentation lives in CLAUDE.md § "Instagram latest post". Two deviations from the text below: (1) the endpoint is `/api/v1/latest-post` behind Firebase App Check (repo convention for Unity-facing routes), not a public `/api/latest-post`; (2) the dashboard's *Generate token* button turned out to issue an **already long-lived** token — exchanging it fails with OAuthException 190 "Failed to decrypt", so `scripts/instagram_token_exchange.py --already-long-lived` stores it directly and the refresh job takes over.

---

## ✅ Phase 1 — Meta / Instagram setup (DONE)

- [x] Instagram account converted to a Professional (Business/Creator) account
- [x] Meta developer account created at developers.facebook.com
- [x] Meta app created: **"Mergulho Virtual-IG"**, use case *Manage messaging & content on Instagram* (Instagram API with Instagram Login)
- [x] Instagram Tester role assigned to our Instagram account (Roles tab) and invite accepted on instagram.com (Settings → Apps and websites → Tester invites)
- [x] Instagram account connected under *Instagram → API Setup with Instagram Login*; token generation available

**Skipped on purpose (not needed for this use case):** webhooks, business login setup, app review, messaging/comments permissions (we only use `instagram_business_basic` to read our own media).

### Credentials inventory (server-side only — NEVER ship these in the Unity app)

| Item | Where it lives |
|---|---|
| Instagram App ID | Meta dashboard → API Setup page |
| Instagram App Secret | Meta dashboard → API Setup page |
| Instagram User ID | Returned by `/me` once we have a token |
| Long-lived access token | To be generated in Phase 2, stored in secrets manager / env var / DB |

---

## ✅ Phase 2 — Tokens (DONE 2026-07-17 — via `--already-long-lived`, see status note above)

- [ ] In the dashboard (*API Setup with Instagram Login → Generate access tokens*), click **Generate token** next to the connected account and copy the short-lived token (shown once)
- [ ] Exchange it for a **long-lived token** (valid ~60 days):
  - `GET https://graph.instagram.com/access_token?grant_type=ig_exchange_token&client_secret={APP_SECRET}&access_token={SHORT_LIVED_TOKEN}`
- [ ] Store the long-lived token securely on the backend (env var / secrets manager / DB — pick per our infra)
- [ ] Verify it works: `GET https://graph.instagram.com/me?fields=id,username&access_token={TOKEN}` → note the returned **Instagram User ID**

**Token refresh rule:** a long-lived token can be refreshed any time *before* expiry for a new 60-day token:
`GET https://graph.instagram.com/refresh_access_token?grant_type=ig_refresh_token&access_token={CURRENT_TOKEN}`
If it expires, the manual generation must be repeated — so automated refresh (below) is critical.

---

## ✅ Phase 3 — Backend (DONE 2026-07-17 — `services/instagram.py`, `api/endpoints/instagram_api.py`)

- [ ] **Token refresh job** — scheduled (e.g. weekly): call the refresh endpoint, replace the stored token atomically. Alert (email/Slack/log-based) on failure so an expiring token is never silent.
- [ ] **Fetch job** — scheduled every 15–60 min:
  - `GET https://graph.instagram.com/me/media?fields=id,caption,media_type,media_url,thumbnail_url,permalink,timestamp&limit=1&access_token={TOKEN}`
- [ ] **Normalize** the latest post into our own JSON shape:
  ```json
  {
    "media_type": "IMAGE | VIDEO | CAROUSEL_ALBUM",
    "image_url": "<our cached copy — see below>",
    "video_url": "<Instagram CDN URL, videos only>",
    "caption": "...",
    "permalink": "https://www.instagram.com/p/...",
    "timestamp": "ISO-8601"
  }
  ```
  - `IMAGE` → use `media_url` as the image
  - `VIDEO` (includes Reels) → image = `thumbnail_url`, video = `media_url`
  - `CAROUSEL_ALBUM` → treat as a single image using the cover `media_url` (pragmatic choice; `children` edge exists if we ever want more)
- [ ] **Cache the image/thumbnail bytes on our server** and serve them from our own URL — Instagram `media_url`/`thumbnail_url` are signed CDN links that expire. Do **not** proxy video bytes; pass the fresh CDN video URL through (regular polling keeps it valid).
- [ ] **Public endpoint** `GET /api/latest-post` → returns the cached JSON instantly (no Instagram call in the request path). Add short HTTP cache headers (e.g. `Cache-Control: public, max-age=300`).
- [ ] **Failure behavior:** if an Instagram fetch fails, keep serving the last known good post. The app must never receive an empty/error response due to an Instagram hiccup.

---

## ✅ Phase 4 — Unity app (DONE 2026-07-17 — `InstagramPostWidget.cs` on AboutScreen, built by `Tools > Mergulho Virtual > Create Instagram Widget`)

- [ ] **UI widget:** `RawImage` (+ `AspectRatioFitter`, "Fit In Parent") for the media, `TMP_Text` for the caption, play-button overlay (hidden for images), loading spinner/placeholder state
- [ ] **Fetch JSON** from `/api/latest-post` with `UnityWebRequest` on widget open / app start (coroutine or async/await)
- [ ] **Load image** via `UnityWebRequestTexture`, set texture + aspect ratio, hide spinner
- [ ] **Video playback** (only when `media_type == "VIDEO"`):
  - Show thumbnail + play button; do NOT autoplay
  - On tap: `VideoPlayer` with `VideoSource.Url` → `Prepare()` (show spinner while buffering) → render to a `RenderTexture` sized from `vp.width/vp.height` → assign to the `RawImage` → `Play()`
  - Audio: `AudioSource` + `audioOutputMode = VideoAudioOutputMode.AudioSource` (set **before** `Prepare()`); start muted with tap-to-unmute, Instagram-style
  - On prepare/stream failure: revert to thumbnail + play button, fall back to opening `permalink`
- [ ] **Tap-to-open:** whole widget (except play button) → `Application.OpenURL(permalink)` (opens Instagram app if installed, browser otherwise)
- [ ] **Offline/instant-launch cache:** save last JSON + image to `Application.persistentDataPath`; show it immediately on next launch, then refresh in background
- [ ] **Graceful degradation:** on any fetch failure with no cache, hide the widget entirely (never a broken UI)

---

## 🧪 Testing checklist

- [ ] Latest post as **image** → displays with correct aspect ratio
- [x] Latest post as **video/Reel** → thumbnail + play → streams with sound toggle *(verified on Android device 2026-07-17; Linux editor always errors on H.264 — expected)*
- [ ] Latest post as **carousel** → cover image displays
- [ ] Real **Android** device + real **iPhone** (Unity editor video backend differs from devices — editor testing proves nothing for video)
- [ ] Airplane mode / bad network → cached post or hidden widget, no broken UI
- [ ] Manually run token refresh job → stored token changes
- [ ] Publish a new Instagram post → app reflects it within the polling interval
- [ ] Confirm no Instagram credentials/tokens exist anywhere in the Unity project or build

---

## 📤 Future / separate feature: sharing FROM the app to Instagram

(Discussed but not part of this integration — keep for later.)

1. **Native share sheet** (easiest): capture AR view → `ScreenCapture` / `RenderTexture` → share via NativeShare plugin (iOS `UIActivityViewController` / Android `ACTION_SEND`). Covers Instagram + all other apps.
2. **Share to Instagram Stories deep link** (nicer UX): `instagram-stories://share` (iOS, data via UIPasteboard) / `com.instagram.share.ADD_TO_STORY` intent (Android). Requires a Facebook App ID — we now have one. Supports background image/video, transparent PNG sticker (great for AR captures), gradient colors, link sticker.
3. AR Foundation gotcha: plain screenshots include the UI — blit the AR camera to a `RenderTexture` or hide the UI canvas for one frame.

---

## ⚠️ Key constraints to remember

- Long-lived tokens last **60 days**; refresh before expiry or the manual dashboard flow must be repeated
- Instagram media CDN URLs **expire** — always serve images from our own cache
- Rate limit exists (200 calls/user/hour on Graph APIs) — irrelevant at our polling frequency, but never call Instagram from the app/request path
- Videos are standard **H.264/AAC MP4 over HTTPS** — hardware-decoded on both platforms, no ATS/cleartext issues
- App name on Meta cannot contain "Instagram"/"IG"/"Meta" — ours is already accepted, don't rename carelessly
- Keep at least **two admins** on the Meta app (App settings → Roles) so we're never locked out