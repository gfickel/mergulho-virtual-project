# Mergulho Virtual — backend ops
#
# Production backend runs on a GCE e2-micro VM (us-central1-a) as the systemd
# service `mergulho-backend`. See docs/deploy-gce-vm.md for the full runbook.
#
# Usage: `make <target>` from the repo root. `make help` lists everything.

VM        := app-backend
ZONE      := us-central1-a
SERVICE   := mergulho-backend
REMOTE    := gcloud compute ssh $(VM) --zone=$(ZONE) --command

.DEFAULT_GOAL := help

UNITY     := $(HOME)/Unity/Hub/Editor/6000.3.14f1/Editor/Unity
UPROJ     := src/app/MergulhoVirtual
ULOG      := $(UPROJ)/Logs
DSPY      := tools/design_system/.venv/bin/python

# ds-shots is the one Unity target that must NOT pass -nographics (it needs a
# real graphics device to rasterise the UI Toolkit panels), so it needs an X
# display. Your environment's DISPLAY wins; override with `make ds-shots DISPLAY=:0`.
DISPLAY   ?= :1

.PHONY: help ssh logs status restart deploy release health indexes backend-debug \
        ds-tokens ds-icons ds-setup ds-test ds-test-play ds-shots ds-compile ui-setup ui-beaches-setup \
        articles articles-check \
        ar-sim ar-sweep \
        apk apk-install apk-run apk-log apk-token device win-build

help: ## List available targets
	@grep -E '^[a-zA-Z_-]+:.*?## .*$$' $(MAKEFILE_LIST) \
		| sort \
		| awk 'BEGIN {FS = ":.*?## "}; {printf "  \033[36m%-14s\033[0m %s\n", $$1, $$2}'

## --- VM: backend service ---------------------------------------------------

ssh: ## Open an interactive shell on the VM
	gcloud compute ssh $(VM) --zone=$(ZONE)

logs: ## Tail the backend logs (Ctrl-C to stop)
	$(REMOTE)="sudo journalctl -u $(SERVICE) -f"

status: ## Show backend service status + listening port
	$(REMOTE)="sudo systemctl status $(SERVICE) --no-pager; echo; sudo ss -ltnp | grep 8000 || echo '(nothing on 8000)'"

restart: ## Restart the backend service (no code change)
	$(REMOTE)="sudo systemctl restart $(SERVICE) && sudo systemctl is-active $(SERVICE)"

deploy: ## Pull latest origin/main on the VM and restart the backend
	$(REMOTE)="cd ~/mergulho-virtual && git pull && sudo systemctl restart $(SERVICE) && sudo systemctl is-active $(SERVICE)"

release: ## Push local main to GitHub, then deploy on the VM
	git push origin main
	$(MAKE) deploy

health: ## Hit the backend endpoints from the VM's localhost (count expects 401)
	$(REMOTE)="curl -s -o /dev/null -w 'count: HTTP %{http_code} (expect 401)\n' localhost:8000/api/v1/avistamentos/count; curl -s -o /dev/null -w 'root:  HTTP %{http_code}\n' localhost:8000/"

## --- Firestore indexes (run from repo root, local) -------------------------

indexes: ## Deploy Firestore composite indexes (backfills 5-15 min)
	firebase deploy --only firestore:indexes

## --- Local dev -------------------------------------------------------------

backend-debug: ## Run the backend locally in debug mode (needs Firestore emulator on :8080)
	cd src/backend && BACKEND_DEBUG=1 uvicorn main:app --host 0.0.0.0 --port 8000 --reload

## --- Design system (Unity CLI — CLOSE THE UNITY EDITOR FIRST) --------------
# The ds-setup/ds-test/ds-compile targets drive the Unity editor headlessly;
# they fail with "project already open" if the editor has the project open.

ds-tokens: ## Regenerate M3 color tokens from tools/design_system/brand-theme.json
	$(DSPY) tools/design_system/generate_md3_tokens.py \
		--from-json tools/design_system/brand-theme.json

ds-icons: ## Re-subset MaterialSymbols.ttf from material_symbols_icons.txt
	$(DSPY) tools/design_system/subset_material_symbols.py

