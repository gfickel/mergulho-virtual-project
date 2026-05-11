"""
Populate the debug Firestore + local_storage with sample avistamentos so the
web UI and Unity client have something to render against a fresh emulator.

Idempotent: each seed entry uses a stable doc id (seed-NNN); re-running skips
entries that already exist.

Runnable two ways:
  - Auto on uvicorn startup when BACKEND_DEBUG=1 (wired up in main.py).
  - Manually:  cd src/backend && BACKEND_DEBUG=1 python -m scripts.seed_debug_data
"""

from __future__ import annotations

import datetime
import io
import logging
import os
import sys
from typing import Optional

from PIL import Image, ImageDraw, ImageFont

# Allow running as `python scripts/seed_debug_data.py` from backend/ — when invoked
# that way Python only adds `scripts/` to sys.path, not `backend/`, so the
# `from database import db` below would fail without this insert.
if __name__ == "__main__" and __package__ in (None, ""):
    sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))

from database import db  # noqa: E402
from services.storage import upload_bytes  # noqa: E402

logger = logging.getLogger("backend.seed")


# Six sightings spread across beaches/species/dates so list filters, the count
# endpoint, and the detail page all have something non-trivial to show.
SEED_AVISTAMENTOS = [
    {
        "registro": "seed-001",
        "local": "Praia do Sancho",
        "data_hora_iso": "2026-05-08T09:15:00-02:00",
        "nome_popular": "Tubarão-martelo",
        "observacao": "Cardume de 3 indivíduos próximos aos pináculos.",
        "color": (39, 94, 138),
    },
    {
        "registro": "seed-002",
        "local": "Baía dos Porcos",
        "data_hora_iso": "2026-05-09T11:42:00-02:00",
        "nome_popular": "Tubarão-tigre",
        "observacao": "Avistamento isolado, ~3 m de comprimento.",
        "color": (181, 137, 0),
    },
    {
        "registro": "seed-003",
        "local": "Praia do Leão",
        "data_hora_iso": "2026-05-09T15:05:00-02:00",
        "nome_popular": "Tubarão-limão",
        "observacao": "Patrulhando o costão.",
        "color": (203, 161, 53),
    },
    {
        "registro": "seed-004",
        "local": "Sueste Beach",
        "data_hora_iso": "2026-05-10T07:30:00-02:00",
        "nome_popular": "Tubarão-lixa",
        "observacao": "Descansando entre as pedras.",
        "color": (88, 110, 117),
    },
    {
        "registro": "seed-005",
        "local": "Atalaia Beach",
        "data_hora_iso": "2026-05-10T16:20:00-02:00",
        "nome_popular": "Tubarão-de-recife",
        "observacao": "Dupla cruzando a piscina natural.",
        "color": (38, 139, 122),
    },
    {
        "registro": "seed-006",
        "local": "Praia da Conceição",
        "data_hora_iso": "2026-05-11T08:55:00-02:00",
        "nome_popular": "Arraia-manta",
        "observacao": "Indivíduo grande, ~4 m de envergadura.",
        "color": (108, 113, 196),
    },
]

_IMAGE_SIZE = (1600, 1200)


def _make_placeholder_jpeg(label: str, color: tuple[int, int, int]) -> bytes:
    """Solid-color JPEG with the species/beach label baked in, so the seed data
    is visually distinguishable in the list/view templates."""
    img = Image.new("RGB", _IMAGE_SIZE, color)
    draw = ImageDraw.Draw(img)

    font: Optional[ImageFont.ImageFont]
    try:
        font = ImageFont.truetype("DejaVuSans-Bold.ttf", 64)
    except OSError:
        font = ImageFont.load_default()

    draw.text((60, 60), label, fill="white", font=font)
    draw.text(
        (60, _IMAGE_SIZE[1] - 80),
        "MERGULHO VIRTUAL · DEBUG SEED",
        fill=(255, 255, 255, 180),
        font=font,
    )

    buf = io.BytesIO()
    img.save(buf, format="JPEG", quality=85)
    return buf.getvalue()


def seed_avistamentos() -> int:
    """Insert any missing seed avistamentos. Returns the number newly created."""
    coll = db.collection("avistamentos")
    created = 0
    skipped = 0

    for entry in SEED_AVISTAMENTOS:
        registro = entry["registro"]
        if coll.document(registro).get().exists:
            skipped += 1
            continue

        label = f"{entry['nome_popular']}\n{entry['local']}"
        jpeg = _make_placeholder_jpeg(label, entry["color"])

        # Same blob layout as production (originals/ + imagens/). The display
        # path is what read_avistamento generates a URL for; the originals path
        # mirrors prod and exercises the local storage round-trip.
        upload_bytes(f"originals/{registro}.jpg", jpeg, "image/jpeg")
        upload_bytes(f"imagens/{registro}.jpg", jpeg, "image/jpeg")

        dt = datetime.datetime.fromisoformat(entry["data_hora_iso"])
        coll.document(registro).set(
            {
                "registro": registro,
                "local": entry["local"],
                "data_hora_iso": dt.isoformat(),
                "dia_registro": str(dt.day),
                "mes_registro": str(dt.month),
                "ano_registro": str(dt.year),
                "nome_popular": entry["nome_popular"],
                "observacao": entry["observacao"],
                "modo_registro": "seed",
            }
        )
        created += 1
        logger.info("[seed] created avistamentos/%s", registro)

    logger.info("[seed] done — created=%d skipped=%d", created, skipped)
    return created


if __name__ == "__main__":
    logging.basicConfig(level=logging.INFO)
    seed_avistamentos()
