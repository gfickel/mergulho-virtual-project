#!/usr/bin/env python3
"""Parse the official DHN (Brazilian Navy) tide-table PDF for Fernando de Noronha
into a structured JSON of `{date: [[HHMM, height_m_LAT], ...]}`.

Use case: ground-truth comparison for `generate_tides.py`'s pyTMD/GOT5.6
predictions. Heights in the DHN PDF are referenced to chart datum (LAT) — the
PDF header prints `Nível Médio 1.28 m`, which is the MSL→LAT offset for this
station. Subtract 1.28 m from DHN heights to compare against pyTMD's MSL output.

SETUP:
  Requires `pdftotext` (Debian/Ubuntu: `apt install poppler-utils`).

USAGE:
  ./parse_dhn_tide_table.py <input.pdf> [output.json]
  Defaults: input  = src/app/MergulhoVirtual/tabua_mare_noronha_2026_marinha.pdf
            output = tools/dhn_tides_2026.json

Layout of the source PDF (one year per file, 3 pages):
- Each page has 4 month-columns. Page 1 = Jan-Apr, page 2 = May-Aug, page 3 = Sep-Dec.
- Each month column has 2 sub-columns: days 01-16 left, 17-31 right.
  → 8 sub-columns per page, each 19 chars wide after `pdftotext -layout`.
- Each day-cell spans 4 lines:
    line 1: "DD  HHMM A.AA"   (day number + 1st tide)
    line 2: "WWW HHMM A.AA"   (weekday + 2nd tide)
    line 3: "    HHMM A.AA"   (3rd tide; sometimes a moon-phase glyph)
    line 4: "    HHMM A.AA"   (4th tide; missing when the next tide is past midnight)
- Lunar-phase glyphs become text artifacts that don't match the HHMM A.AA regex
  and are silently dropped; the day number is sourced from the leading column,
  not from the glyph line, so this is safe.

If you need to re-target a different year, update YEAR + the page list at
the top of `main()` (the day count per month is computed from `calendar`,
so leap years just work).
"""
import calendar
import json
import re
import subprocess
import sys
from pathlib import Path

YEAR = 2026

PAGE_MONTHS = [
    ["Janeiro", "Fevereiro", "Março", "Abril"],         # page 1
    ["Maio", "Junho", "Julho", "Agosto"],               # page 2
    ["Setembro", "Outubro", "Novembro", "Dezembro"],    # page 3
]
MONTH_NUM = {
    "Janeiro": 1, "Fevereiro": 2, "Março": 3, "Abril": 4,
    "Maio": 5, "Junho": 6, "Julho": 7, "Agosto": 8,
    "Setembro": 9, "Outubro": 10, "Novembro": 11, "Dezembro": 12,
}

CELL_WIDTH = 19  # each sub-column is 19 chars wide in pdftotext -layout output
N_COLS = 8       # 4 months × 2 sub-columns

# HHMM A.AA — height may be negative (rare; would happen near LAT itself).
TIDE_RE = re.compile(r"\b(\d{4})\s+(-?\d{1,2}\.\d{2})")
DAY_RE = re.compile(r"^\s*(\d{1,2})\s")


def pdf_to_layout_text(pdf_path: Path) -> str:
    """Run `pdftotext -layout` and return the result as a string."""
    result = subprocess.run(
        ["pdftotext", "-layout", str(pdf_path), "-"],
        check=True, capture_output=True, text=True,
    )
    return result.stdout


