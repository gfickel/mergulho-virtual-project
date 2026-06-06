#!/usr/bin/env python3
"""Generate firestore.indexes.json from a declarative list of query patterns.

Firestore requires a composite index for every unique combination of
equality-filtered fields + order_by field. Maintaining that by hand sprawls
fast — this script enumerates all subsets of each declared filter set,
appends the sort field, and emits the Firebase-CLI format.

Workflow when adding a filter to the operator UI:
    1. Add the field name to the relevant pattern's `filter_fields`.
    2. Run this script: `python3 tools/generate_firestore_indexes.py`.
    3. `firebase deploy --only firestore:indexes`.

Stdlib only — no venv. Run from the repo root or any cwd; the output path
is resolved relative to this script's location.
"""

from __future__ import annotations

import itertools
import json
from pathlib import Path
from typing import List, Dict, Any


# Each pattern describes one (collection, sort) tuple plus all the fields the
# operator UI can filter on. The script generates 2^N - 1 composite indexes
# per pattern, covering every non-empty subset of filter_fields combined with
# the sort field. Single-field equality filters where the filter field equals
# the sort field don't need composites (Firestore auto-indexes them); we still
# emit them and let Firebase deduplicate during deploy — simpler than
# special-casing.
QUERY_PATTERNS: List[Dict[str, Any]] = [
    {
        "collection": "avistamentos",
        "filter_fields": [
            "ano_registro",
            "mes_registro",
            "dia_registro",
            "local",
            "nome_popular",
        ],
        "sort_field": "registro",
        "sort_order": "ASCENDING",
    },
    {
        "collection": "telemetria",
        # `date` is filtered with range operators (>=, <=); since it also IS
        # the sort field, range-on-sort-field is auto-indexed. We only need
        # composites for OTHER fields combined with date as the sort field.
        "filter_fields": ["oid"],
        "sort_field": "date",
        "sort_order": "ASCENDING",
    },
]


def build_indexes() -> List[Dict[str, Any]]:
    indexes: List[Dict[str, Any]] = []
    for pattern in QUERY_PATTERNS:
        collection = pattern["collection"]
        filters = pattern["filter_fields"]
        sort_field = pattern["sort_field"]
        sort_order = pattern["sort_order"]

        for r in range(1, len(filters) + 1):
            for subset in itertools.combinations(filters, r):
                fields = [
                    {"fieldPath": f, "order": "ASCENDING"} for f in subset
                ]
                fields.append({"fieldPath": sort_field, "order": sort_order})
                indexes.append(
                    {
                        "collectionGroup": collection,
                        "queryScope": "COLLECTION",
                        "fields": fields,
                    }
                )
    return indexes


def main() -> None:
    indexes = build_indexes()
    payload = {"indexes": indexes, "fieldOverrides": []}
    out = Path(__file__).resolve().parents[1] / "firestore.indexes.json"
    out.write_text(json.dumps(payload, indent=2) + "\n")

    by_collection: Dict[str, int] = {}
    for idx in indexes:
        by_collection[idx["collectionGroup"]] = by_collection.get(idx["collectionGroup"], 0) + 1
    print(f"Wrote {len(indexes)} composite indexes to {out}")
    for coll, n in by_collection.items():
        print(f"  {coll}: {n}")


if __name__ == "__main__":
    main()
