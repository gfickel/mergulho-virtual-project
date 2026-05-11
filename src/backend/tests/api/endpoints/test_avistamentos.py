import pytest
from unittest.mock import MagicMock, patch
from httpx import AsyncClient


@pytest.fixture
def mock_admin_db():
    with patch("api.endpoints.avistamentos_admin.db") as mock:
        yield mock


@pytest.fixture
def mock_api_db():
    with patch("api.endpoints.avistamentos_api.db") as mock:
        yield mock


@pytest.fixture
def mock_query_avistamentos():
    with patch("api.endpoints.avistamentos_admin.query_avistamentos") as mock:
        yield mock


@pytest.mark.asyncio
async def test_list_avistamentos_html(async_client: AsyncClient, mock_query_avistamentos):
    mock_query_avistamentos.return_value = ([], 1, 10, False)

    response = await async_client.get("/avistamentos")

    assert response.status_code == 200
    assert "text/html" in response.headers["content-type"]


@pytest.mark.asyncio
async def test_create_avistamento(async_client: AsyncClient, mock_api_db):
    # Idempotency-Key not yet present in Firestore → endpoint creates the doc.
    mock_doc_ref = MagicMock()
    mock_doc_ref.get.return_value.exists = False
    mock_api_db.collection.return_value.document.return_value = mock_doc_ref

    with patch(
        "api.endpoints.avistamentos_api.resize_image_preserving_exif",
        return_value=b"resized",
    ), patch("api.endpoints.avistamentos_api.upload_bytes") as mock_upload:
        response = await async_client.post(
            "/api/v1/avistamentos",
            headers={"Idempotency-Key": "test-key-001"},
            data={
                "timestamp": "2026-05-11T12:00:00Z",
                "beach": "Baía do Sancho",
                "species_guess": "Tubarão-martelo",
                "notes": "Two sharks circling",
            },
            files={"photo": ("photo.jpg", b"fake-jpeg-bytes", "image/jpeg")},
        )

    assert response.status_code == 201
    body = response.json()
    assert body["status"] == "created"
    assert body["registro"] == "test-key-001"
    assert body["avistamento"]["modo_registro"] == "app"
    assert mock_upload.call_count == 2  # originals/ + imagens/
    mock_doc_ref.set.assert_called_once()


@pytest.mark.asyncio
async def test_create_avistamento_idempotent(async_client: AsyncClient, mock_api_db):
    # Existing doc with same Idempotency-Key → endpoint returns 200 + existing payload.
    mock_doc_ref = MagicMock()
    existing = mock_doc_ref.get.return_value
    existing.exists = True
    existing.to_dict.return_value = {"registro": "test-key-002", "modo_registro": "app"}
    mock_api_db.collection.return_value.document.return_value = mock_doc_ref

    response = await async_client.post(
        "/api/v1/avistamentos",
        headers={"Idempotency-Key": "test-key-002"},
        data={"timestamp": "2026-05-11T12:00:00Z"},
        files={"photo": ("photo.jpg", b"fake", "image/jpeg")},
    )

    assert response.status_code == 200
    assert response.json()["status"] == "already_exists"
    mock_doc_ref.set.assert_not_called()


@pytest.mark.asyncio
async def test_create_avistamento_missing_idempotency_key(async_client: AsyncClient):
    response = await async_client.post(
        "/api/v1/avistamentos",
        data={"timestamp": "2026-05-11T12:00:00Z"},
        files={"photo": ("photo.jpg", b"fake", "image/jpeg")},
    )
    assert response.status_code == 400


@pytest.mark.asyncio
async def test_read_avistamento_html(async_client: AsyncClient, mock_admin_db):
    registro_id = "123"
    mock_doc = MagicMock()
    mock_doc.exists = True
    mock_doc.to_dict.return_value = {"registro": registro_id, "nome_popular": "Tubarão"}
    mock_admin_db.collection.return_value.document.return_value.get.return_value = mock_doc

    response = await async_client.get(f"/avistamentos/{registro_id}")

    assert response.status_code == 200
    assert "text/html" in response.headers["content-type"]


@pytest.mark.asyncio
async def test_read_avistamento_not_found(async_client: AsyncClient, mock_admin_db):
    mock_doc = MagicMock()
    mock_doc.exists = False
    mock_admin_db.collection.return_value.document.return_value.get.return_value = mock_doc

    response = await async_client.get("/avistamentos/999")
    assert response.status_code == 404


@pytest.mark.asyncio
async def test_delete_avistamento_form(async_client: AsyncClient, mock_admin_db):
    registro_id = "123"
    mock_doc = MagicMock()
    mock_doc.exists = True
    mock_admin_db.collection.return_value.document.return_value.get.return_value = mock_doc

    # 303 is a redirect — disable httpx auto-follow so we can assert on it directly.
    response = await async_client.post(
        f"/avistamentos/{registro_id}/delete", follow_redirects=False
    )

    assert response.status_code == 303
    assert response.headers["location"] == "/avistamentos"
    mock_admin_db.collection.return_value.document.return_value.delete.assert_called_once()
