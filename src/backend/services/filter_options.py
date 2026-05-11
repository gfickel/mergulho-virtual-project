"""Distinct values for operator-UI filter dropdowns.

The `local` and `nome_popular` fields in Firestore don't match the Unity-side
canonical lists (places.json + AnimalDefs) — CSV-imported pre-app sightings
introduce variants and entries that the app never sees. Hard-coding from the
Unity assets would hide those records from the operator. So we read distinct
values straight out of Firestore.

Cost: one full-collection projected scan per refresh. Projection (`select()`)
keeps the read cheap by transferring only the two fields we need; each
document still counts as one Firestore read for billing, but at typical
operator-UI scale (low thousands of docs) this is sub-cent per refresh.

Cache: 5-minute TTL, in-process. Each Cloud Run instance builds its own
cache; that's fine for an IAP-gated admin UI with light traffic. Invalidate
explicitly via `invalidate_cache()` after writes that could introduce a new
value (e.g., admin edits via the avistamentos edit form).
"""

from __future__ import annotations

import logging
import time
from threading import Lock
from typing import Dict, List

from database import db


logger = logging.getLogger(__name__)

_CACHE_TTL_SECONDS = 300
_PROJECT_FIELDS = ["local", "nome_popular"]

_lock = Lock()
_cache: Dict[str, object] = {"data": None, "ts": 0.0}


def _scan() -> Dict[str, List[str]]:
    docs = db.collection("avistamentos").select(_PROJECT_FIELDS).stream()
    beaches: set[str] = set()
    species: set[str] = set()
    for doc in docs:
        d = doc.to_dict() or {}
        local = d.get("local")
        nome = d.get("nome_popular")
        if local:
            beaches.add(str(local).strip())
        if nome:
            species.add(str(nome).strip())
    return {
        "beaches": sorted((b for b in beaches if b), key=str.lower),
        "species": sorted((s for s in species if s), key=str.lower),
    }


def list_distinct_filter_values() -> Dict[str, List[str]]:
    """Return {'beaches': [...], 'species': [...]} from Firestore, cached."""
    now = time.time()
    with _lock:
        data = _cache["data"]
        ts = _cache["ts"]
        if data is not None and now - ts < _CACHE_TTL_SECONDS:
            return data  # type: ignore[return-value]

    # Scan outside the lock so concurrent requests don't serialize on a slow
    # Firestore round-trip. Worst case: two scans race during a cache miss;
    # both writes are equivalent so the last one wins harmlessly.
    try:
        fresh = _scan()
    except Exception as e:
        logger.warning("filter_options scan failed; reusing stale cache: %s", e)
        with _lock:
            # Fall back to the (possibly-stale) cached value; empty lists if
            # nothing has ever been cached.
            return _cache["data"] or {"beaches": [], "species": []}  # type: ignore[return-value]

    with _lock:
        _cache["data"] = fresh
        _cache["ts"] = now
    return fresh


def invalidate_cache() -> None:
    """Drop the cache. Call after writes that could introduce a new value."""
    with _lock:
        _cache["data"] = None
        _cache["ts"] = 0.0
