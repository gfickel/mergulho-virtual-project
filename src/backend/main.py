import json

from fastapi import FastAPI, Request, HTTPException, Header
from fastapi.responses import HTMLResponse, JSONResponse
from fastapi.staticfiles import StaticFiles
from fastapi.templating import Jinja2Templates
from google.cloud import firestore
from typing import Optional

# Initialize Firestore client
db = firestore.Client.from_service_account_json('./serviceAccountKey.json')

app = FastAPI()

app.mount("/static", StaticFiles(directory="static"), name="static")


templates = Jinja2Templates(directory="templates")


def query_avistamentos(
    page: int = 1,
    page_size: int = 10,
    dia_registro: Optional[int] = None,
    mes_registro: Optional[int] = None,
    ano_registro: Optional[int] = None,
):
    """
    Função comum para buscar avistamentos do Firestore com paginação e filtros.
    Retorna uma tupla: (items, page, page_size, has_more)
    """
    # Sanitiza parâmetros básicos
    page = max(page, 1)
    page_size = max(min(page_size, 100), 1)  # limita page_size entre 1 e 100

    offset = (page - 1) * page_size

    query = db.collection("avistamentos").order_by("registro")

    # Filtros opcionais por data de registro (valores vêm como int; no Firestore são strings)
    if dia_registro is not None:
        query = query.where("dia_registro", "==", str(dia_registro))
    if mes_registro is not None:
        query = query.where("mes_registro", "==", str(mes_registro))
    if ano_registro is not None:
        query = query.where("ano_registro", "==", str(ano_registro))

    query = query.offset(offset).limit(page_size)
    docs = query.stream()
    items = [doc.to_dict() for doc in docs]

    # Verifica se há mais itens (se retornou exatamente page_size, pode haver mais)
    has_more = len(items) == page_size

    return items, page, page_size, has_more


def build_avistamentos_url(
    page: int,
    page_size: int,
    dia_registro: Optional[int] = None,
    mes_registro: Optional[int] = None,
    ano_registro: Optional[int] = None,
) -> str:
    """
    Constrói a URL para a lista de avistamentos com os parâmetros de query.
    """
    params = []
    params.append(f"page={page}")
    params.append(f"page_size={page_size}")
    if dia_registro is not None:
        params.append(f"dia_registro={dia_registro}")
    if mes_registro is not None:
        params.append(f"mes_registro={mes_registro}")
    if ano_registro is not None:
        params.append(f"ano_registro={ano_registro}")
    
    return "/avistamentos?" + "&".join(params)


@app.get("/")
async def root():
    return {"message": "Virtual Diving API"}

@app.get("/avistamentos")
async def list_avistamentos(
    request: Request,
    page: int = 1,
    page_size: int = 10,
    dia_registro: Optional[int] = None,
    mes_registro: Optional[int] = None,
    ano_registro: Optional[int] = None,
    format: Optional[str] = None,
    accept: Optional[str] = Header(None),
):
    """
    Retorna avistamentos paginados em HTML ou JSON.
    
    Por padrão retorna HTML para navegadores. Para JSON, use:
    - ?format=json ou
    - Header Accept: application/json
    """
    items, page, page_size, has_more = query_avistamentos(
        page=page,
        page_size=page_size,
        dia_registro=dia_registro,
        mes_registro=mes_registro,
        ano_registro=ano_registro,
    )

    # Decide o formato: JSON se format=json ou Accept contém application/json
    return_json = (
        format == "json"
        or (accept and "application/json" in accept and "text/html" not in accept)
    )

    if return_json:
        return JSONResponse(
            {
                "page": page,
                "page_size": page_size,
                "count": len(items),
                "items": items,
            }
        )

    # Retorna HTML
    next_page = page + 1 if has_more else None
    prev_page = page - 1 if page > 1 else None

    next_page_url = (
        build_avistamentos_url(
            next_page, page_size, dia_registro, mes_registro, ano_registro
        )
        if next_page
        else None
    )
    prev_page_url = (
        build_avistamentos_url(
            prev_page, page_size, dia_registro, mes_registro, ano_registro
        )
        if prev_page
        else None
    )

    return templates.TemplateResponse(
        "avistamentos/list.html",
        {
            "request": request,
            "items": items,
            "page": page,
            "page_size": page_size,
            "next_page_url": next_page_url,
            "prev_page_url": prev_page_url,
            "dia_registro": dia_registro,
            "mes_registro": mes_registro,
            "ano_registro": ano_registro,
        },
    )


@app.post("/avistamentos/{registro}")
async def create_avistamento(registro, body):
    json_data = json.loads(body)
    registro_ref = db.collection("avistamentos").document(registro)
    registro_ref.set(json_data)
    return {"message": "Avistamento criado com sucesso", "avistamento": json_data}


@app.get("/avistamentos/{registro}")
async def read_avistamento(
    request: Request,
    registro: str,
    format: Optional[str] = None,
    accept: Optional[str] = Header(None),
):
    """
    Retorna um avistamento específico em HTML ou JSON.
    
    Por padrão retorna HTML para navegadores. Para JSON, use:
    - ?format=json ou
    - Header Accept: application/json
    """
    doc_ref = db.collection("avistamentos").document(registro)
    doc = doc_ref.get()

    if not doc.exists:
        raise HTTPException(status_code=404, detail="Avistamento não encontrado")

    avistamento = doc.to_dict()

    # Decide o formato: JSON se format=json ou Accept contém application/json
    return_json = (
        format == "json"
        or (accept and "application/json" in accept and "text/html" not in accept)
    )

    if return_json:
        return JSONResponse(avistamento)

    return templates.TemplateResponse(
        request=request,
        name="avistamentos/view.html",
        context={"request": request, "registro": registro, "avistamento": avistamento},
    )

@app.get("/telemetry")
async def telemetry():
    return {"message": "Placeholder for shark monitoring"}