ds-setup: ## Create/refresh font assets + PanelSettings + GalleryScene (headless)
	@mkdir -p $(ULOG)
	@$(UNITY) -batchmode -quit -nographics -projectPath $(UPROJ) \
		-executeMethod MergulhoVirtual.DesignSystem.Editor.DesignSystemSetup.SetupAll \
		-logFile $(ULOG)/ds-setup.log \
		|| { echo "FAILED — last 60 log lines:"; tail -60 $(ULOG)/ds-setup.log; exit 1; }
	@echo "ds-setup OK (log: $(ULOG)/ds-setup.log)"

ds-test: ## Run design-system EditMode tests headlessly
	@mkdir -p $(ULOG)
	@$(UNITY) -batchmode -nographics -projectPath $(UPROJ) \
		-runTests -testPlatform EditMode \
		-assemblyNames "MergulhoVirtual.DesignSystem.Tests.Editor;MergulhoVirtual.UI.Tests.Editor;Assembly-CSharp-Editor" \
		-testResults $(abspath $(ULOG))/ds-test-editmode.xml \
		-logFile $(ULOG)/ds-test-editmode.log \
		|| { echo "FAILED — results: $(ULOG)/ds-test-editmode.xml"; \
		     python3 tools/summarize_test_results.py $(ULOG)/ds-test-editmode.xml || true; exit 1; }
	@python3 tools/summarize_test_results.py $(ULOG)/ds-test-editmode.xml
	@echo "ds-test OK ($(ULOG)/ds-test-editmode.xml)"

ds-test-play: ## Run design-system PlayMode interaction tests headlessly
	@mkdir -p $(ULOG)
	@$(UNITY) -batchmode -nographics -projectPath $(UPROJ) \
		-runTests -testPlatform PlayMode \
		-assemblyNames "MergulhoVirtual.DesignSystem.Tests.Runtime" \
		-testResults $(abspath $(ULOG))/ds-test-playmode.xml \
		-logFile $(ULOG)/ds-test-playmode.log \
		|| { echo "FAILED — results: $(ULOG)/ds-test-playmode.xml"; \
		     python3 tools/summarize_test_results.py $(ULOG)/ds-test-playmode.xml || true; exit 1; }
	@python3 tools/summarize_test_results.py $(ULOG)/ds-test-playmode.xml
	@echo "ds-test-play OK ($(ULOG)/ds-test-playmode.xml)"

ui-setup: ## Build/wire the UI Toolkit app shell (router + nav bar + screens) into MainScene (headless)
	@mkdir -p $(ULOG)
	@$(UNITY) -batchmode -quit -nographics -projectPath $(UPROJ) \
		-executeMethod AppUiBuilder.BuildHeadless \
		-logFile $(ULOG)/ui-setup.log \
		|| { echo "FAILED — last 60 log lines:"; tail -60 $(ULOG)/ui-setup.log; exit 1; }
	@echo "ui-setup OK (log: $(ULOG)/ui-setup.log)"

# Superseded by ui-setup (the shell replaced the single-screen Beaches host in
# Slice 1). Kept so existing muscle memory / docs keep working.
ui-beaches-setup: ui-setup

ds-shots: ## Render the UI Toolkit screens + DS gallery to PNGs in .shots/ (headless; filter with SHOT=home)
	@mkdir -p $(ULOG)
	@MV_SHOT_FILTER="$(SHOT)" DISPLAY=$(DISPLAY) $(UNITY) -batchmode -quit -projectPath $(UPROJ) \
		-executeMethod MergulhoVirtual.UiShots.UiScreenshotHarness.CaptureAll \
		-logFile $(ULOG)/ds-shots.log \
		|| { echo "FAILED — last 60 log lines:"; tail -60 $(ULOG)/ds-shots.log; exit 1; }
	@awk -F'[ =]' '/^UI-SHOTS-SUMMARY/ { printf "ds-shots OK — %s PNG(s) written, %s subject(s) skipped -> %s\n", $$3, $$5, $$7; ok=1 } \
	     END { if (!ok) print "ds-shots OK (no summary line; see $(ULOG)/ds-shots.log)" }' $(ULOG)/ds-shots.log

