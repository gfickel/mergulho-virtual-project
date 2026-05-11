import pytest
from unittest.mock import patch
from httpx import AsyncClient


@pytest.fixture
def mock_query_telemetria():
    with patch("api.endpoints.telemetria_admin.query_telemetria") as mock:
        yield mock


@pytest.mark.asyncio
async def test_telemetria_html(async_client: AsyncClient, mock_query_telemetria):
    mock_query_telemetria.return_value = ([], 1, 10, False)

    response = await async_client.get("/telemetria")

    assert response.status_code == 200
    assert "text/html" in response.headers["content-type"]
