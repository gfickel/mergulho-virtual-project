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

.PHONY: help ssh logs status restart deploy release health indexes backend-debug \
        ds-tokens ds-icons ds-setup ds-test ds-test-play ds-compile ui-beaches-setup

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

ds-tokens: ## Regenerate M3 color tokens from the seed (see tools/design_system/)
	$(DSPY) tools/design_system/generate_md3_tokens.py

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
		-assemblyNames "MergulhoVirtual.DesignSystem.Tests.Editor;MergulhoVirtual.UI.Tests.Editor" \
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

ui-beaches-setup: ## Build/wire the UI Toolkit Beaches screen into MainScene (headless)
	@mkdir -p $(ULOG)
	@$(UNITY) -batchmode -quit -nographics -projectPath $(UPROJ) \
		-executeMethod BeachesUiScreenBuilder.BuildHeadless \
		-logFile $(ULOG)/ui-beaches-setup.log \
		|| { echo "FAILED — last 60 log lines:"; tail -60 $(ULOG)/ui-beaches-setup.log; exit 1; }
	@echo "ui-beaches-setup OK (log: $(ULOG)/ui-beaches-setup.log)"

ds-compile: ## Headless compile check (imports + compiles, no side effects)
	@mkdir -p $(ULOG)
	@$(UNITY) -batchmode -quit -nographics -projectPath $(UPROJ) \
		-logFile $(ULOG)/ds-compile.log \
		|| { echo "COMPILE FAILED — errors:"; grep -E "error CS|Compilation failed" $(ULOG)/ds-compile.log | head -30; exit 1; }
	@echo "ds-compile OK"
