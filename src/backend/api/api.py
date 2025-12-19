from fastapi import APIRouter
from api.endpoints import avistamentos, telemetria

api_router = APIRouter()
api_router.include_router(avistamentos.router, tags=["avistamentos"])
api_router.include_router(telemetria.router, tags=["telemetria"])
