# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project overview

Mergulho Virtual ("Virtual Diving") is a Unity-based AR mobile app targeting Android (ARCore) with iOS support for the photo-picker / sighting-report path. It identifies marine life via on-device ONNX classification of the AR camera feed, overlays a location-aware UI (GPS + reverse geocoding of Fernando de Noronha beaches), places AR 3D models (e.g. Tubarão Martelo / hammerhead shark) the user can tap for info, reports sightings counts pulled from a backend HTTP API, lets users contribute their own sightings (photo + metadata) via a queued background upload, and shows the project's latest Instagram post on the About screen (served through the backend, never directly from Instagram).

The Unity project lives at [src/app/MergulhoVirtual/](src/app/MergulhoVirtual/). The repo root above `src/` currently contains only this file and README — all buildable code is inside the Unity project.

- Unity editor version: `6000.3.14f1` (Unity 6) — see [ProjectVersion.txt](src/app/MergulhoVirtual/ProjectSettings/ProjectVersion.txt). Match this when opening the project; Unity will otherwise auto-upgrade assets.
- Render pipeline: URP 17.3.0 (`com.unity.render-pipelines.universal`).
- Solution file: [MergulhoVirtual.slnx](src/app/MergulhoVirtual/MergulhoVirtual.slnx) (note: `.slnx`, not `.sln`).

## Build, run, and test

There is no CLI build wrapper; all development goes through the Unity Editor opened on `src/app/MergulhoVirtual/`.

