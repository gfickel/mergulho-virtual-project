#!/usr/bin/env python3
"""Populate AnimalDef ScriptableObjects with Wikipedia descriptions, lead images,
and Commons credits.

Reads:  src/app/MergulhoVirtual/Assets/Resources/Animals/*.asset
Writes: same files (description, imageName, photoCredit fields, only when empty)
        src/app/MergulhoVirtual/Assets/Resources/Animals/<imageName>.<ext>

Usage:
    python3 tools/populate_animals.py            # dry-run (default)
    python3 tools/populate_animals.py --apply
    python3 tools/populate_animals.py --apply --force  # overwrite existing fields

Strategy mirrors tools/populate_beaches.py: for each AnimalDef whose fields are
empty (or under --force), try a list of candidate Wikipedia titles (scientific
binomial first — those have the most reliable coverage), pulling the lead
extract for the description and the lead image (with Commons attribution) for
the thumbnail. Stdlib only.
"""
from __future__ import annotations

import argparse
import json
import re
import sys
import time
import unicodedata
import uuid
from html.parser import HTMLParser
from pathlib import Path
from typing import Optional
from urllib.error import HTTPError
from urllib.parse import urlencode
from urllib.request import Request, urlopen

REPO_ROOT = Path(__file__).resolve().parent.parent
ANIMALS_DIR = REPO_ROOT / "src/app/MergulhoVirtual/Assets/Resources/Animals"
SPRITE_META_TEMPLATE = ANIMALS_DIR / "tiger_shark.jpg.meta"  # any existing sprite .meta works

USER_AGENT = (
    "MergulhoVirtual-AnimalPopulator/1.0 "
    "(https://github.com/; guilhermefickel@gmail.com)"
)
THUMB_WIDTH = 1280
REQUEST_TIMEOUT = 30
SLEEP_BETWEEN = 1.5

# Per-asset Wikipedia title candidates, scientific names first. The asset's
# m_Name (filename without .asset) is the lookup key. The displayName from the
# .asset is appended automatically as a last-ditch try.
ARTICLE_TITLE_CANDIDATES: dict[str, list[str]] = {
    "tiger_shark": [
        "Galeocerdo cuvier",
        "Tubarão-tigre",
        "Tiger shark",
    ],
    "hammerhead": [
        "Sphyrna mokarran",
        "Tubarão-martelo-gigante",
        "Great hammerhead",
    ],
    "lemon_shark": [
        "Negaprion brevirostris",
        "Tubarão-limão",
        "Lemon shark",
    ],
    "nurse_shark": [
        "Ginglymostoma cirratum",
        "Tubarão-lixa",
        "Nurse shark",
    ],
    "reef_shark": [
        "Carcharhinus acronotus",
        "Tubarão-bico-fino",
        "Blacknose shark",
        "Rhizoprionodon porosus",
    ],
}


# ---------- http ----------

def http_get_json(url: str, retries: int = 1) -> Optional[dict]:
    for attempt in range(retries + 1):
        try:
            req = Request(url, headers={"User-Agent": USER_AGENT, "Accept": "application/json"})
            with urlopen(req, timeout=REQUEST_TIMEOUT) as resp:
                return json.loads(resp.read().decode("utf-8"))
        except HTTPError as e:
            if e.code == 429 and attempt < retries:
                wait = 30
                print(f"  ! 429 rate-limited, sleeping {wait}s…", file=sys.stderr)
                time.sleep(wait)
                continue
            print(f"  ! HTTP {e.code} for {url}", file=sys.stderr)
            return None
        except Exception as e:
            print(f"  ! request failed ({e}) for {url}", file=sys.stderr)
            return None
    return None


def http_get_bytes(url: str) -> Optional[bytes]:
    try:
        req = Request(url, headers={"User-Agent": USER_AGENT})
        with urlopen(req, timeout=REQUEST_TIMEOUT) as resp:
            return resp.read()
    except Exception as e:
        print(f"  ! download failed ({e}) for {url}", file=sys.stderr)
        return None


# ---------- text utils ----------

class _HTMLStripper(HTMLParser):
    def __init__(self) -> None:
        super().__init__()
        self._chunks: list[str] = []

    def handle_data(self, data: str) -> None:
        self._chunks.append(data)

    def get_text(self) -> str:
        return "".join(self._chunks)


def strip_html(html: str) -> str:
    s = _HTMLStripper()
    s.feed(html or "")
    text = s.get_text()
    return re.sub(r"\s+", " ", text).strip()