## --- Educational articles (pure Python — the editor may stay OPEN) ---------
# content/articles/*.md -> Assets/Resources/articles.json. These two never touch
# Unity, so unlike every ds-*/ui-* target above they are safe with the editor
# open. `make articles` DOES write inside Assets/Resources/ though, so Unity will
# reimport articles.json (and any photo installed into Assets/Resources/Articles/)
# the next time the editor has focus. An unchanged corpus rewrites nothing at all,
# so a no-op build causes no reimport either. Stdlib only, no venv.

articles: ## Build Assets/Resources/articles.json from content/articles/*.md (VERBOSE=1 for block counts)
	@python3 tools/build_articles.py $(if $(VERBOSE),--verbose,)

articles-check: ## Validate content/articles/*.md without writing anything (CI / pre-commit)
	@python3 tools/build_articles.py --check

## --- AR wave-drift simulation (Unity CLI — CLOSE THE UNITY EDITOR FIRST) ---
# Offline harness that reproduces wave-induced AR drift and scores stabilizers
# against it. Pure CPU: -nographics is correct here (no panels to rasterise).
# Filter to one scenario with `make ar-sim ARSIM=pan`. Writes CSV time series +
# manifest.json to the gitignored .arsim/ at the repo root.

ar-sim: ## Run the offline AR wave-drift sweep and print the metrics table (headless)
	@mkdir -p $(ULOG)
	@MV_ARSIM_FILTER="$(ARSIM)" $(UNITY) -batchmode -quit -nographics -projectPath $(UPROJ) \
		-executeMethod MergulhoVirtual.ArSim.ArSimHarness.RunAllHeadless \
		-logFile $(ULOG)/ar-sim.log \
		|| { echo "FAILED — last 60 log lines:"; tail -60 $(ULOG)/ar-sim.log; exit 1; }
	@sed -n '/^AR-SIM-TABLE-BEGIN$$/,/^AR-SIM-TABLE-END$$/p' $(ULOG)/ar-sim.log | sed '1d;$$d'
	@echo "ar-sim OK (log: $(ULOG)/ar-sim.log)"

# Sweeps the wave model's least-grounded parameters and reports whether the
# RANKING of the stabilizers survives them. Three blocks, selected with
# `make ar-sweep BLOCK=device|jumps|threshold` (default: all three):
#   device     a_th x relaxationFraction
#   jumps      jumpRateHz / jumpMedianM / jumpTriggerM / genuineJumpFraction, one at a time
#   threshold  relocalizationJumpThreshold as a CONTINUUM x genuineJumpFraction -- the
#              crossover that decides whether "suppress every translation" holds up
#   budget     the correction budget's ASSUMED error rate x the model's TRUE rate --
#              the robustness question for the path-based gate (a match is not a
#              result; the off-diagonal is)
# Slower than ar-sim: dozens of full sweeps.
ar-sweep: ## Sweep the wave model's weak parameters and report ranking stability (headless; BLOCK=device|jumps|threshold|budget)
	@mkdir -p $(ULOG)
	@MV_ARSWEEP_BLOCK="$(BLOCK)" $(UNITY) -batchmode -quit -nographics -projectPath $(UPROJ) \
		-executeMethod MergulhoVirtual.ArSim.ArSimSweep.RunAllHeadless \
		-logFile $(ULOG)/ar-sweep.log \
		|| { echo "FAILED — last 60 log lines:"; tail -60 $(ULOG)/ar-sweep.log; exit 1; }
	@sed -n '/^AR-SWEEP-TABLE-BEGIN$$/,/^AR-SWEEP-TABLE-END$$/p' $(ULOG)/ar-sweep.log | sed '1d;$$d'
	@echo "ar-sweep OK (log: $(ULOG)/ar-sweep.log)"

ds-compile: ## Headless compile check (imports + compiles, no side effects)
	@mkdir -p $(ULOG)
	@$(UNITY) -batchmode -quit -nographics -projectPath $(UPROJ) \
		-logFile $(ULOG)/ds-compile.log \
		|| { echo "COMPILE FAILED — errors:"; grep -E "error CS|Compilation failed" $(ULOG)/ds-compile.log | head -30; exit 1; }
	@echo "ds-compile OK"

