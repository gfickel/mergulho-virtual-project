"""Unity-bound avistamentos endpoints.

Mounted under /api/v1 by api.api. Returns JSON only — no content negotiation.
Will be App Check-protected in Stage 2 (the router-level dependency is added by
api.api when this module is included).
"""

import datetime
import os
from typing import Optional

from fastapi import APIRouter, File, Form, Header, HTTPException, UploadFile
from fastapi.responses import JSONResponse

from database import db
from services.avistamentos import count_avistamentos
from services.image_processing import ImageDecodeError, resize_image_preserving_exif
from services.storage import upload_bytes


router = APIRouter()


# Bucket layout for app-submitted sightings:
#   originals/<registro>.<ext>  — raw bytes from the device, EXIF intact
#   imagens/<registro>.jpg      — display variant, max 1600 px, JPEG q85, EXIF preserved
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


@router.get("/avistamentos/count")
async def get_avistamentos_count(
    dia_registro: Optional[int] = None,
    mes_registro: Optional[int] = None,
    ano_registro: Optional[int] = None,
):
    """Total sighting count, optionally filtered by date. Used by the AR HUD."""
    total = count_avistamentos(
        dia_registro=dia_registro,
        mes_registro=mes_registro,
        ano_registro=ano_registro,
    )
    return JSONResponse({"count": total})
