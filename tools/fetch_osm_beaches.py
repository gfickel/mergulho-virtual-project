#!/usr/bin/env python3
"""
Fetch hand-traced beach polygons for Fernando de Noronha from OpenStreetMap
(natural=beach ways) via the Overpass API, and write them as points in the
same lat/lon shape places.json uses.

Output: tools/osm_beaches.json  ->  [{name, osm_id, points:[{lat,lon}, ...]}]

No hidden fallbacks: if every Overpass endpoint fails, or a way has no
geometry, it raises and the script crashes.

Usage:
    python3 tools/fetch_osm_beaches.py
    python3 tools/fetch_osm_beaches.py --endpoint https://overpass-api.de/api/interpreter
"""
import argparse
import json
import urllib.request
from pathlib import Path

REPO = Path(__file__).resolve().parents[1]
OUT = REPO / "tools" / "osm_beaches.json"

# Main-island bounding box (south, west, north, east).
BBOX = (-3.88, -32.47, -3.80, -32.37)

# Tried in order; first one that returns JSON wins. Each attempt is printed.
ENDPOINTS = [
    "https://maps.mail.ru/osm/tools/overpass/api/interpreter",
    "https://overpass-api.de/api/interpreter",
    "https://overpass.kumi.systems/api/interpreter",
]

QUERY = f"""[out:json][timeout:90];
way["natural"="beach"]({BBOX[0]},{BBOX[1]},{BBOX[2]},{BBOX[3]});
out geom;"""


def fetch(endpoints):
    for url in endpoints:
        print(f"trying {url} ...")
        req = urllib.request.Request(url, data=QUERY.encode("utf-8"),
                                     headers={"Content-Type": "text/plain"})
        raw = urllib.request.urlopen(req, timeout=95).read()
        if raw[:1] in (b"{", b"["):
            print(f"  ok ({len(raw)} bytes)")
            return json.loads(raw)
        print("  endpoint busy / non-JSON, trying next")
    raise RuntimeError("all Overpass endpoints failed")


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--endpoint", help="use only this Overpass endpoint")
    args = ap.parse_args()

    data = fetch([args.endpoint] if args.endpoint else ENDPOINTS)

    beaches = []
    for e in data["elements"]:
        if e["type"] != "way":
            continue
        name = e["tags"]["name"]  # crash if a beach way has no name
        points = [{"lat": g["lat"], "lon": g["lon"]} for g in e["geometry"]]
        if len(points) < 3:
            raise ValueError(f"{name}: only {len(points)} points")
        beaches.append({"name": name, "osm_id": e["id"], "points": points})

    beaches.sort(key=lambda b: b["name"])
    OUT.write_text(json.dumps(beaches, ensure_ascii=False, indent=2), encoding="utf-8")
    print(f"\nwrote {len(beaches)} beaches -> {OUT}")
    for b in beaches:
        print(f"  {len(b['points']):>3} pts  {b['name']}")


if __name__ == "__main__":
    main()
