from fastapi import FastAPI, Request
from fastapi.responses import HTMLResponse
from fastapi.staticfiles import StaticFiles
from fastapi.templating import Jinja2Templates
from google.cloud import firestore

# Initialize Firestore client
db = firestore.Client.from_service_account_json('./serviceAccountKey.json')

app = FastAPI()

app.mount("/static", StaticFiles(directory="static"), name="static")


templates = Jinja2Templates(directory="templates")


@app.get("/items/{id}", response_class=HTMLResponse)
async def read_item(request: Request, id: str):
    return templates.TemplateResponse(
        request=request, name="item.html", context={"id": id}
    )

@app.get("/")
async def root():
    return {"message": "Virtual Diving API"}

@app.get("/avistamentos")
async def get_avistamentos(page: int = 1, page_size: int = 10):
    """
    Retorna avistamentos paginados.

    - page: página (1, 2, 3, ...)
    - page_size: quantidade de itens por página
    """
    # Sanitiza parâmetros básicos
    page = max(page, 1)
    page_size = max(min(page_size, 100), 1)  # limita page_size entre 1 e 100

    offset = (page - 1) * page_size

    query = (
        db.collection("avistamentos")
        .order_by("registro")
        .offset(offset)
        .limit(page_size)
    )

    docs = query.stream()

    items = [doc.to_dict() for doc in docs]

    return {
        "page": page,
        "page_size": page_size,
        "count": len(items),
        "items": items,
    }

@app.post("/avistamentos/{registro}")
async def create_city(registro, body):
    json_data = json.loads(body)
    registro_ref = db.collection('avistamentos').document(registro)
    registro_ref.set(json_data)
    return {"message": "Avistamento criado com sucesso", "avistamento": json_data}

@app.get("/telemetry")
async def telemetry():
    return {"message": "Placeholder for shark monitoring"}
