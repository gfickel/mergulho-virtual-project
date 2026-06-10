import os

# Must be set before importing main: config.DEBUG_MODE is read at import time,
# and the /api/v1 tests rely on the debug bypass of App Check (plus the
# emulator/local-disk clients, which need no GCP credentials).
os.environ["BACKEND_DEBUG"] = "1"

import pytest
import pytest_asyncio
from unittest.mock import patch
from httpx import AsyncClient, ASGITransport
from typing import AsyncGenerator
from main import app


@pytest_asyncio.fixture
async def async_client() -> AsyncGenerator[AsyncClient, None]:
    async with AsyncClient(transport=ASGITransport(app=app), base_url="http://test") as client:
        yield client


@pytest.fixture
def mock_api_db():
    """Firestore client as seen by the Unity-facing /api/v1 endpoints."""
    with patch("api.endpoints.avistamentos_api.db") as mock:
        yield mock


@pytest.fixture
def mock_admin_db():
    """Firestore client as seen by the operator HTML endpoints."""
    with patch("api.endpoints.avistamentos_admin.db") as mock:
        yield mock


@pytest.fixture
def mock_admin_list_services():
    """Everything the HTML list endpoint touches besides the db client.

    All three must be patched — leaving any of them live makes the test depend
    on a running Firestore emulator (it hangs in gRPC retries without one).
    Yields the query_avistamentos mock; the other two get harmless defaults.
    """
    with patch(
        "api.endpoints.avistamentos_admin.query_avistamentos",
        return_value=([], 1, 10, False),
    ) as mock_query, patch(
        "api.endpoints.avistamentos_admin.count_avistamentos", return_value=0
    ), patch(
        "api.endpoints.avistamentos_admin.list_distinct_filter_values",
        return_value={"beaches": [], "species": []},
    ):
        yield mock_query
