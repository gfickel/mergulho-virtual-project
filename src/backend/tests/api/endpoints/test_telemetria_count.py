import pytest
from httpx import AsyncClient
from unittest.mock import patch


@pytest.mark.asyncio
async def test_telemetria_count(async_client: AsyncClient):
    with patch("api.endpoints.telemetria_admin.count_telemetria") as mock_count:
        mock_count.return_value = 100

        response = await async_client.get("/telemetria/count")

        assert response.status_code == 200
        assert response.json() == {"count": 100}
        mock_count.assert_called_once()
