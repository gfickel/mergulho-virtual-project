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
        ds-tokens ds-icons ds-setup ds-test ds-test-play ds-shots ds-compile ui-setup ui-beaches-setup

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
		     grep -oE 'result="[A-Za-z]+"' $(ULOG)/ds-test-editmode.xml | sort | uniq -c || true; exit 1; }
	@echo "ds-test OK ($(ULOG)/ds-test-editmode.xml)"

ds-test-play: ## Run design-system PlayMode interaction tests headlessly
	@mkdir -p $(ULOG)
	@$(UNITY) -batchmode -nographics -projectPath $(UPROJ) \
		-runTests -testPlatform PlayMode \
		-assemblyNames "MergulhoVirtual.DesignSystem.Tests.Runtime" \
		-testResults $(abspath $(ULOG))/ds-test-playmode.xml \
		-logFile $(ULOG)/ds-test-playmode.log \
		|| { echo "FAILED — results: $(ULOG)/ds-test-playmode.xml"; exit 1; }
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

ds-compile: ## Headless compile check (imports + compiles, no side effects)
	@mkdir -p $(ULOG)
	@$(UNITY) -batchmode -quit -nographics -projectPath $(UPROJ) \
		-logFile $(ULOG)/ds-compile.log \
		|| { echo "COMPILE FAILED — errors:"; grep -E "error CS|Compilation failed" $(ULOG)/ds-compile.log | head -30; exit 1; }
	@echo "ds-compile OK"
