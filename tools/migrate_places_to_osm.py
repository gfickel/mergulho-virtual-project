#!/usr/bin/env python3
"""
Rewrite places.json's coarse geocoder bounding boxes with precise coastlines.

Source geometry, in priority order:
  1. OSM natural=beach ways        -> tools/osm_beaches.json (fetch_osm_beaches.py)
  2. OSM natural=beach relation    -> Cacimba do Padre (multipolygon 5476220 outer ring)
  3. OSM point node + buffer       -> Sharks Cove, Buraco da Raquel (only points exist in OSM)
  4. geocoder center + buffer      -> Quixaba (no OSM geometry at all; shrink its old box)

Only the `points` array of each beach is touched. Every other key -- name,
displayName, imageName, description, photoCredit and anything hand-added later --
survives verbatim, because the rewrite copies the entry's own `.items()` rather
than rebuilding a fixed field list; file order is preserved too. 13 beaches get exact OSM
outlines, Cacimba gets its real relation outline (14 precise total), and the 3
un-traceable spots become small point-polygons centered on their best-known
coordinate -- far tighter than the ~1 km boxes they replace, so the new
nearest-distance resolver no longer sees distance-0 ties with their neighbors.

No hidden fallbacks: an unmapped beach or missing OSM source raises and crashes.

Usage:
    python3 tools/migrate_places_to_osm.py            # dry run: prints the diff
    python3 tools/migrate_places_to_osm.py --apply    # write places.json in place
"""
import argparse
import json
import math
from pathlib import Path

REPO = Path(__file__).resolve().parents[1]
PLACES = REPO / "src/app/MergulhoVirtual/Assets/Resources/places.json"
OSM = REPO / "tools" / "osm_beaches.json"

M_PER_DEG_LAT = 111_320.0

# places.json name -> osm_beaches.json name (13 exact-outline matches)
OSM_NAME = {
    "Praia do Sancho": "Praia do Sancho",
    "Baía dos Porcos": "Praia dos Porcos",
    "Praia do Bode": "Praia do Bode",
    "Praia do Americano": "Praia do Americano",
    "Boldró Beach": "Praia do Boldró",
    "Praia da Conceição": "Praia da Conceição",
    "Praia do Meio": "Praia do Meio",
    "Praia do Cachorro": "Praia do Cachorro",
    "Praia do Porto de Santo Antônio Noronha": "Praia do Porto",
    "Enseada da Caieira": "Praia da Caieiras",
    "Atalaia Beach": "Praia do Atalaia",
    "Sueste Beach": "Praia do Sueste",
    "Praia do Leão": "Praia do Leão",
}

# Cacimba do Padre: OSM multipolygon 5476220, outer member way 30671435 (39 pts).
# Fetched once via `relation(5476220); out geom;` -- inlined so this script needs no network.
CACIMBA_OUTER = [
    (-3.85064, -32.43990), (-3.85056, -32.44018), (-3.85070, -32.44024),
    (-3.85066, -32.44036), (-3.85052, -32.44048), (-3.85052, -32.44057),
    (-3.85041, -32.44069), (-3.85045, -32.44070), (-3.85053, -32.44067),
    (-3.85056, -32.44070), (-3.85055, -32.44080), (-3.85054, -32.44081),
    (-3.85025, -32.44105), (-3.85023, -32.44098), (-3.85019, -32.44090),
    (-3.85000, -32.44061), (-3.84985, -32.44034), (-3.84968, -32.44006),
    (-3.84945, -32.43967), (-3.84928, -32.43930), (-3.84916, -32.43897),
    (-3.84906, -32.43863), (-3.84898, -32.43806), (-3.84894, -32.43759),
    (-3.84892, -32.43727), (-3.84888, -32.43701), (-3.84883, -32.43677),
    (-3.84890, -32.43673), (-3.84889, -32.43677), (-3.84896, -32.43681),
    (-3.84905, -32.43699), (-3.84928, -32.43713), (-3.84982, -32.43789),
    (-3.84998, -32.43808), (-3.85024, -32.43863), (-3.85028, -32.43874),
    (-3.85038, -32.43908), (-3.85058, -32.43956), (-3.85064, -32.43990),
]

# Point-polygon centers (lat, lon) + half-extent (m). No OSM outline exists.
POINT_POLYS = {
    # Enseada dos Tubarões node 5729312891 -- the snorkel cove behind the port.
    # 60 m so it splits the 138 m gap to Buraco da Raquel symmetrically (no mutual tie).
    "Sharks Cove": ((-3.83319, -32.39788), 60.0),
    # Buraco do Raquel node 3237520609 -- small tidal pool spot, ~138 m from Sharks Cove.
    "Buraco da Raquel": ((-3.83439, -32.39759), 60.0),
    # No OSM feature; keep the geocoder's own center for "Praia da Quixaba". 35 m is the
    # largest square that stays clear of the tightly packed Bode/Americano/Cacimba outlines.
    "Praia da Quixaba": ((-3.84884, -32.43577), 35.0),
}


def load_osm():
    data = json.loads(OSM.read_text(encoding="utf-8"))
    return {b["name"]: [(p["lon"], p["lat"]) for p in b["points"]] for b in data}


def square_ring(lat, lon, half_m):
    """Closed axis-aligned ring (lon,lat) of side 2*half_m centered on (lat,lon)."""
    dlat = half_m / M_PER_DEG_LAT
    dlon = half_m / (M_PER_DEG_LAT * math.cos(math.radians(lat)))
    return [
        (lon - dlon, lat - dlat), (lon + dlon, lat - dlat),
        (lon + dlon, lat + dlat), (lon - dlon, lat + dlat),
        (lon - dlon, lat - dlat),
    ]


def new_ring(name, osm):
    if name in OSM_NAME:
        return [(x, y) for x, y in osm[OSM_NAME[name]]], "osm-way"
    if name == "Praia da Cacimba do Padre":
        return [(lon, lat) for lat, lon in CACIMBA_OUTER], "osm-relation"
    if name in POINT_POLYS:
        (lat, lon), half = POINT_POLYS[name]
        return square_ring(lat, lon, half), f"point+{half:.0f}m"
    raise KeyError(f"no geometry rule for beach {name!r}")


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--apply", action="store_true", help="write places.json in place")
    args = ap.parse_args()

    osm = load_osm()
    places = json.loads(PLACES.read_text(encoding="utf-8"))

    out = []
    print(f"{'beach':40} {'old':>4} -> {'new':>4}  source")
    for p in places:
        ring, src = new_ring(p["name"], osm)
        entry = {k: (v if k != "points" else [{"lat": lat, "lon": lon} for lon, lat in ring])
                 for k, v in p.items()}
        if "points" not in entry:  # defensive: keep points even if original lacked it
            entry["points"] = [{"lat": lat, "lon": lon} for lon, lat in ring]
        out.append(entry)
        print(f"{p['name']:40} {len(p['points']):>4} -> {len(ring):>4}  {src}")

    if args.apply:
        PLACES.write_text(json.dumps(out, ensure_ascii=False, indent=2) + "\n",
                          encoding="utf-8")
        print(f"\nwrote {PLACES}")
    else:
        print("\ndry run -- rerun with --apply to write places.json")


if __name__ == "__main__":
    main()
