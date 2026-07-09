#!/usr/bin/env python3
"""
Plot the Noronha beach polygons and debug the app's point-in-beach logic.

- x axis = longitude, y axis = latitude (the app's lon->x, lat->y convention).
- point_in_polygon() is an exact port of ReverseGeocoding.IsPointInQuadrilateral
  (even-odd ray casting) -- this is what the app ships today (strict "inside").
- beach_distance_m() returns 0 if the point is inside a polygon, else the meters
  to the nearest edge. "Within D meters of a beach" == "inside the beach buffered
  by D", but computed without ever building the buffered polygon -- so it works
  on the detailed OSM traces, not just the axis-aligned boxes.
- The "within (m)" slider is that threshold D. It applies to the PRIMARY layer
  (OSM if --osm, else the boxes) and its true buffer boundary is drawn as a
  dashed contour of the distance field.
- Clicking / typing a point prints the beach the app returns today, every
  overlapping box, and the nearest beach + its distance in meters.

No hidden fallbacks: malformed data or bad input raises and the script crashes.

Usage:
    python3 tools/beach_polygon_debug.py                       # boxes only
    python3 tools/beach_polygon_debug.py --osm                 # + OSM overlay
    python3 tools/beach_polygon_debug.py --osm --within 40     # start at 40 m
    python3 tools/beach_polygon_debug.py --osm -3.8331 -32.398 # test one lat,lon
"""
import argparse
import json
import math
from pathlib import Path

import numpy as np
import matplotlib.pyplot as plt
from matplotlib.path import Path as MplPath
from matplotlib.widgets import Slider, TextBox

REPO = Path(__file__).resolve().parents[1]
PLACES_JSON = REPO / "src/app/MergulhoVirtual/Assets/Resources/places.json"
OSM_JSON = REPO / "tools" / "osm_beaches.json"  # produced by fetch_osm_beaches.py

# Local flat-earth projection, good to sub-meter over Noronha's few km.
M_PER_DEG_LAT = 111_320.0
LAT0 = -3.85
KX = M_PER_DEG_LAT * math.cos(math.radians(LAT0))  # meters per degree of longitude
KY = M_PER_DEG_LAT                                  # meters per degree of latitude


def load_places(path):
    """Load a places-shaped file into [{name, poly=[(lon,lat), ...]}]. Crashes on bad data."""
    data = json.loads(path.read_text(encoding="utf-8"))
    places = []
    for p in data:
        poly = [(float(pt["lon"]), float(pt["lat"])) for pt in p["points"]]
        places.append({"name": p["name"], "poly": poly})
    return places


def point_in_polygon(px, py, poly):
    """Exact port of ReverseGeocoding.IsPointInQuadrilateral. poly = [(x,y), ...]."""
    if len(poly) < 3:
        raise ValueError("polygon needs >= 3 points")
    inside = False
    n = len(poly)
    j = n - 1
    for i in range(n):
        xi, yi = poly[i]
        xj, yj = poly[j]
        if ((yi > py) != (yj > py)) and (
            px < (xj - xi) * (py - yi) / (yj - yi) + xi
        ):
            inside = not inside
        j = i
    return inside


def _seg_dist_m(px, py, ax, ay, bx, by):
    """Distance (meters) from point to segment a->b, all already in the meter frame."""
    dx, dy = bx - ax, by - ay
    l2 = dx * dx + dy * dy
    t = 0.0 if l2 == 0 else max(0.0, min(1.0, ((px - ax) * dx + (py - ay) * dy) / l2))
    cx, cy = ax + t * dx, ay + t * dy
    return math.hypot(px - cx, py - cy)


def beach_distance_m(lat, lon, poly):
    """0 if (lon,lat) is inside poly, else meters to the nearest edge."""
    if point_in_polygon(lon, lat, poly):
        return 0.0
    px, py = lon * KX, lat * KY
    best = math.inf
    n = len(poly)
    for i in range(n):
        ax, ay = poly[i][0] * KX, poly[i][1] * KY
        bx, by = poly[(i + 1) % n][0] * KX, poly[(i + 1) % n][1] * KY
        d = _seg_dist_m(px, py, ax, ay, bx, by)
        if d < best:
            best = d
    return best


