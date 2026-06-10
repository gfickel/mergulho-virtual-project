"""Stage 2: App Check gating on /api/v1/*.

The conftest sets BACKEND_DEBUG=1, so by default the gate is bypassed and the
existing endpoint tests don't have to send a header. These tests toggle
config.DEBUG_MODE off to exercise the prod gate path.
"""

import pytest
from unittest.mock import patch
from httpx import AsyncClient


@pytest.fixture
def prod_mode_no_app_check_init():
    """Disable debug bypass without actually initializing firebase_admin."""
    with patch("config.DEBUG_MODE", False), patch(
        "api.dependencies._ensure_firebase_initialized"
    ) as mock_init:
        yield mock_init


@pytest.mark.asyncio
async def test_api_v1_rejects_missing_header(
    async_client: AsyncClient, prod_mode_no_app_check_init
):
    response = await async_client.get("/api/v1/avistamentos/count")
    assert response.status_code == 401
    assert "X-Firebase-AppCheck" in response.json()["detail"]
    # Should fail before firebase_admin is ever touched.
    prod_mode_no_app_check_init.assert_not_called()


@pytest.mark.asyncio
async def test_api_v1_rejects_invalid_token(
    async_client: AsyncClient, prod_mode_no_app_check_init
):
    with patch("firebase_admin.app_check.verify_token", side_effect=ValueError("bad sig")):
        response = await async_client.get(
            "/api/v1/avistamentos/count",
            headers={"X-Firebase-AppCheck": "not-a-real-token"},
        )
    assert response.status_code == 401
    assert response.json()["detail"] == "Invalid App Check token"


@pytest.mark.asyncio
async def test_api_v1_accepts_valid_token(
    async_client: AsyncClient, prod_mode_no_app_check_init
):
    with patch("firebase_admin.app_check.verify_token", return_value={"app_id": "fake"}), patch(
        "api.endpoints.avistamentos_api.count_avistamentos", return_value=7
    ):
        response = await async_client.get(
            "/api/v1/avistamentos/count",
            headers={"X-Firebase-AppCheck": "valid-token"},
        )
    assert response.status_code == 200
    assert response.json() == {"count": 7}


@pytest.mark.asyncio
async def test_admin_routes_ignore_app_check_header(
    async_client: AsyncClient, mock_admin_list_services
):
    """Admin routes are protected by IAP at the infra layer, not App Check.
    They must remain reachable in debug mode regardless of the header.
    """
    response = await async_client.get("/avistamentos")
    assert response.status_code == 200
