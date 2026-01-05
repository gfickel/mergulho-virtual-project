from typing import Optional, List, Tuple, Dict, Any
from google.cloud import firestore
from database import db


def query_telemetria(
    page: int = 1,
    page_size: int = 10,
    oid: Optional[str] = None,
    date_start: Optional[int] = None,
    date_end: Optional[int] = None,
) -> Tuple[List[Dict[str, Any]], int, int, bool]:
    """
    Common function to query telemetry from Firestore with pagination and filters.
    Returns a tuple: (items, page, page_size, has_more)
    """
    # Sanitize parameters
    page = max(page, 1)
    page_size = max(min(page_size, 100), 1)

    offset = (page - 1) * page_size

    query = db.collection("telemetria").order_by("date")

    # Optional filters
    if oid is not None:
        query = query.where(filter=firestore.FieldFilter("oid", "==", oid))
    
    # Date range filters (assuming 'date' is timestamp int)
    if date_start is not None:
        query = query.where(filter=firestore.FieldFilter("date", ">=", int(date_start)))
    if date_end is not None:
        query = query.where(filter=firestore.FieldFilter("date", "<=", int(date_end)))

    query = query.offset(offset).limit(page_size)

    docs = query.stream()

    items = [doc.to_dict() for doc in docs]

    has_more = len(items) == page_size

    return items, page, page_size, has_more


def build_telemetria_url(
    page: int,
    page_size: int,
    oid: Optional[str] = None,
    date_start: Optional[int] = None,
    date_end: Optional[int] = None,
) -> str:
    """
    Builds the URL for the telemetry list with query parameters.
    """
    params = []
    params.append(f"page={page}")
    params.append(f"page_size={page_size}")
    if oid is not None:
        params.append(f"oid={oid}")
    if date_start is not None:
        params.append(f"date_start={date_start}")
    if date_end is not None:
        params.append(f"date_end={date_end}")

    return "/telemetria?" + "&".join(params)
