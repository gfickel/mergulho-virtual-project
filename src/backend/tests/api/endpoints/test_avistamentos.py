
import pytest
from unittest.mock import MagicMock, patch
from httpx import AsyncClient

@pytest.fixture
def mock_db():
    with patch("api.endpoints.avistamentos.db") as mock:
        yield mock

@pytest.fixture
def mock_query_avistamentos():
    with patch("api.endpoints.avistamentos.query_avistamentos") as mock:
        yield mock

@pytest.mark.asyncio
async def test_list_avistamentos(async_client: AsyncClient, mock_query_avistamentos):
    mock_query_avistamentos.return_value = ([], 1, 10, False)
    
    response = await async_client.get("/avistamentos", headers={"Accept": "application/json"})
    
    assert response.status_code == 200
    data = response.json()
    assert data["items"] == []
    assert data["page"] == 1
    assert data["page_size"] == 10

@pytest.mark.asyncio
async def test_create_avistamento(async_client: AsyncClient, mock_db):
    registro_id = "123"
    payload = {"species": "Shark", "location": "Recife"}
    
    # Mock the document reference and set method
    mock_doc_ref = MagicMock()
    mock_db.collection.return_value.document.return_value = mock_doc_ref
    
    # We need to send body as string because the endpoint expects 'body' from path/query/form?
    # Wait, the endpoint signature is async def create_avistamento(registro, body).
    # 'body' is treated as a query param by FastAPI if not using Body/Pydantic model.
    # Let's verify how it behaves. If I pass query param ?body=... it works.
    
    import json
    body_str = json.dumps(payload)
    
    response = await async_client.post(f"/avistamentos/{registro_id}", params={"body": body_str})
    
    assert response.status_code == 200
    assert response.json()["message"] == "Avistamento criado com sucesso"
    mock_doc_ref.set.assert_called_once_with(payload)

@pytest.mark.asyncio
async def test_read_avistamento(async_client: AsyncClient, mock_db):
    registro_id = "123"
    mock_doc = MagicMock()
    mock_doc.exists = True
    mock_doc.to_dict.return_value = {"id": registro_id, "species": "Shark"}
    
    mock_db.collection.return_value.document.return_value.get.return_value = mock_doc
    
    response = await async_client.get(f"/avistamentos/{registro_id}", headers={"Accept": "application/json"})
    
    assert response.status_code == 200
    assert response.json() == {"id": registro_id, "species": "Shark"}

@pytest.mark.asyncio
async def test_read_avistamento_not_found(async_client: AsyncClient, mock_db):
    registro_id = "999"
    mock_doc = MagicMock()
    mock_doc.exists = False
    
    mock_db.collection.return_value.document.return_value.get.return_value = mock_doc
    
    response = await async_client.get(f"/avistamentos/{registro_id}", headers={"Accept": "application/json"})
    
    assert response.status_code == 404

@pytest.mark.asyncio
async def test_delete_avistamento(async_client: AsyncClient, mock_db):
    registro_id = "123"
    mock_doc = MagicMock()
    mock_doc.exists = True
    
    mock_db.collection.return_value.document.return_value.get.return_value = mock_doc
    
    response = await async_client.delete(f"/avistamentos/{registro_id}", params={"format": "json"})
    
    assert response.status_code == 200
    assert response.json()["message"] == "Avistamento deletado com sucesso"
    mock_db.collection.return_value.document.return_value.delete.assert_called_once()