# ---------------------------------------------------------------------------
# Android device builds
#
# The Editor must be CLOSED, same as every other Unity target here. The scene
# list is named inside AndroidBuilder, not taken from EditorBuildSettings (which
# holds only a disabled SampleScene).
#
# `make apk` is a DEVELOPMENT build: AppCheckTokenProvider branches on
# Debug.isDebugBuild, so it uses the Firebase Debug provider and prints a debug
# token to logcat on first run. Register that token once in Firebase Console ->
# App Check -> Manage debug tokens, or every /api/v1 call comes back 401.
# `make apk DEV=0` builds release (Play Integrity) instead.
# ---------------------------------------------------------------------------

APK    ?= $(UPROJ)/Builds/Android/mergulho-virtual.apk
APPID  := dev.mergulhovirtual
DEV    ?= 1

apk: ## Build an Android APK headlessly (development; DEV=0 for release, APK=path to override)
	@mkdir -p $(ULOG)
	@echo "Building $(if $(filter 0,$(DEV)),release,development) APK (IL2CPP/ARM64 — first build can take 10-30 min)..."
	@MV_BUILD_OUTPUT="$(abspath $(APK))" MV_BUILD_DEV=$(DEV) \
		$(UNITY) -batchmode -quit -nographics -projectPath $(UPROJ) \
		-buildTarget Android \
		-executeMethod AndroidBuilder.BuildApk \
		-logFile $(ULOG)/android-build.log \
		|| { echo "BUILD FAILED — errors:"; \
		     grep -E "error CS|AndroidBuilder\]|BuildFailedException|error:" $(ULOG)/android-build.log | head -40; \
		     echo "(full log: $(ULOG)/android-build.log)"; exit 1; }
	@ls -lh $(APK) | awk '{ printf "apk OK — %s (%s)\n", $$9, $$5 }'

apk-install: ## Install the built APK on the connected device (adb)
	@adb get-state >/dev/null 2>&1 || { echo "No device — plug the phone in, enable USB debugging, accept the RSA prompt."; exit 1; }
	adb install -r $(APK)

apk-run: ## Launch the app on the connected device
	adb shell monkey -p $(APPID) -c android.intent.category.LAUNCHER 1 >/dev/null

apk-log: ## Tail Unity + Firebase logcat from the device (Ctrl-C to stop)
	adb logcat -c
	adb logcat -v time Unity:V DebugAppCheckProvider:V FirebaseAppCheck:V firebase:V AndroidRuntime:E '*:S'

apk-token: ## Print the Firebase App Check debug token this device logged (development builds)
	@adb logcat -d | grep -iE "debug secret|debug token" | tail -5 \
		|| echo "Not in the log buffer — run 'make apk-log' and relaunch the app."

device: apk apk-install apk-run ## Build, install and launch in one go


# ---------------------------------------------------------------------------
# Windows review build (throwaway target for the designer)
#
# The Editor must be CLOSED, same as every other Unity target here. Produces a
# windowed, resizable Windows player so the V2 screens can be clicked through on
# the designer's machine -- see docs/windows-review-build.md. Mono backend (the
# installed module ships no Windows IL2CPP toolchain for a Linux host), and the
# Standalone player settings are applied AND restored inside
# WindowsReviewBuild.cs, so ProjectSettings.asset is never left modified.
#
# -buildTarget Win64 is REQUIRED: without it Unity builds with whatever target was
# last active. Output lands in $(UPROJ)/Builds/Windows/, which the Unity project's
# .gitignore excludes; zip that folder WHOLE (the .exe is useless without its
# MergulhoVirtual_Data/ and UnityPlayer.dll beside it).
# ---------------------------------------------------------------------------

win-build: ## Build the Windows review .exe for the designer (headless; editor closed)
	@mkdir -p $(ULOG)
	@$(UNITY) -batchmode -quit -nographics -projectPath $(UPROJ) \
		-buildTarget Win64 \
		-executeMethod WindowsReviewBuild.BuildHeadless \
		-logFile $(ULOG)/win-build.log \
		|| { echo "FAILED — last 60 log lines:"; tail -60 $(ULOG)/win-build.log; exit 1; }
	@awk -F'[ =]' '/^WIN-BUILD-SUMMARY/ { size=$$3; sub(/^.*path=/, ""); printf "win-build OK — %s MB -> %s\n", size, $$0; ok=1 } \
	     END { if (!ok) print "win-build OK -> $(UPROJ)/Builds/Windows/ (no summary line; see $(ULOG)/win-build.log)" }' $(ULOG)/win-build.log
