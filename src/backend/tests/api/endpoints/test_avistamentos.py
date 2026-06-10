import pytest
from unittest.mock import MagicMock, patch
from httpx import AsyncClient


@pytest.mark.asyncio
async def test_list_avistamentos_html(async_client: AsyncClient, mock_admin_list_services):
    mock_admin_list_services.return_value = ([], 1, 10, False)

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
    # The blob keys are load-bearing: view/list pages sign imagens/<registro>.jpg.
    assert mock_upload.call_count == 2
    original_call, display_call = mock_upload.call_args_list
    assert original_call.args[0] == "originals/test-key-001.jpg"
    assert original_call.args[1] == b"fake-jpeg-bytes"  # raw bytes, EXIF intact
    assert display_call.args[0] == "imagens/test-key-001.jpg"
    assert display_call.args[1] == b"resized"
    assert display_call.args[2] == "image/jpeg"
    mock_doc_ref.set.assert_called_once()


@pytest.mark.asyncio
async def test_create_avistamento_unknown_extension_falls_back_to_jpg(
    async_client: AsyncClient, mock_api_db
):
    mock_doc_ref = MagicMock()
    mock_doc_ref.get.return_value.exists = False
    mock_api_db.collection.return_value.document.return_value = mock_doc_ref

    with patch(
        "api.endpoints.avistamentos_api.resize_image_preserving_exif",
        return_value=b"resized",
    ), patch("api.endpoints.avistamentos_api.upload_bytes") as mock_upload:
        response = await async_client.post(
            "/api/v1/avistamentos",
            headers={"Idempotency-Key": "test-key-ext"},
            data={"timestamp": "2026-05-11T12:00:00Z"},
            files={"photo": ("photo.bmp", b"fake", "image/bmp")},
        )

    assert response.status_code == 201
    assert mock_upload.call_args_list[0].args[0] == "originals/test-key-ext.jpg"


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
async def test_create_avistamento_invalid_timestamp(async_client: AsyncClient, mock_api_db):
    mock_doc_ref = MagicMock()
    mock_doc_ref.get.return_value.exists = False
    mock_api_db.collection.return_value.document.return_value = mock_doc_ref

    response = await async_client.post(
        "/api/v1/avistamentos",
        headers={"Idempotency-Key": "test-key-ts"},
        data={"timestamp": "11/05/2026 12:00"},
        files={"photo": ("photo.jpg", b"fake", "image/jpeg")},
    )
    assert response.status_code == 400
    assert "timestamp" in response.json()["detail"]


@pytest.mark.asyncio
async def test_create_avistamento_empty_photo(async_client: AsyncClient, mock_api_db):
    mock_doc_ref = MagicMock()
    mock_doc_ref.get.return_value.exists = False
    mock_api_db.collection.return_value.document.return_value = mock_doc_ref

    response = await async_client.post(
        "/api/v1/avistamentos",
        headers={"Idempotency-Key": "test-key-empty"},
        data={"timestamp": "2026-05-11T12:00:00Z"},
        files={"photo": ("photo.jpg", b"", "image/jpeg")},
    )
    assert response.status_code == 400
    assert response.json()["detail"] == "photo is empty"


@pytest.mark.asyncio
async def test_create_avistamento_missing_photo(async_client: AsyncClient):
    # FastAPI rejects the request at validation time — no photo part at all.
    response = await async_client.post(
        "/api/v1/avistamentos",
        headers={"Idempotency-Key": "test-key-nophoto"},
        data={"timestamp": "2026-05-11T12:00:00Z"},
    )
    assert response.status_code == 422


@pytest.mark.asyncio
async def test_create_avistamento_undecodable_image(async_client: AsyncClient, mock_api_db):
    # Real bytes through the real Pillow pipeline — not a mock. Garbage that
    # isn't an image must surface as 415, and nothing may be uploaded.
    mock_doc_ref = MagicMock()
    mock_doc_ref.get.return_value.exists = False
    mock_api_db.collection.return_value.document.return_value = mock_doc_ref

    with patch("api.endpoints.avistamentos_api.upload_bytes") as mock_upload:
        response = await async_client.post(
            "/api/v1/avistamentos",
            headers={"Idempotency-Key": "test-key-415"},
            data={"timestamp": "2026-05-11T12:00:00Z"},
            files={"photo": ("photo.jpg", b"definitely not an image", "image/jpeg")},
        )

    assert response.status_code == 415
    mock_upload.assert_not_called()
    mock_doc_ref.set.assert_not_called()


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
async def test_edit_avistamento_form(async_client: AsyncClient, mock_admin_db):
    mock_doc = MagicMock()
    mock_doc.exists = True
    mock_doc.to_dict.return_value = {"registro": "123", "nome_popular": "Tubarão"}
    mock_admin_db.collection.return_value.document.return_value.get.return_value = mock_doc

    response = await async_client.get("/avistamentos/123/edit")

    assert response.status_code == 200
    assert "text/html" in response.headers["content-type"]


@pytest.mark.asyncio
async def test_edit_avistamento_form_not_found(async_client: AsyncClient, mock_admin_db):
    mock_doc = MagicMock()
    mock_doc.exists = False
    mock_admin_db.collection.return_value.document.return_value.get.return_value = mock_doc

    response = await async_client.get("/avistamentos/999/edit")
    assert response.status_code == 404


@pytest.mark.asyncio
async def test_update_avistamento_form(async_client: AsyncClient, mock_admin_db):
    mock_doc_ref = MagicMock()
    mock_doc_ref.get.return_value.exists = True
    mock_admin_db.collection.return_value.document.return_value = mock_doc_ref

    response = await async_client.post(
        "/avistamentos/123",
        data={
            "registro": "123",          # readonly field — must be dropped
            "nome_popular": "Tubarão-tigre",
            "observacao": "",           # blank — must be dropped
            "local": "None",            # "None" sentinel — must be dropped
        },
        follow_redirects=False,
    )

    assert response.status_code == 303
    assert response.headers["location"] == "/avistamentos/123"
    mock_doc_ref.update.assert_called_once_with({"nome_popular": "Tubarão-tigre"})


@pytest.mark.asyncio
async def test_update_avistamento_form_all_fields_blank(
    async_client: AsyncClient, mock_admin_db
):
    mock_doc_ref = MagicMock()
    mock_doc_ref.get.return_value.exists = True
    mock_admin_db.collection.return_value.document.return_value = mock_doc_ref

    response = await async_client.post(
        "/avistamentos/123",
        data={"registro": "123", "observacao": ""},
        follow_redirects=False,
    )

    assert response.status_code == 303
    mock_doc_ref.update.assert_not_called()


@pytest.mark.asyncio
async def test_update_avistamento_form_not_found(async_client: AsyncClient, mock_admin_db):
    mock_doc_ref = MagicMock()
    mock_doc_ref.get.return_value.exists = False
    mock_admin_db.collection.return_value.document.return_value = mock_doc_ref

    response = await async_client.post(
        "/avistamentos/999", data={"nome_popular": "X"}, follow_redirects=False
    )
    assert response.status_code == 404
    mock_doc_ref.update.assert_not_called()


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


@pytest.mark.asyncio
async def test_delete_avistamento_form_not_found(async_client: AsyncClient, mock_admin_db):
    mock_doc = MagicMock()
    mock_doc.exists = False
    mock_admin_db.collection.return_value.document.return_value.get.return_value = mock_doc

    response = await async_client.post("/avistamentos/999/delete", follow_redirects=False)
    assert response.status_code == 404
    mock_admin_db.collection.return_value.document.return_value.delete.assert_not_called()