def first_sentences(text: str, max_chars: int = 480) -> str:
    text = text.strip()
    if len(text) <= max_chars:
        return text
    cut = text[:max_chars]
    m = re.search(r"[\.\!\?](?!.*[\.\!\?])", cut)
    if m:
        return cut[: m.end()].strip()
    return cut.rsplit(" ", 1)[0].rstrip(",;: ") + "…"


# ---------- wikipedia article lookup ----------

def fetch_article(lang: str, title: str) -> Optional[dict]:
    api = f"https://{lang}.wikipedia.org/w/api.php"
    params = {
        "action": "query",
        "format": "json",
        "redirects": "1",   # let species redirects resolve (e.g. binomial -> common name)
        "prop": "extracts|pageimages|pageprops",
        "exintro": "1",
        "explaintext": "1",
        "piprop": "original|name|thumbnail",
        "pithumbsize": str(THUMB_WIDTH),
        "titles": title,
    }
    data = http_get_json(f"{api}?{urlencode(params)}")
    time.sleep(SLEEP_BETWEEN)
    if not data:
        return None
    pages = data.get("query", {}).get("pages", {})
    if not pages:
        return None
    page = next(iter(pages.values()))
    if "missing" in page or page.get("pageid", 0) <= 0:
        return None
    pageprops = page.get("pageprops") or {}
    if "disambiguation" in pageprops:
        return None
    extract = (page.get("extract") or "").strip()
    if not extract:
        return None
    image_title = page.get("pageimage")
    image_url = None
    thumb = page.get("thumbnail")
    if thumb and thumb.get("source"):
        image_url = thumb["source"]
    elif page.get("original") and page["original"].get("source"):
        image_url = page["original"]["source"]
    return {
        "extract": extract,
        "image_url": image_url,
        "image_title": image_title,  # bare 'Foo.jpg', no 'File:' prefix
        "title": page.get("title", title),
    }


def resolve_article(asset_key: str, display_name: str) -> Optional[dict]:
    candidates = list(ARTICLE_TITLE_CANDIDATES.get(asset_key, []))
    if display_name and display_name not in candidates:
        candidates.append(display_name)
    seen: set[tuple[str, str]] = set()
    # pt first (richer in Portuguese), then en for description coverage.
    # Scientific binomials usually have both pt and en; en often more detailed.
    for lang in ("pt", "en"):
        for title in candidates:
            key = (lang, title)
            if key in seen:
                continue
            seen.add(key)
            page = fetch_article(lang, title)
            if page:
                page["lang"] = lang
                page["matched_title"] = title
                return page
    return None


# ---------- commons credit ----------

def commons_file_meta(file_title: str) -> Optional[dict]:
    api = "https://commons.wikimedia.org/w/api.php"
    title = file_title if file_title.lower().startswith("file:") else f"File:{file_title}"
    params = {
        "action": "query",
        "format": "json",
        "prop": "imageinfo",
        "iiprop": "url|extmetadata|mime",
        "iiextmetadatafilter": "Artist|LicenseShortName|LicenseUrl|Credit",
        "titles": title,
    }
    data = http_get_json(f"{api}?{urlencode(params)}")
    time.sleep(SLEEP_BETWEEN)
    if not data:
        return None
    pages = data.get("query", {}).get("pages", {})
    if not pages:
        return None
    page = next(iter(pages.values()))
    infos = page.get("imageinfo") or []
    if not infos:
        return None
    info = infos[0]
    meta = info.get("extmetadata") or {}
    artist = strip_html((meta.get("Artist") or {}).get("value", "")) or "desconhecido"
    license_short = strip_html((meta.get("LicenseShortName") or {}).get("value", ""))
    return {"artist": artist, "license_short": license_short, "mime": info.get("mime", "")}


def format_credit(artist: str, license_short: str) -> str:
    artist = (artist or "desconhecido").strip()
    license_short = (license_short or "").strip()
    if license_short:
        return f"Foto: {artist} / {license_short} (Wikimedia Commons)"
    return f"Foto: {artist} (Wikimedia Commons)"


# ---------- .asset YAML editing ----------
#
# AnimalDef .asset files are tiny YAML docs with one MonoBehaviour. We don't
# pull a YAML lib; we just rewrite specific top-level keys (indent 2). For
# multi-line double-quoted values, continuations are lines starting with 4+
# spaces — we consume those when locating the field block.
#
# Output style: single-line double-quoted scalar with `\xHH` escapes for
# Latin-1 chars (Unity's own preferred style). Unity will reflow/rewrap on the
# next save, but until then the file parses identically.

