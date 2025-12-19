from fastapi import APIRouter

router = APIRouter()

@router.get("/telemetria")
async def telemetry():
    return {"message": "Monitoramento de tubarões por telemetria"}