def parse_layout_text(text: str) -> dict[str, list[tuple[str, float]]]:
    """Slice the layout text into day-cells and extract tide events per date."""
    # Split into pages on the page-number line. Page numbers in this PDF happen
    # to be 64/65/66 (book numbering) — match any 2-3 digit number alone on a line.
    pages = re.split(r"\n\s*\d{2,3}\s*\n", text)
    pages = [p for p in pages if "ARQUIPÉLAGO" in p]
    if len(pages) != 3:
        raise RuntimeError(f"expected 3 pages, got {len(pages)}")

    days: dict[str, list[tuple[str, float]]] = {}

    for page_idx, page_text in enumerate(pages):
        months = PAGE_MONTHS[page_idx]
        lines = page_text.split("\n")

        # Skip header rows up to the "HORA ALT(m)" row.
        data_start = next(
            (i + 1 for i, line in enumerate(lines)
             if "HORA ALT(m)" in line and line.count("HORA") >= 4),
            None,
        )
        if data_start is None:
            raise RuntimeError(f"no HORA ALT(m) header found on page {page_idx + 1}")

        # Stop at the page footer.
        data_lines: list[str] = []
        for line in lines[data_start:]:
            if "DG6-63" in line or "Original" in line:
                break
            data_lines.append(line)

        # Group into 4-line blocks separated by blank lines.
        blocks: list[list[str]] = []
        current: list[str] = []
        for line in data_lines:
            if line.strip() == "":
                if current:
                    blocks.append(current)
                    current = []
            else:
                current.append(line)
        if current:
            blocks.append(current)

        # Each block is one row of 8 day-cells. Block N contains:
        #   left sub-col  (cols 0,2,4,6) → day N
        #   right sub-col (cols 1,3,5,7) → day N + 16
        for block_idx, block in enumerate(blocks):
            block_row = block_idx + 1
            for col in range(N_COLS):
                cell_lines = [
                    line[col * CELL_WIDTH : (col + 1) * CELL_WIDTH]
                    if col * CELL_WIDTH < len(line) else ""
                    for line in block
                ]

                m = DAY_RE.match(cell_lines[0]) if cell_lines else None
                if not m:
                    continue
                day_num = int(m.group(1))

                month_name = months[col // 2]
                month = MONTH_NUM[month_name]

                expected = block_row if col % 2 == 0 else block_row + 16
                if day_num != expected:
                    print(
                        f"WARN page{page_idx + 1} col{col} block{block_row}: "
                        f"day_num={day_num} expected={expected}",
                        file=sys.stderr,
                    )
                    continue

                days_in_month = calendar.monthrange(YEAR, month)[1]
                if day_num > days_in_month:
                    continue

                cell_text = "\n".join(cell_lines)
                tides = TIDE_RE.findall(cell_text)
                date_iso = f"{YEAR}-{month:02d}-{day_num:02d}"
                days[date_iso] = [(hhmm, float(alt)) for hhmm, alt in tides]

    return days


def validate(days: dict) -> None:
    expected = {
        f"{YEAR}-{m:02d}-{d:02d}"
        for m in range(1, 13)
        for d in range(1, calendar.monthrange(YEAR, m)[1] + 1)
    }
    missing = expected - days.keys()
    extra = days.keys() - expected
    print(f"Parsed {len(days)} days. Expected {len(expected)}.")
    if missing:
        print(f"  MISSING: {sorted(missing)[:10]}{'...' if len(missing) > 10 else ''}")
    if extra:
        print(f"  EXTRA:   {sorted(extra)}")
    counts: dict[int, int] = {}
    for v in days.values():
        counts[len(v)] = counts.get(len(v), 0) + 1
    print(f"  entries-per-day distribution: {sorted(counts.items())}")


def main():
    repo_root = Path(__file__).resolve().parent.parent
    default_pdf = repo_root / "src/app/MergulhoVirtual/tabua_mare_noronha_2026_marinha.pdf"
    default_out = Path(__file__).resolve().parent / "dhn_tides_2026.json"

    pdf_path = Path(sys.argv[1]) if len(sys.argv) > 1 else default_pdf
    out_path = Path(sys.argv[2]) if len(sys.argv) > 2 else default_out

    if not pdf_path.exists():
        print(f"ERROR: PDF not found at {pdf_path}", file=sys.stderr)
        sys.exit(1)

    text = pdf_to_layout_text(pdf_path)
    days = parse_layout_text(text)
    validate(days)

    out_path.write_text(
        json.dumps({k: days[k] for k in sorted(days)}, indent=2, ensure_ascii=False)
    )
    print(f"\nWrote {out_path}")


if __name__ == "__main__":
    main()