def matches_within(lat, lon, places, margin_m):
    """(dist_m, name) for beaches within margin_m of (lat,lon), nearest first."""
    out = [(beach_distance_m(lat, lon, pl["poly"]), pl["name"]) for pl in places]
    return sorted((d, n) for d, n in out if d <= margin_m)


def distance_field(layer, lons, lats):
    """Grid of meters-to-nearest-beach over (lons x lats); 0 inside any polygon."""
    xx, yy = np.meshgrid(lons, lats)
    pts = np.column_stack([xx.ravel(), yy.ravel()])
    xm, ym = pts[:, 0] * KX, pts[:, 1] * KY
    field = np.full(pts.shape[0], np.inf)
    for pl in layer:
        poly = np.array(pl["poly"])
        pm = np.column_stack([poly[:, 0] * KX, poly[:, 1] * KY])
        d = np.full(pts.shape[0], np.inf)
        for i in range(len(pm) - 1):  # poly is a closed ring; this covers every edge
            ax, ay = pm[i]
            bx, by = pm[i + 1]
            dx, dy = bx - ax, by - ay
            l2 = dx * dx + dy * dy
            if l2 == 0:
                dd = np.hypot(xm - ax, ym - ay)
            else:
                t = np.clip(((xm - ax) * dx + (ym - ay) * dy) / l2, 0.0, 1.0)
                dd = np.hypot(xm - (ax + t * dx), ym - (ay + t * dy))
            d = np.minimum(d, dd)
        d[MplPath(pl["poly"]).contains_points(pts)] = 0.0
        field = np.minimum(field, d)
    return xx, yy, field.reshape(xx.shape)


def report(lat, lon, places, osm, margin_m):
    """Resolve the point by nearest beach; print the pick and any clash."""
    strict = [pl["name"] for pl in places if point_in_polygon(lon, lat, pl["poly"])]
    print(f"\n  point  lat={lat:.6f}  lon={lon:.6f}   within={margin_m:.0f} m")
    print(f"  app today (boxes, first strict-inside): {strict[0] if strict else None}")
    if len(strict) > 1:
        print(f"    overlapping boxes: {strict}")

    layer = osm if osm is not None else places
    tag = "OSM" if osm is not None else "boxes"
    ranked = sorted((beach_distance_m(lat, lon, pl["poly"]), pl["name"]) for pl in layer)
    within = [(d, n) for d, n in ranked if d <= margin_m]
    nd, nn = ranked[0]
    winner = nn if nd <= margin_m else None

    print(f"  nearest {tag}: {nn}  ({nd:.0f} m)")
    print(f"  resolver picks: {winner}")
    if len(within) > 1:
        sd, sn = within[1]
        print(f"    clash: {len(within)} within {margin_m:.0f} m; nearest wins -> "
              f"{nn} beats {sn} by {sd - nd:.0f} m")
    return nd, nn, within


