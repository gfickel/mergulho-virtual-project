"""Operator-facing avistamentos endpoints (HTML).

Mounted with no prefix by api.api; the admin Cloud Run service is fronted by
IAP, so these routes are protected at the infra layer rather than in code.
"""

from datetime import datetime
from typing import Annotated, Any, Dict, Optional

from fastapi import APIRouter, HTTPException, Request
from fastapi.responses import RedirectResponse
from pydantic import BeforeValidator

from config import templates
from database import db
from services.avistamentos import (
    build_avistamentos_url,
    count_avistamentos,
    query_avistamentos,
)
from services.filter_options import list_distinct_filter_values
from services.storage import find_video_blob, generate_signed_url


router = APIRouter()


# The HTML filter form submits empty strings for blank number inputs
# (e.g. ?dia_registro=&mes_registro=); without this coercion FastAPI rejects
# them with int_parsing errors instead of treating them as "no filter."
OptionalIntFromForm = Annotated[
    Optional[int],
    BeforeValidator(lambda v: None if v == "" else v),
]

# Same coercion for select fields that submit "" when "Todos" is picked.
OptionalStrFromForm = Annotated[
    Optional[str],
    BeforeValidator(lambda v: None if v == "" else v),
]


@router.get("/avistamentos")
async def list_avistamentos(
    request: Request,
    page: int = 1,
    page_size: int = 10,
    dia_registro: OptionalIntFromForm = None,
    mes_registro: OptionalIntFromForm = None,
    ano_registro: OptionalIntFromForm = None,
    local: OptionalStrFromForm = None,
    nome_popular: OptionalStrFromForm = None,
):
    """Paginated HTML list of avistamentos with filter form."""
    filter_kwargs = dict(
        dia_registro=dia_registro,
        mes_registro=mes_registro,
        ano_registro=ano_registro,
        local=local,
        nome_popular=nome_popular,
    )
    total = count_avistamentos(**filter_kwargs)
    # Clamp page_size up front so total_pages matches what query_avistamentos
    # will actually use (it also clamps page_size to [1, 100] internally).
    page_size = max(min(page_size, 100), 1)
    total_pages = max(1, (total + page_size - 1) // page_size)
    page = max(1, min(page, total_pages))

    items, page, page_size, has_more = query_avistamentos(
        page=page,
        page_size=page_size,
        **filter_kwargs,
    )

    # signed-URL generation is local (crypto sign, no network) so it's cheap per row;
    # <img onerror> in the template falls back when the object is missing.
    for a in items:
        try:
            a["image_url"] = generate_signed_url(f"imagens/{a['registro']}.jpg")
        except Exception:
            a["image_url"] = None

    next_page = page + 1 if has_more else None
    prev_page = page - 1 if page > 1 else None

    next_page_url = (
        build_avistamentos_url(next_page, page_size, **filter_kwargs)
        if next_page
        else None
    )
    prev_page_url = (
        build_avistamentos_url(prev_page, page_size, **filter_kwargs)
        if prev_page
        else None
    )

    distinct = list_distinct_filter_values()

    return templates.TemplateResponse(
        "avistamentos/list.html",
        {
            "request": request,
            "items": items,
            "page": page,
            "page_size": page_size,
            "next_page_url": next_page_url,
            "prev_page_url": prev_page_url,
            "total": total,
            "total_pages": total_pages,
            "dia_registro": dia_registro,
            "mes_registro": mes_registro,
            "ano_registro": ano_registro,
            "local": local,
            "nome_popular": nome_popular,
            # Newest-first list for the Ano dropdown. Static range keeps this
            # cheap; Firestore has no efficient distinct-year aggregation and
            # the operator UI doesn't need it to be data-derived.
            "year_options": list(range(datetime.now().year, 1999, -1)),
            "beach_options": distinct["beaches"],
            "species_options": distinct["species"],
        },
    )


@router.get("/avistamentos/{registro}")
async def read_avistamento(request: Request, registro: str):
    """HTML detail page for a single avistamento."""
    doc_ref = db.collection("avistamentos").document(registro)
    doc = doc_ref.get()

    if not doc.exists:
        raise HTTPException(status_code=404, detail="Avistamento não encontrado")

    avistamento = doc.to_dict()

    image_filename = f"imagens/{registro}.jpg"
    try:
        image_url = generate_signed_url(image_filename)
    except Exception as e:
        print(f"Erro ao gerar URL assinada: {e}")
        image_url = None

    # Some registros are videos instead of photos,
    # stored by convention at videos/<registro>.<ext>.
    video_url = None
    try:
        video_blob = find_video_blob(registro)
        if video_blob:
            video_url = generate_signed_url(video_blob)
    except Exception as e:
        print(f"Erro ao gerar URL assinada do vídeo: {e}")

    return templates.TemplateResponse(
        request=request,
        name="avistamentos/view.html",
        context={
            "request": request,
            "registro": registro,
            "avistamento": avistamento,
            "image_url": image_url,
            "video_url": video_url,
        },
    )


@router.get("/avistamentos/{registro}/edit")
async def edit_avistamento_form(request: Request, registro: str):
    """HTML edit form for a single avistamento."""
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


@router.post("/avistamentos/{registro}")
async def update_avistamento_form(registro: str, request: Request):
    """Form submit from edit.html. Redirects back to the view page."""
    doc_ref = db.collection("avistamentos").document(registro)
    doc = doc_ref.get()

    if not doc.exists:
        raise HTTPException(status_code=404, detail="Avistamento não encontrado")

    form_data = await request.form()
    update_data: Dict[str, Any] = {}
    for key, value in form_data.items():
        # Ignore empty fields, "None" sentinel, and the readonly registro field.
        if key != "registro" and value and value != "None" and value != "":
            update_data[key] = value

    if update_data:
        doc_ref.update(update_data)

    return RedirectResponse(url=f"/avistamentos/{registro}", status_code=303)


@router.post("/avistamentos/{registro}/delete")
async def delete_avistamento_form(registro: str):
    """Form submit from the view page's danger zone. Redirects to the list."""
    doc_ref = db.collection("avistamentos").document(registro)
    doc = doc_ref.get()

    if not doc.exists:
        raise HTTPException(status_code=404, detail="Avistamento não encontrado")

    doc_ref.delete()

    return RedirectResponse(url="/avistamentos", status_code=303)
