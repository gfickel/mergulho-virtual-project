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
    mock_count_avistamentos.assert_called_once_with(
        dia_registro=None, mes_registro=None, ano_registro=None
    )


@pytest.mark.asyncio
async def test_count_avistamentos_threads_date_filters(
    async_client: AsyncClient, mock_count_avistamentos
):
    # The endpoint's whole job is threading the query params into the service.
    mock_count_avistamentos.return_value = 3

    response = await async_client.get(
        "/api/v1/avistamentos/count?dia_registro=5&mes_registro=6&ano_registro=2026"
    )

    assert response.status_code == 200
    assert response.json() == {"count": 3}
    mock_count_avistamentos.assert_called_once_with(
        dia_registro=5, mes_registro=6, ano_registro=2026
    )