def main():
    ap = argparse.ArgumentParser(description=__doc__,
                                 formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("lat", nargs="?", type=float, help="latitude to test")
    ap.add_argument("lon", nargs="?", type=float, help="longitude to test")
    ap.add_argument("--within", type=float, default=0.0, help="initial threshold in meters")
    ap.add_argument("--places", type=Path, default=PLACES_JSON)
    ap.add_argument("--osm", action="store_true",
                    help="overlay OSM polygons (green) and buffer those instead")
    ap.add_argument("--osm-file", type=Path, default=OSM_JSON)
    args = ap.parse_args()

    if (args.lat is None) != (args.lon is None):
        raise SystemExit("give BOTH lat and lon, or neither")

    places = load_places(args.places)
    print(f"Loaded {len(places)} beaches from {args.places}")
    osm = None
    if args.osm:
        osm = load_places(args.osm_file)
        print(f"Loaded {len(osm)} OSM beaches from {args.osm_file}")

    layer = osm if osm is not None else places  # primary layer that gets buffered

    # Distance field is independent of the threshold, so compute it once.
    xs = [x for pl in places for x, _ in pl["poly"]]
    ys = [y for pl in places for _, y in pl["poly"]]
    pad = 0.03 * max(max(xs) - min(xs), max(ys) - min(ys))
    lons = np.linspace(min(xs) - pad, max(xs) + pad, 260)
    lats = np.linspace(min(ys) - pad, max(ys) + pad, 220)
    print("computing distance field ...")
    fxx, fyy, ffield = distance_field(layer, lons, lats)

    fig, ax = plt.subplots(figsize=(11, 9))
    plt.subplots_adjust(bottom=0.16)
    state = {"margin": args.within, "markers": []}

    def draw_polys():
        ax.clear()
        m = state["margin"]
        for pl in places:
            bx = [x for x, _ in pl["poly"]]
            by = [y for _, y in pl["poly"]]
            ax.plot(bx, by, color="steelblue", lw=1.0, alpha=0.5)
            ax.fill(bx, by, color="steelblue", alpha=0.07)
            cx, cy = sum(bx[:-1]) / (len(bx) - 1), sum(by[:-1]) / (len(by) - 1)
            ax.annotate(pl["name"], (cx, cy), fontsize=7, ha="center", alpha=0.75)
        if osm is not None:
            for pl in osm:
                gx = [x for x, _ in pl["poly"]]
                gy = [y for _, y in pl["poly"]]
                ax.plot(gx, gy, color="forestgreen", lw=1.6, alpha=0.9)
                ax.fill(gx, gy, color="forestgreen", alpha=0.12)
                cx, cy = sum(gx) / len(gx), sum(gy) / len(gy)
                ax.annotate(pl["name"], (cx, cy), fontsize=7, ha="center",
                            color="darkgreen", alpha=0.9,
                            xytext=(0, -9), textcoords="offset points")
        buf_color = "forestgreen" if osm is not None else "darkorange"
        if m > 0:
            ax.contour(fxx, fyy, ffield, levels=[m], colors=[buf_color],
                       linestyles="--", linewidths=1.4)
        ax.set_xlabel("longitude")
        ax.set_ylabel("latitude")
        tag = "OSM" if osm is not None else "boxes"
        ax.set_title(f"Noronha beaches  (dashed = {tag} buffered +{m:.0f} m)   "
                     "click to test a point")
        ax.grid(True, alpha=0.3)
        ax.set_aspect(1.0 / math.cos(math.radians(LAT0)))  # true shape at this latitude

    def mark(lat, lon):
        nd, nn, within = report(lat, lon, places, osm, state["margin"])
        matched = nd <= state["margin"]
        # gray = nothing within D; green = clean single pick; orange = clash resolved by nearest
        color = "gray" if not matched else ("orange" if len(within) > 1 else "green")
        (dot,) = ax.plot(lon, lat, "x", color=color, ms=12, mew=3)
        label = "none" if not matched else (
            f"{nn}  (nearest of {len(within)})" if len(within) > 1 else nn)
        txt = ax.annotate(label, (lon, lat), fontsize=8, color=color,
                          xytext=(6, 6), textcoords="offset points")
        state["markers"] += [dot, txt]
        fig.canvas.draw_idle()

    draw_polys()

    def on_click(event):
        if event.inaxes is ax and event.xdata is not None:
            mark(event.ydata, event.xdata)  # ydata=lat, xdata=lon

    fig.canvas.mpl_connect("button_press_event", on_click)

    ax_slider = plt.axes([0.15, 0.06, 0.55, 0.03])
    slider = Slider(ax_slider, "within (m)", 0.0, 300.0, valinit=args.within, valstep=5.0)

    def on_slide(val):
        state["margin"] = val
        state["markers"] = []       # ax.clear() in draw_polys drops the marker artists
        draw_polys()
        fig.canvas.draw_idle()

    slider.on_changed(on_slide)

    ax_box = plt.axes([0.80, 0.06, 0.15, 0.04])
    box = TextBox(ax_box, "lat,lon ", initial="")

    def on_submit(text):
        text = text.strip()
        if not text:
            return
        lat_s, lon_s = text.split(",")
        mark(float(lat_s), float(lon_s))

    box.on_submit(on_submit)

    if args.lat is not None:
        mark(args.lat, args.lon)

    plt.show()


if __name__ == "__main__":
    main()
