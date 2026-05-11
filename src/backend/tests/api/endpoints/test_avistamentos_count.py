import pytest
from unittest.mock import patch
from httpx import AsyncClient


@pytest.fixture
def mock_count_avistamentos():
    with patch("api.endpoints.avistamentos_api.count_avistamentos") as mock:
        yield mock


@pytest.mark.asyncio
async def test_count_avistamentos(async_client: AsyncClient, mock_count_avistamentos):
    mock_count_avistamentos.return_value = 42

    response = await async_client.get("/api/v1/avistamentos/count")

    assert response.status_code == 200
    assert response.json() == {"count": 42}
    mock_count_avistamentos.assert_called_once()
