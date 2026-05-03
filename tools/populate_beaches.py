#!/usr/bin/env python3
"""Populate places.json with Wikipedia descriptions, lead images, and credits.

Reads:  src/app/MergulhoVirtual/Assets/Resources/places.json
Writes: same file (description, imageName, photoCredit fields, only when empty)
        src/app/MergulhoVirtual/Assets/Resources/Beaches/<slug>.<ext>

Usage:
    python3 tools/populate_beaches.py            # dry-run (default), prints plan
    python3 tools/populate_beaches.py --apply    # actually write files
    python3 tools/populate_beaches.py --apply --force  # overwrite existing fields

Strategy (in order, per beach):
  1. Try a list of candidate page titles on pt.wikipedia (then en.wikipedia)
     for the *description*. Only accept pages that:
       - exist (not "missing"),
       - aren't redirects (redirects=0),
       - aren't disambiguation pages,
       - aren't the generic Fernando de Noronha article.
     The lead-image from a real article wins for the *image*, too.

  2. If no real article was found (or the article had no image), fall back to a
     Commons file-search anchored to "Fernando de Noronha" so we don't pick up
     a same-named place elsewhere (e.g. Cuba's Bay of Pigs). We only accept a
     file whose own categories mention "Fernando de Noronha" or "Noronha".

  3. Image attribution comes from Commons extmetadata (Artist + LicenseShortName).
     Format: "Foto: <Artist> / <License> (Wikimedia Commons)".

Honest expectations: pt.wiki has very thin per-beach coverage for Fernando de
Noronha — most beaches won't get a description from this script. Misses are
listed at the end so they can be filled in by hand in places.json.

Stdlib only (urllib). Run from the repo root.
"""
from __future__ import annotations

import argparse
import json
import re
import sys
import time
import unicodedata
from html.parser import HTMLParser
from pathlib import Path
from typing import Optional
from urllib.error import HTTPError
from urllib.parse import quote, urlencode
from urllib.request import Request, urlopen

REPO_ROOT = Path(__file__).resolve().parent.parent
PLACES_JSON = REPO_ROOT / "src/app/MergulhoVirtual/Assets/Resources/places.json"
BEACHES_DIR = REPO_ROOT / "src/app/MergulhoVirtual/Assets/Resources/Beaches"

USER_AGENT = (
    "MergulhoVirtual-BeachPopulator/1.0 "
    "(https://github.com/; guilhermefickel@gmail.com)"
)
THUMB_WIDTH = 1280
REQUEST_TIMEOUT = 30
SLEEP_BETWEEN = 1.5  # be polite to the API; bumped after seeing 429s

# Tokens that, when present in a Commons file's category list (or article
# extract), indicate the content is actually about Fernando de Noronha.
NORONHA_TOKENS = ("fernando de noronha", "noronha", "fernando_de_noronha")

# Page titles that an article candidate must NOT resolve to. The MediaWiki
# extracts API silently follows redirects even with redirects=0, so any
# unbacked beach name lands on the generic "Fernando de Noronha" article.
ARTICLE_TITLE_BLOCKLIST = {"fernando de noronha"}

# Page titles to attempt for the *description*, in priority order. The literal
# place name is appended automatically. Lists are tried as-is on pt.wikipedia
# first, then on en.wikipedia.
ARTICLE_TITLE_CANDIDATES: dict[str, list[str]] = {
    "Praia do Sancho":                       ["Praia do Sancho (Fernando de Noronha)"],
    "Baía dos Porcos":                       ["Baía dos Porcos (Fernando de Noronha)"],
    "Praia da Cacimba do Padre":             ["Cacimba do Padre"],
    "Praia da Quixaba":                      ["Quixaba (Fernando de Noronha)"],
    "Praia do Bode":                         [],
    "Praia do Americano":                    [],
    "Boldró Beach":                          ["Praia do Boldró", "Boldró"],
    "Praia da Conceição":                    ["Praia da Conceição (Fernando de Noronha)"],
    "Praia do Meio":                         ["Praia do Meio (Fernando de Noronha)"],
    "Praia do Cachorro":                     ["Praia do Cachorro (Fernando de Noronha)"],
    "Praia do Porto de Santo Antônio Noronha": ["Porto de Santo Antônio (Fernando de Noronha)"],
    "Sharks Cove":                           ["Buraco da Raquel"],
    "Buraco da Raquel":                      [],
    "Enseada da Caieira":                    ["Caieira (Fernando de Noronha)"],
    "Atalaia Beach":                         ["Praia do Atalaia (Fernando de Noronha)"],
    "Sueste Beach":                          ["Praia do Sueste", "Baía do Sueste"],
    "Praia do Leão":                         ["Praia do Leão (Fernando de Noronha)"],
}