KEY_RE = re.compile(r'^  ([a-zA-Z_][a-zA-Z0-9_]*):')


def yaml_doublequoted(s: str) -> str:
    if not s:
        return ""  # bare empty => unquoted, treated as empty string by Unity
    out = ['"']
    for ch in s:
        cp = ord(ch)
        if ch == '"':
            out.append('\\"')
        elif ch == '\\':
            out.append('\\\\')
        elif ch == '\n':
            out.append('\\n')
        elif 0x20 <= cp <= 0x7E:
            out.append(ch)
        elif cp <= 0xFF:
            out.append(f"\\x{cp:02X}")
        elif cp <= 0xFFFF:
            out.append(f"\\u{cp:04X}")
        else:
            out.append(f"\\U{cp:08X}")
    out.append('"')
    return "".join(out)


def find_field_block(lines: list[str], field: str) -> Optional[tuple[int, int]]:
    """Return [start, end) line indices covering `field`'s key line plus any
    indented continuation lines. None if not present."""
    head_re = re.compile(rf'^  {re.escape(field)}:(?:\s|$)')
    for i, line in enumerate(lines):
        if head_re.match(line):
            j = i + 1
            # consume continuation lines (indent > 2 spaces)
            while j < len(lines) and lines[j].startswith("    "):
                j += 1
            return (i, j)
    return None


def replace_field(text: str, field: str, new_value: str) -> str:
    """Replace (or insert) a top-level scalar field in a Unity .asset doc."""
    lines = text.splitlines(keepends=True)
    payload = yaml_doublequoted(new_value) if new_value else ""
    new_line = f"  {field}: {payload}\n" if payload else f"  {field}:\n"
    block = find_field_block(lines, field)
    if block:
        i, j = block
        return "".join(lines[:i] + [new_line] + lines[j:])
    # Field doesn't exist yet — append at end (Unity reads by name, order is free).
    if not text.endswith("\n"):
        text += "\n"
    return text + new_line


def read_field(text: str, field: str) -> str:
    """Best-effort scalar read: returns the field's value as a Python string,
    or '' if missing/empty. Handles unquoted, single-line double-quoted, and
    multi-line double-quoted scalars."""
    lines = text.splitlines()
    block = find_field_block([l + "\n" for l in lines], field)
    if not block:
        return ""
    i, j = block
    raw = lines[i].split(":", 1)[1].strip()
    cont = [l.strip() for l in lines[i + 1:j]]
    full = " ".join([raw] + cont).strip()
    if not full:
        return ""
    if full.startswith('"') and full.endswith('"') and len(full) >= 2:
        body = full[1:-1]
        # decode \xHH, \uHHHH, \", \\
        try:
            return body.encode("ascii", "backslashreplace").decode("unicode_escape")
        except Exception:
            return body
    return full


# ---------- main ----------

def ensure_sprite_meta(image_path: Path) -> bool:
    """Generate a .meta next to image_path with Sprite import settings, copied
    from SPRITE_META_TEMPLATE with a fresh GUID. No-op if a .meta already
    exists (don't clobber Unity-managed metadata). Returns True if created."""
    meta_path = image_path.with_suffix(image_path.suffix + ".meta")
    if meta_path.exists():
        return False
    if not SPRITE_META_TEMPLATE.exists():
        print(f"  ! no sprite .meta template at {SPRITE_META_TEMPLATE}; skipping", file=sys.stderr)
        return False
    template = SPRITE_META_TEMPLATE.read_text(encoding="utf-8")
    new_guid = uuid.uuid4().hex
    text = re.sub(r"^guid: [0-9a-f]{32}", f"guid: {new_guid}", template, count=1, flags=re.M)
    meta_path.write_text(text, encoding="utf-8")
    return True


def slugify(name: str) -> str:
    folded = unicodedata.normalize("NFKD", name)
    ascii_only = "".join(c for c in folded if not unicodedata.combining(c))
    ascii_only = ascii_only.encode("ascii", "ignore").decode("ascii").lower()
    slug = re.sub(r"[^a-z0-9]+", "_", ascii_only).strip("_")
    return slug or "animal"


