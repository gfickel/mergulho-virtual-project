#!/usr/bin/env python3
"""Build the app-consumed hourly tide JSON from the parsed DHN ground-truth events.

Pipeline:
    parse_dhn_tide_table.py  : PDF → tools/dhn_tides_<year>.json (events)
    build_app_tides.py       : events → src/.../Resources/tides_noronha.json (hourly)

Heights in the produced JSON are LAT (chart datum) — same convention as the
official Brazilian Navy table, which is what local users expect to see (all
positive, "0 m" = lowest astronomical tide). The published `Nível Médio` for
Bahia de Santo Antônio is 1.28 m above LAT — rendered as a dashed reference
line in the sparkline.

Interpolation: cosine fit between adjacent (HHMM, height) extrema. Standard
tide-curve interpolation: smooth, derivative=0 at peaks (so peaks stay peaks
under hourly resampling), midpoint = mean of the two extrema.

Year boundaries: the DHN PDF is one calendar year. To produce a smooth curve
through Jan 1 00:00 UTC and Dec 31 23:00 UTC, we mirror-extrapolate a single
half-cycle off each end (synthetic event of opposite extreme at the same
half-period as the first/last observed gap). The first ~6 h of Jan 1 and last
~6 h of Dec 31 are therefore approximate; everything between is anchored to
real DHN extrema.

USAGE:
  ./build_app_tides.py [events.json] [output.json]
  Defaults: events = tools/dhn_tides_2026.json
            output = src/app/MergulhoVirtual/Assets/Resources/tides_noronha.json
"""
import json
import math
import sys
from datetime import datetime, timedelta, timezone
from pathlib import Path

LOCAL_TZ = timezone(timedelta(hours=-2))  # Fernando de Noronha = UTC-2 (no DST)
SAMPLES_PER_DAY = 24
STATION = "Fernando de Noronha"
LAT, LON = -3.85, -32.44


def load_events(events_path: Path):
    """Returns (year, [(utc_datetime, height_lat_m), ...]) sorted by time."""
    raw = json.loads(events_path.read_text())
    events = []
    years = set()
    for date_iso, day_events in raw.items():
        y, m, d = (int(x) for x in date_iso.split("-"))
        years.add(y)
        for hhmm, h in day_events:
            local = datetime(y, m, d, int(hhmm[:2]), int(hhmm[2:]), tzinfo=LOCAL_TZ)
            events.append((local.astimezone(timezone.utc), float(h)))
    if len(years) != 1:
        raise RuntimeError(f"events span multiple years: {sorted(years)}")
    events.sort()
    return years.pop(), events


def label_kinds(events):
    """For each event, decide H or L by comparing to its neighbors. DHN events
    strictly alternate, so we tag from the first event using sign-of-derivative
    and verify the alternation downstream."""
    kinds = []
    for i, (_t, h) in enumerate(events):
        prev_h = events[i - 1][1] if i > 0 else None
        next_h = events[i + 1][1] if i < len(events) - 1 else None
        if prev_h is not None and next_h is not None:
            kinds.append("H" if h > prev_h and h > next_h else "L")
        elif prev_h is not None:
            kinds.append("H" if h > prev_h else "L")
        elif next_h is not None:
            kinds.append("H" if h > next_h else "L")
        else:
            kinds.append("?")
    # Sanity: events should strictly alternate H/L within a day; warn if not.
    for i in range(1, len(kinds)):
        if kinds[i] == kinds[i - 1]:
            print(f"WARN: consecutive {kinds[i]} at {events[i - 1][0]} → {events[i][0]} "
                  f"(h={events[i - 1][1]:.2f} → {events[i][1]:.2f})", file=sys.stderr)
    return kinds


def extend_boundaries(events):
    """Mirror-extrapolate one half-cycle at each end so cosine interp can
    cover the full calendar year. The synthetic event adopts the opposite
    extreme's height and a matching half-period offset."""
    if len(events) < 2:
        raise RuntimeError("need at least 2 events to extrapolate")
    t0, h0 = events[0]
    t1, h1 = events[1]
    events.insert(0, (t0 - (t1 - t0), h1))

    tn, hn = events[-1]
    tnm1, hnm1 = events[-2]
    events.append((tn + (tn - tnm1), hnm1))
    return events