# Search seeds for the Commons image-only fallback, anchored to FdN. The literal
# place name is appended automatically.
COMMONS_QUERY_HINTS: dict[str, list[str]] = {
    "Praia do Sancho":                       ["Sancho", "Baía do Sancho"],
    "Praia da Quixaba":                      ["Quixaba", "Praia Quixaba"],
    "Enseada da Caieira":                    ["Caieira", "Praia Caieira"],
    "Boldró Beach":                          ["Praia do Boldró", "Boldró"],
    "Atalaia Beach":                         ["Praia do Atalaia"],
    "Sueste Beach":                          ["Praia do Sueste", "Baía do Sueste"],
    "Sharks Cove":                           ["Buraco da Raquel"],
    "Praia do Porto de Santo Antônio Noronha": ["Porto de Santo Antônio"],
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

def slugify(name: str) -> str:
    folded = unicodedata.normalize("NFKD", name)
    ascii_only = "".join(c for c in folded if not unicodedata.combining(c))
    ascii_only = ascii_only.encode("ascii", "ignore").decode("ascii").lower()
    slug = re.sub(r"[^a-z0-9]+", "_", ascii_only).strip("_")
    return slug or "beach"


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
    text = re.sub(r"\s+", " ", text).strip()
    return text


def first_sentences(text: str, max_chars: int = 480) -> str:
    text = text.strip()
    if len(text) <= max_chars:
        return text
    cut = text[:max_chars]
    m = re.search(r"[\.\!\?](?!.*[\.\!\?])", cut)
    if m:
        return cut[: m.end()].strip()
    return cut.rsplit(" ", 1)[0].rstrip(",;: ") + "…"


# ---------- wikipedia article lookup (strict) ----------

def fetch_article(lang: str, title: str) -> Optional[dict]:
    """Strict article fetch: redirects=0, reject disambig + missing.

    Returns {extract, image_url, image_title, title} on success, else None.
    """
    api = f"https://{lang}.wikipedia.org/w/api.php"
    params = {
        "action": "query",
        "format": "json",
        "redirects": "0",
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
    resolved_title = (page.get("title") or "").strip()
    if resolved_title.lower() in ARTICLE_TITLE_BLOCKLIST:
        return None
    # Article must actually be about Fernando de Noronha. Catches name collisions
    # like 'Baía dos Porcos' -> the Cuban Bay of Pigs.
    extract_lc = extract.lower()
    if not any(tok in extract_lc for tok in NORONHA_TOKENS):
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
        "image_title": image_title,
        "title": page.get("title", title),
    }


def resolve_article(name: str) -> Optional[dict]:
    candidates = list(ARTICLE_TITLE_CANDIDATES.get(name, []))
    candidates.append(name)
    seen: set[tuple[str, str]] = set()
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


# ---------- commons image fallback ----------

def commons_search_files(query: str, limit: int = 8) -> list[str]:
    """Return file titles ('File:foo.jpg') matching the search query."""
    api = "https://commons.wikimedia.org/w/api.php"
    params = {
        "action": "query",
        "format": "json",
        "list": "search",
        "srsearch": query,
        "srnamespace": "6",
        "srlimit": str(limit),
    }
    data = http_get_json(f"{api}?{urlencode(params)}")
    time.sleep(SLEEP_BETWEEN)
    if not data:
        return []
    return [hit["title"] for hit in data.get("query", {}).get("search", []) or []]


def commons_file_meta(file_title: str) -> Optional[dict]:
    """Return {url, thumb_url, categories, artist, license_short, mime} for a Commons file."""
    api = "https://commons.wikimedia.org/w/api.php"
    params = {
        "action": "query",
        "format": "json",
        "prop": "imageinfo|categories",
        "iiprop": "url|extmetadata|mime|size",
        "iiextmetadatafilter": "Artist|LicenseShortName|LicenseUrl|Credit",
        "iiurlwidth": str(THUMB_WIDTH),
        "cllimit": "max",
        "titles": file_title,
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
    cats = [c.get("title", "") for c in (page.get("categories") or [])]
    return {
        "url": info.get("url"),
        "thumb_url": info.get("thumburl") or info.get("url"),
        "mime": info.get("mime", ""),
        "categories": cats,
        "artist": artist,
        "license_short": license_short,
    }


def is_noronha_file(meta: dict) -> bool:
    blob = " ".join(meta.get("categories", [])).lower()
    return any(tok in blob for tok in NORONHA_TOKENS)


def is_photo_mime(mime: str) -> bool:
    return mime.lower() in ("image/jpeg", "image/jpg", "image/png", "image/webp")


# Words that don't help disambiguate one FdN beach from another.
GENERIC_BEACH_WORDS = {
    "praia", "praias", "beach", "beaches", "baia", "bahia", "enseada",
    "buraco", "cove", "porto", "do", "da", "de", "dos", "das", "the",
    "fernando", "noronha", "santo", "antonio",
}


def beach_name_tokens(name: str) -> list[str]:
    """Significant ascii-folded tokens identifying *this* beach."""
    folded = unicodedata.normalize("NFKD", name)
    ascii_only = "".join(c for c in folded if not unicodedata.combining(c))
    parts = re.split(r"[^a-zA-Z0-9]+", ascii_only.lower())
    return [p for p in parts if len(p) > 2 and p not in GENERIC_BEACH_WORDS]


def file_matches_name(file_title: str, meta: dict, tokens: list[str]) -> bool:
    """True iff at least one beach-name token appears in the filename or a category."""
    if not tokens:
        return True  # nothing to match on; trust the search ranking
    blob = (file_title + " " + " ".join(meta.get("categories", []))).lower()
    return any(tok in blob for tok in tokens)


def commons_category_files(category: str, limit: int = 15) -> list[str]:
    """Return file titles in a Commons category. Empty if category doesn't exist."""
    api = "https://commons.wikimedia.org/w/api.php"
    params = {
        "action": "query",
        "format": "json",
        "list": "categorymembers",
        "cmtype": "file",
        "cmlimit": str(limit),
        "cmtitle": f"Category:{category}",
    }
    data = http_get_json(f"{api}?{urlencode(params)}")
    time.sleep(SLEEP_BETWEEN)
    if not data:
        return []
    return [m["title"] for m in data.get("query", {}).get("categorymembers", []) or []]


def commons_image_fallback(name: str) -> Optional[dict]:
    """Find a Commons photo of this beach.

    Two passes:
      1. Try Commons categories matching the beach name (precise: only files
         actually filed under that beach).
      2. Fall back to text search anchored to "Fernando de Noronha", and require
         the file's name or categories to contain a beach-name token (so a
         search match isn't accepted for a different FdN beach).
    """
    tokens = beach_name_tokens(name)

    # 1) Category lookup (precise)
    category_titles = list(COMMONS_QUERY_HINTS.get(name, []))
    category_titles.append(name)
    seen_files: set[str] = set()
    for cat in category_titles:
        for ftitle in commons_category_files(cat):
            if ftitle in seen_files:
                continue
            seen_files.add(ftitle)
            meta = commons_file_meta(ftitle)
            if not meta or not is_photo_mime(meta.get("mime", "")):
                continue
            if not is_noronha_file(meta):
                continue
            return {
                "thumb_url": meta["thumb_url"],
                "image_title": ftitle,
                "artist": meta["artist"],
                "license_short": meta["license_short"],
            }

    # 2) Text search (broader; require name-token match)
    queries: list[str] = []
    for hint in COMMONS_QUERY_HINTS.get(name, []):
        queries.append(f"{hint} Fernando de Noronha")
    queries.append(f"{name} Fernando de Noronha")
    for q in queries:
        for ftitle in commons_search_files(q, limit=8):
            if ftitle in seen_files:
                continue
            seen_files.add(ftitle)
            meta = commons_file_meta(ftitle)
            if not meta or not is_photo_mime(meta.get("mime", "")):
                continue
            if not is_noronha_file(meta):
                continue
            if not file_matches_name(ftitle, meta, tokens):
                continue
            return {
                "thumb_url": meta["thumb_url"],
                "image_title": ftitle,
                "artist": meta["artist"],
                "license_short": meta["license_short"],
            }
    return None


# ---------- attribution formatting ----------

def format_credit(artist: str, license_short: str) -> str:
    artist = (artist or "desconhecido").strip()
    license_short = (license_short or "").strip()
    if license_short:
        return f"Foto: {artist} / {license_short} (Wikimedia Commons)"
    return f"Foto: {artist} (Wikimedia Commons)"


def fetch_image_credit(file_title: str) -> Optional[str]:
    meta = commons_file_meta(file_title)
    if not meta:
        return None
    return format_credit(meta["artist"], meta["license_short"])


# ---------- main ----------

def process(places: list[dict], apply: bool, force: bool) -> tuple[int, int, int, list[str]]:
    filled_desc = 0
    downloaded = 0
    filled_credit = 0
    misses: list[str] = []

    if apply:
        BEACHES_DIR.mkdir(parents=True, exist_ok=True)

    for place in places:
        name = place.get("name") or ""
        if not name:
            continue
        slug = slugify(name)

        has_desc = bool((place.get("description") or "").strip())
        has_img = bool((place.get("imageName") or "").strip())
        has_credit = bool((place.get("photoCredit") or "").strip())

        if not force and has_desc and has_img and has_credit:
            print(f"[skip] {name}: already complete")
            continue

        print(f"[fetch] {name}")

        # 1) Try to find a real Wikipedia article
        article = resolve_article(name)

        new_desc = None
        chosen_image_url = None
        chosen_image_title = None

        if article:
            print(f"  article: {article['lang']}.wiki '{article['matched_title']}' -> '{article['title']}'")
            new_desc = first_sentences(article["extract"])
            chosen_image_url = article.get("image_url")
            chosen_image_title = article.get("image_title")

        # 2) If we still lack an image, do a Commons file search anchored to FdN
        need_image = (force or not has_img) and not chosen_image_url
        if need_image:
            fb = commons_image_fallback(name)
            if fb:
                print(f"  commons fallback: {fb['image_title']}")
                chosen_image_url = fb["thumb_url"]
                chosen_image_title = fb["image_title"]
            else:
                print("  commons fallback: no photo with FdN context")

        if not article and not chosen_image_url:
            misses.append(name)
            print("  -> no description, no image; leaving fields untouched")
            continue

        # Apply description
        if new_desc and (force or not has_desc):
            place["description"] = new_desc
            filled_desc += 1
            print(f"  description: {new_desc[:80]}{'…' if len(new_desc) > 80 else ''}")

        # Apply image
        if chosen_image_url and (force or not has_img):
            ext = "jpg"
            m = re.search(r"\.([A-Za-z0-9]{3,4})(?:\?|$)", chosen_image_url)
            if m:
                ext = m.group(1).lower()
                if ext == "jpeg":
                    ext = "jpg"
            if ext == "svg":
                print(f"  ! skipping SVG image (not a photograph): {chosen_image_url}")
            else:
                target = BEACHES_DIR / f"{slug}.{ext}"
                if apply:
                    data = http_get_bytes(chosen_image_url)
                    if data:
                        target.write_bytes(data)
                        print(f"  wrote image: {target.relative_to(REPO_ROOT)} ({len(data)} bytes)")
                        place["imageName"] = slug
                        downloaded += 1
                else:
                    print(f"  would download: {chosen_image_url} -> {target.relative_to(REPO_ROOT)}")
                    place["imageName"] = slug  # planned; final write skipped
                    downloaded += 1

        # Apply credit
        if chosen_image_title and (force or not has_credit):
            credit = fetch_image_credit(chosen_image_title)
            if credit:
                place["photoCredit"] = credit
                filled_credit += 1
                print(f"  credit: {credit}")

    return filled_desc, downloaded, filled_credit, misses


def main() -> int:
    parser = argparse.ArgumentParser(
        description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter
    )
    parser.add_argument("--apply", action="store_true", help="Actually write files. Default is dry-run.")
    parser.add_argument("--force", action="store_true", help="Overwrite fields/images even if already populated.")
    args = parser.parse_args()

    if not PLACES_JSON.exists():
        print(f"places.json not found at {PLACES_JSON}", file=sys.stderr)
        return 1

    places = json.loads(PLACES_JSON.read_text(encoding="utf-8"))
    print(f"Loaded {len(places)} beaches from {PLACES_JSON.relative_to(REPO_ROOT)}")
    print(f"Mode: {'APPLY' if args.apply else 'DRY-RUN'}{' --force' if args.force else ''}\n")

    filled_desc, downloaded, filled_credit, misses = process(places, apply=args.apply, force=args.force)

    print()
    print(f"Summary: descriptions={filled_desc}, images={downloaded}, credits={filled_credit}, misses={len(misses)}")
    if misses:
        print("\nNo Wikipedia/Commons match (fill these manually in places.json):")
        for m in misses:
            print(f"  - {m}")

    if args.apply:
        PLACES_JSON.write_text(
            json.dumps(places, ensure_ascii=False, indent=2) + "\n",
            encoding="utf-8",
        )
        print(f"\nWrote {PLACES_JSON.relative_to(REPO_ROOT)}")
    else:
        print("\n(dry-run; no files written. Re-run with --apply to commit.)")

    return 0


if __name__ == "__main__":
    sys.exit(main())
