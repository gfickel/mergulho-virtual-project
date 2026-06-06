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

.PHONY: help ssh logs status restart deploy release health indexes backend-debug

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