def hourly_heights(events, year):
    """Sample hourly UTC heights from year-01-01 00:00 UTC through year-12-31 23:00 UTC."""
    start = datetime(year, 1, 1, 0, 0, tzinfo=timezone.utc)
    end = datetime(year, 12, 31, 23, 0, tzinfo=timezone.utc)
    n_hours = int((end - start).total_seconds() / 3600) + 1

    heights = []
    j = 0  # cursor into events; events[j] <= t < events[j+1]
    for k in range(n_hours):
        t = start + timedelta(hours=k)
        while j + 1 < len(events) and events[j + 1][0] <= t:
            j += 1
        if j >= len(events) - 1:
            heights.append(events[-1][1])
            continue
        ta, ha = events[j]
        tb, hb = events[j + 1]
        period_s = (tb - ta).total_seconds()
        if period_s <= 0:
            heights.append(ha)
            continue
        frac = (t - ta).total_seconds() / period_s
        h = (ha + hb) / 2 + (ha - hb) / 2 * math.cos(math.pi * frac)
        heights.append(h)
    return start, heights


def main():
    repo_root = Path(__file__).resolve().parent.parent
    default_events = Path(__file__).resolve().parent / "dhn_tides_2026.json"
    default_out = repo_root / "src/app/MergulhoVirtual/Assets/Resources/tides_noronha.json"

    events_path = Path(sys.argv[1]) if len(sys.argv) > 1 else default_events
    out_path = Path(sys.argv[2]) if len(sys.argv) > 2 else default_out

    if not events_path.exists():
        print(f"ERROR: events JSON not found at {events_path}", file=sys.stderr)
        print("Run parse_dhn_tide_table.py first.", file=sys.stderr)
        sys.exit(1)

    year, events = load_events(events_path)
    n_real = len(events)
    kinds = label_kinds(events)  # "H" or "L" per real event, in order
    extended = extend_boundaries(list(events))  # don't mutate `events`; we still need it pristine
    start_utc, heights = hourly_heights(extended, year)
    end_utc = start_utc + timedelta(hours=len(heights))

    out = {
        "station": STATION,
        "lat": LAT,
        "lon": LON,
        "datum": "LAT",
        "nivel_medio_m": 1.28,
        "source": "DHN tide table — cosine-interpolated between official extrema",
        "events_count": n_real,
        "generated_at": datetime.now(timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ"),
        "valid_until": end_utc.strftime("%Y-%m-%dT%H:%M:%SZ"),
        "samples_per_day": SAMPLES_PER_DAY,
        "start_date": start_utc.date().isoformat(),
        "heights_m": [round(h, 4) for h in heights],
        # Original DHN extrema, in UTC. Consumed by TideService for accurate
        # next-high/next-low display (the hourly heights_m above can only resolve
        # peak times to HH:00; the events list preserves the published HH:MM).
        "events": [
            {
                "utc": t.strftime("%Y-%m-%dT%H:%M:%SZ"),
                "h": round(h, 2),
                "k": k,
            }
            for (t, h), k in zip(events, kinds)
        ],
    }

    out_path.parent.mkdir(parents=True, exist_ok=True)
    out_path.write_text(json.dumps(out, separators=(",", ":")))
    size_kb = out_path.stat().st_size / 1024
    print(f"Wrote {out_path}")
    print(f"  source       = DHN events JSON ({n_real} extrema)")
    print(f"  samples      = {len(heights)} hourly samples")
    print(f"  size         = {size_kb:.1f} KB")
    print(f"  start_date   = {out['start_date']} (UTC)")
    print(f"  valid_until  = {out['valid_until']}")
    print(f"  range_m      = [{min(heights):+.3f}, {max(heights):+.3f}]  (LAT, all positive)")


if __name__ == "__main__":
    main()
