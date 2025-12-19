
import pytest
from httpx import AsyncClient

@pytest.mark.asyncio
async def test_telemetry(async_client: AsyncClient):
    response = await async_client.get("/telemetria")
    assert response.status_code == 200
    assert response.json() == {"message": "Monitoramento de tubarões por telemetria"}
