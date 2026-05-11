"""Operator-facing telemetria endpoints (HTML).

Mounted with no prefix by api.api; behind IAP in prod.
"""

from datetime import datetime
from typing import Optional

from fastapi import APIRouter, Request
from fastapi.responses import JSONResponse

from config import templates
from services.telemetria import build_telemetria_url, count_telemetria, query_telemetria


router = APIRouter()


def _parse_date_param(date_str: Optional[str], is_end: bool = False) -> Optional[int]:
    """Accept either a numeric timestamp or YYYY-MM-DD; end-of-day for is_end."""
    if not date_str or not date_str.strip():
        return None
    try:
        return int(date_str)
    except ValueError:
        pass
    try:
        dt = datetime.strptime(date_str, "%Y-%m-%d")
        if is_end:
            dt = dt.replace(hour=23, minute=59, second=59)
        return int(dt.timestamp())
    except ValueError:
        return None


@router.get("/telemetria")
async def list_telemetria(
    request: Request,
    page: int = 1,
    page_size: int = 10,
    oid: Optional[str] = None,
    date_start: Optional[str] = None,
    date_end: Optional[str] = None,
):
    """Paginated HTML list of telemetry rows."""
    d_start = _parse_date_param(date_start)
    d_end = _parse_date_param(date_end, is_end=True)

    if oid is not None and not oid.strip():
        oid = None

    items, page, page_size, has_more = query_telemetria(
        page=page,
        page_size=page_size,
        oid=oid,
        date_start=d_start,
        date_end=d_end,
    )

    for item in items:
        if item.get("date"):
            try:
                dt = datetime.fromtimestamp(item["date"])
                item["date_str"] = dt.strftime("%d/%m/%Y %H:%M")
            except (ValueError, TypeError):
                item["date_str"] = str(item["date"])
        else:
            item["date_str"] = ""

    next_page = page + 1 if has_more else None
    prev_page = page - 1 if page > 1 else None

    next_page_url = (
        build_telemetria_url(next_page, page_size, oid, date_start, date_end)
        if next_page
        else None
    )
    prev_page_url = (
        build_telemetria_url(prev_page, page_size, oid, date_start, date_end)
        if prev_page
        else None
    )

    return templates.TemplateResponse(
        "telemetria/list.html",
        {
            "request": request,
            "items": items,
            "page": page,
            "page_size": page_size,
            "next_page_url": next_page_url,
            "prev_page_url": prev_page_url,
            "oid": oid,
            "date_start": date_start,
            "date_end": date_end,
        },
    )


@router.get("/telemetria/count")
async def get_telemetria_count(
    oid: Optional[str] = None,
    date_start: Optional[str] = None,
    date_end: Optional[str] = None,
):
    """JSON count for dashboard widgets."""
    d_start = _parse_date_param(date_start)
    d_end = _parse_date_param(date_end, is_end=True)
    if oid is not None and not oid.strip():
        oid = None
    total = count_telemetria(oid=oid, date_start=d_start, date_end=d_end)
    return JSONResponse({"count": total})
