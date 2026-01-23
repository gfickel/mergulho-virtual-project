from datetime import datetime

from fastapi import APIRouter, Request, Header
from fastapi.responses import JSONResponse
from typing import Optional

from services.telemetria import query_telemetria, build_telemetria_url, count_telemetria
from config import templates

router = APIRouter()

@router.get("/telemetria")
async def list_telemetria(
    request: Request,
    page: int = 1,
    page_size: int = 10,
    oid: Optional[str] = None,
    date_start: Optional[str] = None,
    date_end: Optional[str] = None,
    format: Optional[str] = None,
    count: bool = False,
    accept: Optional[str] = Header(None),
):
    """
    Returns paginated telemetry data in HTML or JSON.

    For JSON, use:
    - ?format=json or
    - Header Accept: application/json
    """
    # Parse dates from string (handles empty strings from HTML forms)
    def parse_date_param(date_str: Optional[str], is_end: bool = False) -> Optional[int]:
        if not date_str or not date_str.strip():
            return None
        # Try numeric timestamp first
        try:
            return int(date_str)
        except ValueError:
            pass
        
        # Try YYYY-MM-DD
        try:
            dt = datetime.strptime(date_str, "%Y-%m-%d")
            if is_end:
                 # Set to end of day
                 dt = dt.replace(hour=23, minute=59, second=59)
            return int(dt.timestamp())
        except ValueError:
            return None

    d_start = parse_date_param(date_start)
    d_end = parse_date_param(date_end, is_end=True)

    # Sanitize OID
    if oid is not None and not oid.strip():
        oid = None

    if count:
        total = count_telemetria(
            oid=oid,
            date_start=d_start,
            date_end=d_end,
        )
        return JSONResponse({"count": total})

    items, page, page_size, has_more = query_telemetria(
        page=page,
        page_size=page_size,
        oid=oid,
        date_start=d_start,
        date_end=d_end,
    )

    # Decide format: JSON if format=json or Accept contains application/json (and not preferring html)
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
    
    # Format dates for HTML template
    for item in items:
        if item.get("date"):
            try:
                dt = datetime.fromtimestamp(item["date"])
                item["date_str"] = dt.strftime("%d/%m/%Y %H:%M")
            except (ValueError, TypeError):
                item["date_str"] = str(item["date"])
        else:
            item["date_str"] = ""

    # Return HTML
    next_page = page + 1 if has_more else None
    prev_page = page - 1 if page > 1 else None

    next_page_url = (
        build_telemetria_url(
            next_page, page_size, oid, date_start, date_end
        )
        if next_page
        else None
    )
    prev_page_url = (
        build_telemetria_url(
            prev_page, page_size, oid, date_start, date_end
        )
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
