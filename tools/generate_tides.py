#!/usr/bin/env python3
"""Generate one year of hourly tide heights for Fernando de Noronha.

Output: src/app/MergulhoVirtual/Assets/Resources/tides_noronha.json
Re-run yearly to refresh the rolling 1-year window.

Production prediction path: pyTMD + GOT5.6 (Goddard Ocean Tide model from
NASA GSFC, ~5 cm typical accuracy where altimetry resolves the location).
GOT5.6 supplements GOT5.5 with extra third-degree constituents — both must
be downloaded. NetCDF model files (~250 MB total) live in pyTMD's default
cache (`~/.cache/pytmd/GOT5.5/`, `~/.cache/pytmd/GOT5.6/`); see SETUP below.

Heights are referenced to mean sea level (MSL) — both positive and negative
values appear. Brazilian DHN tide tables use chart datum (LAT) instead, all
positive. The Unity consumer treats heights as a flat shape, so MSL is fine;
shift the values up by ~½ tidal range if you ever need chart-datum display.

SETUP (one-time):
  1) Create venv and install deps:
       cd tools
       python3 -m venv .venv
       .venv/bin/pip install -r requirements.txt
  2) Download model files via pyTMD's NASA fetcher (~30 min on a slow link):
       .venv/bin/python -c "import pyTMD.datasets; \\
         pyTMD.datasets.fetch_gsfc_got('GOT5.5'); \\
         pyTMD.datasets.fetch_gsfc_got('GOT5.6')"
  3) Run: .venv/bin/python generate_tides.py
"""
import json
import sys
from datetime import datetime, timedelta, timezone
from pathlib import Path

import numpy as np
import pyTMD.compute
import timescale.time

STATION = "Fernando de Noronha"
LAT = -3.85
LON = -32.44
SAMPLES_PER_DAY = 24
DAYS = 366  # round up so a leap year is fully covered
MODEL = "GOT5.6"


def predict(start_utc, n_hours):
    """Predict ocean tide heights at (LON, LAT) for n_hours hourly samples
    starting at start_utc. Returns (heights_m_list, label)."""
    end_utc = start_utc + timedelta(hours=n_hours - 1)
    times = timescale.time.date_range(
        start_utc.strftime("%Y-%m-%d %H:%M:%S"),
        end_utc.strftime("%Y-%m-%d %H:%M:%S"),
        1, "h",
    )
    heights_m = pyTMD.compute.tide_elevations(
        np.atleast_1d(LON),
        np.atleast_1d(LAT),
        times,
        model=MODEL,
        crs=4326,
        type="time series",
        standard="datetime",
        method="linear",
    )
    arr = np.asarray(heights_m).reshape(-1)
    if np.ma.isMaskedArray(heights_m):
        arr = np.ma.filled(heights_m, np.nan).reshape(-1)
    if np.any(np.isnan(arr)):
        raise RuntimeError(
            f"pyTMD returned NaN heights at ({LAT}, {LON}). "
            f"{MODEL} likely doesn't resolve this point — verify the model "
            "files are present under ~/.cache/pytmd/ and that the lat/lon "
            "falls in open ocean grid cells.")
    return [round(float(h), 4) for h in arr], f"pyTMD + {MODEL}"


def main():
    start = datetime.now(timezone.utc).replace(
        hour=0, minute=0, second=0, microsecond=0
    )
    valid_until = start + timedelta(days=DAYS)
    n = SAMPLES_PER_DAY * DAYS

    heights, path = predict(start, n)

    out = {
        "station": STATION,
        "lat": LAT,
        "lon": LON,
        "generated_at": start.isoformat().replace("+00:00", "Z"),
        "valid_until": valid_until.isoformat().replace("+00:00", "Z"),
        "samples_per_day": SAMPLES_PER_DAY,
        "start_date": start.date().isoformat(),
        "heights_m": heights,
    }

    repo_root = Path(__file__).resolve().parent.parent
    out_path = repo_root / "src/app/MergulhoVirtual/Assets/Resources/tides_noronha.json"
    out_path.parent.mkdir(parents=True, exist_ok=True)
    out_path.write_text(json.dumps(out, separators=(",", ":")))

    size_kb = out_path.stat().st_size / 1024
    print(f"Wrote {out_path}")
    print(f"  path         = {path}")
    print(f"  samples      = {n}")
    print(f"  size         = {size_kb:.1f} KB")
    print(f"  generated_at = {out['generated_at']}")
    print(f"  valid_until  = {out['valid_until']}")
    print(f"  range_m      = [{min(heights):+.3f}, {max(heights):+.3f}]")


if __name__ == "__main__":
    main()
