
import pytest
from httpx import AsyncClient

from unittest.mock import patch, MagicMock

@pytest.fixture
def mock_query_telemetria():
    with patch("api.endpoints.telemetria.query_telemetria") as mock:
        yield mock

@pytest.mark.asyncio
async def test_telemetry(async_client: AsyncClient, mock_query_telemetria):
    # Setup mock to return an empty list of items
    mock_query_telemetria.return_value = ([], 1, 10, False)
    
    # Request JSON explicitly
    response = await async_client.get("/telemetria", headers={"Accept": "application/json"})
    
    assert response.status_code == 200
    data = response.json()
    assert data["items"] == []
    assert data["page"] == 1
    assert data["page_size"] == 10