def process(apply: bool, force: bool) -> tuple[int, int, int, list[str]]:
    filled_desc = 0
    downloaded = 0
    filled_credit = 0
    misses: list[str] = []

    asset_files = sorted(ANIMALS_DIR.glob("*.asset"))
    if not asset_files:
        print(f"No AnimalDef .asset files in {ANIMALS_DIR}", file=sys.stderr)
        return 0, 0, 0, []

    for asset_path in asset_files:
        asset_key = asset_path.stem  # filename without .asset
        text = asset_path.read_text(encoding="utf-8")

        display_name = read_field(text, "displayName") or asset_key
        cur_desc = read_field(text, "description")
        cur_image = read_field(text, "imageName")
        cur_credit = read_field(text, "photoCredit")

        if not force and cur_desc and cur_image and cur_credit:
            print(f"[skip] {asset_key}: already complete")
            continue

        print(f"[fetch] {asset_key} ({display_name})")

        article = resolve_article(asset_key, display_name)
        new_desc = None
        chosen_image_url = None
        chosen_image_title = None

        if article:
            print(f"  article: {article['lang']}.wiki '{article['matched_title']}' -> '{article['title']}'")
            new_desc = first_sentences(article["extract"])
            chosen_image_url = article.get("image_url")
            chosen_image_title = article.get("image_title")
        else:
            print("  ! no Wikipedia article matched; skipping description")

        if not chosen_image_url and not new_desc:
            misses.append(asset_key)
            print("  -> nothing to write; leaving fields untouched")
            continue

        # ---- description ----
        if new_desc and (force or not cur_desc):
            text = replace_field(text, "description", new_desc)
            filled_desc += 1
            print(f"  description: {new_desc[:80]}{'…' if len(new_desc) > 80 else ''}")

        # ---- image ----
        wrote_image = False
        if chosen_image_url and (force or not cur_image):
            ext = "jpg"
            m = re.search(r"\.([A-Za-z0-9]{3,4})(?:\?|$)", chosen_image_url)
            if m:
                ext = m.group(1).lower()
                if ext == "jpeg":
                    ext = "jpg"
            if ext == "svg":
                print(f"  ! skipping SVG image (not a photograph): {chosen_image_url}")
            else:
                target = ANIMALS_DIR / f"{asset_key}.{ext}"
                if apply:
                    data = http_get_bytes(chosen_image_url)
                    if data:
                        target.write_bytes(data)
                        text = replace_field(text, "imageName", asset_key)
                        downloaded += 1
                        wrote_image = True
                        print(f"  wrote image: {target.relative_to(REPO_ROOT)} ({len(data)} bytes)")
                        if ensure_sprite_meta(target):
                            print(f"  wrote sprite .meta: {target.name}.meta")
                else:
                    print(f"  would download: {chosen_image_url} -> {target.relative_to(REPO_ROOT)}")
                    text = replace_field(text, "imageName", asset_key)
                    downloaded += 1
                    wrote_image = True

        # ---- credit ----
        # Only attribute when we actually wrote (or are about to write) the image
        # for this asset. Skipping otherwise avoids stamping a Wikipedia credit
        # onto a user-supplied photo.
        if chosen_image_title and (force or not cur_credit) and (wrote_image or force):
            meta = commons_file_meta(chosen_image_title)
            if meta:
                credit = format_credit(meta["artist"], meta["license_short"])
                text = replace_field(text, "photoCredit", credit)
                filled_credit += 1
                print(f"  credit: {credit}")

        if apply:
            asset_path.write_text(text, encoding="utf-8")
            print(f"  wrote {asset_path.relative_to(REPO_ROOT)}")

    return filled_desc, downloaded, filled_credit, misses


def main() -> int:
    parser = argparse.ArgumentParser(
        description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter
    )
    parser.add_argument("--apply", action="store_true", help="Actually write files. Default is dry-run.")
    parser.add_argument("--force", action="store_true", help="Overwrite fields/images even if already populated.")
    args = parser.parse_args()

    if not ANIMALS_DIR.is_dir():
        print(f"Animals dir not found at {ANIMALS_DIR}", file=sys.stderr)
        return 1

    print(f"Scanning {ANIMALS_DIR.relative_to(REPO_ROOT)}")
    print(f"Mode: {'APPLY' if args.apply else 'DRY-RUN'}{' --force' if args.force else ''}\n")

    filled_desc, downloaded, filled_credit, misses = process(apply=args.apply, force=args.force)

    print()
    print(f"Summary: descriptions={filled_desc}, images={downloaded}, credits={filled_credit}, misses={len(misses)}")
    if misses:
        print("\nNo Wikipedia match (fill these manually):")
        for m in misses:
            print(f"  - {m}")
    if not args.apply:
        print("\n(dry-run; no files written. Re-run with --apply to commit.)")

    return 0


if __name__ == "__main__":
    sys.exit(main())