- **Play / iterate**: open [Assets/Scenes/MainScene.unity](src/app/MergulhoVirtual/Assets/Scenes/MainScene.unity) and press Play. AR features that depend on real device sensors (camera, GPS) are stubbed in editor — see [GPSHandler.cs:73-80](src/app/MergulhoVirtual/Assets/Scripts/GPSHandler.cs#L73-L80) which hardcodes a Fernando de Noronha coordinate under `UNITY_EDITOR`. Note: the stub coordinate `(-3.85, -32.44)` now falls inside **Praia da Cacimba do Padre**'s real OSM outline, so editor `CurrentPlaceName` auto-resolves to that beach on Play (it was null before the 2026-07 places.json migration to precise coastlines). To exercise the override path in-editor, pick a different beach from the `BeachSelectorDropdown`; to test the "off all beaches → null" path, move the stub to a coordinate >`beachBufferMeters` from every polygon.
- **Android build**: File > Build Profiles > Android. ARCore + FineLocation/CoarseLocation permissions are required at runtime; the code requests them in [GPSHandler.cs:32-38](src/app/MergulhoVirtual/Assets/Scripts/GPSHandler.cs#L32-L38).
- **On-device logs**: `com.unity.mobile.android-logcat` is included — use Window > Analysis > Android Logcat rather than plain `adb logcat` to get Unity-aware filtering.
- **Tests**: EditMode only, via Window > General > Test Runner. Tests live under [Assets/Scripts/Tests/Editor/](src/app/MergulhoVirtual/Assets/Scripts/Tests/Editor/) and use NUnit (`com.unity.test-framework` 1.6.0). The reverse-geocoding tests read `Resources/places.json` via `Resources.Load`, so they must be invoked from the editor test runner, not an isolated NUnit runner. Individual tests: right-click a `[Test]` method in the Test Runner window > Run.

## Backend service — setup and debug mode

The FastAPI backend at [src/backend/](src/backend/) ships as a single image and runs as **one process** both in production (a GCE free-tier VM with `ROUTER_MODE=both`) and in local dev. Routes are split between two routers, gated by `ROUTER_MODE` so the same image *could* be split across processes if ever needed:
- **`/api/v1/*`** (App Check-gated, Unity-facing): `POST /api/v1/avistamentos` (multipart sighting upload), `GET /api/v1/avistamentos/count` (HUD counter), and `GET /api/v1/latest-post` + `/api/v1/latest-post/image` (cached Instagram latest post — see **Instagram latest post** under Architecture). See [api/endpoints/avistamentos_api.py](src/backend/api/endpoints/avistamentos_api.py) and [instagram_api.py](src/backend/api/endpoints/instagram_api.py). The router has a global `Depends(verify_app_check)` in [api/api.py](src/backend/api/api.py) — every request must carry a valid `X-Firebase-AppCheck` header in non-debug mode.
- **`/` + `/avistamentos*` + `/telemetria*`** (Cloudflare Access-gated, operator-facing): landing, paginated list, view, edit/delete forms, telemetry list + count. HTML-only — the old `?format=json` content negotiation is gone (admin is HTML-only behind Cloudflare Access, no external JSON consumer). See [api/endpoints/avistamentos_admin.py](src/backend/api/endpoints/avistamentos_admin.py) and [telemetria_admin.py](src/backend/api/endpoints/telemetria_admin.py).

Which router(s) the process serves is gated by **`ROUTER_MODE`** in [config.py](src/backend/config.py):
- `ROUTER_MODE=api` — `/api/v1/*` only (Unity-facing, App Check in code).
- `ROUTER_MODE=admin` — admin HTML only (`/` + `/avistamentos*` + `/telemetria*`, operator-facing, Cloudflare Access at the edge).
- `ROUTER_MODE=both` (default) — single process serves everything; this is what both the production VM and local dev run. No path collisions because the two routers own disjoint (method, path) pairs.

See **Production deploy — GCE free-tier VM + Cloudflare** below for the deploy steps.

Two credential modes, controlled by `BACKEND_DEBUG`:
- **Prod / cloud mode** (default, `BACKEND_DEBUG` unset): real Firestore + GCS via **Application Default Credentials** (ADC). On the GCE VM the attached service account is auto-detected via the metadata server — no key file in the image. Locally outside debug mode, set `GOOGLE_APPLICATION_CREDENTIALS=./serviceAccountKey.json` (or run `gcloud auth application-default login`); the legacy hardcoded `from_service_account_json('./serviceAccountKey.json')` path in [database.py](src/backend/database.py) / [services/storage.py](src/backend/services/storage.py) is gone, replaced by `firestore.Client()` / `storage.Client()` which read ADC.
- **Debug / local mode** (`BACKEND_DEBUG=1`): Firestore client points at the local emulator via `FIRESTORE_EMULATOR_HOST`; GCS writes go to the local folder `src/backend/local_storage/{originals,imagens}/` and are served by FastAPI at `/local_storage/...`. **App Check verification is also bypassed** (see [api/dependencies.py](src/backend/api/dependencies.py)) so the LAN backend keeps accepting unauthenticated requests from the Unity editor. Same code paths as prod otherwise — `db.collection().document().set(…)`, `services.storage.upload_bytes(…)`, `generate_signed_url(…)`, just routed at local substitutes. All four pieces (Firestore, GCS, App Check, seed data) are gated on `DEBUG_MODE` in [config.py](src/backend/config.py).

### One-time setup

- **Python env**: this repo uses the conda env `trn_backend` (Python 3.12). Activate it (`conda activate trn_backend`) and `pip install -r src/backend/requirements.txt` — `Pillow`, `pillow-heif`, `google-cloud-firestore`, `google-cloud-storage`, `fastapi`, `uvicorn`, etc. are all pinned there.
- **Java JRE** (only for debug mode — the Firestore emulator is a Java app):
  ```bash
  sudo apt install default-jre-headless
  ```
- **gcloud CLI + Firestore emulator component** (only for debug mode). **Do not install gcloud via snap** — snap sandboxes the install so `gcloud components install` errors out with `"You cannot perform this action because this Google Cloud CLI installation is managed by an external package manager"`, and the snap variant has no Firestore-emulator component of its own. Use Google's apt repo, which ships the emulator as its own package:
  ```bash
  sudo snap remove google-cloud-cli  # only if you previously installed via snap
  sudo apt-get install apt-transport-https ca-certificates gnupg curl
  curl https://packages.cloud.google.com/apt/doc/apt-key.gpg | sudo gpg --dearmor -o /usr/share/keyrings/cloud.google.gpg
  echo "deb [signed-by=/usr/share/keyrings/cloud.google.gpg] https://packages.cloud.google.com/apt cloud-sdk main" | sudo tee /etc/apt/sources.list.d/google-cloud-sdk.list
  sudo apt-get update
  sudo apt-get install google-cloud-cli google-cloud-cli-firestore-emulator
  ```
  The emulator runs offline — no `gcloud auth login` or project config required.

### Running in debug mode

Two terminals:

```bash
# Terminal 1 — Firestore emulator (port matches FIRESTORE_EMULATOR_HOST in config.py)
gcloud emulators firestore start --host-port=localhost:8080

# Terminal 2 — backend
conda activate trn_backend
cd src/backend
BACKEND_DEBUG=1 uvicorn main:app --host 0.0.0.0 --port 8000 --reload
```

The emulator is **in-memory by default** — killing it wipes everything and the next backend startup re-seeds from scratch. Pass `--data-dir=./.firestore-emulator-data` to persist across emulator restarts.

What `BACKEND_DEBUG=1` actually changes (all in [config.py](src/backend/config.py)):
- [database.py](src/backend/database.py) sets `os.environ["FIRESTORE_EMULATOR_HOST"]` from `FIRESTORE_EMULATOR_HOST` (default `localhost:8080`) before importing `google.cloud.firestore`, then constructs `firestore.Client(project=FIRESTORE_EMULATOR_PROJECT)` (default `mergulho-virtual-debug`). The env var is the SDK's documented switch — same client class as prod, no auth, routes to the emulator's gRPC endpoint.
- [services/storage.py](src/backend/services/storage.py) writes bytes to `LOCAL_STORAGE_DIR / blob_name` (`src/backend/local_storage/<blob>`) and `generate_signed_url` returns a relative `/local_storage/<blob>` URL — no signing, no expiration. Same blob layout as the prod bucket (`originals/<registro>.<ext>`, `imagens/<registro>.jpg`), so [read_avistamento](src/backend/api/endpoints/avistamentos.py#L194-L223) and `view.html` work unchanged.
- [main.py](src/backend/main.py) `mkdir`s `local_storage/` and mounts it at `/local_storage` via `StaticFiles`. The `mkdir` is load-bearing — `StaticFiles(directory=...)` validates the directory exists at construction time, not at first request.

### Debug seed data

On startup, the FastAPI `lifespan` hook in [main.py](src/backend/main.py) calls [scripts/seed_debug_data.py](src/backend/scripts/seed_debug_data.py)'s `seed_avistamentos()` if `BACKEND_DEBUG=1`. Six sample sightings (`seed-001` … `seed-006`) across distinct beaches/species/dates, each with a Pillow-generated solid-color placeholder JPEG that's written to both `local_storage/originals/<id>.jpg` and `local_storage/imagens/<id>.jpg`. Each Firestore doc carries `modo_registro: "seed"` as a sentinel — makes them easy to spot among real app submissions (`"app"`) and CSV-imported pre-app records (other values).

- **Idempotent**: each entry uses a stable doc id; the script checks `coll.document(registro).get().exists` per id and skips if present. Safe to re-run, safe across uvicorn `--reload` cycles.
- **Failure-tolerant**: the lifespan hook wraps the call in `try/except` and downgrades errors to `logger.warning("Debug seed skipped: %s", ...)`. If the emulator isn't up yet when uvicorn boots (race), the seed silently no-ops and the server still serves; rerun the seed manually once the emulator is up.
- **Manual reseed** (without restarting the server, e.g. after wiping the emulator):
  ```bash
  cd src/backend
  BACKEND_DEBUG=1 python -m scripts.seed_debug_data
  ```
- **Adding more sightings**: append to the `SEED_AVISTAMENTOS` list at the top of [seed_debug_data.py](src/backend/scripts/seed_debug_data.py). Beach names must match entries in [places.json](src/app/MergulhoVirtual/Assets/Resources/places.json) exactly (case + accents) if you want the Unity beach filter to match them later — same constraint as `BeachSharkSpawner`'s Inspector list.
- **Instagram seed**: the same lifespan hook also calls `seed_instagram_post()` in debug mode — a placeholder post + Pillow JPEG so the About-screen Instagram widget works in the editor with zero Meta credentials. It checks the stored doc's `media_id` sentinel and never clobbers a real fetched post (relevant if you point a debug backend at real credentials).

### Browsing the web UI

With the backend running (either mode), the web UI is at the same host:
- [http://localhost:8000/avistamentos](http://localhost:8000/avistamentos) — paginated list ([templates/avistamentos/list.html](src/backend/templates/avistamentos/list.html))
- `http://localhost:8000/avistamentos/<registro>` — view a single record ([view.html](src/backend/templates/avistamentos/view.html)); the `<img>` resolves `/local_storage/imagens/<registro>.jpg` (debug) or a signed GCS URL (prod)
- `http://localhost:8000/avistamentos/<registro>/edit` — edit form ([edit.html](src/backend/templates/avistamentos/edit.html))
- `http://localhost:8000/telemetria` — telemetry list ([templates/telemetria/list.html](src/backend/templates/telemetria/list.html))

The admin routes are HTML-only — there is no `?format=json` switch any more. Programmatic consumers should not exist on the admin side (it's Cloudflare Access-protected operator UI); any JSON the Unity client needs goes through `/api/v1/*`.

### Talking to the backend from the device

**As of 2026-06-05 both Unity URLs point at the prod HTTPS backend `https://mergulhovirtual.dev`** (see **Updating Unity URLs for prod** below), not a LAN IP. To run the Unity client against a *local* dev backend instead, repoint [BackendServices.cs:10](src/app/MergulhoVirtual/Assets/Scripts/BackendServices.cs#L10) `ApiUrl` and the `RegisterScreenController.uploadUrl` Inspector field ([MainScene.unity:8872](src/app/MergulhoVirtual/Assets/Scenes/MainScene.unity#L8872)) to your machine's LAN IP (the maintainer's was `http://192.168.68.108:8000`). Cleartext HTTP against a LAN IP is gated on Android by [AndroidManifest.xml](src/app/MergulhoVirtual/Assets/Plugins/Android/AndroidManifest.xml) — device builds need a `network_security_config.xml` cleartext whitelist or HTTPS (the prod Cloudflare URL is HTTPS, so it doesn't hit this). Editor + simulator builds talk to a LAN/localhost backend directly and don't hit that restriction.

## Production deploy — GCE free-tier VM + Cloudflare

**Status (2026-06-05): FULL STACK LIVE — backend VM + Cloudflare (Tunnel + Access) + Firebase App Check + Unity client are all DONE and verified end-to-end.** App Check confirmed working from both the Unity editor (`200` via a registered debug token) and a real Android device (`200` via a logcat debug token), with enforcement proven (no-token / bogus-token both `401` at the edge). Remaining items are optional/future: iOS (App Attest), real Play-Store release signing (Play App Signing SHA-256 + Play-track install for `PLAY_RECOGNIZED`), and dropping the now-unneeded AndroidManifest cleartext whitelist. This section is the single source of truth for the deploy (the earlier standalone `docs/deploy-gce-vm.md` runbook was folded into here and deleted). The earlier Cloud Run + IAP plan is **retired** (do not resurrect without the user re-opening that option).

> **Billing correction (the runbook gets this wrong):** GCP Always Free still requires an **open billing account with a payment method attached** — `compute.googleapis.com` will not even enable without one (`FAILED_PRECONDITION: Billing account ... is not open`). "Free tier" means $0 *within* the `e2-micro` / 30 GB-PD limits, **not** "no billing account." So the original reason for retiring Cloud Run ("it required open billing, the VM avoids it") was mistaken — the VM path needs open billing too. Billing account in use: `Mergulho Virtual Billing` = `015365-4719A7-B6E805`, linked to `mergulho-virtual-2025`.

**What's live (2026-06-02):**
- VM **`app-backend`** (`e2-micro`, `us-central1-a`, Debian 12, user `trn`, internal IP `10.128.0.2`); firewall closed, no public ingress yet.
- systemd service **`mergulho-backend`** running `uvicorn main:app --host 127.0.0.1 --port 8000 --workers 1` with `ROUTER_MODE=both` and `BACKEND_DEBUG` **unset**. Verified: `/api/v1/avistamentos/count` → **401** (App Check enforced, ADC→Firestore auth works), `/` and `/avistamentos` → 200.
- Default compute SA `863458035684-compute@developer.gserviceaccount.com` carries the three roles described below.
- Firebase **linked** to the project; **32 Firestore composite indexes deployed** (the firebase index deploy below is done).
- Code reaches the VM via a read-only **GitHub deploy key** (`~/.ssh/github_deploy` on the VM, registered as a Deploy Key on `gfickel/mergulho-virtual-project`). Redeploy with the repo-root **[Makefile](Makefile)** (`make deploy` = pull+restart on the VM; `make release` = push local `main` then deploy; also `make logs`/`status`/`ssh`/`health`), or manually `cd ~/mergulho-virtual && git pull && sudo systemctl restart mergulho-backend`.
- **Canonical repo is `git@github.com:gfickel/mergulho-virtual-project.git` (2026-06-08), not a fork.** The original `gfickel/mergulho-virtual` was a fork of `mchelem/mergulho-virtual`, and GitHub blocks Git LFS uploads to forks — so once the Firebase Unity SDK import added native binaries over the 100 MB limit, the project moved to a fresh non-fork repo with **Git LFS** for the heavy binaries. Tracking lives in the repo-root **[.gitattributes](.gitattributes)** (`*.bundle`, `*.so`, `*.srcaar`, `*.exe`, `FirebaseCppApp*.dll`); a fresh clone needs `git lfs install`. Locally `origin` = the new repo, `oldfork` = the old fork (backup). **The VM has no `git-lfs` installed and doesn't need it** — the backend never reads `Assets/Firebase/`, so `git pull` on the VM just checks out the small LFS pointer files (no 240 MB fetch onto the 1 GB box); don't `git lfs install` there.
- **Runtime is the VM's system Python 3.11** in a venv at `~/mergulho-virtual/src/backend/.venv` — **not** the pyenv-built 3.12 the runbook prescribes. There are no 3.12-only deps and every requirement ships a cp311 wheel, so the slow/OOM-prone source compile on the 1 GB box was deliberately skipped. (The conda dev env `trn_backend` is still 3.12; this is a VM-only deviation.)

**Cloudflare DONE (2026-06-04):** domain `mergulhovirtual.dev` (Cloudflare Registrar, project account `virtualmergulho@gmail.com`); Tunnel `app-backend` runs as systemd `cloudflared` on the VM (config `/etc/cloudflared/config.yml` → localhost:8000); Access gate (Zero Trust Free, One-time PIN login, application type **"Public DNS"**) protects `/avistamentos`+`/telemetria`, Allow-list = both `guilhermefickel@gmail.com` + `virtualmergulho@gmail.com`. Verified: `/api/v1/avistamentos/count`→401, `/avistamentos`→200-behind-Access. The backend is now reachable over HTTPS at `https://mergulhovirtual.dev`.

**What remains:** iOS App Attest registration only (Android Play Integrity + the whole Unity-side install/repoint are DONE 2026-06-05 — see the App Check / Unity sections below, which now document the as-built state). Optional follow-ups: Play App Signing SHA-256 for Play-Store release builds, dropping the AndroidManifest cleartext whitelist.

**Architecture**: one `e2-micro` VM in `us-central1` runs a **single uvicorn process with `ROUTER_MODE=both`** ([config.py](src/backend/config.py)) — no router split, no second machine. The two trust boundaries are gated differently from that one process:
- `/api/v1/*` (Unity-facing) — **Firebase App Check**, verified in code ([api/dependencies.py](src/backend/api/dependencies.py)). There is **no user login** in the app; App Check attests the request came from a genuine, untampered install of the signed app (Play Integrity on Android, App Attest on iOS), not from `curl` or a repackaged APK. The token is not a secret you manage — it's minted on-device, signed by Google/Apple, bound to your app identity, and expires hourly; the backend verifies it via the Firebase Admin SDK using the VM's attached service account (ADC).
- `/avistamentos*` + `/telemetria*` + `/` (operator HTML) — **Cloudflare Access** (Google SSO, free tier ≤50 users) at the edge, gating those paths to `guilhermefickel@gmail.com` **and** `virtualmergulho@gmail.com` (the project account). This is the IAP-equivalent for the VM path; the FastAPI process never sees a password.

**Transport**: **Cloudflare Tunnel**. `cloudflared` on the VM dials *outbound* to Cloudflare; TLS terminates at the edge and the tunnel forwards plain HTTP to `uvicorn` on `localhost:8000`. The VM firewall stays fully closed — no inbound 80/443, no static IP (an *unattached* static IP costs ~$3/mo if you ever stop the VM). The domain is an **`.app`** bought at Cloudflare Registrar (HSTS-preloaded, so browsers refuse plain HTTP at the protocol level — defense-in-depth on top of App Check).

**Service account** (VM's attached SA) needs exactly three roles: `roles/datastore.user`, `roles/storage.objectAdmin` on the `avistamentos` bucket, and `roles/iam.serviceAccountTokenCreator` on **itself**. The last is load-bearing for `storage.Blob.generate_signed_url(version="v4")` — without a private key in the image the library signs via the IAM Credentials API, and without the self-binding every signed URL throws `you need a private key to sign credentials...`.

**Constraints to plan around**: 1 GB RAM → single uvicorn worker (`--workers 1`), no Firestore emulator on the VM (emulator stays a local-dev thing). 30 GB standard PD is plenty (photos go to GCS, not local disk). ~1 GB/mo free egress → Cloudflare caches static responses and VM→Firestore/GCS traffic is internal-network (free), so the cap mainly bites on heavy external admin browsing. `e2-micro` is shared-core — fine for single-digit sightings/day, the first thing to outgrow under real traffic. **Never set `BACKEND_DEBUG` on the VM** — it disables App Check verification and swaps in the local emulator/disk storage, leaving `/api/v1` wide open.

### Cloudflare Tunnel + Access — the remaining transport/admin-gate steps

Domain is **`mergulhovirtual.dev`**, registered 2026-06-04 at Cloudflare Registrar under the **project account `virtualmergulho@gmail.com`** (NOT the personal `guilhermefickel@gmail.com`). All Cloudflare work — `cloudflared tunnel login`, Zero Trust / Access config — must be done logged into `virtualmergulho@gmail.com`. `.dev` is HSTS-preloaded just like `.app`, so the plain-HTTP-refused property holds. `<your-vm-user>` = `trn`. The original runbook said `.app` / placeholder `yourdomain.app`; that's now `mergulhovirtual.dev` everywhere below.

**0. Create the Cloudflare account + buy the domain** *(done 2026-06-04)*: `mergulhovirtual.dev` bought at Cloudflare Registrar under `virtualmergulho@gmail.com` on the free plan (Tunnel + Access are free-tier). Buying at Cloudflare Registrar **auto-created the zone** — no separate DNS setup, no nameserver change, no manual record (the Tunnel creates its own CNAME in step "Tunnel" below via `cloudflared tunnel route dns`).

Then do the rest on the VM:

**Tunnel** (exposes the localhost-only backend over HTTPS; the VM firewall stays closed):

```bash
# Install cloudflared from Cloudflare's apt repo
sudo mkdir -p --mode=0755 /usr/share/keyrings
curl -fsSL https://pkg.cloudflare.com/cloudflare-main.gpg \
  | sudo tee /usr/share/keyrings/cloudflare-main.gpg >/dev/null
echo "deb [signed-by=/usr/share/keyrings/cloudflare-main.gpg] https://pkg.cloudflare.com/cloudflared $(lsb_release -cs) main" \
  | sudo tee /etc/apt/sources.list.d/cloudflared.list
sudo apt-get update && sudo apt-get install -y cloudflared

cloudflared tunnel login                              # browser URL — pick the zone
cloudflared tunnel create app-backend
cloudflared tunnel route dns app-backend mergulhovirtual.dev   # Tunnel manages its own CNAME; no manual A record
```

Config at `~/.cloudflared/config.yml`:

```yaml
tunnel: app-backend
credentials-file: /home/<your-vm-user>/.cloudflared/<TUNNEL-UUID>.json
ingress:
  - hostname: mergulhovirtual.dev
    service: http://localhost:8000
  - service: http_status:404
```

Run it as a service, then verify from anywhere:

```bash
sudo cloudflared service install
sudo systemctl enable --now cloudflared
curl -s -o /dev/null -w "%{http_code}\n" https://mergulhovirtual.dev/api/v1/avistamentos/count   # 401 expected (no token)
```

**Access** (IAP-equivalent; Google SSO, free tier ≤50 users — gates the operator HTML):

1. Cloudflare dash → **Zero Trust → Access → Applications → Add an application → Self-hosted**.
2. Application domain `mergulhovirtual.dev`, **path** `/avistamentos` (add a second application for `/telemetria`, or a path prefix that covers both — Access matches prefixes).
3. Identity provider: add **Google** under Zero Trust → Settings → Authentication (one-time OAuth), then the policy: name `operators`, action **Allow**, Include → **Emails** → add both `guilhermefickel@gmail.com` and `virtualmergulho@gmail.com`.
4. Leave `/api/v1/*` and `/` **unprotected** by Access — the app path must stay open (App Check guards it) and the landing page is harmless.

Test: `https://mergulhovirtual.dev/avistamentos` → Google login → list renders; an incognito window without your account is blocked. Then do the Unity URL repoint below and drop the AndroidManifest cleartext whitelist.

### Firebase / App Check setup (one-time per project)

1. **Link Firebase to the GCP project** *(done 2026-06-02)*: Console → Firebase → "Add Firebase to GCP project" → pick `mergulho-virtual-2025`. No new project needed; this attaches Firebase to the existing GCP project so Firestore stays in place. **Gotcha:** the CLI (`firebase projects:addfirebase mergulho-virtual-2025`) and the REST `:addFirebase` both return a bare `403 PERMISSION_DENIED` until the **Firebase Terms of Service have been accepted once** in the Console — even for a project `roles/owner`. Accept the ToS via the Console flow first, after which the CLI works. (Also enable `firebase.googleapis.com` first if it isn't.)
2. **Register the Android app** *(done 2026-06-05)*: Firebase Console → Project Settings → General → Add app → Android. Package name = `applicationIdentifier.Android` in [ProjectSettings/ProjectSettings.asset](src/app/MergulhoVirtual/ProjectSettings/ProjectSettings.asset) = **`dev.mergulhovirtual`** (reverse-DNS of the owned domain; was `com.trn.mergulhovirtual`). SHA fingerprints from `keytool -list -v -keystore ~/.android/debug.keystore -alias androiddebugkey -storepass android` — add **both SHA-1 and SHA-256** (Play Integrity keys off SHA-256); for Play-Store builds also add the Play App Signing SHA-256 from the Play Console. `google-services.json` downloaded to [Assets/](src/app/MergulhoVirtual/Assets/) — its `package_name` MUST equal `applicationIdentifier.Android` or tokens are rejected. **It's gitignored** (along with `GoogleService-Info.plist` and the auto-generated `StreamingAssets/google-services-desktop.json`) — each dev/CI supplies its own.
3. **Register the iOS app** *(not done)*: same flow, bundle ID from Player → iOS settings (also `dev.mergulhovirtual`). Download `GoogleService-Info.plist` to [Assets/](src/app/MergulhoVirtual/Assets/).
4. **Enable App Check providers**: Firebase Console → App Check → register the app, click it, pick the provider. Android **Play Integrity** *(done 2026-06-05)* — keep the default verdict settings (require app integrity `PLAY_RECOGNIZED` ON, account-details `LICENSED` OFF, device-integrity unchecked); iOS **App Attest** *(pending)*. Leave the **APIs** (enforcement) tab alone — the backend verifies tokens itself via the Admin SDK; no client-side Firestore is used.
5. **Debug tokens — editor and device differ (key gotcha):**
   - **Editor/desktop does NOT auto-print a token.** You must CREATE one in Console → App Check → (app) → ⋮ → Manage debug tokens, then feed it to the SDK. [AppCheckTokenProvider.cs](src/app/MergulhoVirtual/Assets/Scripts/AppCheckTokenProvider.cs)'s `LoadEditorDebugToken()` (`#if UNITY_EDITOR`) reads it from env var `FIREBASE_APPCHECK_DEBUG_TOKEN` or the gitignored `<projectRoot>/firebase-appcheck-debug-token.txt`, then calls `DebugAppCheckProviderFactory.Instance.SetDebugToken(...)`. Restart Play after creating the file.
   - **Android Development Build DOES auto-print** a token to **logcat** (`DebugAppCheckProvider: Enter this debug secret ... <UUID>`) on first run — register THAT in Manage debug tokens. It's a **separate** token from the editor's; every editor and every device needs its own. Use Window → Analysis → Android Logcat, filter `DebugAppCheckProvider`.

### Unity Firebase SDK install

The Firebase Unity SDK (13.11.0, the version in use — downloaded to `firebase_unity_sdk_13.11.0/` at the repo root) ships as **`.unitypackage` files, NOT `.tgz` UPM tarballs**. The earlier plan in this file assumed the `.tgz`/`Packages/firebase/`/`manifest.json` route — that does not apply to this download: there is no `dotnet4/` folder and no tarballs anywhere in it, only `firebase_unity_sdk/Firebase<Module>.unitypackage` files. Install via the Editor's package importer instead.

1. Download the Firebase Unity SDK zip from https://firebase.google.com/download/unity and extract. It contains `firebase_unity_sdk/` with one `.unitypackage` per module (`FirebaseAppCheck.unitypackage`, `FirebaseAuth.unitypackage`, …). There is no separate `FirebaseApp` package — the Firebase **core** (`FirebaseApp`) **and EDM4U** are bundled inside every module package, so importing one module is self-contained.
2. **Import only `FirebaseAppCheck.unitypackage`** — that single package covers everything [AppCheckTokenProvider.cs](src/app/MergulhoVirtual/Assets/Scripts/AppCheckTokenProvider.cs) uses (`using Firebase; using Firebase.AppCheck;`). The app does **not** talk to Firestore/Auth directly (the backend does Firestore server-side via ADC), so don't import those — they only bloat the build. In the Editor: **Assets → Import Package → Custom Package…** → select `firebase_unity_sdk/FirebaseAppCheck.unitypackage` → leave everything checked → **Import**. This drops `Assets/Firebase/` and `Assets/ExternalDependencyManager/` into the project (not `Packages/`).
   - **`.unitypackage` import is an Editor-GUI action** — it can't be done by editing `manifest.json` from the CLI, and it can't be done from outside the open Editor (Unity locks the project, and the import triggers EDM4U which runs Gradle inside the Editor).
   - On import, EDM4U prompts **"Enable Android Auto-resolution?"** → **Enable**. It then downloads the Firebase/Play Integrity Android AARs (needs internet, ~1 min). Re-run manually any time via **Assets → External Dependency Manager → Android Resolver → Resolve**.
3. **`google-services.json` must be in `Assets/`** for Firebase to initialize at runtime (package import works without it, but `FirebaseApp.CheckAndFixDependenciesAsync` fails on device without it). It comes from the Console step (register the Android app with package name **`dev.mergulhovirtual`** — see Firebase setup above). The file's `package_name` must match `applicationIdentifier.Android` in [ProjectSettings.asset](src/app/MergulhoVirtual/ProjectSettings/ProjectSettings.asset) exactly, or App Check tokens are rejected.
4. **Add the scripting define**: Project Settings → Player → Other Settings → Scripting Define Symbols → add `FIREBASE_APPCHECK_ENABLED`. Apply to both Android and iOS platform targets. This flips [AppCheckTokenProvider.cs](src/app/MergulhoVirtual/Assets/Scripts/AppCheckTokenProvider.cs) from the no-op stub to the real Firebase implementation. Without the define, GetTokenAsync returns null and no header is sent (which is fine against a `BACKEND_DEBUG=1` LAN backend).
5. iOS: ensure `NSPhotoLibraryUsageDescription` and (for App Attest) any required entitlements are present in the Xcode project that Unity exports.

### Updating Unity URLs for prod *(done 2026-06-05)*

Both URLs now point at `https://mergulhovirtual.dev`:
- [BackendServices.cs:10](src/app/MergulhoVirtual/Assets/Scripts/BackendServices.cs#L10) `ApiUrl` = `https://mergulhovirtual.dev/api/v1/avistamentos/count` (code constant).
- `RegisterScreenController.uploadUrl` = `https://mergulhovirtual.dev/api/v1/avistamentos` — the **Inspector serialized value** at [MainScene.unity:8872](src/app/MergulhoVirtual/Assets/Scenes/MainScene.unity#L8872), which wins over the C# default (bumping the code constant alone wouldn't have updated it).

To repoint at a LAN dev backend again, swap the host back in both places (the Inspector one in `MainScene → ScreenUI → RegisterScreen`). Cleartext HTTP against a LAN IP would also need the [AndroidManifest.xml](src/app/MergulhoVirtual/Assets/Plugins/Android/AndroidManifest.xml) cleartext whitelist; on HTTPS it's unused and can be dropped for device-only prod builds.

## Firestore indexes — declarative + deployed via Firebase CLI

The operator UI's [Avistamentos list](src/backend/templates/avistamentos/list.html) lets you filter by `ano_registro`, `mes_registro`, `dia_registro`, `local`, `nome_popular` (and more later); the result list orders by `registro`. Firestore requires a **pre-built composite index for every unique combination of equality-filtered fields + the order_by field** — a query like `where("ano_registro", "==", "2024").order_by("registro")` will return a `FailedPrecondition: The query requires an index` 500 on the first hit, with a console URL to provision it one at a time. Clicking those URLs as errors arrive doesn't scale past a handful of filters, so the project uses indexes-as-code.

### File layout (repo root, not inside `src/backend/`)

- [tools/generate_firestore_indexes.py](tools/generate_firestore_indexes.py) — declarative source of truth. `QUERY_PATTERNS` at the top lists each `(collection, filter_fields, sort_field)` tuple. The script enumerates every non-empty subset of `filter_fields`, appends `sort_field`, and emits the Firebase-CLI JSON shape. Stdlib only; no venv.
- [firestore.indexes.json](firestore.indexes.json) — **generated; do not hand-edit**. 32 composite indexes currently (31 for avistamentos = 2^5 - 1 filter subsets × the `registro` sort field, 1 for telemetria = `(oid, date)`).
- [firebase.json](firebase.json) — minimal Firebase config; the only key is `firestore.indexes`. Firestore security rules aren't configured because all access is server-side via ADC (the backend), not from client SDKs.
- [.firebaserc](.firebaserc) — project alias; `default` points at `mergulho-virtual-2025`.

### One-time setup

Firebase CLI ships as a standalone binary (no Node/npm needed — `sudo npm install -g firebase-tools` errors with `npm: command not found` on a vanilla Linux box):

```bash
curl -sL https://firebase.tools | bash   # installs to /usr/local/bin/firebase
firebase login                            # browser OAuth, one-time
```

### Deploy

```bash
# Run from repo root (where firebase.json lives).
firebase deploy --only firestore:indexes
```

The CLI returns in seconds, but Firestore backfills each index in the background — the Firebase Console (Firestore → Indexes tab) shows them as "Building" for **5–15 minutes** on the initial deploy. Queries that need a still-building index get the same `FailedPrecondition` 500 until it flips to "Enabled." Subsequent deploys with no diff are fast; deploys that add a single new index also build in the background.

The backend deploy (VM `git pull` + `systemctl restart`) and the index deploy (`firebase deploy --only firestore:indexes`) are **independent steps with no shared toolchain** — touching one doesn't touch the other.

### Adding a new filter to the operator UI

1. Append the field name to the relevant pattern's `filter_fields` in [tools/generate_firestore_indexes.py](tools/generate_firestore_indexes.py).
2. `python3 tools/generate_firestore_indexes.py` — regenerates `firestore.indexes.json`.
3. `firebase deploy --only firestore:indexes` — backfill in 5–15 min.
4. Update [services/avistamentos.py](src/backend/services/avistamentos.py)'s `_build_query` to accept the new field, the endpoint in [api/endpoints/avistamentos_admin.py](src/backend/api/endpoints/avistamentos_admin.py) to thread it through, and the template form in [templates/avistamentos/list.html](src/backend/templates/avistamentos/list.html).

The order is load-bearing: deploy indexes **before** rolling out the UI change, or the first filter hit goes 500.

### Scaling headroom and the 200-index limit

Firestore allows **200 composite indexes per database** (free tier; same in paid). With N filter fields all combined with one sort field, the index count is `2^N - 1`. Current state:

| Filter fields | Indexes generated |
|---|---|
| 5 (now) | 31 |
| 6 | 63 |
| 7 | 127 |
| 8 | 255 ← over the limit |

So this approach is good for ~7 filter fields. Past that, the realistic options are:
- **Drop the explicit `order_by("registro")`** and use Firestore's implicit `__name__` order. Single-field equality queries then use auto-created indexes (no composite needed); only multi-field equality queries need composites. Halves the count roughly.
- **Switch the schema to a sortable composite field** (e.g., a `data_iso` "2024-07-15" string) and use range filters on it. Drastically reduces the index count because range-on-sort-field is auto-indexed; only non-date facets (local, species) need composites alongside it.

Neither is needed yet — flag this when filter count starts pushing past 6.

### Local dev / `BACKEND_DEBUG=1`

The Firestore emulator **does not enforce indexes** — every query "just works" against it regardless of what's in `firestore.indexes.json`. This means index errors are a pure prod-mode failure mode; you can't catch a missing index by running the emulator. The fastest way to flush out missing indexes before prod is to hit each filter combo against the deployed VM admin UI (or a staging clone) and watch the logs.

## Architecture

The runtime is a small set of single-responsibility `MonoBehaviour`s wired into `MainScene`. They do not talk to each other directly — each one owns one concern and is composed via Inspector references.

### Camera-feed ML inference — [CameraFeedToInference.cs](src/app/MergulhoVirtual/Assets/Scripts/CameraFeedToInference.cs)

Subscribes to `ARCameraManager.frameReceived`, samples **once every 5 s** (`FRAME_INTERVAL`, [CameraFeedToInference.cs:39](src/app/MergulhoVirtual/Assets/Scripts/CameraFeedToInference.cs#L39)), converts the `XRCpuImage` into an RGBA32 `Texture2D`, resizes to the model's input dims, and runs Unity Inference Engine (`com.unity.ai.inference` 2.6.1) on `BackendType.GPUCompute`. The bundled model is [Resources/mobilenet_v2.onnx](src/app/MergulhoVirtual/Assets/Resources/mobilenet_v2.onnx) (ImageNet-1k, default 224×224). Class labels come from [Resources/class_desc.txt](src/app/MergulhoVirtual/Assets/Resources/class_desc.txt) — one label per line, ImageNet classes. Output is argmax-then-label; there's no softmax or top-k. When swapping models, update both the `.onnx` asset reference in the Inspector and `class_desc.txt`; the script assumes output shape `[1, N_classes]`.

Tensor/worker lifecycle is manual: `worker` and any cached input tensors are `Dispose()`d in `OnDestroy` — follow the same pattern for any tensors you add, or you will leak GPU memory on scene reloads.

### Zero-shot sea detection — [SeaDetector.cs](src/app/MergulhoVirtual/Assets/Scripts/SeaDetector.cs) (+ hooks in [CameraFeedToInference.cs](src/app/MergulhoVirtual/Assets/Scripts/CameraFeedToInference.cs))

Detects "is the camera pointing at the sea?" with **no training data**: a MobileCLIP2-S0 image encoder ([Resources/mobileclip_image_encoder.onnx](src/app/MergulhoVirtual/Assets/Resources/mobileclip_image_encoder.onnx), 45 MB, tracked via Git LFS) produces a 512-dim embedding, and "sea vs not-sea" is defined purely by precomputed text-prompt embeddings ([Resources/sea_embeddings.json](src/app/MergulhoVirtual/Assets/Resources/sea_embeddings.json): 10 classes, one `isSea` flag each). Dot products → softmax (logitScale 64.3) → sum of `isSea` probabilities → EMA smoothing → on/off hysteresis. Consumers poll `IsSeaVisible`/`SmoothedScore` or wire the `onSeaDetected`/`onSeaLost` UnityEvents (nothing consumes them yet). Tooling, prompt lists, and the offline test harness live in [tools/sea_detector/](tools/sea_detector/) (own venv at `.venv`; see its README — note `hf-hub:apple/MobileCLIP2-S0` 404s, the scripts use open_clip's built-in `MobileCLIP2-S0`+`dfndr2b` config instead). Tweaking prompts = re-run `precompute_text_embeddings.py` + re-copy the JSON; no ONNX re-export.

- **Wiring**: the component sits on `ComputerVisionServices` next to `CameraFeedToInference`, which calls `seaDetector.Classify(...)` on the same 5-second AR frame it feeds mobilenet — so it inherits the `inferenceEnabled` screen gating. `SeaDetector` auto-loads its two assets via `Resources.Load` when the Inspector slots are empty (they are, in the scene). Own `Worker`, async `ReadbackRequest` readback, no per-frame allocation.
- **Orientation is load-bearing** ([CameraFeedToInference.GetUprightCameraTexture](src/app/MergulhoVirtual/Assets/Scripts/CameraFeedToInference.cs)): the `XRCpuImage` arrives sensor-native landscape regardless of how the phone is held, and a sideways frame tanks CLIP scores (a 90% beach can drop to ~10%). The frame is rotated per `Screen.orientation` **in MirrorY-converted (bottom-up) coordinates, which inverts the usual convention: Portrait needs 270°, not 90°** (confirmed empirically via the rotation sweep — portrait rot=270 scored 98%), then center-cropped to the display aspect so the model sees what the user sees, not the sensor's wider FOV.
- **Thresholds** (ON 0.5 / OFF 0.35, smoothing 0.5 at the 5 s cadence) sit in the measured gap: Noronha beach photos score 0.56–0.99, non-sea 0.01–0.31 (underwater shark close-ups ≈0.1 "pool"; sRGB→linear color-space shift is ~1% — irrelevant, verified).
- **Debug switches** (both default off): `SeaDetector.runSelfTest` classifies [Resources/sea_selftest.png](src/app/MergulhoVirtual/Assets/Resources/sea_selftest.png) (praia_do_sancho @256², uncompressed import) at startup and logs score + embedding head vs. the Python reference — expected ~82.6%, device measured 82.7%, proving on-device parity; `CameraFeedToInference.debugTryAllRotations` classifies each tick at 0/90/180/270 (blocking) and dumps `sea_debug_raw.png`/`sea_debug_upright.png` to `persistentDataPath`. Debug workflow: parity first (self-test), then orientation (sweep), then content (pull the PNGs).

### Beach AR stabilization — [Assets/Scripts/AR/](src/app/MergulhoVirtual/Assets/Scripts/AR/) (built by `Tools > Mergulho Virtual > Setup AR Stabilization`)

Stops ocean waves from fooling ARCore's visual-inertial tracking while preserving full 6DOF. Productionized from the prototype in [tools/ar/](tools/ar/) (kept as design notes — the app versions differ where flagged below). Three layers on one `ARStabilization` root GameObject, wired by [ArStabilizationBuilder.cs](src/app/MergulhoVirtual/Assets/Editor/ArStabilizationBuilder.cs):

- **[StillnessDetector.cs](src/app/MergulhoVirtual/Assets/Scripts/AR/StillnessDetector.cs)** — IMU "is the phone physically still?" (EMA-smoothed gyro + linear accel, slow-entry/fast-exit hysteresis). **Rewritten for the new Input System** (`UnityEngine.InputSystem.Gyroscope` / `LinearAccelerationSensor`, enabled via `InputSystem.EnableDevice`) because the project runs `activeInputHandler: 1` where the prototype's `Input.gyro` throws. In editor there are no IMU devices → `HasSensors` false, gate inert (expected). Units are backend-dependent (gyro ≈ rad/s, accel ≈ g) — tune from the live telemetry readouts, not assumed units.
- **[SpuriousMotionGate.cs](src/app/MergulhoVirtual/Assets/Scripts/AR/SpuriousMotionGate.cs)** — while still, per-frame AR camera translation is hallucinated, so the XR Origin is counter-shifted the same amount (camera can't be written; the TrackedPoseDriver owns it). Big single-frame jumps (> `relocalizationJumpThreshold`) are deliberate relocalizations and pass through. `[DefaultExecutionOrder(100)]`.
- **[GpsArKalmanFusion.cs](src/app/MergulhoVirtual/Assets/Scripts/AR/GpsArKalmanFusion.cs)** — two 1D Kalman filters (east/north): AR displacement is the predict step (process noise ∝ meters walked), GNSS fixes are the update step (R = reported accuracy², clamped by `minGpsAccuracy`). `DriftError` = fused − AR-only estimate; `ApplyDriftCorrection()` bleeds it into the XR Origin capped at `maxCorrectionSpeed` (default 0.25 m/s). **Heading**: seeded from a 2 s compass average, then **auto-refined from the GPS track** — every `headingSegmentMeters` (8 m) walked, the AR displacement direction vs. the GNSS displacement direction re-measures the azimuth of Unity +Z (rejected when the two lengths disagree >2×, i.e. GPS jump or gated AR). This is the prototype README's "best" option and matters because a wrong heading rotates every correction.

Plus two support pieces:

- **[GnssProvider.cs](src/app/MergulhoVirtual/Assets/Scripts/AR/GnssProvider.cs)** — best-available GNSS. On Android it bypasses Unity's LocationService and subscribes to the raw `LocationManager` **GPS provider** via an `AndroidJavaProxy` LocationListener (~2 Hz, true per-fix accuracy, course speed/bearing, satellite count from extras). Callbacks arrive on the **Android main thread** — primitives are extracted immediately and handed to Unity's `Update()` through a lock; don't touch Unity APIs in the listener. Falls back to polling `Input.location` (editor/iOS/native failure). Waits for the FineLocation grant that GPSHandler requests; [AndroidManifest.xml](src/app/MergulhoVirtual/Assets/Plugins/Android/AndroidManifest.xml) now declares `ACCESS_FINE_LOCATION` explicitly (Unity only auto-injects it for `Input.location`, not for native LocationManager use). Coexists with GPSHandler's `Input.location` — separate streams, no conflict.
- **[ArStabilizationController.cs](src/app/MergulhoVirtual/Assets/Scripts/AR/ArStabilizationController.cs)** — glue at `[DefaultExecutionOrder(200)]` (after the gate): applies drift correction each LateUpdate, by default **only while the user is walking** (`correctOnlyWhileMoving` — shifts are invisible mid-stride; the gate holds things steady while standing). Owns **field-tuning persistence**: every tunable round-trips through `<persistentDataPath>/ar_stabilization_tuning.json` (loaded in Awake over the scene defaults, saved debounced ~1 s after a panel change). `ResetToDefaults()` restores the values authored in the scene. To promote beach-tuned values to code defaults, pull the JSON via `adb shell run-as dev.mergulhovirtual cat files/ar_stabilization_tuning.json` (or Android Logcat's device file browser) and copy the numbers into the scripts/scene.

**On-beach tuning UI — [ArTuningPanel.cs](src/app/MergulhoVirtual/Assets/Scripts/AR/ArTuningPanel.cs)**: an "AJUSTE AR" pill on the AR HUD (`ScreenUI/MainScreen/ArTuning`) opens a panel with live telemetry (IsStill, smoothed gyro/accel, suppressed total, GNSS accuracy/age/satellites, drift vector, heading + calibration state, all at 4 Hz) and sliders/toggles for **every** stillness/gate/Kalman parameter — no recompile needed in the field. The builder only creates the shell (button, card, telemetry TMP, ScrollRect); the rows are **generated at runtime** from the parameter table in `ArTuningPanel.BuildRows()`, so adding a tunable = one `SliderRow(...)` line + the matching field in `ArStabilizationController.TuningData`, zero Inspector work. Tuning workflow at the beach follows [tools/ar/README.md](tools/ar/README.md) § "Tuning in the field". To hide the debug pill for a public release, deactivate `MainScreen/ArTuning/ToggleButton`.

**Parameter reference** — everything below is panel-exposed and persisted to the tuning JSON unless marked *(Inspector-only)*. Panel slider ranges live in `ArTuningPanel.BuildRows()`; code defaults in the scripts.

*StillnessDetector* (IMU stillness — feeds the gate and `correctOnlyWhileMoving`):
| Parameter | Default | Meaning / how to tune |
|---|---|---|
| `gyroStillThreshold` | 0.12 rad/s | Smoothed rotation rate below which the phone counts as still. Hand tremor is typically 0.02–0.10 and varies per person — **raise** if `IsStill` flickers while standing (test with several people), read the actual value off the telemetry. |
| `accelStillThreshold` | 0.03 g | Same idea for smoothed linear acceleration (gravity removed). Units are backend-dependent — trust the telemetry readout, not the unit label. |
| `enterStillTime` | 0.4 s | How long the IMU must stay quiet before stillness is declared (slow entry, prevents flicker). |
| `exitMultiplier` | 1.6 | An instantaneous reading above threshold×this breaks stillness immediately (fast exit). **Lower** if `IsStill` lags when you start walking — content briefly "sticks". |
| `emaAlpha` | 0.25 | Per-frame EMA factor on the raw IMU. Lower = smoother but slower to react (interacts with both thresholds). |

*SpuriousMotionGate* (anti-wave translation gate):
| Parameter | Default | Meaning / how to tune |
|---|---|---|
| `minDelta` | 0.0005 m | Per-frame camera translation below this is ignored as float noise (panel shows it in **mm/frame**). Rarely needs touching. |
| `relocalizationJumpThreshold` | 0.35 m | Single-frame jumps **larger** than this are treated as ARCore deliberately relocalizing and pass through even while still; 0 = suppress everything. **Lower** it if content "fights back" while standing still (tracker relocalizations being suppressed then re-applied). |

*GpsArKalmanFusion* (GPS↔AR drift filter + heading):
| Parameter | Default | Meaning / how to tune |
|---|---|---|
| `processNoisePerMeter` | 0.05 m²/m | Variance added per meter walked — how fast trust shifts from AR dead-reckoning to GNSS while moving. **Raise** if drift corrections lag on long walks; **lower** (or raise `minGpsAccuracy`) if content wobbles with GPS noise. |
| `minGpsAccuracy` | 3 m | Reported accuracies better than this are clamped (phones over-report). Effectively a floor on measurement noise R. |
| `maxUsableAccuracy` | 25 m | Fixes reporting worse accuracy than this are dropped entirely (no update step). |
| `maxCorrectionSpeed` | 0.25 m/s | Cap on how fast `ApplyDriftCorrection()` bleeds `DriftError` into the XR Origin. Keep small so corrections stay imperceptible; raise only if drift outruns the bleed. |
| `headingSegmentMeters` | 8 m | GNSS-track distance walked per heading re-measurement (AR path direction vs. GPS path direction). Smaller = calibrates sooner but noisier per segment. |
| `headingBlend` | 0.5 | Weight of each heading re-measurement after the first (1 = jump straight to the new value). |
| `compassAverageSeconds` | 2 s | *(Inspector-only)* Duration of the startup compass average that seeds the heading before the GPS track refines it. |

*ArStabilizationController* (switches — useful for A/B tests at the beach):
| Parameter | Default | Meaning |
|---|---|---|
| `gateEnabled` | on | Master switch for the IMU stillness gate. |
| `driftCorrectionEnabled` | on | Master switch for bleeding GPS drift corrections into the XR Origin. |
| `correctOnlyWhileMoving` | on | Apply corrections only while the IMU reports motion — shifts are invisible mid-stride, and the gate already holds the world steady while standing. Turn off only to debug the filter itself. |

*GnssProvider* *(both Inspector-only — a native re-subscribe is needed, so they're deliberately not in the panel)*: `minTimeMs` (500 ms) and `minDistanceM` (0 m) — the minimum interval/distance between native GPS fixes requested from Android's `LocationManager`.

Re-running the builder replaces both the `ARStabilization` root and the `ArTuning` UI (same iterate-in-code-XOR-editor rule as the screen builders).

### Location + reverse geocoding — [GPSHandler.cs](src/app/MergulhoVirtual/Assets/Scripts/GPSHandler.cs) + [ReverseGeocoding.cs](src/app/MergulhoVirtual/Assets/Scripts/ReverseGeocoding.cs)

`GPSHandler` starts `Input.location`, polls every frame, and asks `ReverseGeocoding.GetPlaceName(Vector2(lon, lat), beachBufferMeters)` for a human-readable place. **Coordinate convention is lon→x, lat→y everywhere** — do not swap these; the tests and `places.json` both rely on it. `beachBufferMeters` is a `[SerializeField]` on `GPSHandler` (Inspector: `LocationServices → GPSHandler → Beach Buffer Meters`, default **50 m**) — the max distance from a beach outline at which the point still resolves to that beach. It also exposes `CurrentPlaceName` + a `PlaceChanged` event (fires only when the resolved beach actually changes, not every frame), and `SetBeachOverride(name)` / `ClearBeachOverride()` so the GPS result can be bypassed from UI.

`ReverseGeocoding` is a static utility that lazily loads [Resources/places.json](src/app/MergulhoVirtual/Assets/Resources/places.json) into memory on first call, caching each place's ring as a `Vector2[]` (`placePolygons`) so the per-frame `GetPlaceName` call allocates nothing. Each "place" is a named polygon of `{lat, lon}` points; the polygons may be concave and need not be quadrilaterals despite the `IsPointInQuadrilateral` name. `JsonUtility` can't parse a top-level JSON array, so the loader wraps the file in `{ "places": ... }` before deserializing — keep `places.json` as a bare array at the file level. `GetAllPlaceNames()` exposes the loaded list for UI population (used by the beach dropdown).

**Resolver = nearest-beach-within-D, not first-match-inside.** `GetPlaceName(coord, D)` computes `BeachDistanceMeters` for every beach (0 if the point is inside the polygon via even-odd ray casting, else the meters to the nearest edge using a local flat-earth projection at `RefLatDeg = -3.85`, matching [tools/beach_polygon_debug.py](tools/beach_polygon_debug.py)) and returns the **closest** beach if it's within `D`, else null. This replaced the old first-box-in-file-order-that-contains-you logic, which made standing at e.g. Sharks Cove resolve to the giant "Porto de Santo Antônio" bounding box. Nearest-wins makes polygon overlaps harmless (file order no longer matters), so the 30 overlapping geocoder-box pairs are no longer a correctness hazard. **The geometry itself was migrated 2026-07 from geocoder bounding boxes to precise coastlines**: 13 beaches from OSM `natural=beach` ways, Cacimba do Padre from OSM multipolygon relation `5476220` (14 precise total), and 3 spots with no OSM outline (Quixaba, Sharks Cove, Buraco da Raquel) as small point-polygons centered on their best-known coordinate — sized so none overlaps a precise neighbor (no distance-0 ties). The migration is reproducible via [tools/migrate_places_to_osm.py](tools/migrate_places_to_osm.py) (`--apply`); OSM outlines are refreshed by [tools/fetch_osm_beaches.py](tools/fetch_osm_beaches.py), and [tools/beach_polygon_debug.py](tools/beach_polygon_debug.py) is an interactive matplotlib debugger for testing points against the data.

There is also a [Resources/praias_noronha.kml](src/app/MergulhoVirtual/Assets/Resources/praias_noronha.kml) source file; `places.json` appears to be the runtime-friendly derivation. Treat the KML as source-of-truth for beach boundaries.

### Per-beach shark spawning — [BeachSharkSpawner.cs](src/app/MergulhoVirtual/Assets/Scripts/BeachSharkSpawner.cs) + [BeachSelectorDropdown.cs](src/app/MergulhoVirtual/Assets/Scripts/UI/BeachSelectorDropdown.cs)

`BeachSharkSpawner` subscribes to `GPSHandler.PlaceChanged` and, via an Inspector-wired list of `{ beachName → GameObject[] sharkPrefabs }`, destroys the previously-spawned instances and instantiates the new beach's prefabs under its `spawnRoot` (defaults to its own transform). Beach names in the Inspector must match entries in [places.json](src/app/MergulhoVirtual/Assets/Resources/places.json) exactly. Shark prefabs live in [Assets/Prefabs/Sharks/](src/app/MergulhoVirtual/Assets/Prefabs/Sharks/), source FBX/textures/extracted materials in `Assets/Models/<kebab-name>/`, and Animator Controllers in [Assets/Animations/](src/app/MergulhoVirtual/Assets/Animations/) (one `.controller` per species, shared across the project rather than colocated with the model).

`BeachSelectorDropdown` is a `TMP_Dropdown` that lets the user manually pick a beach (useful for demos and for editor testing — the GPS stub now resolves to Cacimba do Padre, so the dropdown is how you reach any other beach in-editor). It lives under `BeachesScreen > BeachesList` so the user reaches it via the BottomNav's *Beaches* button. Index 0 is `Automático (GPS)` and calls `ClearBeachOverride()`; any other index calls `SetBeachOverride(name)`. It populates itself from `ReverseGeocoding.GetAllPlaceNames()` on `Start()`, so new entries in `places.json` appear automatically without code changes. The component can live on the same GameObject as the Dropdown itself — no wrapper GameObject needed.

There is no persistent shark GameObject in the scene; everything is spawned by `BeachSharkSpawner`. When adding a new species, follow "Adding a new shark species" below for the full FBX → prefab → spawner workflow; if tap-to-info is also wanted, see the `ObjectInteraction` note below.

### Animals catalog + 3D viewer — [AnimalsScreenController.cs](src/app/MergulhoVirtual/Assets/Scripts/UI/AnimalsScreenController.cs) + [AnimalViewerInput.cs](src/app/MergulhoVirtual/Assets/Scripts/UI/AnimalViewerInput.cs) + [AnimalDef.cs](src/app/MergulhoVirtual/Assets/Scripts/Content/AnimalDef.cs) + [AnimalsScreenBuilder.cs](src/app/MergulhoVirtual/Assets/Editor/AnimalsScreenBuilder.cs)

A two-stage screen built like Beaches but with a live 3D playground in place of the static photo. List view = the same `ListItem` cards as Beaches, populated from every `AnimalDef` ScriptableObject under [Resources/Animals/](src/app/MergulhoVirtual/Assets/Resources/Animals/). Detail view = hero 3D viewer card (RawImage rendering a dedicated camera) + scrollable name/description card + floating back button.

**3D viewer rig — sibling of `ScreenUI` at the scene root, NOT under the Canvas.** `AnimalViewerRig` lives at world position `(5000, 0, 0)` so it sits comfortably beyond the AR camera's far plane (typical 100 m) and can't be visible from the AR scene even if a stray cullingMask lets through. Contains `ViewerCamera` (FOV 35°, solid deep-navy clear color, target = [Assets/RenderTextures/AnimalViewer.renderTexture](src/app/MergulhoVirtual/Assets/RenderTextures/AnimalViewer.renderTexture)), a three-point directional light rig — `KeyLight` (warm, upper-front-right) + `FillLight` (subtle cool, upper-opposite, pitched *down* — never below) + `RimLight` (aquatic blue, behind-above, for silhouette separation against the navy clear color) — and `Turntable` (empty Transform; the controller spawns models under it). Camera and all three lights have `cullingMask` restricted to the `AnimalViewer` layer (defined at index 3 in [TagManager.asset](src/app/MergulhoVirtual/ProjectSettings/TagManager.asset)) so they don't cross-contaminate the AR scene.

**Why a separate rig instead of an in-canvas world-space camera**: rendering 3D models inside the same camera as AR is messy (depth conflicts, lighting overlap, AR session overhead). RenderTexture isolates the viewer from AR completely — the AR session can stay paused on this screen via `ScreenManager.useOptimizedPerformance`, and the viewer camera renders at the UI's frame rate without fighting the AR pipeline.

**`AnimalViewerInput`** lives on the `RawImage` (UI element), implements `IPointer*Handler` + `IDragHandler`, and writes back to the world-space `turntable` Transform (rotate) and `viewerCamera` Transform (zoom). One pointer = drag-rotate around Y; two pointers = pinch-zoom; mouse wheel = zoom in editor. Min/max camera distance defaults to 0.5–10 m — the upper bound has to bracket the auto-fit distance the controller sets on entry (see "Camera framing" below), or the user's first pinch-out would clamp closer than the auto-framed pose. **Bumping the `[SerializeField]` default does NOT update the value already serialized into the scene** — change the Inspector field on `AnimalsScreen → DetailPanel → ViewerCard → Viewport3D` directly, or right-click the field and *Revert* to pick up the new default.

**`AnimalDef`** is a `ScriptableObject` (Assets > Create > Mergulho Virtual > Animal): `displayName`, `imageName` (string filename loaded via `Resources.Load<Sprite>("Animals/" + imageName)` from [Resources/Animals/](src/app/MergulhoVirtual/Assets/Resources/Animals/)), `description`, `prefab`, `viewerScale`, `viewerOffset`, `photoCredit`, `modelCredit`, and `videos` (a `VideoRef[]` rendered as inline tap-to-play cards — see **Inline video player** below). The string-based image lookup mirrors `places.json`'s `imageName` field on the Beaches side — Sprite assets and AnimalDef `.asset` files cohabit in the same `Resources/Animals/` folder (a flat layout; `LoadAll<AnimalDef>("Animals")` filters by type so the jpgs don't pollute the result). The controller spawns `prefab` under `turntable`, applies `viewerOffset` + `viewerScale` (rotation is **not** overridden — see "Camera framing" below), then re-layers the entire instance to `AnimalViewer` (so the dedicated camera/lights pick it up). Adding a new species' card = drop a new `.asset` under `Resources/Animals/` — no code changes; the controller does `Resources.LoadAll<AnimalDef>("Animals")` on first activation.

**The detail viewer's 3D model comes from `AnimalDef.prefab` — a second, independent wiring of the same shark prefab.** Each species' prefab is referenced from two places: `BeachSharkSpawner`'s Inspector list (AR beach spawning) and the species' `AnimalDef.asset` under `Resources/Animals/` (detail viewer). Fixing or re-wiring one does NOT fix the other. **Empty-viewer failure mode**: `SpawnViewerInstance` silently early-returns when `animal.prefab == null` ([AnimalsScreenController.cs:166](src/app/MergulhoVirtual/Assets/Scripts/UI/AnimalsScreenController.cs#L166)) — no Console warning, the detail screen renders normally but `AnimalViewerRig → Turntable` stays empty. When a species shows no model, check the `prefab` slot on its `.asset` in `Resources/Animals/` first (Inspector shows **"Missing (Game Object)"** if the reference is broken, `None` if never set). The most common way it breaks: the species' FBX was replaced with a new export — see "Replacing an existing species' FBX" under **Adding a new shark species**.

**Camera framing — auto-center + auto-fit on each spawn** ([AnimalsScreenController.SpawnViewerInstance](src/app/MergulhoVirtual/Assets/Scripts/UI/AnimalsScreenController.cs)). The shark prefabs are sized to real-world meters (3–4 m sharks) and have AR-scene-specific transforms baked in (`localPosition (-4, 0, 4)` so they sit broadside of the AR origin), but in the viewer they need to be centered on the turntable and framed against the camera regardless of how the FBX hierarchy places the visible mesh relative to the prefab's root pivot. The controller does both, every time you enter a detail screen:
1. **Position**: instantiate under `turntable`, set `localPosition = animal.viewerOffset` (default `(0,0,0)`) — this throws away the AR-baked offset.
2. **Scale**: `localScale = animal.viewerScale` (default `(1,1,1)` — at real-world scale).
3. **Rotation**: deliberately **NOT** reset. The prefab's rotation (e.g. hammerhead's Y=180° to face camera) carries through to the viewer, so dialing in a rotation on the prefab once shows everywhere. Tiger and lemon have identity rotation, hammerhead has Y=180.
4. **Center**: union all `Renderer.bounds` in the spawned hierarchy and translate the root so `bounds.center` sits at `turntable.position`. This hides FBX-internal pivot offsets — it doesn't matter if the mesh is offset 1 m forward of the FBX root; the visible body lands centered on the turntable. The user's pinch-rotate also orbits around the visible body center, not the pivot.
5. **Fit camera**: compute `distance = (max(bounds.size.{x,y,z}) * 0.5) / tan(fov/2) * 1.15` (15% breathing room), then move the camera along its existing direction vector to that distance and `LookAt(turntable)`. The camera *direction* is preserved (pitch/roll stay whatever was authored), only distance changes. So a 4 m shark lands at ~7.3 m, a 2.5 m lemon shark at ~4.6 m — every species frames consistently regardless of its real-world size.

**Bind-pose `bounds` caveat**: `SkinnedMeshRenderer.bounds` returns the bind-pose AABB transformed to world space (Unity computes it once at import time when `updateWhenOffscreen = false`, the default). For an animation with a wide tail sweep, the centroid can drift a few cm between frames — usually invisible. If it visibly jitters when entering a detail screen, set `updateWhenOffscreen = true` on that species' SkinnedMeshRenderer (slight perf cost, exact bounds every frame) or precompute a tighter `localBounds` in the FBX importer.

**Tuning levers** when auto-fit isn't quite right: `viewerOffset` shifts the body within the frame *after* auto-centering (e.g. push eyes up to the framing center), and `viewerScale` rescales (rarely needed at real-world FBX scale). The 1.15 padding factor lives in [FitCameraToBounds](src/app/MergulhoVirtual/Assets/Scripts/UI/AnimalsScreenController.cs); change it there if the default framing feels too tight or too loose across the catalog.

**Lighting isolation from the AR scene** — the rig's three directional lights restrict `cullingMask` to bit 3 (`AnimalViewer`), and the scene's main `Directional Light` is configured to exclude the same bit ([MainScene.unity](src/app/MergulhoVirtual/Assets/Scenes/MainScene.unity) `m_CullingMask.m_Bits = 1073741879`). This double-sided exclusion is what keeps the rig and the AR sun direction from fighting each other. Without the exclusion on the scene light, the spawned model picks up the AR-scene directional alongside the rig and looks washed out / wrongly directional (silhouette lit from two contradictory directions). If you ever add another directional light to `MainScene` and the viewer suddenly looks off, check that the new light also clears bit 3.

If shading still looks off even with isolation in place, dial the rig itself — `KeyLight`/`FillLight`/`RimLight` color and intensity defaults live at the top of the lights block in [AnimalsScreenBuilder.cs](src/app/MergulhoVirtual/Assets/Editor/AnimalsScreenBuilder.cs). Edit the builder and re-run `Tools > Mergulho Virtual > Create Animals Screen` rather than tweaking the GameObjects in the Hierarchy directly, otherwise the next builder re-run wipes your edits.

**Anti-aliasing in the viewer — RT must be single-sample, MSAA goes on the URP asset, SMAA needs Post Processing**. URP renders the ViewerCamera into an internal multisampled buffer at `URPAsset.m_MSAA` samples, runs post-processing (including SMAA if enabled), then *resolves* the result into the target RenderTexture — so the RT only ever stores the final single-sample image. Three rules that need to hold or the viewer breaks (subtly on desktop, loudly on Android with `RenderPass: Attachment 0 was created with N samples but M samples were requested` every viewer frame):

- **`Mobile_RPAsset.m_MSAA`** is the only knob that produces real MSAA in the viewer (and is the only MSAA URP actually does — `Camera.allowMSAA` is a permission gate, not a sample count). **4 is the practical maximum on Android** — Adreno/Mali clamp 8x at the driver level, leaving URP's renderpass setup configured for 8 against a clamped 4-sample attachment. Desktop GPUs honor 8x natively, which is why simulator hides the bug. The mapping from Android to this asset goes through `ProjectSettings/QualitySettings.asset`: `m_PerPlatformDefaultQuality.Android: 0` → quality entry `Mobile` → its `customRenderPipeline` GUID → `Mobile_RPAsset.asset`.
- **[AnimalViewer.renderTexture](src/app/MergulhoVirtual/Assets/RenderTextures/AnimalViewer.renderTexture) `m_AntiAliasing` must stay at 1.** The Inspector's "Anti-aliasing: 2/4/8 samples" dropdown on a RenderTexture is **dead in URP** — URP doesn't read it for rendering, but Unity *does* allocate the GPU surface at that sample count and bind it as MSAA. Setting it to anything but 1 (a) re-triggers the renderpass mismatch the moment URP's MSAA disagrees, (b) makes the RT un-sample-able by `RawImage` on most mobile GPUs, and (c) shows the misleading Inspector warning *"Camera target texture requires Nx MSAA. Universal pipeline has MSAA disabled"* — which reads like "increase URP MSAA to N" but actually means "this field does nothing useful here, set it to 1." Don't follow the warning's literal advice.
- **SMAA / FXAA / TAA require `renderPostProcessing = true` on the camera.** All three are implemented as post-processing passes; setting `m_Antialiasing: 2` (SMAA) without `m_RenderPostProcessing: 1` silently no-ops AND fires an Inspector warning (paraphrased as "Post Processing not enabled"). Same for the other two modes. The combination on the ViewerCamera is `m_RenderPostProcessing: 1` + `m_Antialiasing: 2` (SMAA, High quality) layered on top of the pipeline's 4x MSAA.

[AnimalsScreenBuilder.cs](src/app/MergulhoVirtual/Assets/Editor/AnimalsScreenBuilder.cs) pins all three (RT to 1, `renderPostProcessing = true`, SMAA High); manual Inspector edits to RT MSAA or the camera's PP toggle get overwritten the next time the builder runs, so fix the builder rather than the asset if a tweak should be permanent. **Editor save vs. external file edit**: when an asset is selected in the Inspector and you Save the scene, Unity rewrites the asset file from the Inspector's cached state — so a `.renderTexture` edit made via Edit/file write may be silently reverted on next save. To make an asset edit stick, either close the Inspector view of that asset before saving, or make the change through the Inspector / a builder script.

**Building / regenerating — Editor menu**: `Tools > Mergulho Virtual > Create Animals Screen` ([AnimalsScreenBuilder.cs](src/app/MergulhoVirtual/Assets/Editor/AnimalsScreenBuilder.cs)) builds the entire `AnimalsScreen` UI hierarchy AND the `AnimalViewerRig` at the scene root, wires every `AnimalsScreenController` + `AnimalViewerInput` field, sets `ScreenManager.animalsScreen`, and binds `BottomNav/AnimalsButton.onClick → ScreenManager.ShowAnimals()`. Same idempotency + iteration rules as `RegisterScreenBuilder`: re-running wipes Inspector edits, so iterate either entirely in code OR entirely in the Editor — never alternately.

**Idempotency footgun — `GameObject.Find` skips inactive objects**: the controller calls `viewerRig.SetActive(false)` in `OnDisable()` whenever AnimalsScreen isn't the current panel, which is most of the time. So if you re-run the builder while on any other screen, a naive `GameObject.Find("AnimalViewerRig")` returns null even though the rig exists — the builder thinks there's nothing to delete and creates a *second* rig alongside the inactive one. The fix the builder uses is `SceneManager.GetActiveScene().GetRootGameObjects()` filtered by name (sees inactive too); generalize this to any builder that creates non-UI siblings of `ScreenUI` whose visibility is controlled by a screen controller. If you ever inherit a scene with duplicates from this footgun: Hierarchy → both rigs share the name; the inactive one has the greyed-out toggle in the Inspector — delete that one.

### Inline video player — [VideoSection.cs](src/app/MergulhoVirtual/Assets/Scripts/UI/VideoSection.cs) + [VideoPlayerController.cs](src/app/MergulhoVirtual/Assets/Scripts/UI/VideoPlayerController.cs) + [VideoRef.cs](src/app/MergulhoVirtual/Assets/Scripts/UI/VideoRef.cs) + [PointerHeldFlag.cs](src/app/MergulhoVirtual/Assets/Scripts/UI/PointerHeldFlag.cs) + [VideoSectionBuilder.cs](src/app/MergulhoVirtual/Assets/Editor/VideoSectionBuilder.cs)

A **screen-agnostic** subsystem for showing one or more streamed videos as inline tap-to-play cards. First used on the Animals detail screen (lemon shark), but nothing in it knows about animals — it's built to be dropped onto Beaches/About/etc. with two lines of glue.

The pieces, in dependency order:
- **`VideoRef`** — neutral `{ string title; string url; }` data type. Any entity exposes a `VideoRef[]`; `AnimalDef.videos` is the first such field. (Beaches are polygons in `places.json`, not ScriptableObjects, so a beach video field is added there when beaches need it — don't prematurely unify.)
- **`VideoPlayerController`** ([RequireComponent(VideoPlayer)]) — the playback engine. `Bind(url, title)` points it at a clip; it streams via `VideoPlayer` in **`APIOnly` render mode** (assigns `videoPlayer.texture` to a `RawImage` on `prepareCompleted`) — **no RenderTexture asset**, the surface auto-sizes to the clip aspect via an `AspectRatioFitter` (so vertical "reels" and landscape both frame right). **Lazy**: nothing streams until the user taps. The whole card is the tap target (play/pause); a control bar adds a play/pause button (**"Pausar"/"Tocar"** — words, not glyphs, to dodge missing-glyph risk), a draggable **seek slider**, and elapsed/total time. Stops and releases the texture on `OnDisable`, so leaving the screen kills the stream. Since 2026-07 it also has **opt-in mute support** for the Instagram widget: `startMuted` + optional `muteButton`/`muteLabel` fields (`ToggleMute()` via `SetDirectAudioMute` across all tracks, applied on prepare; button auto-hides for silent clips) — all defaulting to the old behavior, so existing Animals cards (whose serialized data predates the fields) are unaffected; and `Bind()` self-initializes via `EnsureInit()`, so it's safe to call from another component's `OnEnable` before this component's `Awake` has run.
- **`PointerHeldFlag`** — a 3-line `IPointer{Down,Up}Handler` that sits next to the seek `Slider` and exposes `Held`; the controller skips its per-frame slider update while you're dragging, so playback position doesn't fight the scrub. (Multiple handlers on one GameObject all fire, so it coexists with the Slider's own drag handling.)
- **`VideoSection`** — the reusable spawner. `Show(IReadOnlyList<VideoRef>)` clones an inactive card template once per video and `Bind()`s each clone; `Clear()` destroys them. Toggles **its own GameObject** off when the list is empty (so it contributes no layout height) and self-`Clear()`s on `OnDisable` as a safety net.
- **`VideoSectionBuilder`** (editor) — `static VideoSection Build(parent, roundedMat, accent, cardSurface, textPrimary, textSecondary)` constructs the "Vídeos" header + the card template (poster surface, play overlay, title caption, control bar, slider via a local `MakeHorizontalSlider`, loading/error states), wires the `VideoPlayerController` fields, and returns the wired `VideoSection`. This is the **single source of card construction** — palette is passed in so the card matches the host screen.

**Wiring videos into a screen** (the Animals path is the reference): the screen builder calls `var vs = VideoSectionBuilder.Build(detailContent.transform, …)` and assigns it to the controller's `VideoSection` field; the controller holds `[SerializeField] VideoSection videoSection` and calls `videoSection.Show(theVideos)` on entering detail and `.Clear()` on leaving. For Animals that's [AnimalsScreenBuilder.cs](src/app/MergulhoVirtual/Assets/Editor/AnimalsScreenBuilder.cs) (one `Build` call below the InfoCard) + [AnimalsScreenController.cs](src/app/MergulhoVirtual/Assets/Scripts/UI/AnimalsScreenController.cs). To add videos to Beaches/About, repeat those two lines — no card-UI duplication.

**Hosting — public GCS bucket, no backend.** Educational videos live in a **separate, world-readable** bucket `conteudos-educacionais` (the `videos/` prefix), made public with `gcloud storage buckets add-iam-policy-binding gs://conteudos-educacionais --member=allUsers --role=roles/storage.objectViewer`. The public `https://storage.googleapis.com/conteudos-educacionais/videos/<file>.mp4` URL drops straight into `VideoPlayer.url` — **no signing, no backend endpoint**, unlike the private `avistamentos` bucket whose images are served via signed URLs. Keep user-photo content in `avistamentos` (private/signed) and only public, non-PII content in this bucket. Encode as **H.264 MP4 with `+faststart`** (moves the `moov` atom up so progressive streaming can start before the full file downloads).

**Gotchas:**
- **Linux-editor `VideoPlayer` playback is unreliable** — a clip that buffers forever or errors in the editor on Linux is usually the editor, not the code. Verify on an **Android Development Build** (uses the OS decoder; the URL is plain HTTPS so the AndroidManifest cleartext rule doesn't apply).
- **`VideoPlayer` ships zero UI chrome** — it's a decoder. Every visible control is hand-built uGUI here; there is no native player overlay / PiP / gesture-seek (that would need a native plugin, deliberately out of scope).
- **Template self-reference remap is load-bearing.** The card's Button `onClick` persistent target points at the template's *own* `VideoPlayerController`; `Instantiate` remaps it to each clone's controller (self-reference within the cloned hierarchy), so every spawned card drives its own video. Don't repoint it at a shared instance.
- **`VideoSectionBuilder` re-run wipes manual edits**, same as the screen builders — and because it's invoked *from* `AnimalsScreenBuilder`, re-running `Tools > Mergulho Virtual > Create Animals Screen` rebuilds the video card. Iterate the card entirely in code (edit the builder) or entirely in the Editor, never alternately.
- **The ▶ glyph** in the play overlay ("▶ Assistir") depends on the TMP font (LiberationSans) having U+25B6; if it renders as tofu, drop the glyph in the builder. The play/pause button intentionally uses words to avoid this.

### Tap-to-interact — [ObjectInteraction.cs](src/app/MergulhoVirtual/Assets/Scripts/ObjectInteraction.cs)

Uses the new Input System (`Pointer.current`) — not legacy `Input.GetMouseButton`. Raycasts via `Physics.SphereCast` with a **0.2 m radius** to make small AR objects easier to tap; if you add new interactable objects, ensure they have colliders and matching `targetName`. Currently hardcoded to a single target ("Tubarão Martelo"); generalize via a component on the target prefab rather than growing the `if`-chain here.

### Backend — [BackendServices.cs](src/app/MergulhoVirtual/Assets/Scripts/BackendServices.cs)

Fetches a sighting count from `https://mergulhovirtual.dev/api/v1/avistamentos/count` on Start (the prod Cloudflare HTTPS URL as of 2026-06-05; [BackendServices.cs:10](src/app/MergulhoVirtual/Assets/Scripts/BackendServices.cs#L10), a code constant). Before sending, the coroutine awaits [AppCheckTokenProvider.GetTokenAsync()](src/app/MergulhoVirtual/Assets/Scripts/AppCheckTokenProvider.cs) and attaches the result as `X-Firebase-AppCheck`. With Firebase installed and `FIREBASE_APPCHECK_ENABLED` defined (the as-built state), that returns a real token and the prod backend verifies it (`200`); without them it returns null and the prod backend `401`s (a `BACKEND_DEBUG=1` LAN backend would ignore the missing header). **Footgun:** `BackendServices.countText` is wired to the same `Footer Text` TMP element as `CameraFeedToInference.classificationResultText`, so the ML result overwrites the `Avistamentos: N` count every 5 s — the count fetch works but is invisible on the HUD; verify success via the Console (no `401`) or VM logs, not the label. Fix = give the count its own TMP element and rewire `countText`.

The backend itself is at [src/backend/](src/backend/) (FastAPI). For running it locally — including the `BACKEND_DEBUG=1` mode that swaps real Firestore + GCS for the local Firestore emulator + an on-disk `local_storage/` folder, and auto-seeds six sample sightings on startup — see the **Backend service — setup and debug mode** section above.

`BackendServices` does a one-shot fetch and forgets. For network calls that need to survive flaky connectivity or app restarts (registration POSTs, post-install content downloads, etc.), use the **Background job system** below — don't add retry/persistence ad hoc to `BackendServices`-style code.

### Instagram latest post — [services/instagram.py](src/backend/services/instagram.py) + [InstagramPostWidget.cs](src/app/MergulhoVirtual/Assets/Scripts/UI/InstagramPostWidget.cs) (built by `Tools > Mergulho Virtual > Create Instagram Widget`)

Shows the project Instagram account's latest post (image, Reel, or carousel cover) as a card on the About screen. Full design in [InstagramPlan.md](InstagramPlan.md); **live end-to-end since 2026-07-17** (image + video verified on a real Android device). The app never talks to Instagram — the backend polls the Graph API (`graph.instagram.com`, *Instagram API with Instagram Login*, no app review needed for reading our own account) and the app only ever hits our own App Check-gated endpoints.

**Backend** ([services/instagram.py](src/backend/services/instagram.py), endpoints in [api/endpoints/instagram_api.py](src/backend/api/endpoints/instagram_api.py)):
- State lives in Firestore: `instagram/auth` (long-lived token + user id/username + obtained/refreshed/expires ISO strings) and `instagram/latest_post` (normalized post + `media_id` + `fetched_at`). Image bytes are cached on disk at `INSTAGRAM_CACHE_DIR` ([config.py](src/backend/config.py)): `local_storage/instagram/` in debug, gitignored `instagram_cache/` in prod.
- Two asyncio loops started from the [main.py](src/backend/main.py) lifespan (only when `ROUTER_MODE` serves the api router): a **fetch loop** every 30 min (`/me/media?limit=1` → normalize `IMAGE` / `VIDEO`→thumbnail+CDN-video-URL / `CAROUSEL_ALBUM`→cover → download image to disk via tmp+rename → publish the Firestore doc **last**, so any failure leaves the previous post serving — the last-known-good guarantee) and a **refresh loop** checking every 24 h (refreshes the token when >7 days since last refresh; `logger.error` on failure or when expiry is <10 days — log-based alerting, watch `make logs`). Videos are **not** proxied: the Instagram CDN mp4 URL is passed through, and the 30-min polling keeps it unexpired.
- `GET /api/v1/latest-post` returns `{media_type, image_url, video_url, caption, permalink, timestamp}` (absent fields are **empty strings, never null** — the Unity client parses with `JsonUtility`), `Cache-Control: public, max-age=300`, 404 until a first post is cached. `image_url` points at `GET /api/v1/latest-post/image` (the cached JPEG) and is computed per-request from `PUBLIC_BASE_URL` in config. Both endpoints are read-only (Firestore/disk) — Instagram is never called in the request path.
- **Token bootstrap** (one-time, Phase 2 of the plan): [scripts/instagram_token_exchange.py](src/backend/scripts/instagram_token_exchange.py), run on the VM (writes real Firestore via ADC) or locally with `BACKEND_DEBUG=1` (emulator). **Key gotcha:** the Meta dashboard's *Generate token* button (API Setup with Instagram Login) issues an **already long-lived** (60-day) token — feeding it to `ig_exchange_token` fails with OAuthException 190 `"Failed to decrypt"`. That's what `--already-long-lived` is for: skips the exchange, verifies via `/me`, stores directly (expiry assumed 60 d; the refresh loop takes over from there). The app secret is only ever an argv/env input, never persisted, and the httpx logger is silenced in the script because httpx logs full request URLs (secret + token) at INFO.
- Logging: `main.py` calls `logging.basicConfig(level=INFO)` — without it app-level INFO (e.g. the fetch loop's `[instagram] cached latest post`) never reaches journald, only WARNING+ leaks out via Python's last-resort handler. Silent logs after a restart = success, not failure; confirm via `ls instagram_cache/` on the VM.
- Tests: [tests/services/test_instagram_service.py](src/backend/tests/services/test_instagram_service.py) + [tests/api/endpoints/test_instagram.py](src/backend/tests/api/endpoints/test_instagram.py); all Instagram HTTP mocked via `httpx.MockTransport`.

**Unity** ([InstagramPostWidget.cs](src/app/MergulhoVirtual/Assets/Scripts/UI/InstagramPostWidget.cs), scaffolded by [InstagramWidgetBuilder.cs](src/app/MergulhoVirtual/Assets/Editor/InstagramWidgetBuilder.cs)):
- Lives at `ScreenUI/AboutScreen/AboutScroll/Viewport/Content/InstagramSection` — the builder's first run wrapped the formerly-flat AboutScreen in the standard ScrollRect pattern (`AboutScroll` → Viewport with RectMask2D → Content with VLG+ContentSizeFitter, BottomNav clearance) and moved the existing title text into `Content`. Re-runs replace only `InstagramSection`; same iterate-in-code-XOR-Editor rule as every builder.
- **Cache-first**: last JSON + image persist under `Application.persistentDataPath/instagram/` and display instantly on enable; a background refresh (App Check header via [AppCheckTokenProvider](src/app/MergulhoVirtual/Assets/Scripts/AppCheckTokenProvider.cs), same pattern as `BackendServices`, 5-min in-session throttle) then updates if the post changed. Any failure with no cache hides the whole section (stays hidden until next launch — deliberate, per plan). The `Base Url` serialized field on `InstagramSection`'s `InstagramPostWidget` defaults to `https://mergulhovirtual.dev`; repoint it to a LAN `BACKEND_DEBUG=1` backend for editor testing (debug mode ignores the missing App Check header) and don't save the scene with the LAN value.
- Tap anywhere on the card → `Application.OpenURL(permalink)`; `VIDEO` posts show thumbnail + play overlay (never autoplay) and stream via the shared [VideoPlayerController](src/app/MergulhoVirtual/Assets/Scripts/UI/VideoPlayerController.cs) with its opt-in `startMuted` + "Ativar som" toggle. On stream error the card reverts to thumbnail + inline error text and the permalink tap still works.
- **Linux-editor videos always fail** — Unity's Linux `VideoPlayer` can't decode H.264 (codec licensing) and Instagram serves H.264/AAC MP4, so `"VideoPlayer cannot play url … Cannot read file"` in the editor is expected and proves nothing. Image/caption/cache/hide logic is editor-testable; playback (mute toggle, seek) needs an Android build.

### Sighting reports — [RegisterScreenController.cs](src/app/MergulhoVirtual/Assets/Scripts/UI/RegisterScreenController.cs) + [ReportSightingJob.cs](src/app/MergulhoVirtual/Assets/Scripts/Jobs/ReportSightingJob.cs) + [GalleryPicker.cs](src/app/MergulhoVirtual/Assets/Scripts/Photo/GalleryPicker.cs)

The Register screen lets a user pick a photo from their phone gallery, tag it (beach + when + optional species/notes), and submit it for upload. The submit path is fire-and-forget from the user's perspective — they bounce back to the AR HUD immediately, and the **Background job system** below handles upload, retry, and offline persistence.

**Photo source — [GalleryPicker.cs](src/app/MergulhoVirtual/Assets/Scripts/Photo/GalleryPicker.cs)**: thin cross-platform wrapper over [yasirkula/UnityNativeGallery](https://github.com/yasirkula/UnityNativeGallery) (added to [Packages/manifest.json](src/app/MergulhoVirtual/Packages/manifest.json) as `com.yasirkula.nativegallery` from a git URL — needs internet on first import, then resolves from the package cache). Single API: `GalleryPicker.PickImage((path, error) => …)`. In Editor falls through to `EditorUtility.OpenFilePanel` so you can iterate without building. **Do not call `NativeGallery.RequestPermission` separately** — `GetImageFromGallery` requests permission internally; in the current package version (`be4007e35700`) it **returns `void`**, and a permission denial surfaces as a `null` path in the callback, indistinguishable from an ordinary cancel. Older docs and earlier versions had it returning `NativeGallery.Permission`; if you assign that return value the Android build fails with `CS0029: Cannot implicitly convert type 'void' to 'NativeGallery.Permission'` while the editor build still compiles, because the device branch is gated by `#if (UNITY_ANDROID || UNITY_IOS) && !UNITY_EDITOR` and the editor never sees it. If you need to distinguish denial from cancel, call `NativeGallery.RequestPermissionAsync(PermissionType.Read, MediaType.Image)` first and only call `GetImageFromGallery` on `Permission.Granted`. iOS requires `NSPhotoLibraryUsageDescription` in Project Settings → Player → iOS → Other Settings (NativeGallery's documentation will nag if it's missing).

**EXIF preservation is the whole point** of the photo path — backend can extract GPS, original timestamp, camera model from the EXIF block. Two places preserve it:
- NativeGallery (Android: `ContentResolver.openInputStream` → `FileOutputStream` raw byte copy; iOS: `PHAsset` request with `version = .original`).
- `ReportSightingJob.Execute` reads the on-disk file with `File.ReadAllBytes` and uploads it as a `MultipartFormFileSection`. **Never** decode → re-encode the image anywhere in this path; `Texture2D.LoadImage` for the in-screen preview is a separate in-memory copy that does not replace the upload bytes.

**`ReportSightingJob` ownership of the image file**: at enqueue time, [RegisterScreenController.OnSubmit](src/app/MergulhoVirtual/Assets/Scripts/UI/RegisterScreenController.cs) copies the picked file to `Application.persistentDataPath/sightings/<guid>.<ext>` and stores that path in the job. This is critical — NativeGallery returns a path inside the app's *cache* directory (Android may evict; iOS exports a temporary), and the user can also delete the original from their gallery between enqueue and execute. Owning a copy under `persistentDataPath/sightings/` makes the upload immune to both. The job deletes the file on `Success`. On `PermanentFailure` the file currently stays put (the queue moves the `.json` envelope to `jobs/failed/` but the sidecar isn't moved with it) — acceptable now since failures are rare and bounded by user actions; revisit if you change failure semantics.

**Submit UX — fire-and-forget**: the controller intentionally does *not* gate the user on backend availability. After validation (photo present, image successfully copied), it enqueues, sets a `"Enviando avistamento…"` status, resets the form, and calls `screenManager.ShowMain()`. Even queue-full or `JobQueue.Instance == null` are handled by logging only — never block the user on infra. If the upload eventually fails permanently, that surfaces via `JobCompleted` (currently nothing is wired to it; if you add a "retry / view failed sightings" UI, that's the hook).

**Backend contract** — implemented at [POST /api/v1/avistamentos](src/backend/api/endpoints/avistamentos_api.py) in the FastAPI service: `multipart/form-data` with one file part `photo` and form fields `beach`, `timestamp` (RFC3339 UTC), `species_guess`, `notes`. Two headers:
- **`Idempotency-Key`** (required) — the server uses it as the Firestore document id AND as the `<registro>` in the bucket key, so a retry with the same key is first-write-wins (`doc.exists` short-circuits to `200 + existing avistamento`, no second blob write). The app sends the same `Guid.NewGuid().ToString("N")` it baked into the local sighting filename — generated once per submit at [RegisterScreenController.OnSubmit](src/app/MergulhoVirtual/Assets/Scripts/UI/RegisterScreenController.cs) and persisted into the queued `ReportSightingJob`, so JobQueue retries (TCP timeout after the server already wrote, app kill mid-upload, etc.) collapse server-side instead of duplicating documents. Without this header the server returns 400.
- **`X-Firebase-AppCheck`** (required in prod) — fetched by [AppCheckTokenProvider.cs](src/app/MergulhoVirtual/Assets/Scripts/AppCheckTokenProvider.cs); see **Production deploy — GCE free-tier VM + Cloudflare** above for the full setup. In `BACKEND_DEBUG=1` mode the backend ignores the header, so the LAN dev workflow keeps working before Firebase is installed.

**401-on-retry semantics**: [ReportSightingJob.Execute](src/app/MergulhoVirtual/Assets/Scripts/Jobs/ReportSightingJob.cs) classifies a 401 on the *first* attempt as `TransientFailure` (and passes `forceRefresh: true` to AppCheck on the next attempt to bypass the SDK token cache — the cached token is what the server just rejected). 401 on attempt 2+ → `PermanentFailure`, on the assumption that a fresh token also getting rejected means the app is genuinely deregistered. This lets a long-queued job (token aged past 1h while offline) retry cleanly with a refreshed token instead of moving straight to `jobs/failed/`.

**`uploadUrl` = `https://mergulhovirtual.dev/api/v1/avistamentos`** as of 2026-06-05 — set in the **Inspector serialized value** at [MainScene.unity:8872](src/app/MergulhoVirtual/Assets/Scenes/MainScene.unity#L8872) (the C# default still reads the old LAN IP; the Inspector value wins, so only the serialized one matters). On HTTPS via Cloudflare, the [AndroidManifest.xml](src/app/MergulhoVirtual/Assets/Plugins/Android/AndroidManifest.xml) cleartext whitelist is no longer exercised; it's only needed if you repoint at a plain-HTTP LAN backend for dev.

**GCP bucket layout** (bucket = `avistamentos` per [config.py](src/backend/config.py)): two blobs per app-submitted sighting, written atomically by the endpoint:
- `originals/<registro>.<ext>` — raw bytes from the device, EXIF intact, kept as canonical source-of-truth.
- `imagens/<registro>.jpg` — Pillow-resized display variant (max 1600 px longest side, JPEG q85, EXIF + ICC profile preserved). The `imagens/` path is **load-bearing** — [read_avistamento](src/backend/api/endpoints/avistamentos.py) generates its signed URL from `imagens/{registro}.jpg`, so renaming the prefix breaks every existing list/view page (including the CSV-imported pre-app sightings, which use the same convention).

**Server-side resize, not client-side**, lives in [services/image_processing.py](src/backend/services/image_processing.py). EXIF is preserved by passing `image.info["exif"]` (raw bytes) back to `image.save(..., exif=...)` — Pillow doesn't re-interpret the block, so GPS, DateTimeOriginal, camera model, and orientation tag carry through unchanged. The orientation tag's `PixelXDimension`/`YDimension` and the embedded thumbnail go stale after resize but no viewer reads those — they read the actual JPEG SOF marker. **Why server-side**: Unity's `Texture2D.EncodeToJPG` strips all EXIF, and re-splicing the APP1 segment cross-platform (Android/iOS HEIC → JPEG paths) is painful — Pillow does the right thing in one `image.save` call. Trade-off: full-size originals get uploaded; long-tail download bandwidth (every list/view page hit) and storage cost win. **HEIC**: optional `pillow-heif` import in `image_processing.py` registers the HEIF opener; without the wheel installed, HEIC uploads return 415 `unsupported image format`. Both packages are pinned in [requirements.txt](src/backend/requirements.txt) — `pip install -r requirements.txt` after pulling.

**Firestore document shape** (app-submitted): `registro` = the Idempotency-Key GUID (32 hex chars, distinguishable from CSV-imported `registro`s which are sequential numbers/dates), `local` = beach name, `data_hora_iso` = the raw RFC3339 timestamp, `dia_registro`/`mes_registro`/`ano_registro` = string ints parsed from that timestamp (matches the existing query filters in [services/avistamentos.py](src/backend/services/avistamentos.py) so list filters work uniformly across both sources), `nome_popular` = species_guess, `observacao` = notes, `modo_registro` = `"app"` (sentinel that lets you separate app submissions from imported CSV rows in queries).

**Adding fields to the form**: extend `ReportSightingJob`'s `Data` struct + `SerializeData`/`DeserializeData` to round-trip the new field, add a `MultipartFormDataSection` for it in `Execute`, and have the controller populate it on enqueue. The job's persisted JSON envelope auto-handles versioning *only* in the JsonUtility "missing field stays default" sense — if you rename or delete a field, in-flight queued jobs from old versions will silently lose that data on reload.

**Building / regenerating the screen — Editor menu**: `Tools > Mergulho Virtual > Create Register Screen` ([RegisterScreenBuilder.cs](src/app/MergulhoVirtual/Assets/Editor/RegisterScreenBuilder.cs)) builds the entire `RegisterScreen` GameObject hierarchy under `ScreenUI` from code, following every UI rule below (RoundedRectCard material, RectMask2D over `Mask`, ScrollRect→Viewport→Content with proper VLG settings, BottomNav clearance, sibling order). It's idempotent (prompts to replace), and **auto-wires** `ScreenManager.registerScreen`, `RegisterScreenController.gpsHandler`/`screenManager`, and the `BottomNav > ReportButton` `OnClick → ScreenManager.ShowRegister()`. Re-run it after pulling a change that adds new form fields or restructures the layout — manual Inspector edits to the screen will be wiped, so iterate either entirely in code (re-run the menu) or entirely in the Editor (don't re-run the menu). Don't try to do both alternately.

### Background job system — [Assets/Scripts/Jobs/](src/app/MergulhoVirtual/Assets/Scripts/Jobs/)

`JobQueue` is a singleton MonoBehaviour ([JobQueue.cs](src/app/MergulhoVirtual/Assets/Scripts/Jobs/JobQueue.cs)) that owns a persistent FIFO of `Job` objects, retrying each one with exponential backoff while the app is alive and resuming from disk on next launch. Use it for any network operation where "user might not have signal right now" or "user might close the app before this finishes" is a real scenario. **Scope**: jobs survive *app kill* (file-on-disk reloads on next launch), but they do **not** run while the app is fully killed/swiped away — that would require Android `WorkManager` via a native plugin and is intentionally out of scope (see `WorkManager`/foreground-service note in design history if you ever need it).

**Calling it**:
```csharp
JobQueue.GetOrCreate().Enqueue(new HttpPostJob {
    Url = "https://example.com/register",
    JsonBody = "{\"name\":\"...\"}",
    IdempotencyKeyHeader = "user-42-register-v1",  // optional, server-side dedupe
});
```
Three job types ship: [HttpPostJob.cs](src/app/MergulhoVirtual/Assets/Scripts/Jobs/HttpPostJob.cs) for JSON POSTs, [FileDownloadJob.cs](src/app/MergulhoVirtual/Assets/Scripts/Jobs/FileDownloadJob.cs) for HTTP→file with optional SHA-256 verify, and [ReportSightingJob.cs](src/app/MergulhoVirtual/Assets/Scripts/Jobs/ReportSightingJob.cs) for multipart photo uploads (see "Sighting reports" above). Subscribe to `JobQueue.Instance.JobCompleted` for in-session completion (best-effort — does not survive restart), or query `GetStatus(jobId)` on boot to check whether a previously-enqueued job is `Pending`, has `Failed`, or is `NotFound`. **`NotFound` is ambiguous** — it means *either* the job already succeeded (success records aren't kept), *or* it was never enqueued. Callers that need to distinguish these should write a sentinel to PlayerPrefs at enqueue time and clear it from `JobCompleted`.

**Use `JobQueue.GetOrCreate()` over `JobQueue.Instance`** for any caller that initiates jobs. `Instance` returns null if no `JobServices` GameObject is in the scene (and the documented scene layout below is aspirational — the queue is normally auto-spawned, not pre-wired). `GetOrCreate()` lazily creates one with `DontDestroyOnLoad`, so the user is never blocked by a missing scene component. Read-only callers (status queries, event subscriptions) can use `Instance` directly since they have nothing useful to do without an existing queue.

**Persistence shape**: one file per job at `Application.persistentDataPath/jobs/<jobid>.json`; permanent failures move to `jobs/failed/<jobid>.json` for debugging instead of being deleted. One-file-per-job (vs. one big queue file) gives atomic per-job lifecycle and avoids rewrite-everything contention. Writes go through `<file>.tmp` + `File.Move` so a crash mid-write leaves either the old file or the new file, never a half-written one. **`JsonUtility` can't deserialize polymorphically**, so each job is wrapped in a `JobEnvelope` with a `type` discriminator and a `data` field containing the type-specific JSON as a nested string — that's why every concrete job overrides `SerializeData`/`DeserializeData` for its own fields. No Newtonsoft dependency.

**Backoff** ([JobQueue.cs:29-36](src/app/MergulhoVirtual/Assets/Scripts/Jobs/JobQueue.cs#L29-L36)): 5s → 30s → 2min → 10min → 1h, capped at 1h. `TransientFailure` (5xx, 408, 429, `ConnectionError`) reschedules; `PermanentFailure` (other 4xx) moves to `failed/`. Network-required jobs (`Job.RequiresNetwork == true`, the default) are also gated on `Application.internetReachability` — if offline, they're skipped on each tick until reachability returns, regardless of backoff timing.

**Lifecycle gotchas worth knowing before extending**:

- **Custom job types must be registered before disk-load**. Production calls `EnsureInitialized()` then `LoadPendingFromDisk()` from `Start`, so `JobQueue.Instance.RegisterType<MyJob>()` should run earlier — from another component's `Awake`, or via a manual `LoadPendingFromDisk()` re-call after late registration. Otherwise old `MyJob`-typed files on disk get skipped with a `LogError` ("unknown job type"). The simplest registration site is right inside `JobQueue.EnsureInitialized()` next to `HttpPostJob`/`FileDownloadJob`/`ReportSightingJob` — that runs before disk-load no matter who triggers it.
- **Lazy initialization is intentional**. `EnsureInitialized()` runs from `Awake` *and* from any public entry (`Enqueue`, `GetStatus`, `RunOnceForTests`), guarded by an `initialized` flag. The lazy path exists because Unity EditMode tests don't reliably fire `Awake` on `AddComponent` — production never relies on it, but it makes the queue robust to that.
- **`FileDownloadJob` does NOT HTTP-resume on retry** — each retry restarts from byte 0 and overwrites the `.partial` file. Fine for small images; if you start downloading large videos, add a `Range: bytes=N-` header + `DownloadHandlerFile(append: true)` and verify the CDN returns `206 Partial Content` (servers that return `200` instead would silently corrupt the file).
- **No success records**. The queue deletes the job file on success and emits `JobCompleted`. Once `JobCompleted` fires, the only place that knew about the job is gone. If you need durable "did this ever succeed?" semantics, persist a flag yourself.
- **Concurrency = 1**. The loop runs one job at a time. If you add a slow `FileDownloadJob`, it blocks subsequent `HttpPostJob`s behind it. Adding per-type concurrency is a future change, not a current footgun — just be aware.

**Adding a new job type** — minimal skeleton:
```csharp
public class MyJob : Job
{
    public string SomeField;
    public override string Type => "MyJob";

    [Serializable] private struct Data { public string someField; }
    protected internal override string SerializeData() =>
        JsonUtility.ToJson(new Data { someField = SomeField });
    protected internal override void DeserializeData(string data) =>
        SomeField = JsonUtility.FromJson<Data>(data).someField;

    public override IEnumerator Execute(Action<JobResult> setResult)
    {
        // do work; setResult(Success | TransientFailure | PermanentFailure)
        yield break;
    }
}

// Wire up once at startup, before LoadPendingFromDisk runs.
// Easiest: add it right inside JobQueue.EnsureInitialized().
JobQueue.GetOrCreate().RegisterType<MyJob>();
```

**Tests** ([JobQueueTests.cs](src/app/MergulhoVirtual/Assets/Scripts/Tests/Editor/JobQueueTests.cs), [JobSerializationTests.cs](src/app/MergulhoVirtual/Assets/Scripts/Tests/Editor/JobSerializationTests.cs)) use three internal seams exposed via [Assets/Scripts/AssemblyInfo.cs](src/app/MergulhoVirtual/Assets/Scripts/AssemblyInfo.cs) (`InternalsVisibleTo("Assembly-CSharp-Editor")`): `JobQueue.TestRootOverride` redirects storage to a temp dir per test, `IsOnlineOverride` swaps the connectivity check, and `RunOnceForTests()` runs one due job synchronously (the production `RunLoop` uses `WaitForSeconds`, which doesn't tick reliably in EditMode). When adding tests for a new job type, follow the `TestJob` pattern at the bottom of `JobQueueTests.cs` — it bypasses real HTTP and lets the test set the result directly.

### UI and screen navigation — [ScreenManager.cs](src/app/MergulhoVirtual/Assets/Scripts/UI/ScreenManager.cs), [SplashScreen.cs](src/app/MergulhoVirtual/Assets/Scripts/UI/SplashScreen.cs)

The app uses a **single scene with UI panels**, not multiple scenes. Navigation is done by `SetActive` on child GameObjects under one root `Canvas` (`ScreenUI`) — **never** `SceneManager.LoadScene`. Reason: AR session startup, camera permission, GPS fix, and the ONNX worker + GPU tensor allocations in [CameraFeedToInference.cs](src/app/MergulhoVirtual/Assets/Scripts/CameraFeedToInference.cs) are expensive and visibly slow on Android; reloading the scene to "go back to Main" restarts all of them. Keep AR/sensors/inference components at scene root — only UI panels live under the Canvas.

Scene layout — the actual top-level GameObjects sit flat at the scene root (there is **no** `Systems` parent; each concern is its own root GameObject):
```
MainScene (root)
├── AR Session, XR Origin            (AR foundation — persistent, never disabled)
├── Directional Light, EventSystem   (Unity base)
├── LocationServices                 → GPSHandler
├── ComputerVisionServices           → CameraFeedToInference
├── BackendServices                  → BackendServices
├── ScreenManager                    → ScreenManager
├── Beach Shark Spawner              → BeachSharkSpawner (instantiates prefabs per beach)
├── AnimalViewerRig                  (3D playground rig used by AnimalsScreen detail; built by Tools > Mergulho Virtual > Create Animals Screen; lives at world (5000,0,0) far from AR origin)
├── ARStabilization                  → StillnessDetector + SpuriousMotionGate + GnssProvider + GpsArKalmanFusion + ArStabilizationController (beach AR drift mitigation; built by Tools > Mergulho Virtual > Setup AR Stabilization)
└── ScreenUI (Canvas)
    ├── SplashScreen      (static Image; auto-advances to MainScreen via SplashScreen.cs)
    ├── MainScreen        (AR HUD: classification text, place name, sighting count; also hosts ArTuning — the "AJUSTE AR" pill + field-tuning panel from the AR Stabilization builder)
    ├── BeachesScreen     (BeachesScreenController toggles list ↔ detail)
    │   ├── BeachesList     (Header + BeachSelectorDropdown + ListScroll of beach cards)
    │   └── BeachesDetail   (per-beach detail; content wrapped in a ScrollRect — see UI layout rules)
    ├── AnimalsScreen     (AnimalsScreenController; list of AnimalDefs ↔ detail with hero 3D viewer + scrollable description + inline video cards; built by Tools > Mergulho Virtual > Create Animals Screen)
    ├── AboutScreen       (about copy in AboutScroll ScrollRect + InstagramSection latest-post card; card built by Tools > Mergulho Virtual > Create Instagram Widget)
    ├── RegisterScreen    (RegisterScreenController; photo card + beach dropdown + when/notes + submit; built by Tools > Mergulho Virtual > Create Register Screen)
    └── BottomNav         (persistent nav bar — AR / Beaches / Animals / Report / About buttons)
```

`JobQueue` is **not** in the scene tree — `JobQueue.GetOrCreate()` auto-spawns a `JobServices` GameObject with `DontDestroyOnLoad` on first call. If you ever pre-place one in the scene anyway (e.g. to set Inspector tunables like `maxQueueSize`), the singleton check in `Awake` still works — but don't have both.

Current screens are Splash, Main, Beaches, Animals, About, Register. `ScreenManager.Show(target)` enables exactly one screen, disables the others, and toggles `BottomNav` (visible on every screen except Splash). Navigation is wired Inspector-side: the five UI Buttons inside `BottomNav` bind directly to `ScreenManager.ShowMain()` / `ShowBeaches()` / `ShowAnimals()` / `ShowRegister()` / `ShowAbout()` — there is no router or per-screen controller. To add a new screen: add a `GameObject` field on `ScreenManager`, a `ShowX()` method, and extend the `Show()` body with one more `SetActive` line; create a child under `ScreenUI`; if it should be top-level navigable, add a button to `BottomNav` and wire its `OnClick` to `ShowX()`.

**`BottomNav` must be a sibling of the screens, not a child of any one of them** — if it's nested under e.g. `MainScreen`, it inherits that screen's active state instead of being controlled by `ScreenManager`, and starts showing/hiding with the wrong screen (including being visible during Splash because `MainScreen` happened to be active in the saved scene). The bar uses [background_crop.png](src/app/MergulhoVirtual/Assets/Images/UI/background_crop.png) as a 9-slice sprite (Sliced Image Type + non-zero borders set in the sprite's import settings) so it scales horizontally without distorting the rounded corners. The five button sprites — [buttons_ar.png](src/app/MergulhoVirtual/Assets/Images/UI/buttons_ar.png), [buttons_beaches.png](src/app/MergulhoVirtual/Assets/Images/UI/buttons_beaches.png), [buttons_animals.png](src/app/MergulhoVirtual/Assets/Images/UI/buttons_animals.png), [ui_register.png](src/app/MergulhoVirtual/Assets/Images/UI/ui_register.png), [buttons_about.png](src/app/MergulhoVirtual/Assets/Images/UI/buttons_about.png) — bake the icon and label into the image (except `ui_register.png` which is icon-only), so each Button is just an `Image` (no child `Text (TMP)`).

**Inference gate**: `ScreenManager.Show()` also sets `CameraFeedToInference.inferenceEnabled` based on whether the main screen is visible, so tensor work is paused while Splash/Beaches/Animals/About/Register are up ([CameraFeedToInference.cs:113-116](src/app/MergulhoVirtual/Assets/Scripts/CameraFeedToInference.cs#L113-L116)). Any new screen that fully covers the AR camera should remain subject to this pattern — don't bypass it by toggling screens outside `ScreenManager`.

**Performance gate**: `ScreenManager` also pauses AR and unlocks the render rate on non-AR screens, gated by the `Use Optimized Performance` Inspector bool on `ScreenManager` (default on; uncheck for the legacy "AR + 30 fps everywhere" behavior). When on: on Beaches/Animals/About/Register, `arSession.enabled = false` (camera + tracker stop — big battery win, sub-second resume on return; no anchors used here, so no re-localization concerns), `arSession.matchFrameRateRequested = false`, and `Application.targetFrameRate = displayRefreshRate` so UI scrolls at the panel's native rate (90/120 Hz) instead of being clamped to the camera's 30 fps. On Splash + Main, AR stays enabled and target rate is 30 — **Splash deliberately keeps AR alive** so camera/tracker init hides behind the splash image (the same rationale that drives the single-scene architecture; if you exclude Splash from `arShouldRun`, AR re-init becomes user-visible on first MainScreen entry). `QualitySettings.vSyncCount = 0` is set once in `Start()` because `Application.targetFrameRate` is silently ignored on some Android targets when vSync is on.

**UI framework: migrating uGUI → UI Toolkit (decided 2026-09-14).** The existing uGUI + TextMeshPro screens are throwaway debug UI; a designer is producing a Material Design 3 look on top of the new **design system** at [Assets/DesignSystem/](src/app/MergulhoVirtual/Assets/DesignSystem/) (see **Design system** section below). Migration follows the strangler pattern per [design-system-implementation-plan.md](design-system-implementation-plan.md) — old uGUI screens stay live until each new UI Toolkit screen reaches parity; never a big-bang cutover. Until then the uGUI rules in this file still apply to the legacy screens.

## Working in this codebase

- **Resources loading**: the code deliberately uses `Resources.Load<TextAsset>("name")` (no extension) for `class_desc`, `places`, etc. Anything referenced this way must live under `Assets/Resources/` — moving it breaks runtime loading silently (you get a `null` TextAsset and a `LogError`, not a compile error).
- **Unity 6 + URP + AR Foundation 6**: several APIs used here (`Worker`, `TextureConverter.ToTensor`, `ARCameraFrameEventArgs`) are recent. When searching docs, pin to Unity 6 / URP 17 / AR Foundation 6 — older Barracuda/Sentis and AR Foundation 4.x examples will mislead you. Specific footgun: in AR Foundation 6 the frame-rate-matching property is `arSession.matchFrameRateRequested` (settable bool) with `matchFrameRateEnabled` as the read-only "what the subsystem actually negotiated" companion — **not** `matchFrameRate`. Older docs and forum posts mention `matchFrameRate`; that name compiles in AR Foundation 4 but errors in 5/6 with `'ARSession' does not contain a definition for 'matchFrameRate'`.
- **Prefabs and scene references**: `MonoBehaviour` fields are wired via the Inspector in `MainScene.unity`/prefabs. Renaming a serialized field (e.g. `classificationResultText`) without a `[FormerlySerializedAs]` attribute will silently drop its Inspector reference.
- **`AspectCover` doesn't observe sprite changes**: it computes the aspect ratio in `OnEnable` (which fires before `Bind` on a freshly instantiated prefab) and never reruns. If you swap an `Image.sprite` at runtime on a component with `AspectCover` (data-driven list items, e.g. [BeachesScreenController](src/app/MergulhoVirtual/Assets/Scripts/UI/BeachesScreenController.cs) populating photo cards via [ListItemView.Bind](src/app/MergulhoVirtual/Assets/Scripts/UI/ListItemView.cs)), call `AspectCover.Refresh()` after the assignment — otherwise the `AspectRatioFitter` keeps its design-time ratio and the photo renders the wrong shape (too tall, too wide, or letterboxed inside the mask).
- **Generated files**: `Library/`, `Temp/`, `Logs/`, `UserSettings/`, and all `*.csproj`/`*.sln` under the Unity project are generated by the editor — do not edit or commit them. The `.gitignore` already excludes them.
- **Tide JSON is built from the official DHN tide table** (Brazilian Navy), not a numerical model. Source-of-truth PDF: [src/app/MergulhoVirtual/tabua_mare_noronha_2026_marinha.pdf](src/app/MergulhoVirtual/tabua_mare_noronha_2026_marinha.pdf), printed annually by Marinha do Brasil for *Arquipélago de Fernando de Noronha (Baía de Santo Antônio)*, station Carta 52. Two-step pipeline:
  1. [tools/parse_dhn_tide_table.py](tools/parse_dhn_tide_table.py): PDF → `tools/dhn_tides_2026.json` (3–4 official extrema per day; 365-day coverage; uses `pdftotext -layout` to slice the 8 sub-columns per page; self-validates day count).
  2. [tools/build_app_tides.py](tools/build_app_tides.py): events JSON → [Assets/Resources/tides_noronha.json](src/app/MergulhoVirtual/Assets/Resources/tides_noronha.json), which carries **two** parallel views of the data: `heights_m` (8760 hourly samples for the year, cosine-interpolated between adjacent extrema, mirror-extrapolated one half-cycle on each year boundary) for the smooth sparkline curve, and `events` (1410 `{utc, h, k}` records, the literal DHN extrema in UTC) for the next-high/next-low display in `ConditionsCard`. [TideService.cs](src/app/MergulhoVirtual/Assets/Scripts/TideService.cs) reads `events` to preserve the published HH:MM peak times exactly — the hourly grid alone can only resolve peaks to HH:00, which doesn't match the navy table when read by a local user (e.g. DHN says low at 12:40 0.42 m; the hourly scan would have said 12:00 0.40 m).
- **Heights are LAT-referenced** (chart datum / lowest astronomical tide) — same convention as the DHN printed table, so values are essentially all positive (rare extreme spring tides print as `-0.03 m` in the official table itself; cosine interp passes through them). The PDF header prints `Nível Médio 1.28 m` — that IS the MSL→LAT offset for this station, drawn as a dashed horizontal reference in [TideSparkline.cs](src/app/MergulhoVirtual/Assets/Scripts/UI/TideSparkline.cs) (`NivelMedioM = 1.28f`). Locals reading "2.2 m próxima alta" in `ConditionsCard` will match the navy table exactly.
- **Tide accuracy**: cross-checked across all **939 official DHN tide events** in the May–Dec 2026 prediction window when this was still pyTMD/GOT5.6: mean abs error 37 mm, p95 79 mm, max 118 mm. Now that the app is fed the DHN table directly, app-vs-DHN error is just hourly-sampling residual (mean 17 mm, max 39 mm) and there's no model to disagree with. The pyTMD path remains in the repo as a fallback for when no DHN PDF is available for a year.
- **Regenerating tides for a new year**: download the next year's PDF from Marinha do Brasil, drop it under `src/app/MergulhoVirtual/`, then `./tools/parse_dhn_tide_table.py <pdf>` followed by `./tools/build_app_tides.py`. No venv needed — only `pdftotext` (poppler-utils) plus the Python stdlib. Update `YEAR` at the top of [tools/parse_dhn_tide_table.py](tools/parse_dhn_tide_table.py) when crossing into the new calendar year. The legacy pyTMD generator is still at [tools/generate_tides.py](tools/generate_tides.py) (needs venv + ~250 MB GOT5.6 model download — see its SETUP block); only reach for it if no DHN table is available, and remember it produces MSL-referenced heights, not LAT.
- **External UPM packages (besides Unity's own)**: `com.yasirkula.nativegallery` (git URL in [Packages/manifest.json](src/app/MergulhoVirtual/Packages/manifest.json)) is required for the gallery picker. First import needs internet; after that it resolves from the package cache. iOS additionally needs `NSPhotoLibraryUsageDescription` set in Project Settings → Player → iOS → Other Settings (NativeGallery prints a warning otherwise). Don't reach directly for `NativeGallery.*` from new code — go through [GalleryPicker.cs](src/app/MergulhoVirtual/Assets/Scripts/Photo/GalleryPicker.cs) so the Editor fallback path stays uniform.

## Design system — UI Toolkit + Material Design 3 ([Assets/DesignSystem/](src/app/MergulhoVirtual/Assets/DesignSystem/))

The M3 component library replacing the debug uGUI screens (plan: [design-system-implementation-plan.md](design-system-implementation-plan.md); status 2026-09-14: Phase 1 foundations + **ALL Phase 2 components #1–#13 built** (MdButton/MdIconButton/MdChip/MdFab/MdProgressIndicator [MdLinearProgress + MdCircularProgress]/MdCard/MdListItem/MdTextField/MdMenu + MdDropdown/MdTopAppBar [static, no collapse-on-scroll yet]/MdNavigationBar/MdDialog + MdSnackbar/MdBottomSheet [modal; drag-to-dismiss deferred, handle is visual-only]/MdSparkline) + gallery + tests; **all suites green** (headless `make ds-test` 83/83 EditMode + `make ds-test-play` 16/16 PlayMode, 2026-09-14). The **overlay layer** (`MdOverlay.EnsureLayer`, in [MdMenu.cs](src/app/MergulhoVirtual/Assets/DesignSystem/Components/MdMenu/MdMenu.cs)) is the shared popup surface — MdDialog, MdSnackbar, and MdBottomSheet all reuse it. MdBottomSheet is a code-opened one-shot (`MdBottomSheet.Open(anchor)` returns the sheet; add content to it — children route into the content slot via a `contentContainer` override, below the drag handle); scrim tap closes (only `Closed` fires), taps on the sheet surface don't (pickable sheet inside a hit-transparent bottom-docking holder). MdDialog/MdSnackbar are code-opened one-shots (`MdDialog.Open(anchor, …)` / `MdSnackbar.Show(anchor, …)`, any attached element works as context), but the elements are also constructible standalone for EditMode structure tests; snackbar auto-dismisses (default 4 s, 0 = sticky) and a new `Show` replaces the current one. PlayMode-test footgun their tests hit: the themeless test panel's docRoot is width×0, so the overlay/holder chain has DEFINITE height 0 and Yoga's default `flex-shrink: 1` collapses even explicit child heights to 0 — popup-content tests must set `flexShrink = 0` down the chain (MdMenu never hit this because its popup is `position: absolute`, out of flex flow). MdNavigationBar destinations are set from code via `SetDestinations` (MdDropdown.SetChoices pattern, no UXML items); MdTopAppBar keeps its title in BOTH an in-row label (small/center-aligned) and a second-line headline label (medium/large) with USS picking which is visible, so variant switches never reparent. MdSparkline replaces the uGUI TideSparkline in Phase 3: data-driven (`SetSamples(hourlyHeights)` + `Baseline` [NaN = hidden; e.g. Nível Médio 1.28] + `ExtremumLabelFormatter` `(sampleIndex, isHigh) → text`), Catmull-Rom-smoothed Painter2D drawing where every color concern (fill/line/baseline/high dots/low dots) is its own internal layer element painted with its USS-resolved `color` — the MdCircularProgress.Arc pattern, so TokenDiscipline holds even for canvas drawing. Phase 3 has started with the **Beaches screen as the first strangler slice — see "Phase 3 — UI layer" below**; App UI spike pending device verdict — see [docs/appui-spike.md](docs/appui-spike.md)).

- **Isolation is compile-time enforced**: `MergulhoVirtual.DesignSystem.asmdef` cannot reference `Assembly-CSharp`, AR, or Firebase (Unity's asmdef graph only flows the other way). Gallery/Tests/Editor asmdefs sit alongside; a future `MergulhoVirtual.UI` asmdef (screens + ViewModels) comes in Phase 3.
- **Everything visual is a token; everything is regenerable.** The designer will restyle the whole app, so components may only consume `var(--md-*)` variables — never literal colors/sizes. `Tokens/_colors-*.uss` are GENERATED (`make ds-tokens`, seed/Theme-Builder-export → full M3 scheme via [tools/design_system/generate_md3_tokens.py](tools/design_system/generate_md3_tokens.py)); `_typography/_shape/_state/_motion.uss` are hand-maintained. `Theme-Light.tss`/`Theme-Dark.tss` bundle tokens + per-component USS; theme switch = swap `PanelSettings.themeStyleSheet`. **`TokenDisciplineTests` fails if a component USS hard-codes a color, uses an undefined token, or the two themes drift.**
- **Component contract** (reference implementation: [MdButton.cs](src/app/MergulhoVirtual/Assets/DesignSystem/Components/MdButton/MdButton.cs)): `[UxmlElement]` partial class; data in via `[UxmlAttribute]` properties, events out via C# events; root element = ≥48dp touch target, visible `__container` child holds a first-child `.md-state-layer` (base class in `_state.uss`) driven by `:hover/:active/:focus` opacities; BEM classes (`md-button--filled`); own `.uss` imported by BOTH theme files (keep the two import lists in sync — tested). A component ships code + USS + gallery entry + tests or it isn't done.
- **Icons**: `MdIcon` renders Material Symbols by name from a subset font (10.7 MB → ~13 KB). Adding an icon = add the name to [tools/design_system/material_symbols_icons.txt](tools/design_system/material_symbols_icons.txt) → `make ds-icons` → `make ds-setup`. Unknown names render empty + editor warning.
- **dp calibration**: PanelSettings uses Constant Physical Size @ 160 dpi so **1 USS px = 1 dp** — transcribe M3 spec values literally.
- **UI Toolkit limitations accepted**: no `line-height` (type scale is size/tracking only), no `box-shadow` (elevation = surface-container colors, as M3 dark theme does), no `cubic-bezier()` (nearest named easings, see `_motion.uss`), font weight = separate font asset (Roboto-Medium).
- **Headless workflow — minimize editor GUI** (editor must be CLOSED for `ds-setup/ds-test/ds-compile`): `make ds-tokens` / `ds-icons` (Python, editor may stay open) · `make ds-setup` (FontAssets + PanelSettings + GalleryScene via `-executeMethod`, also under Tools > Mergulho Virtual > Design System) · `make ds-test` / `ds-test-play` / `ds-compile`. FontAssets are TextCore dynamic SDF generated from the TTFs — regenerate after re-subsetting icons.
- **The gallery is the debugging weapon**: [GalleryScene.unity](src/app/MergulhoVirtual/Assets/DesignSystem/Gallery/) shows every component/variant/state with live light/dark toggle + resolved-token swatches. Reproduce UI bugs there first; if it reproduces it's a component bug (fix + regression test), if not it's screen wiring. One `Build*Section` method per component in [GalleryController.cs](src/app/MergulhoVirtual/Assets/DesignSystem/Gallery/GalleryController.cs).

### Phase 3 — UI layer ([Assets/UI/](src/app/MergulhoVirtual/Assets/UI/)) and the Beaches strangler slice

The first Phase 3 screen is live in the scene (2026-09-14): **Beaches, rebuilt on UI Toolkit** as `MergulhoVirtual.UI.asmdef` (references DesignSystem only; Assembly-CSharp auto-references it back, which is also why [MoonPhase.cs](src/app/MergulhoVirtual/Assets/UI/Domain/MoonPhase.cs) could move here from `Assets/Scripts/` with no caller changes). Layering per plan:

- **Interfaces** ([Assets/UI/Interfaces/](src/app/MergulhoVirtual/Assets/UI/Interfaces/)): `IBeachCatalog`/`IConditionsService`/`ITideService`/`IBeachOverride` + engine-free mirrors `BeachInfo`/`ConditionsData`/`TideData` of the Assembly-CSharp snapshot types. The mapping lives ONLY in [UiServiceAdapters.cs](src/app/MergulhoVirtual/Assets/Scripts/UI/UiToolkit/UiServiceAdapters.cs) (Assembly-CSharp), which wraps `ConditionsService`/`TideService`/`GPSHandler`/`ReverseGeocoding`.
- **ViewModel** ([BeachesViewModel.cs](src/app/MergulhoVirtual/Assets/UI/ViewModels/BeachesViewModel.cs)): plain C#, no UnityEngine; holds list↔detail state, the override-dropdown state, and ALL pt-BR row formatting ported string-for-string from the legacy `ConditionsCardView` (wave/tide/moon/wind/water/freshness + sparkline extremum labels). Injectable `utcNow`/`toLocalTime` keep tests deterministic/timezone-proof. 15 EditMode tests in [Assets/UI/Tests/Editor/](src/app/MergulhoVirtual/Assets/UI/Tests/Editor/) run in `make ds-test` (assembly `MergulhoVirtual.UI.Tests.Editor` is in the Makefile's `-assemblyNames`).
- **Screen** ([BeachesScreen.cs](src/app/MergulhoVirtual/Assets/UI/Screens/BeachesScreen.cs)): built in code (no UXML — the components' data APIs `SetChoices`/`SetLeadingImage`/`SetSamples` are code-only). Composes MdTopAppBar (Medium) + MdDropdown (beach override, replaces `BeachSelectorDropdown`) + full-width **photo cards** for the list (M3 card-with-media: 140dp cover via `Image` ScaleAndCrop, title + one-line ellipsized teaser, `.md-state-layer` press tint driven by screen USS, `Clickable` manipulator; beaches without a photo get a token-colored placeholder with an MdIcon "waves" so every card keeps the same shape) + MdCard conditions rows + MdSparkline (Baseline 1.28, VM-formatted labels). [BeachesScreen.uss](src/app/MergulhoVirtual/Assets/UI/Screens/BeachesScreen.uss) is token-only — **`TokenDisciplineTests` now scans `Assets/UI/**/*.uss` too**. Edge insets are handled by the host, NOT by `SafeAreaElement` (deliberate deviation): top/left/right safe-area insets are applied as *padding on the opaque content container* so its surface paints under the status bar/notch (SafeAreaElement would leave that strip transparent → raw camera feed), and it also works in the Device Simulator (mocked `Screen.safeArea`; SafeAreaElement skips editor panels). The root stays transparent + `PickingMode.Ignore` with a measured bottom inset so the uGUI BottomNav stays visible/tappable whichever of uGUI/UITK draws on top.
- **Host + strangler wiring**: [BeachesUiToolkitHost.cs](src/app/MergulhoVirtual/Assets/Scripts/UI/UiToolkit/BeachesUiToolkitHost.cs) (Assembly-CSharp composition root on the scene-root `BeachesScreenUITK` GameObject, next to a `UIDocument` using [Assets/UI/AppPanelSettings.asset](src/app/MergulhoVirtual/Assets/UI/) — same 160-dpi dp calibration as the DS asset but `themeStyleSheet = Theme-Dark`, kept separate so the gallery default stays Light). It rebuilds the visual tree every `OnEnable` (UIDocument tears it down on deactivate) but keeps the VM/adapters alive across toggles. `ScreenManager` gained `beachesScreenUiToolkit` + `useUiToolkitBeaches` (default ON): `ShowBeaches()` routes to whichever screen the toggle picks and `Show()` deactivates the other — **untick `Use Ui Toolkit Beaches` on the `ScreenManager` GameObject to fall back to the legacy uGUI screen**. The legacy `BeachesScreen` uGUI object stays in the scene untouched until parity is declared.
- **Scaffolder**: `Tools > Mergulho Virtual > Create Beaches Screen (UI Toolkit)` ([BeachesUiScreenBuilder.cs](src/app/MergulhoVirtual/Assets/Editor/BeachesUiScreenBuilder.cs)), headless via **`make ui-beaches-setup`** (editor closed). Idempotent: re-creates/rewires `BeachesScreenUITK` + `AppPanelSettings` + the ScreenManager fields in place.
- **Known behavior kept for parity**: the conditions card follows the GPS/override-active beach, not the browsed detail beach (same as legacy) — the card title now says so explicitly (`Condições · <beach>` when they differ). Next screens per plan: Animals/Register + `MdRouter`/`MdNavigationBar` once more than one UITK screen exists.

## UI layout rules

Most of the friction in the Beaches/Animals screen rollout came from getting Layout Group settings wrong, then chasing visible symptoms (white squares, narrow rows, hidden BottomNav) instead of the underlying layout cause. Internalize these defaults before designing or editing any screen under `ScreenUI`.

### Default Vertical/Horizontal Layout Group settings

Unless you have a specific reason to deviate, every Layout Group on a content panel — and the Horizontal Layout Group inside a list-item prefab — wants:

- **Control Child Size: Width ✓ Height ✓**. Without Height, every child's `LayoutElement.PreferredHeight` is silently ignored — the layout group only positions children, doesn't size them. Symptom: "I set `PreferredHeight = 300` but the banner still renders at 100." Without Width, same thing for `PreferredWidth`.
- **Child Force Expand: Width ✓ Height ✗**. Width force-expand is what makes children with no explicit `PreferredWidth` stretch to fill the column; without it, list rows shrink to their natural content width and look "squished." Height should *not* force-expand because a single child is meant to absorb leftover vertical space (see next bullet) — force-expanding height pads every child instead.
- Designate **exactly one child with `LayoutElement.FlexibleHeight = 1`** to absorb remaining vertical space — typically the `ScrollView` in a list panel, or `DescriptionText` in a detail panel. Other siblings declare a fixed `PreferredHeight`.

### LayoutElement is not auto-attached

`UI > Image`, `UI > Text - TextMeshPro`, `UI > Button`, etc. add their primary component plus the basics, but **never** add a `LayoutElement`. To set Preferred/Min/Flexible sizes, **Add Component → Layout Element** explicitly. Editor-new contributors look for the fields on the Image or RectTransform itself, don't find them, and assume the instructions are wrong — flag this proactively when writing Editor walkthroughs.

### Sibling order under `ScreenUI` is render order

uGUI renders later siblings on top of earlier ones, and raycasts hit later siblings first. Implications that recur in this project:

- **`BottomNav` must be the *last* child of `ScreenUI`** — otherwise any full-stretch screen panel later in the sibling list draws on top of it, hiding the nav and blocking taps. `ScreenManager.Show()` already toggles `bottomNav.SetActive()` correctly; that doesn't help if BottomNav is below the active screen panel in the sibling list. Failure mode: tap "Beaches", reach the list, can't navigate anywhere.
- **Detail panels are siblings of their list panels** under each screen, both toggled inactive/active by the screen controller (`BeachesScreenController` etc.). Nesting the detail inside the list means hiding the list also hides the detail.
- **Screen container `Image` keeps `Raycast Target = on` deliberately** to block taps from bleeding through to whatever's underneath. Don't try to "fix" hidden BottomNav by moving it under a screen — fix the sibling order instead.

### Don't stretch-full anchor children of a layout group

Children of a Layout Group should have **non-stretch anchors** (top-center or top-left — first row of the anchor preset selector, *no Alt-click*). Stretch-full anchors `(0,0)/(1,1)` override the layout group's positioning and make the child fill its entire parent — the canonical "white square covers everything" bug. Stretch-full is right for screen *containers* (`BeachesScreen`, `AnimalsScreen`) directly under the Canvas, and wrong for any content child inside them.

### Diagnosing "white square covers the layout"

A bare `Image` with `Source Image = None` renders as a white rectangle the size of its `RectTransform`. When one is covering other elements, the cause is almost always one of:

1. **Stretch-full anchors** on the Image's RectTransform — see above.
2. **Layout group not controlling height** — the element is rendering at default 100×100 instead of its declared `PreferredHeight`.
3. **Sibling order** — the Image is a later sibling of the text/element it's covering.

Confirm by reading the saved scene file: `grep m_Name Assets/Scenes/MainScene.unity`, locate the offender, and read its `RectTransform` (anchors, sizeDelta) and adjacent `LayoutElement` block. Unity does not write to disk until Ctrl+S — when the user reports "X is missing/broken", ask them to save first. A stale `.unity` file is misleading; a freshly-saved one is source-of-truth and overrides screenshots.

### Tall detail panels need a ScrollRect

A detail panel with a fixed-height stack (banner + headings + multi-row text + chart) easily overflows the available height in landscape. Without a `ScrollRect`, the parent `VerticalLayoutGroup` shrinks children below their preferred height, and any TMP with `Overflow Mode = Overflow` spills onto the next sibling — that's how `ConditionsCard`'s "Atualizado: ..." freshness line ended up rendering on top of `TideSparkLine` before `BeachesDetail` was wrapped.

Pattern (used in `BeachesDetail`):
```
BeachesDetail (Image bg only — no layout group, no ContentSizeFitter)
└── Scroll View (ScrollRect, vertical only; full-stretch with bottom = BottomNav clearance)
    └── Viewport (Mask)
        └── Content (VerticalLayoutGroup + ContentSizeFitter Vertical=Preferred)
            └── BannerImage / NameText / DescriptionText / ConditionsCard / TideSparkLine / BackButton
```

Notes that bit us building this:

- **Move the `VerticalLayoutGroup` off the screen container onto `Content`.** Leaving it on `BeachesDetail` while children move under `Content` makes the layout group try to lay out the lone Scroll View child and ignore everything below.
- **`Content` needs `ContentSizeFitter` with `Vertical Fit = Preferred Size`**, otherwise its height stays at the Viewport's height and there's nothing to scroll. The Vertical Layout Group on `Content` provides preferred height; the fitter applies it to the RectTransform.
- **Keep bottom clearance** (Scroll View's RectTransform `Bottom ≈ 220`) so the last item doesn't get covered by `BottomNav`.
- **Reset scroll position on entry**: `BeachesScreenController.ShowDetail()` sets `detailScroll.verticalNormalizedPosition = 1f` — without this, re-entering a detail starts at wherever you last scrolled.
- **`Image.preserveAspect` for runtime-assigned sprites**: when an `Image`'s `Source Image` is left empty in the editor (because the sprite is loaded by code, e.g. `BeachesScreenController.detailImage`), the Inspector hides the `Preserve Aspect` toggle — but the underlying `m_PreserveAspect` field is still serialized. Set it from script (`detailImage.preserveAspect = true;`) next to the sprite assignment, not via the Inspector.

### Code-driven Editor scaffolders for new screens

For any non-trivial screen (multiple cards, ScrollRect, several rows of inputs), prefer a `Tools > Mergulho Virtual > Create <Screen>` Editor menu over hand-building in the Inspector. Reference implementations: [RegisterScreenBuilder.cs](src/app/MergulhoVirtual/Assets/Editor/RegisterScreenBuilder.cs) (form-style screen) and [AnimalsScreenBuilder.cs](src/app/MergulhoVirtual/Assets/Editor/AnimalsScreenBuilder.cs) (list ↔ detail with an out-of-canvas 3D rig sibling) — one menu click builds the entire screen GameObject hierarchy under `ScreenUI` with every UI rule above already applied (VLG settings, RectMask2D over `Mask`, sibling order, BottomNav clearance, RoundedRectCard material, ScrollRect→Viewport→Content), AND auto-wires the screen into `ScreenManager.<screen>` and the BottomNav button's `OnClick` via `SerializedObject` + `UnityEventTools.AddPersistentListener`. `AnimalsScreenBuilder` also shows the pattern for creating non-UI sibling GameObjects (the `AnimalViewerRig` at the scene root) inside the same scaffolder so they're created and wired in lockstep with the screen.

Why it's worth the upfront code:

- **Every UI rule is enforced once**, in code, instead of re-discovered per screen via the "white square covering everything" failure mode. New contributors crib from existing factory helpers (e.g. `MakeFieldCard`, `MakeStyledInput`, `MakeStyledDropdown` in `RegisterScreenBuilder`) instead of reinventing.
- **Design tokens are explicit.** Palette colors (`BgNavy`, `CardSurface`, `Accent`, …) and spacing constants live as `static readonly` fields at the top — change one value, re-run the menu, see it everywhere.
- **No Inspector dragging.** Auto-wiring eliminates the most common "I added a screen but the button does nothing" failure mode.
- **Idempotent re-runs.** Prompts to *Replace* if the screen already exists, so iterating on layout = edit code → re-run → save scene.

Limits — don't reach for this when:

- **You're going to hand-edit the screen in the Inspector afterwards.** Re-running the menu wipes those edits. Pick one path per screen: iterate entirely in code (re-run the menu) OR entirely in the Editor (don't re-run). Mixing alternately loses work — say so explicitly when handing the workflow to the user.
- **You need to wire references to assets the scaffolder can't infer** (Animator controllers, prefab references not loadable by a stable name). Either load via `Resources.Load`/`AssetDatabase.LoadAssetAtPath` from inside the scaffolder so the wiring is also reproducible, or accept that those slots need re-dragging after each re-run.
- **The screen is trivial** (one Image + one TMP). Splash-class screens don't need a scaffolder; the cost only pays off at ≥3 layout groups or ≥4 wired references.

When you do build a new scaffolder, follow `RegisterScreenBuilder`'s structure: low-level helpers (`NewUI`, `StretchFull`, `NewText`, `AddLayoutElement`) at the bottom, factory helpers (`MakeFieldCard` etc.) in the middle, the build sequence at the top under `[MenuItem]`. Keep the sequence comment-banner-separated by section so it reads top-to-bottom like a wireframe.

## TMP sprite assets and emoji icons

The pill UI ([ConditionsPillView.cs](src/app/MergulhoVirtual/Assets/Scripts/UI/ConditionsPillView.cs)) inlines emoji glyphs (🌊 wave, 🌑–🌘 moon phases) by emitting Unicode codepoints from C# and letting TMP route them through a sprite asset. This was painful to wire up the first time; the gotchas below are the ones that actually wasted hours, not the ones in TMP's docs.

### Wiring path

`TMP_Text` → font asset (`LiberationSans SDF`) finds the codepoint missing → falls through to **TMP Settings → Default Sprite Asset** (Project Settings → TextMesh Pro) → that asset's `m_SpriteCharacterTable` matches `m_Unicode` → renders the sprite. Custom sprite assets live alongside their source PNGs (e.g. [Assets/Images/UI/moon/moon_sheet.asset](src/app/MergulhoVirtual/Assets/Images/UI/moon/)). Chain additional sprite assets via the **active Sprite Asset's own `Fallback Sprite Assets` list** — *not* via the font asset's Fallback Font Assets list, which only accepts `TMP_FontAsset` (sprite assets are a different type and the slot rejects them).

### Sprite-asset gotchas (in order of nastiness)

- **`Create → TextMeshPro → Sprite Asset` on a multi-selection produces one `.asset` per source texture, not one bundled asset.** To get a single asset with N entries you need a single source texture (a sprite sheet) sliced into N sprites, then run Create on the sheet. Combining individual PNGs into a sheet is one ImageMagick line: `convert a.png b.png ... +append sheet.png`. Resize source PNGs to a sane size first (`-filter Lanczos -resize 128x128 -strip`) — OpenMoji ships at 618×618, which is 25× the texture memory you actually need for a UI pill.
- **All sprites in a single asset (or reachable via fallback) must share consistent dimensions.** TMP sizes glyphs by their `GlyphRect` width/height relative to the font's em — a 618-tall sprite chained next to a 128-tall sprite renders ~5× larger than the others. Resize sources, or hand-edit the metrics block to a uniform scale.
- **Slice pivot at sheet creation time bakes into the glyph's `m_HorizontalBearingX`.** Default sprite slice pivot is **Center**, which makes TMP set `BX = -width/2` — every emoji renders centered on the cursor and overlaps the previous character. Set **Pivot = Bottom Left** in the Sprite Editor *before* creating the Sprite Asset. If the asset already exists with bad bearings, **the `Update Sprite Asset` button does NOT recompute metrics from new pivots** — fix by editing the `.asset` YAML directly: `sed -i 's/m_HorizontalBearingX: -64/m_HorizontalBearingX: 0/g' path/to/asset.asset`.
- **Vertical alignment with text needs `m_HorizontalBearingY` ≈ 75% of sprite height.** With sprite height 128, BY=64 centers the sprite on the baseline (half hangs below text), BY=96 puts the bottom on the baseline and the top near cap-height. Same `sed` pattern works.
- **Unicode codepoints in `m_SpriteCharacterTable` are stored as DECIMAL, not hex** — `🌕` = `1F315` hex = **127765** decimal. Filenames like `1F315_color.png` do **not** auto-populate this column on Sprite Asset creation; either set each entry manually in the Inspector (it accepts hex there), or hand-edit the YAML and remember to convert.
- **The shipped `EmojiOne` sample sprite asset only maps 16 codepoints** — face emoji (smileys, hearts, frowns). It does NOT cover moons, waves, weather, food, or anything else. Don't recommend "test against EmojiOne first" — it'll miss almost every codepoint and the user wastes time chasing why a glyph "doesn't work."
- **`TMP_SpriteAsset` is not a `TMP_FontAsset`.** It will never appear in the Font dropdown on a `TMP_Text` component. Sprites flow in only via the fallback chain described above. New users hunting the Font dropdown for "EmojiOne" will conclude something's broken — flag this proactively.

### Source PNGs and content-creator pipeline

OpenMoji (https://openmoji.org, CC-BY-SA) is the chosen emoji set in this project. Filenames are `<HEX_CODEPOINT>_color.png`. Standard import workflow:

1. Download the codepoint PNGs to [Assets/Images/UI/moon/](src/app/MergulhoVirtual/Assets/Images/UI/moon/) (folder name is historical — extends to non-moon icons too).
2. `cd` there and resize: `convert *.png -filter Lanczos -resize 128x128 -strip <out>.png` (or per-file).
3. (Multi-emoji case) Combine into a sheet: `convert a.png b.png ... +append sheet.png` — order matters, append left to right; choose an order that's stable so future re-renders don't shuffle codepoints.
4. Unity import: Texture Type = **Sprite (2D and UI)**, Sprite Mode = **Single** (one PNG) or **Multiple** (sheet) → **Pivot = Bottom Left** → Apply.
5. Right-click → **Create → TextMeshPro → Sprite Asset**.
6. Open the asset → set the `Unicode` column on each `m_SpriteCharacterTable` entry (hex in the Inspector, decimal in the YAML).
7. Wire as TMP Default Sprite Asset OR add to an existing asset's Fallback Sprite Assets.

### Emitting emoji from C# code

Use C# `\U` escapes for the codepoint (`\U0001F315` for full moon — *uppercase* `U`, eight hex digits, since these are non-BMP). **Do not** rely on pasting literal emoji into Unity's Inspector text field — some Unity versions silently strip non-BMP codepoints when typed/pasted into the Inspector, even though the same characters render fine when set from script. If a Unicode emoji "doesn't render" but `<sprite=N>` works, suspect Inspector stripping before suspecting the sprite asset wiring.

## Rounded UI corners — use the SDF shader, not `Mask`

`UnityEngine.UI.Mask` clips via the GPU stencil buffer, which is binary (alpha threshold → in/out, no AA). When the masking sprite has rounded corners (e.g. [background_crop.png](src/app/MergulhoVirtual/Assets/Images/UI/background_crop.png)), children get a 1-pixel-wide stair-step boundary along every curve. Don't try to fix it by upscaling the mask sprite — the source is fine; the stencil test is the bottleneck. The Beaches list cards initially used `Mask` and produced visibly jagged photo corners; the project switched to a small custom shader instead.

The rounded-card pattern in this project (Beaches list cards, reusable for Animals etc.):

- **Shader**: [Assets/Shaders/UI/RoundedRect.shader](src/app/MergulhoVirtual/Assets/Shaders/UI/RoundedRect.shader). `UI/RoundedRect` — drop-in for `UI/Default` plus an SDF rounded-rect coverage with `fwidth`-based AA along the curve.
- **Material**: [Assets/Materials/UI/RoundedRectCard.mat](src/app/MergulhoVirtual/Assets/Materials/UI/RoundedRectCard.mat). One asset, shared across all cards (so they batch). The `_Radius` property sets the corner radius in canvas units.
- **`RectMask2D` on the card root** — non-negotiable. The shader rounds corners of the parent `RectMask2D`'s `_ClipRect`, **not** the Image's own mesh rect. That decoupling is what lets a photo Image with `AspectCover EnvelopeParent` (oversized to cover-fit the card) still produce rounded corners aligned to the card edges. Without `RectMask2D`, the shader falls back to UI/Default behavior — square corners — and the photo overflows past the card.
- **Apply the material to every visible Image** in the card (background fill, photo, gradient overlay). RectMask2D's clip applies via `_ClipRect`, but only Graphics that actually use the SDF shader get rounded — anything else inherits a binary rectangular clip via `UnityGet2DClipping` and produces square corners poking into the rounded region.

Reference structure — [Assets/Prefabs/UI/ListItem.prefab](src/app/MergulhoVirtual/Assets/Prefabs/UI/ListItem.prefab):
```
ListItem (RectMask2D + Image[dark navy fill, RoundedRectCard mat] + Button + LayoutElement + ListItemView)
├── Thumbnail        (Image[no sprite at design time, RoundedRectCard mat] + AspectRatioFitter + AspectCover)
├── GradientOverlay  (Image[card_gradient_bottom, RoundedRectCard mat], bottom-anchored 140 px)
└── Title            (TMP_Text)
```

Footguns this combination dodges that the previous `Mask` setup didn't:
- Stencil aliasing on curves (the original problem).
- Photo overflow when AspectCover oversizes the rect — RectMask2D rectangularly clips it cleanly (no jaggies on axis-aligned edges), and the SDF rounds the corners on top.
- Gradient corners poking out — applying the same material to the gradient means its bottom corners cleanly follow the card's rounded shape.
- **`Mask` + transparent Image clips everything to nothing.** A `Mask` component needs its `Graphic` (usually an Image) to render to the stencil buffer, but `CanvasRenderer.cullTransparentMesh = true` (the default) drops a fully-transparent mesh entirely → no stencil mark → all children inside the mask get clipped away. Symptom: a screen that's "just the background color, no children visible" right after wiring up a ScrollRect with a fresh Viewport. Fix: use `RectMask2D` (which clips by rect, no Graphic required) instead of `Mask`, or, if you must use `Mask`, give the Image a non-zero alpha (even `1/255` works) or set `cullTransparentMesh = false` on its CanvasRenderer.

Don't apply the material to TMP text — TMP has its own SDF font rendering with its own clip-rect handling, and swapping the material breaks the font.

## Adding a new shark species

End-to-end workflow from a Sketchfab download to a beach-spawned, animated, looping AR shark. Assumes the source is a typical Sketchfab FBX bundle (`source/<file>.fbx` + `textures/*.png`); the gotchas below are specific to that source — Sketchfab's FBX exports don't ship with import-friendly defaults for Unity 6 + URP.

### 1. Drop assets into the project

Mirror the existing layout — one folder per species, kebab-case name. `source/` and `textures/` are seeded by hand; `Materials/` is created by step 2's *Extract Materials* and stays alongside them:

```
Assets/Models/<kebab-name>/
├── source/<name>.fbx     ← rename to drop spaces and parens; asset paths need to stay clean
├── textures/*.png        ← rename to a clean Species_<MapType>Map.<ext> convention before import (drop Sketchfab's V10/4K/00000-suffixed names)
└── Materials/            ← populated by Extract Materials in step 2; don't pre-create
```

Three reference species in the project, all structurally identical: [great-hammerhead-shark](src/app/MergulhoVirtual/Assets/Models/great-hammerhead-shark/), [tiger-shark-galeocerdo-cuvier](src/app/MergulhoVirtual/Assets/Models/tiger-shark-galeocerdo-cuvier/), [lemon-shark](src/app/MergulhoVirtual/Assets/Models/lemon-shark/). Each has a matching prefab at [Assets/Prefabs/Sharks/<name>.prefab](src/app/MergulhoVirtual/Assets/Prefabs/Sharks/) and Animator Controller at [Assets/Animations/<Name>.controller](src/app/MergulhoVirtual/Assets/Animations/).

### 2. Fix FBX import settings (Sketchfab gotchas)

Click the FBX in the Project window. For each tab in the Inspector:

- **Rig tab → Animation Type**: defaults to **None** on Sketchfab FBXs, which means **no Animator component is added when you instantiate the prefab** — the GameObject in the Hierarchy will only have a Transform. Set to **Generic** → Apply. (If Generic still produces no Animator, the FBX may be transform-animated only — try **Legacy** as fallback, which adds an `Animation` component instead.)
- **Animation tab → Clips list → per-clip Loop Time**: defaults to **off**. The clip will play once then freeze. Tick **Loop Time** for each cycle that should loop, then Apply. There is **no Loop setting on the Animator Controller** — looping is entirely driven by this checkbox on the clip, so visually diffing two `.controller` files won't reveal the cause when one shark loops and another doesn't.
- **Materials tab → Extract Materials...**: extracts to a folder you choose (see "Linux folder picker quirk" below). Then for each extracted `.mat`, switch its **Shader** to **Universal Render Pipeline > Lit** — the default Standard shader renders magenta under URP. Wire textures: ColorMap → **Base Map**; NormalMap → **Normal Map** (click "Fix Now" when prompted to mark it as a normal map); MetalnessMap → **Metallic Map** with the Smoothness slider as a quick approximation, or pack metallic+inverted-roughness into one texture for a correct PBR result.
- **Model tab → Scale Factor**: defaults to **1.0**, which imports the FBX in its native units — for Sketchfab assets that varies wildly (millimeters, centimeters, "100×life-size"), and the three reference FBXs in this project originally spanned a ~50× inherent-size range. **Convention**: Scale Factor bakes the **real-world body length in meters**, prefab Transform scale stays at `(1, 1, 1)` always, and `1 Unity unit = 1 m` everywhere downstream. To find the right value, drop the FBX into the Hierarchy at the default Scale Factor `1.0`, then run **Tools → Mergulho Virtual → Print Shark Sizes** ([SharkSizeReporter.cs](src/app/MergulhoVirtual/Assets/Editor/SharkSizeReporter.cs)) — it prints native bounds and the exact Scale Factor to type back into the importer for each species' real-world target length, then re-run after Apply to confirm `✓ already at target`. Add new species to `TargetLengthsM` at the top of the script before running so it produces a target line for them.

### 3. Animator Controller

Animator Controllers live in [Assets/Animations/](src/app/MergulhoVirtual/Assets/Animations/) — one `.controller` per species, shared across the project (not colocated with the model folder). This keeps the Animations folder as a single browse target when comparing or copying state machines between species.

- Project window → [Assets/Animations/](src/app/MergulhoVirtual/Assets/Animations/) → right-click → **Create > Animator Controller**, name it after the species (existing files: `HammerheadShark.controller`, `TigerShark.controller`, `lemon_shark.controller` — naming has drifted between PascalCase and snake_case; pick whichever matches your prefab name and stop worrying about it).
- Double-click the controller → Animator window opens.
- Expand the FBX in the Project window (click its arrow to reveal nested clips) → drag the animation clip into the Animator window. Unity creates a default state auto-connected from `Entry` — that state plays automatically.
- The controller is assigned to the **prefab's** Animator component in the next step, *not* to a Hierarchy instance — the spawner instantiates from the prefab at runtime, so a Hierarchy assignment never sees the spawned object.

### 4. Build the prefab

- Drag the FBX from `Assets/Models/<kebab-name>/source/` into the Hierarchy temporarily.
- Inspector → Animator component → drag the controller from step 3 into the **Controller** slot.
- **Leave Transform Scale at `(1, 1, 1)`** — real-world length is baked into the FBX via Scale Factor in step 2. Tune Position + Rotation only, per-species, so the body broadside is visible from the AR origin; rotation is usually `Y = 90°` (FBX nose typically points along +X). Spawn distance is per-species since a 4 m shark frames very differently from a 2.5 m one at the same `z` — expect to push `z` farther than the legacy `(-4, 0, 4)` once models are at real-world size (a 4 m shark at 4 m straddles the camera).
- Drag the Hierarchy GameObject into [Assets/Prefabs/Sharks/](src/app/MergulhoVirtual/Assets/Prefabs/Sharks/) → **choose `Prefab Variant`** when prompted (NOT *Original Prefab*). A Variant live-references the FBX, so future Scale Factor changes — and any other FBX-side fixes — propagate to the prefab automatically. *Original Prefab* bakes the entire bone hierarchy into the prefab as static data; after that, changing FBX Scale Factor only rescales mesh vertices (not the baked bones), the SkinnedMeshRenderer skins through the old bones, and the rendered shark stays at the old size despite the importer reporting the new value. Discovered the hard way with `tiger_shark`, which had to be recreated as a Variant once we tried to bake real-world scale.
- Delete the Hierarchy instance — the spawner owns the lifecycle.

To later tweak the controller, scale, or position, double-click the prefab to enter **Prefab Mode** and edit there; spawned instances pick up the changes next play.

### 5. Wire into the spawner

- Hierarchy → select **`Beach Shark Spawner`** (root GameObject).
- Inspector → **Beaches** list → find the beach entry whose name matches a [places.json](src/app/MergulhoVirtual/Assets/Resources/places.json) entry exactly (case + accents) → expand its `Shark Prefabs` array → `+` → drag the prefab in.
- If a beach doesn't already have an entry in the list, add one with the matching name first.

### 6. Test in editor

Press Play → the GPS stub doesn't fall in any polygon, so tap **Beaches** in the BottomNav and use the **`BeachSelectorDropdown`** to pick the wired beach → the prefab should spawn under `Beach Shark Spawner` and animate.

### 7. (Optional) Add to the Animals catalog screen

The Animals screen lists every `AnimalDef` ScriptableObject under [Resources/Animals/](src/app/MergulhoVirtual/Assets/Resources/Animals/) and lets the user inspect a 3D model in a turntable viewer (see "Animals catalog + 3D viewer" architecture section). To make the new species appear there too:

- **Assets > Create > Mergulho Virtual > Animal** → save under `Resources/Animals/<kebab-name>.asset`.
- Fill in `displayName` (Portuguese), `imageName` (string — the filename of a sprite-typed image at `Resources/Animals/<imageName>.<ext>`, no extension in the field), `description`, `prefab` (the same one wired into `BeachSharkSpawner`), and `photoCredit` (e.g. `Foto: Author / CC BY-SA 4.0 (Wikimedia Commons)`). The viewer auto-fits the camera per shark (see "Camera framing" in the architecture section), so you usually don't need to touch `viewerScale` / `viewerOffset` — leave them at defaults `(1,1,1)` / `(0,0,0)` unless you want to nudge the body within the auto-framed pose. To attach educational clips, add entries to the `videos` list (each a `title` + a public MP4 URL — see **Inline video player**); they render as tap-to-play cards below the description with no code or scaffolder change.
- **Source the image automatically**: [tools/populate_animals.py](tools/populate_animals.py) (mirrors `populate_beaches.py`) reads every `.asset` under `Resources/Animals/`, looks up Wikipedia by scientific binomial (then common name) for empty fields, downloads the lead image, fills `description` + `imageName` + `photoCredit`, and auto-generates a Sprite-typed `.meta` next to the image (cloned from `tiger_shark.jpg.meta` with a fresh GUID, so Unity imports it as Sprite without an Inspector trip). Add the species to `ARTICLE_TITLE_CANDIDATES` at the top of the script (asset filename → `[binomial, pt common name, en common name]`), then `python3 tools/populate_animals.py --apply`. Stdlib only, no venv. Default behavior only fills empty fields — pass `--force` to rewrite existing values.

No code or scaffolder re-run needed — the controller does `Resources.LoadAll<AnimalDef>("Animals")` on first activation.

### Replacing an existing species' FBX breaks every reference to its prefab

Because the shark prefabs are **Prefab Variants** of the FBX, the variant's root GameObject has no fileID of its own — Unity *computes* it from the FBX's internal object ids, which the importer derives from node names. Swap the FBX for a different export (new rig, renamed root node) and that computed fileID changes — and **every serialized reference to the prefab root silently dangles**, even though the prefab's GUID and file path are unchanged. The references die in *both* wiring sites at once:

- `AnimalDef.prefab` on the `.asset` under [Resources/Animals/](src/app/MergulhoVirtual/Assets/Resources/Animals/) → Animals detail viewer shows an empty `Turntable` with no Console warning ([SpawnViewerInstance](src/app/MergulhoVirtual/Assets/Scripts/UI/AnimalsScreenController.cs#L166) early-returns on null).
- Each `Shark Prefabs` slot in `Beach Shark Spawner`'s **Beaches** list — and a prefab wired into *multiple* beaches breaks in **every** entry; re-dragging it into one beach doesn't heal the others (this exact half-fix happened with nurse_shark: Sancho re-wired, Cacimba do Padre left stale).

**After replacing any FBX under `Assets/Models/<species>/source/`**, re-drag the prefab from [Assets/Prefabs/Sharks/](src/app/MergulhoVirtual/Assets/Prefabs/Sharks/) into: (1) the `prefab` slot of the species' `.asset` in `Resources/Animals/`, and (2) every beach entry listing it in `Beach Shark Spawner` → save the scene. Broken slots show **"Missing (Game Object)"** in the Inspector. Also re-check the FBX import settings (step 2) and the prefab's Animator Controller — a new export resets to Sketchfab-default rig/loop settings.

### Linux folder picker quirk for "Extract Materials..." / "Extract Textures..."

Under GTK, Unity's folder picker opens in CREATE_FOLDER mode and shows a "Name" / "filename" field that looks like a save dialog. It is still a folder picker — the name field is the **new subfolder** to create at the navigated location. Type `Materials` (or `Textures`), navigate into the species folder, and click the action button. An empty name will be rejected; pick a real name.

## Unity Editor — quick reference

The maintainer is learning Unity. These are the Editor operations that come up often in this repo:

- **Create a root GameObject**: right-click empty space in the Hierarchy → *Create Empty*. For a child, right-click the intended parent instead. Rename in place (F2 or double-click).
- **Attach a script as a component**: select the GameObject → Inspector → *Add Component* at the bottom → start typing the class name. Prefer attaching to an existing, relevant GameObject over creating a new empty one — only create a dedicated GameObject when the logic truly needs its own lifecycle (independent enable/disable).
- **Wire a `[SerializeField]` slot**: drag a GameObject from the Hierarchy (or an asset from the Project window) onto the field in the Inspector. For typed MonoBehaviour fields, drop the GameObject that has that component — Unity resolves it automatically.
- **Don't trust this CLAUDE.md for exact Hierarchy structure**: always verify against the actual `.unity` file before giving or following Editor instructions (grep `m_Name:` in `Assets/Scenes/*.unity`). The structure here drifts as the project evolves.
- **"Missing (Mono Script)" on a component**: the `.cs` file was deleted but the component reference survived on any GameObject/prefab that had it. Fix: select the GameObject, find the component showing "Missing", click its ⋮ (kebab) menu → *Remove Component*. If many GameObjects are affected, menu *GameObject > Remove Missing Scripts*. Scene-level overrides (components added on top of a prefab instance) persist even after the underlying script is gone; sometimes easier to delete the whole orphan `MonoBehaviour` block from the `.unity` file directly, then reimport.
- **Before creating a prefab**: check [Assets/Prefabs/](src/app/MergulhoVirtual/Assets/Prefabs/) — the scene GameObject you're looking at is often already an instance of a prefab that exists on disk (scene YAML shows `m_CorrespondingSourceObject` with a prefab GUID → search the project's `.meta` files for that GUID). You can reference the existing prefab directly; no need to drag-to-Project.
- **Dragging from Hierarchy to Project fails with "Missing script"**: almost always an orphan component on the source GameObject — remove it first (see above).
- **Script recompile**: Unity recompiles when you Alt-Tab back to the Editor after external edits. A compile error blocks Play mode; check the Console.
- **Refer to files by their actual on-disk name, not by a name you suggested as a rename.** Unity names auto-created assets after their source — `Create → TMP → Sprite Asset` on `moon_sheet.png` produces `moon_sheet.asset`, not `MoonSpriteAsset.asset`; the same applies to extracted Materials, generated Sprite Atlases, and `.meta` files. If you suggest a rename, present it as optional and **keep using the original name in any subsequent step** until the user confirms the rename happened. Don't compose later instructions around the suggested name as if it were already on disk — that produces "I don't have a file called X, only Y" pushback.
