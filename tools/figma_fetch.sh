#!/usr/bin/env bash
# Fetch the Prototipos file's node tree + PNG renders of every top-level frame.
set -euo pipefail
FILE_KEY=8e8pBsY0T5hpMdpwoinoXI
OUT="${1:-$(dirname "$0")/../.figma-sync}"
TOKEN="${FIGMA_TOKEN:-$(grep -oP '(?<=FIGMA_TOKEN=).*' ~/.figma-token 2>/dev/null || true)}"
[ -n "$TOKEN" ] || { echo "no FIGMA_TOKEN (env or ~/.figma-token)" >&2; exit 1; }
mkdir -p "$OUT"
curl -sf -H "X-Figma-Token: $TOKEN" \
  "https://api.figma.com/v1/files/$FILE_KEY" > "$OUT/file.json"
python3 - "$OUT" <<'PY'
import json, sys, pathlib
out = pathlib.Path(sys.argv[1])
doc = json.loads((out / "file.json").read_text())
print("file:", doc.get("name"), "| last modified:", doc.get("lastModified"))
frames = []
for page in doc["document"]["children"]:
    print(f"\nPAGE: {page['name']}")
    for n in page.get("children", []):
        if n["type"] in ("FRAME", "COMPONENT", "COMPONENT_SET"):
            b = n.get("absoluteBoundingBox") or {}
            print(f"  {n['id']:>12}  {n['type']:<14} {int(b.get('width',0))}x{int(b.get('height',0))}  {n['name']}")
            frames.append(n["id"])
(out / "frame_ids.txt").write_text(",".join(frames))
print(f"\n{len(frames)} frames -> frame_ids.txt")
PY
IDS=$(cat "$OUT/frame_ids.txt")
[ -n "$IDS" ] || exit 0
curl -sf -H "X-Figma-Token: $TOKEN" \
  "https://api.figma.com/v1/images/$FILE_KEY?ids=$IDS&format=png&scale=2" > "$OUT/images.json"
python3 - "$OUT" <<'PY'
import json, sys, pathlib, urllib.request, re
out = pathlib.Path(sys.argv[1]); (out / "png").mkdir(exist_ok=True)
imgs = json.loads((out / "images.json").read_text())["images"]
for nid, url in imgs.items():
    if not url: continue
    p = out / "png" / (re.sub(r"[^0-9A-Za-z]", "_", nid) + ".png")
    urllib.request.urlretrieve(url, p); print("saved", p.name)
PY

# --- structural dump: one annotated layout tree per V2 frame ---
python3 - "$OUT" <<'PY'
import json, sys, pathlib
out = pathlib.Path(sys.argv[1]); st = out / "struct"; st.mkdir(exist_ok=True)
doc = json.loads((out / "file.json").read_text())
pages = [p for p in doc["document"]["children"] if p["name"] == "Protótipo"]
if not pages:
    print("no 'Protótipo' page — nothing to dump"); raise SystemExit
def hexof(c, o=1.0):
    a = c.get("a", 1) * o
    h = "#%02X%02X%02X" % tuple(round(c[k] * 255) for k in ("r", "g", "b"))
    return h if a >= .999 else f"{h}/{a:.2f}"
def paint(lst):
    r = []
    for f in lst or []:
        if not f.get("visible", True): continue
        t = f.get("type", "")
        r.append(hexof(f["color"], f.get("opacity", 1)) if t == "SOLID"
                 else "grad" if t.startswith("GRADIENT") else "img" if t == "IMAGE" else t)
    return ",".join(r)
def line(n, d):
    b = n.get("absoluteBoundingBox") or {}
    p = [f"{'  '*d}{n['name']}  <{n['type']}>"]
    if b: p.append(f"{round(b.get('width',0))}x{round(b.get('height',0))}")
    lm = n.get("layoutMode")
    if lm and lm != "NONE":
        pad = [round(n.get(k, 0)) for k in ("paddingTop","paddingRight","paddingBottom","paddingLeft")]
        p.append(f"AL:{lm[0]} gap:{n.get('itemSpacing',0)} pad:{'/'.join(map(str,pad))}")
        if n.get("primaryAxisAlignItems"): p.append(n["primaryAxisAlignItems"][:6])
        if n.get("counterAxisAlignItems"): p.append("x" + n["counterAxisAlignItems"][:6])
    if (f := paint(n.get("fills"))): p.append(f"fill:{f}")
    if (s := paint(n.get("strokes"))): p.append(f"stroke:{s}@{n.get('strokeWeight',1)}")
    cr = n.get("cornerRadius") or n.get("rectangleCornerRadii")
    if cr: p.append("r:" + (str(cr) if not isinstance(cr, list) else "/".join(str(round(x)) for x in cr)))
    for e in n.get("effects", []):
        if e.get("visible", True) and e["type"] == "DROP_SHADOW":
            p.append(f"shadow:{round(e['radius'])}"); break
    if n["type"] == "TEXT":
        s = n.get("style", {})
        p.append('"%s"' % n.get("characters", "")[:90])
        p.append(f"[{s.get('fontFamily')} {s.get('fontWeight')} {round(s.get('fontSize',0))}px "
                 f"ls{s.get('letterSpacing',0):.2f}]")
    return "  ".join(p)
for fr in pages[0]["children"]:
    if fr["type"] != "FRAME": continue
    buf = []
    def walk(n, d=0):
        if not n.get("visible", True):
            buf.append(f"{'  '*d}{n['name']}  <{n['type']}> [HIDDEN]"); return
        buf.append(line(n, d))
        for c in n.get("children", []): walk(c, d + 1)
    walk(fr)
    name = fr["name"].replace(" ", "_").replace("/", "-")
    (st / f"{name}__{fr['id'].replace(':','_')}.txt").write_text("\n".join(buf))
    print(f"struct: {name} ({len(buf)} lines)")
PY
