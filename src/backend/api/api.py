"""Two-router layout for client-vs-admin separation.

- api_router (/api/v1/*): Unity-bound JSON endpoints. App Check-gated in
  Stage 2. Deployed as the "app-backend" Cloud Run service in prod
  (ROUTER_MODE=api).
- admin_router (no prefix): operator HTML endpoints. Behind Cloud IAP in
  prod. Deployed as the "admin-backend" Cloud Run service (ROUTER_MODE=admin).

In local debug (ROUTER_MODE=both, the default) main.py includes both so a
single uvicorn process serves everything — paths don't collide because the
app router only owns POST /avistamentos and GET /avistamentos/count, while
admin owns GET /avistamentos (HTML list) and the per-registro paths.
"""

from fastapi import APIRouter, Depends, Request
from fastapi.responses import HTMLResponse

from api.dependencies import verify_app_check
from api.endpoints import avistamentos_admin, avistamentos_api, telemetria_admin
from config import templates


api_router = APIRouter(prefix="/api/v1", dependencies=[Depends(verify_app_check)])
api_router.include_router(avistamentos_api.router, tags=["avistamentos"])


admin_router = APIRouter()
admin_router.include_router(avistamentos_admin.router, tags=["avistamentos"])
admin_router.include_router(telemetria_admin.router, tags=["telemetria"])


@admin_router.get("/", response_class=HTMLResponse)
async def index(request: Request):
    return templates.TemplateResponse("index.html", {"request": request})
