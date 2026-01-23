import json
from fastapi import APIRouter, Request, HTTPException, Header, Form
from fastapi.responses import HTMLResponse, JSONResponse, RedirectResponse
from typing import Optional, Dict, Any

from database import db
from config import templates
from services.avistamentos import query_avistamentos, build_avistamentos_url, count_avistamentos
from services.storage import generate_signed_url



router = APIRouter()


@router.get("/avistamentos")
async def list_avistamentos(
    request: Request,
    page: int = 1,
    page_size: int = 10,
    dia_registro: Optional[int] = None,
    mes_registro: Optional[int] = None,
    ano_registro: Optional[int] = None,
    format: Optional[str] = None,
    count: bool = False,
    accept: Optional[str] = Header(None),
):
    """
    Retorna avistamentos paginados em HTML ou JSON.

    Por padrão retorna HTML para navegadores. Para JSON, use:
    - ?format=json ou
    - Header Accept: application/json
    """
    if count:
        total = count_avistamentos(
            dia_registro=dia_registro,
            mes_registro=mes_registro,
            ano_registro=ano_registro,
        )
        return JSONResponse({"count": total})

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


@router.post("/avistamentos/{registro}")
async def create_avistamento(registro, body):
    json_data = json.loads(body)
    registro_ref = db.collection("avistamentos").document(registro)
    registro_ref.set(json_data)
    return {"message": "Avistamento criado com sucesso", "avistamento": json_data}


@router.get("/avistamentos/{registro}")
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

    # Gera URL assinada para a imagem
    # Assumindo que o nome do arquivo é imagens/{registro}.jpg
    image_filename = f"imagens/{registro}.jpg"
    try:
        image_url = generate_signed_url(image_filename)
    except Exception as e:
        print(f"Erro ao gerar URL assinada: {e}")
        image_url = None

    # Decide o formato: JSON se format=json ou Accept contém application/json
    return_json = (
        format == "json"
        or (accept and "application/json" in accept and "text/html" not in accept)
    )

    if return_json:
        response_data = avistamento.copy()
        response_data["image_url"] = image_url
        return JSONResponse(response_data)

    return templates.TemplateResponse(
        request=request,
        name="avistamentos/view.html",
        context={
            "request": request,
            "registro": registro,
            "avistamento": avistamento,
            "image_url": image_url,
        },
    )


@router.get("/avistamentos/{registro}/edit")
async def edit_avistamento_form(request: Request, registro: str):
    """
    Exibe o formulário de edição de um avistamento.
    """
    doc_ref = db.collection("avistamentos").document(registro)
    doc = doc_ref.get()

    if not doc.exists:
        raise HTTPException(status_code=404, detail="Avistamento não encontrado")

    avistamento = doc.to_dict()

    return templates.TemplateResponse(
        request=request,
        name="avistamentos/edit.html",
        context={"request": request, "registro": registro, "avistamento": avistamento},
    )


@router.put("/avistamentos/{registro}")
async def update_avistamento(
    registro: str,
    avistamento_data: Dict[str, Any],
    format: Optional[str] = None,
    accept: Optional[str] = Header(None),
):
    """
    Atualiza um avistamento existente.

    Aceita JSON no body. Para HTML, redireciona após atualização.
    """
    doc_ref = db.collection("avistamentos").document(registro)
    doc = doc_ref.get()

    if not doc.exists:
        raise HTTPException(status_code=404, detail="Avistamento não encontrado")

    # Atualiza o documento
    doc_ref.update(avistamento_data)

    # Busca o documento atualizado
    updated_doc = doc_ref.get()
    updated_avistamento = updated_doc.to_dict()

    # Decide o formato: JSON se format=json ou Accept contém application/json
    return_json = (
        format == "json"
        or (accept and "application/json" in accept and "text/html" not in accept)
    )

    if return_json:
        return JSONResponse(
            {"message": "Avistamento atualizado com sucesso", "avistamento": updated_avistamento}
        )

    # Para HTML, redireciona para a visualização
    return RedirectResponse(url=f"/avistamentos/{registro}", status_code=303)


@router.post("/avistamentos/{registro}")
async def update_avistamento_form(registro: str, request: Request):
    """
    Atualiza um avistamento via formulário HTML.
    """
    doc_ref = db.collection("avistamentos").document(registro)
    doc = doc_ref.get()

    if not doc.exists:
        raise HTTPException(status_code=404, detail="Avistamento não encontrado")

    # Constrói o dicionário com os dados do formulário (apenas campos não vazios)
    form_data = await request.form()
    update_data = {}
    for key, value in form_data.items():
        # Ignora campos vazios, "None" como string, e o campo registro (não deve ser atualizado)
        if key != "registro" and value and value != "None" and value != "":
            update_data[key] = value

    # Atualiza o documento apenas se houver dados para atualizar
    if update_data:
        doc_ref.update(update_data)

    # Redireciona para a visualização
    return RedirectResponse(url=f"/avistamentos/{registro}", status_code=303)


@router.delete("/avistamentos/{registro}")
async def delete_avistamento(
    registro: str,
    format: Optional[str] = None,
    accept: Optional[str] = Header(None),
):
    """
    Remove um avistamento.

    - JSON: DELETE /avistamentos/{registro}?format=json
    - HTML: redireciona para a lista após excluir.
    """
    doc_ref = db.collection("avistamentos").document(registro)
    doc = doc_ref.get()

    if not doc.exists:
        raise HTTPException(status_code=404, detail="Avistamento não encontrado")

    doc_ref.delete()

    # Decide o formato: JSON se format=json ou Accept contém application/json
    return_json = (
        format == "json"
        or (accept and "application/json" in accept and "text/html" not in accept)
    )

    if return_json:
        return JSONResponse({"message": "Avistamento deletado com sucesso", "registro": registro})

    # Para HTML, redireciona para a lista
    return RedirectResponse(url="/avistamentos", status_code=303)


@router.post("/avistamentos/{registro}/delete")
async def delete_avistamento_form(registro: str):
    """
    Remove um avistamento via formulário HTML (POST).
    """
    doc_ref = db.collection("avistamentos").document(registro)
    doc = doc_ref.get()

    if not doc.exists:
        raise HTTPException(status_code=404, detail="Avistamento não encontrado")

    doc_ref.delete()

    return RedirectResponse(url="/avistamentos", status_code=303)
