from datetime import datetime

import pytest
from httpx import AsyncClient
from unittest.mock import patch


@pytest.fixture
def mock_count_telemetria():
    with patch("api.endpoints.telemetria_admin.count_telemetria") as mock:
        yield mock


@pytest.mark.asyncio
async def test_telemetria_count(async_client: AsyncClient, mock_count_telemetria):
    mock_count_telemetria.return_value = 100

    response = await async_client.get("/telemetria/count")

    assert response.status_code == 200
    assert response.json() == {"count": 100}
    mock_count_telemetria.assert_called_once_with(oid=None, date_start=None, date_end=None)


@pytest.mark.asyncio
async def test_telemetria_count_threads_filters(
    async_client: AsyncClient, mock_count_telemetria
):
    mock_count_telemetria.return_value = 7

    response = await async_client.get(
        "/telemetria/count?oid=device-1&date_start=2026-06-01&date_end=2026-06-02"
    )

    # _parse_date_param converts YYYY-MM-DD via local time, end dates to 23:59:59.
    expected_start = int(datetime(2026, 6, 1).timestamp())
    expected_end = int(datetime(2026, 6, 2, 23, 59, 59).timestamp())

    assert response.status_code == 200
    assert response.json() == {"count": 7}
    mock_count_telemetria.assert_called_once_with(
        oid="device-1", date_start=expected_start, date_end=expected_end
    )


@pytest.mark.asyncio
async def test_telemetria_count_blank_oid_treated_as_no_filter(
    async_client: AsyncClient, mock_count_telemetria
):
    mock_count_telemetria.return_value = 0

    response = await async_client.get("/telemetria/count?oid=%20&date_start=garbage")

    assert response.status_code == 200
    mock_count_telemetria.assert_called_once_with(oid=None, date_start=None, date_end=None)
