import datetime
import json
import os
from fastapi import APIRouter, Request, HTTPException, Header, Form, File, UploadFile
from fastapi.responses import HTMLResponse, JSONResponse, RedirectResponse
from typing import Optional, Dict, Any

from database import db
from config import templates
from services.avistamentos import query_avistamentos, build_avistamentos_url, count_avistamentos
from services.storage import generate_signed_url, upload_bytes
from services.image_processing import resize_image_preserving_exif, ImageDecodeError



router = APIRouter()


# Bucket layout for app-submitted sightings:
#   originals/<registro>.<ext>  — raw bytes from the device, EXIF intact
#   imagens/<registro>.jpg      — display variant, max 1600 px, JPEG q85, EXIF preserved
# The display path matches the existing read_avistamento lookup.
_ALLOWED_EXTS = {".jpg", ".jpeg", ".png", ".webp", ".heic", ".heif"}


@router.post("/avistamentos", status_code=201)
async def create_avistamento(
    photo: UploadFile = File(...),
    beach: Optional[str] = Form(None),
    timestamp: str = Form(...),
    species_guess: Optional[str] = Form(None),
    notes: Optional[str] = Form(None),
    idempotency_key: Optional[str] = Header(None, alias="Idempotency-Key"),
):
    """
    App-side sighting submission. Multipart form-data:
      - photo: image file (JPEG/PNG/WebP/HEIC)
      - beach, timestamp (RFC3339), species_guess, notes (form fields)
      - Idempotency-Key header: required, used as the Firestore doc id and bucket key

    Idempotent: a retry with the same Idempotency-Key returns 200 and the existing doc
    instead of creating a duplicate. The app's JobQueue uses one stable key per submission.
    """
    if not idempotency_key:
        raise HTTPException(status_code=400, detail="Idempotency-Key header is required")

    registro = idempotency_key
    doc_ref = db.collection("avistamentos").document(registro)
    existing = doc_ref.get()
    if existing.exists:
        return JSONResponse(
            {"registro": registro, "status": "already_exists", "avistamento": existing.to_dict()},
            status_code=200,
        )

    try:
        dt = datetime.datetime.fromisoformat(timestamp.replace("Z", "+00:00"))
    except ValueError:
        raise HTTPException(status_code=400, detail="timestamp must be ISO 8601 / RFC 3339")

    raw = await photo.read()
    if not raw:
        raise HTTPException(status_code=400, detail="photo is empty")

    try:
        display_bytes = resize_image_preserving_exif(raw, max_dim=1600, quality=85)
    except ImageDecodeError as e:
        raise HTTPException(status_code=415, detail=f"unsupported image format: {e}")

    ext = os.path.splitext(photo.filename or "")[1].lower()
    if ext not in _ALLOWED_EXTS:
        ext = ".jpg"

    upload_bytes(
        f"originals/{registro}{ext}",
        raw,
        photo.content_type or "application/octet-stream",
    )
    upload_bytes(f"imagens/{registro}.jpg", display_bytes, "image/jpeg")

    avistamento = {
        "registro": registro,
        "local": beach or "",
        "data_hora_iso": dt.isoformat(),
        "dia_registro": str(dt.day),
        "mes_registro": str(dt.month),
        "ano_registro": str(dt.year),
        "nome_popular": species_guess or "",
        "observacao": notes or "",
        "modo_registro": "app",
    }
    doc_ref.set(avistamento)

    return JSONResponse(
        {"registro": registro, "status": "created", "avistamento": avistamento},
        status_code=201,
    )


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
