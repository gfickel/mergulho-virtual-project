#!/usr/bin/env python3
"""Build src/app/MergulhoVirtual/Assets/Resources/articles.json from content/articles/*.md.

The app ships NO Markdown parser. This script IS the renderer: it parses the
constrained authoring subset at BUILD time and emits typed block JSON that
`ArticleLibrary` reads with `JsonUtility`. Malformed authoring therefore fails
HERE — on a developer's machine, with a file name and a line number — instead of
on a phone at a beach. See docs/educational-content-options.md §6.1 and §13.

Usage:
    python3 tools/build_articles.py              # build and write (make articles)
    python3 tools/build_articles.py --check      # validate only, write nothing (CI)
    python3 tools/build_articles.py --verbose    # per-article block counts
    python3 tools/build_articles.py --self-test  # in-memory parser/escaping assertions

Stdlib only — no PyYAML, no Markdown library, no venv (same constraint as
tools/generate_firestore_indexes.py). Never opens Unity; safe with the Editor
open, though Unity will reimport articles.json the next time it has focus.

Determinism is a requirement, not a nicety: files are visited in sorted order,
field order is explicit, and an unchanged corpus rewrites nothing at all (the
write is skipped when the bytes match), so `make articles` twice in a row
produces a byte-identical file and no spurious Unity reimport.
"""

from __future__ import annotations

import argparse
import json
import re
import shutil
import sys
import unicodedata
import uuid
from pathlib import Path
from typing import Any, Dict, List, Optional, Tuple

# ---------------------------------------------------------------------------
# Paths
# ---------------------------------------------------------------------------

REPO_ROOT = Path(__file__).resolve().parents[1]
CONTENT_DIR = REPO_ROOT / "content" / "articles"
CONTENT_IMAGES_DIR = CONTENT_DIR / "images"

UNITY_RESOURCES = REPO_ROOT / "src" / "app" / "MergulhoVirtual" / "Assets" / "Resources"
OUTPUT_JSON = UNITY_RESOURCES / "articles.json"
ARTICLE_IMAGES_DIR = UNITY_RESOURCES / "Articles"
ANIMALS_DIR = UNITY_RESOURCES / "Animals"
PLACES_JSON = UNITY_RESOURCES / "places.json"

# Cloned (with a fresh GUID) so a file this script installs imports with the
# right settings without anyone opening the Editor — the populate_animals.py
# trick. Any existing .meta of the right kind works as the template.
SPRITE_META_TEMPLATE = ANIMALS_DIR / "tiger_shark.jpg.meta"       # TextureImporter, Sprite
TEXTASSET_META_TEMPLATE = UNITY_RESOURCES / "beaches_content.json.meta"  # TextScriptImporter
FOLDER_META_TEMPLATE = UNITY_RESOURCES / "Animals.meta"           # DefaultImporter, folderAsset

# ---------------------------------------------------------------------------
# The declarative half — the authoring vocabulary, all in one place
# ---------------------------------------------------------------------------

SCHEMA_VERSION = 1
GENERATED_NOTE = (
    "tools/build_articles.py — do not hand-edit; "
    "edit content/articles/*.md and run `make articles`"
)

# Front matter. `id`/`title`/`summary`/`category` are required; the rest optional.
# Unknown keys are a hard ERROR: front matter is strictly typed, and a typo'd
# key (`titel:`) must fail loudly rather than silently drop the value.
FRONT_MATTER_REQUIRED = ("id", "title", "summary", "category")
FRONT_MATTER_OPTIONAL = ("hero", "heroCredit", "order", "updated")
FRONT_MATTER_KEYS = FRONT_MATTER_REQUIRED + FRONT_MATTER_OPTIONAL

DEFAULT_ORDER = 1000

CALLOUT_TONES = ("info", "warning", "success", "error")

# `@name[arg]` and `@name[arg](url)`. The registry is the whole directive
# vocabulary — anything else is an "unknown directive" error.
DIRECTIVES = {
    "especie": {"takes_url": False},
    "praia": {"takes_url": False},
    "video": {"takes_url": True},
}

# Resources.Load<Sprite> resolves without an extension, so the build has to try
# each importable extension to confirm the asset exists on disk.
IMAGE_EXTENSIONS = (".jpg", ".jpeg", ".png")

# The complete DTO field union from the contract, in emission order. A block
# dict may only contain keys from this tuple, and always emits them in this
# order, so the JSON is stable across runs and across Python versions.
BLOCK_FIELD_ORDER = (
    "type",
    "level",
    "text",
    "items",
    "tone",
    "attribution",
    "src",
    "caption",
    "credit",
    "url",
    "title",
    "key",
    "name",
)

ARTICLE_FIELD_ORDER = (
    "id",
    "title",
    "summary",
    "category",
    "heroImage",
    "heroCredit",
    "order",
    "updated",
    "wordCount",
    "blocks",
)

# Warnings only — these are style smells, not malformed input.
LONG_PARAGRAPH_CHARS = 1200

# The whole of the escaping rule: an author's `<` is emitted as this exact
# sequence. See `escape_text` for why it is `<noparse>` per character and why
# `&` and `>` need nothing at all.
ESCAPED_LT = "<noparse><</noparse>"

# Emitted text may contain these tags and nothing else — plus `ESCAPED_LT`,
# which is an opening tag, a bare `<` and a closing tag that only mean anything
# together, so `check_emitted_tags` consumes it as one unit. Asserted after
# generation, which is the last line of defence against an escaping bug shipping
# a live tag into UI Toolkit's rich-text generator.
ALLOWED_TAG_RE = re.compile(r'</?b>|</?i>|</?u>|</a>|<a href="https://[^\s"<>]*">')

# UI Toolkit's `<a href>`/`<link>` tags carry a 256-character limit
# (docs/educational-content-options.md §"What UI Toolkit rich text can and
# cannot do"). Past it the tag is silently truncated, so reject at build time.
MAX_LINK_HREF_CHARS = 256

ID_RE = re.compile(r"^[a-z0-9]+(-[a-z0-9]+)*$")
ISO_DATE_RE = re.compile(r"^\d{4}-\d{2}-\d{2}$")
# Deliberately narrow: no quotes, no angle brackets, no whitespace, so a URL can
# never break out of the `href="…"` attribute we build around it.
URL_RE = re.compile(r"^https://[^\s\"'<>()]+$")

FRONT_MATTER_FENCE = "---"
FM_LINE_RE = re.compile(r"^([A-Za-z][A-Za-z0-9_]*)\s*:\s*(.*)$")

HEADING_RE = re.compile(r"^(#{1,6})\s*(.*)$")
CALLOUT_RE = re.compile(r"^>\s*\[!([A-Za-z]+)\]\s*(.*)$")
QUOTE_RE = re.compile(r"^>\s?(.*)$")
ATTRIBUTION_RE = re.compile(r"^\s*(?:—|–|--)\s*(.+)$")
BULLET_RE = re.compile(r"^[-*]\s+(.+)$")
NUMBERED_RE = re.compile(r"^\d+[.)]\s+(.+)$")
IMAGE_RE = re.compile(r'^!\[(.*)\]\(\s*([^\s)]+?)(?:\s+"([^"]*)")?\s*\)$')
DIRECTIVE_RE = re.compile(r"^@([A-Za-z]+)\[(.*?)\](?:\((.*?)\))?$")
HRULE_RE = re.compile(r"^(?:-{3,}|\*{3,}|_{3,})$")
COMMENT_OPEN_RE = re.compile(r"^\s*<!--")

INLINE_LINK_RE = re.compile(r"\[([^\]\n]+)\]\(([^)\s]*)\)")
BOLD_RE = re.compile(r"\*\*(?=\S)(.+?)(?<=\S)\*\*", re.S)
ITALIC_STAR_RE = re.compile(r"(?<!\*)\*(?=\S)([^*\n]+?)(?<=\S)\*(?!\*)")
ITALIC_UNDER_RE = re.compile(r"(?<![A-Za-z0-9_])_(?=\S)([^_\n]+?)(?<=\S)_(?![A-Za-z0-9_])")

# Placeholder sentinel for link extraction. Control characters are stripped from
# every source file on read, so this byte cannot occur in author text.
SENTINEL = "\x00"
SENTINEL_RE = re.compile(r"\x00(\d+)\x00")


# ---------------------------------------------------------------------------
# Diagnostics
# ---------------------------------------------------------------------------


class Diagnostics:
    """Collects every problem instead of stopping at the first one — a single
    run should tell the author everything that is wrong with their file."""

    def __init__(self) -> None:
        self.errors: List[str] = []
        self.warnings: List[str] = []

    def error(self, path: Optional[Path], line: Optional[int], message: str) -> None:
        self.errors.append(self._fmt(path, line, message))

    def warn(self, path: Optional[Path], line: Optional[int], message: str) -> None:
        self.warnings.append(self._fmt(path, line, message))

    @staticmethod
    def _fmt(path: Optional[Path], line: Optional[int], message: str) -> str:
        if path is None:
            return message
        try:
            shown = path.relative_to(REPO_ROOT)
        except ValueError:
            shown = path
        where = f"{shown}:{line}" if line else f"{shown}"
        return f"{where}: {message}"

    @property
    def ok(self) -> bool:
        return not self.errors

    def report(self) -> None:
        for w in self.warnings:
            print(f"  warning: {w}", file=sys.stderr)
        for e in self.errors:
            print(f"  ERROR:   {e}", file=sys.stderr)


# ---------------------------------------------------------------------------
# Inline formatting — escaping FIRST, then our own tags
# ---------------------------------------------------------------------------


def escape_text(raw: str) -> str:
    """Neutralise the ONE character that could otherwise become markup: `<`.

    This runs BEFORE any of our tags are inserted, so an author typing `<b>` or
    `<color=red>` gets literal text, never a tag the UI Toolkit rich-text
    generator would honour.

    **Why this is not HTML escaping.** UI Toolkit's text generator does not
    decode named HTML entities, so `&amp;` renders as the five literal
    characters `&amp;` — verified in a render, not assumed. The old rule escaped
    `&`, `<` and `>`, and two thirds of it were both wrong on screen and
    unnecessary: the only character that can open a tag is `<`; `&` and `>` are
    inert on their own and are emitted literally.

    `<` is wrapped in `<noparse>…</noparse>` — a tag Unity 6000.3's supported
    table lists (docs/educational-content-options.md) which suspends tag parsing
    for its contents. Wrapping is **per character**, which is what makes it
    provably injection-proof: an author typing `</noparse>` yields
    `<noparse><</noparse>/noparse>`, whose noparse region holds exactly one `<`,
    so the rest is literal text and nothing can escape the region.
    """
    return raw.replace("<", ESCAPED_LT)


def render_inline(
    raw: str, diag: Diagnostics, path: Path, line: int
) -> str:
    """escape → extract links → emphasis → restore links.

    Links are pulled out to sentinels before emphasis runs, because a URL may
    legitimately contain `_` or `*` (`.../a_b_c.mp4`) and the emphasis regexes
    would otherwise mangle the href.
    """
    text = escape_text(raw)

    stash: List[str] = []

    def take_link(m: "re.Match[str]") -> str:
        label, url = m.group(1), m.group(2)
        if not URL_RE.match(url):
            diag.error(
                path,
                line,
                f"link URL must be an https:// URL with no spaces or quotes: {url!r}",
            )
            # Keep the label so the rest of the line still validates.
            stash.append(apply_emphasis(label))
            return f"{SENTINEL}{len(stash) - 1}{SENTINEL}"
        if len(url) > MAX_LINK_HREF_CHARS:
            diag.error(
                path,
                line,
                f"link URL is {len(url)} characters — UI Toolkit truncates an "
                f"<a href> past {MAX_LINK_HREF_CHARS}, which would ship a link "
                f"that opens the wrong page. Shorten it: {url!r}",
            )
            stash.append(apply_emphasis(label))
            return f"{SENTINEL}{len(stash) - 1}{SENTINEL}"
        # `<u>` around the visible text, because an `<a href>` span gets NO
        # visual treatment of its own — no colour, no underline — and USS cannot
        # reach inside one, so without this the reader cannot tell the words are
        # tappable (verified in a render). `<u>` is a supported tag and carries
        # no colour, so it does not smuggle a literal past the token-discipline
        # rule the way `<color=…>` would.
        stash.append(f'<a href="{url}"><u>{apply_emphasis(label)}</u></a>')
        return f"{SENTINEL}{len(stash) - 1}{SENTINEL}"

    text = INLINE_LINK_RE.sub(take_link, text)
    text = apply_emphasis(text)
    text = SENTINEL_RE.sub(lambda m: stash[int(m.group(1))], text)
    return text


def apply_emphasis(text: str) -> str:
    """`**bold**` then `*italic*` / `_italic_`. Bold must run first or its inner
    asterisks get eaten by the italic pattern."""
    text = BOLD_RE.sub(lambda m: f"<b>{m.group(1)}</b>", text)
    text = ITALIC_STAR_RE.sub(lambda m: f"<i>{m.group(1)}</i>", text)
    text = ITALIC_UNDER_RE.sub(lambda m: f"<i>{m.group(1)}</i>", text)
    return text


def plain_text(rendered: str) -> str:
    """Strip our own tags and undo the escaping, for word counting.

    The escaped `<` has to be taken out of the way FIRST: to a `<[^>]*>` regex
    `<noparse><</noparse>` looks like two tags, so stripping before unescaping
    eats the author's character, and unescaping before stripping hands the
    stripper a bare `<` to pair with the next `>` in the sentence. `\x01` is safe
    as the guard for the same reason SENTINEL is: `read_lines` strips every
    control character from every source file.
    """
    guarded = rendered.replace(ESCAPED_LT, "\x01")
    bare = re.sub(r"<[^>]*>", "", guarded)
    return bare.replace("\x01", "<")


def check_emitted_tags(value: str, diag: Diagnostics, path: Path, where: str) -> None:
    """Assert every `<` in an emitted string is one we put there.

    The invariant, stated exactly: a `<` may only begin one of our own tags
    (`<b> <i> <u> <a href="https://…">` and their closers) or the escape
    sequence `ESCAPED_LT`. Anything else is a raw author character that reached
    the output, i.e. an escaping regression.

    This is a left-to-right scan rather than a `finditer` over `<[^>]*>` because
    neither half of the rule survives that regex: `ESCAPED_LT` reads to it as two
    separate tags that are only legal together, and a bare `<` with no `>`
    after it — the most dangerous case, since it opens a tag the generator will
    hunt a closer for — matches nothing at all and would have passed silently.
    """
    i, n = 0, len(value)
    while i < n:
        if value[i] != "<":
            i += 1
            continue
        if value.startswith(ESCAPED_LT, i):
            i += len(ESCAPED_LT)
            continue
        m = ALLOWED_TAG_RE.match(value, i)
        if m:
            i = m.end()
            continue
        end = value.find(">", i)
        shown = value[i : end + 1] if end >= 0 else value[i : i + 32]
        diag.error(
            path,
            None,
            f"internal: disallowed markup {shown!r} in emitted {where} "
            f"— escaping regression, do not ship",
        )
        i += 1


# ---------------------------------------------------------------------------
# Reading and front matter
# ---------------------------------------------------------------------------


def read_lines(path: Path) -> List[str]:
    """Read a source file as a list of lines with control characters removed.

    Stripping Cc (minus tab) keeps the SENTINEL byte impossible in author text,
    and normalises CRLF away so the block parser only ever sees `\\n`.
    """
    raw = path.read_text(encoding="utf-8")
    raw = raw.replace("\r\n", "\n").replace("\r", "\n")
    cleaned = "".join(
        ch for ch in raw if ch == "\n" or ch == "\t" or unicodedata.category(ch) != "Cc"
    )
    return cleaned.split("\n")


def strip_quotes(value: str) -> str:
    value = value.strip()
    if len(value) >= 2 and value[0] == value[-1] and value[0] in "\"'":
        return value[1:-1]
    return value


def parse_front_matter(
    lines: List[str], diag: Diagnostics, path: Path
) -> Tuple[Dict[str, str], int]:
    """Line-based `key: value` front matter between `---` fences.

    Returns (values, first body line index). An unterminated block is a hard
    error — without it the whole article would silently become front matter.

    Blank lines and HTML comment blocks are allowed BEFORE the opening fence, so
    an authoring note (`<!-- CONTEÚDO DE EXEMPLO -->`) can sit at the very top of
    the file where a reader will actually see it.
    """
    open_at = 0
    while open_at < len(lines):
        stripped = lines[open_at].strip()
        if not stripped:
            open_at += 1
            continue
        if COMMENT_OPEN_RE.match(lines[open_at]):
            while open_at < len(lines) and "-->" not in lines[open_at]:
                open_at += 1
            open_at += 1
            continue
        break

    if open_at >= len(lines) or lines[open_at].strip() != FRONT_MATTER_FENCE:
        diag.error(
            path,
            open_at + 1,
            "missing front matter — the file must open with a `---` line "
            "(HTML comments and blank lines may precede it)",
        )
        return {}, 0

    values: Dict[str, str] = {}
    for i in range(open_at + 1, len(lines)):
        line = lines[i]
        if line.strip() == FRONT_MATTER_FENCE:
            return values, i + 1
        if not line.strip():
            continue
        m = FM_LINE_RE.match(line)
        if not m:
            diag.error(
                path, i + 1, f"front matter must be `key: value`, got {line.strip()!r}"
            )
            continue
        key, value = m.group(1), strip_quotes(m.group(2))
        if key not in FRONT_MATTER_KEYS:
            diag.error(
                path,
                i + 1,
                f"unknown front-matter key {key!r} — allowed: "
                f"{', '.join(FRONT_MATTER_KEYS)}",
            )
            continue
        if key in values:
            diag.error(path, i + 1, f"front-matter key {key!r} appears twice")
            continue
        values[key] = value

    diag.error(
        path,
        len(lines),
        "unterminated front matter — no closing `---` line was found",
    )
    return values, len(lines)


# ---------------------------------------------------------------------------
# Image / reference resolution
# ---------------------------------------------------------------------------


class ResourceIndex:
    """Knows what `Resources.Load` will find, and what the beach/species keys are."""

    def __init__(self, diag: Diagnostics) -> None:
        self.diag = diag
        self.species_keys = self._load_species_keys()
        self.beach_names = self._load_beach_names()
        # Images the author dropped in content/articles/images/ count as
        # resolvable even in --check mode, where nothing is copied yet.
        self.pending_images = self._scan_pending_images()

    @staticmethod
    def _load_species_keys() -> set:
        if not ANIMALS_DIR.is_dir():
            return set()
        return {p.stem for p in sorted(ANIMALS_DIR.glob("*.asset"))}

    def _load_beach_names(self) -> set:
        if not PLACES_JSON.is_file():
            self.diag.warn(None, None, f"{PLACES_JSON} not found — @praia[] unchecked")
            return set()
        try:
            data = json.loads(PLACES_JSON.read_text(encoding="utf-8"))
        except Exception as exc:  # pragma: no cover - corrupt repo state
            self.diag.warn(None, None, f"could not read places.json ({exc})")
            return set()
        # places.json is a bare top-level array; `name` is the machine key
        # (NOT displayName — the resolver, the spawner and the backend all key
        # on `name`, so an article must reference that spelling).
        return {str(p.get("name", "")) for p in data if p.get("name")}

    @staticmethod
    def _scan_pending_images() -> Dict[str, Path]:
        if not CONTENT_IMAGES_DIR.is_dir():
            return {}
        found: Dict[str, Path] = {}
        for p in sorted(CONTENT_IMAGES_DIR.iterdir()):
            if p.is_file() and p.suffix.lower() in IMAGE_EXTENSIONS:
                found[p.stem] = p
        return found

    def sprite_exists(self, src: str) -> bool:
        """`src` is a Resources path without extension, e.g. Beaches/praia_do_sancho."""
        if not src or src.startswith("/") or ".." in src.split("/"):
            return False
        candidate = UNITY_RESOURCES / src
        for ext in IMAGE_EXTENSIONS:
            if candidate.with_suffix(ext).is_file():
                return True
        # Not installed yet, but the author has the file staged for install.
        head, _, tail = src.rpartition("/")
        return head == "Articles" and tail in self.pending_images


# ---------------------------------------------------------------------------
# Block parsing
# ---------------------------------------------------------------------------


class ArticleParser:
    def __init__(self, path: Path, diag: Diagnostics, index: ResourceIndex) -> None:
        self.path = path
        self.diag = diag
        self.index = index
        self.blocks: List[Dict[str, Any]] = []
        self._para: List[Tuple[int, str]] = []
        self._list_kind: Optional[str] = None
        self._list_items: List[Tuple[int, str]] = []
        self._quote: List[Tuple[int, str]] = []
        self._quote_attribution: Optional[str] = None

    # -- flushing -----------------------------------------------------------

    def _flush_paragraph(self) -> None:
        if not self._para:
            return
        line = self._para[0][0]
        raw = " ".join(t for _, t in self._para).strip()
        self._para = []
        if not raw:
            return
        if len(raw) > LONG_PARAGRAPH_CHARS:
            self.diag.warn(
                self.path,
                line,
                f"paragraph is {len(raw)} characters — consider splitting it "
                f"(over {LONG_PARAGRAPH_CHARS} reads as a wall of text on a phone)",
            )
        self.blocks.append(
            {"type": "paragraph", "text": self._inline(raw, line)}
        )

    def _flush_list(self) -> None:
        if not self._list_items:
            self._list_kind = None
            return
        kind = self._list_kind or "bulletList"
        items = [self._inline(t, ln) for ln, t in self._list_items]
        self._list_items = []
        self._list_kind = None
        self.blocks.append({"type": kind, "items": items})

    def _flush_quote(self) -> None:
        if not self._quote:
            self._quote_attribution = None
            return
        line = self._quote[0][0]
        raw = " ".join(t for _, t in self._quote).strip()
        attribution = self._quote_attribution
        self._quote = []
        self._quote_attribution = None
        if not raw:
            return
        block: Dict[str, Any] = {"type": "quote", "text": self._inline(raw, line)}
        if attribution:
            block["attribution"] = self._inline(attribution, line)
        self.blocks.append(block)

    def _flush_all(self) -> None:
        self._flush_paragraph()
        self._flush_list()
        self._flush_quote()

    def _inline(self, raw: str, line: int) -> str:
        return render_inline(raw, self.diag, self.path, line)

    # -- the line loop ------------------------------------------------------

    def parse(self, lines: List[str], start: int) -> List[Dict[str, Any]]:
        i = start
        n = len(lines)
        while i < n:
            raw_line = lines[i]
            line_no = i + 1
            stripped = raw_line.strip()

            # HTML comments are authoring notes, not content: consumed and never
            # emitted. Only a line that STARTS with `<!--` opens one, so a
            # paragraph that merely mentions `<b>` is untouched.
            if COMMENT_OPEN_RE.match(raw_line):
                self._flush_all()
                while i < n and "-->" not in lines[i]:
                    i += 1
                i += 1
                continue

            if not stripped:
                self._flush_all()
                i += 1
                continue

            if HRULE_RE.match(stripped):
                self.diag.error(
                    self.path,
                    line_no,
                    "horizontal rules are not part of the block vocabulary — "
                    "use a `## Título` heading to separate sections",
                )
                i += 1
                continue

            if stripped.startswith("#"):
                self._flush_all()
                self._parse_heading(stripped, line_no)
                i += 1
                continue

            if stripped.startswith(">"):
                i = self._parse_blockquote(lines, i)
                continue

            if stripped.startswith("@"):
                self._flush_all()
                self._parse_directive(stripped, line_no)
                i += 1
                continue

            if stripped.startswith("!["):
                self._flush_all()
                self._parse_image(stripped, line_no)
                i += 1
                continue

            m = BULLET_RE.match(stripped)
            if m:
                self._flush_paragraph()
                self._flush_quote()
                if self._list_kind and self._list_kind != "bulletList":
                    self._flush_list()
                self._list_kind = "bulletList"
                self._list_items.append((line_no, m.group(1).strip()))
                i += 1
                continue

            m = NUMBERED_RE.match(stripped)
            if m:
                self._flush_paragraph()
                self._flush_quote()
                if self._list_kind and self._list_kind != "numberedList":
                    self._flush_list()
                self._list_kind = "numberedList"
                self._list_items.append((line_no, m.group(1).strip()))
                i += 1
                continue

            # Ordinary prose. An inline image would be silently dropped by the
            # paragraph path, so catch it instead of losing it.
            if "![" in stripped:
                self.diag.error(
                    self.path,
                    line_no,
                    "an image must be alone on its line — "
                    '`![Legenda](Pasta/arquivo "Crédito")`',
                )
                i += 1
                continue
            self._flush_list()
            self._flush_quote()
            self._para.append((line_no, stripped))
            i += 1

        self._flush_all()
        return self.blocks

    # -- individual block kinds --------------------------------------------

    def _parse_heading(self, stripped: str, line_no: int) -> None:
        m = HEADING_RE.match(stripped)
        if not m:
            self.diag.error(self.path, line_no, f"malformed heading {stripped!r}")
            return
        hashes, text = m.group(1), m.group(2).strip()
        level = len(hashes)
        if level == 1:
            self.diag.error(
                self.path,
                line_no,
                "a level-1 `#` heading is not allowed in the body — the article "
                "title comes from the front-matter `title:` key; use `##`",
            )
            return
        if level > 3:
            self.diag.error(
                self.path,
                line_no,
                f"heading level {level} has no block representation — "
                "only `##` (level 2) and `###` (level 3) are supported",
            )
            return
        if not text:
            self.diag.error(self.path, line_no, "heading has no text")
            return
        self.blocks.append(
            {"type": "heading", "level": level, "text": self._inline(text, line_no)}
        )

    def _parse_blockquote(self, lines: List[str], i: int) -> int:
        """A `>` run is either one callout or one quote (+ optional attribution)."""
        self._flush_paragraph()
        self._flush_list()
        first = lines[i].strip()
        line_no = i + 1

        m = CALLOUT_RE.match(first)
        if m:
            tone = m.group(1).lower()
            parts = [m.group(2).strip()] if m.group(2).strip() else []
            j = i + 1
            while j < len(lines) and lines[j].strip().startswith(">"):
                qm = QUOTE_RE.match(lines[j].strip())
                body = qm.group(1).strip() if qm else ""
                if body:
                    parts.append(body)
                j += 1
            if tone not in CALLOUT_TONES:
                self.diag.error(
                    self.path,
                    line_no,
                    f"unknown callout tone {tone!r} — allowed: "
                    f"{', '.join(CALLOUT_TONES)}",
                )
                return j
            text = " ".join(parts).strip()
            if not text:
                self.diag.error(self.path, line_no, "callout has no text")
                return j
            self.blocks.append(
                {
                    "type": "callout",
                    "tone": tone,
                    "text": self._inline(text, line_no),
                }
            )
            return j

        # Plain quote.
        j = i
        while j < len(lines) and lines[j].strip().startswith(">"):
            qm = QUOTE_RE.match(lines[j].strip())
            body = qm.group(1).strip() if qm else ""
            if body:
                am = ATTRIBUTION_RE.match(body)
                if am and self._quote:
                    self._quote_attribution = am.group(1).strip()
                else:
                    self._quote.append((j + 1, body))
            j += 1
        self._flush_quote()
        return j

    def _parse_image(self, stripped: str, line_no: int) -> None:
        m = IMAGE_RE.match(stripped)
        if not m:
            self.diag.error(
                self.path,
                line_no,
                "malformed image — expected "
                '`![Legenda](Pasta/arquivo "Foto: Autor / Licença (Fonte)")`',
            )
            return
        caption, src, credit = m.group(1).strip(), m.group(2).strip(), (m.group(3) or "").strip()
        if not self.index.sprite_exists(src):
            self.diag.error(
                self.path,
                line_no,
                f"image src {src!r} does not resolve to a file under "
                f"Assets/Resources/ (tried {', '.join(IMAGE_EXTENSIONS)}) — "
                f"paths are Resources.Load paths WITHOUT an extension, e.g. "
                f"Beaches/praia_do_sancho",
            )
            return
        block: Dict[str, Any] = {"type": "image", "src": src}
        if caption:
            block["caption"] = self._inline(caption, line_no)
        if credit:
            block["credit"] = self._inline(credit, line_no)
        self.blocks.append(block)

    def _parse_directive(self, stripped: str, line_no: int) -> None:
        m = DIRECTIVE_RE.match(stripped)
        if not m:
            self.diag.error(
                self.path,
                line_no,
                f"unknown directive {stripped!r} — a directive must be alone on "
                f"its line: @especie[chave], @praia[Nome], "
                f"@video[Título](https://…)",
            )
            return
        name, arg, url = m.group(1).lower(), m.group(2).strip(), m.group(3)
        spec = DIRECTIVES.get(name)
        if spec is None:
            self.diag.error(
                self.path,
                line_no,
                f"unknown directive @{name}[…] — allowed: "
                f"{', '.join('@' + d for d in sorted(DIRECTIVES))}",
            )
            return
        if spec["takes_url"] and url is None:
            self.diag.error(
                self.path, line_no, f"@{name}[…] needs a (https://…) URL after it"
            )
            return
        if not spec["takes_url"] and url is not None:
            self.diag.error(
                self.path, line_no, f"@{name}[…] does not take a (…) argument"
            )
            return

        if name == "especie":
            if arg not in self.index.species_keys:
                known = ", ".join(sorted(self.index.species_keys)) or "(none found)"
                self.diag.error(
                    self.path,
                    line_no,
                    f"@especie[{arg}] — no AnimalDef at "
                    f"Assets/Resources/Animals/{arg}.asset. Known keys: {known}",
                )
                return
            self.blocks.append({"type": "speciesRef", "key": arg})
            return

        if name == "praia":
            if arg not in self.index.beach_names:
                self.diag.error(
                    self.path,
                    line_no,
                    f"@praia[{arg}] — not an exact `name` in places.json. Note "
                    f"that `name` is the machine key, NOT the pt-BR "
                    f"`displayName` (e.g. `Sueste Beach`, not `Baía do Sueste`)",
                )
                return
            self.blocks.append({"type": "beachRef", "name": arg})
            return

        # @video
        if not URL_RE.match(url or ""):
            self.diag.error(
                self.path,
                line_no,
                f"@video URL must be https:// with no spaces or quotes, got "
                f"{url!r}",
            )
            return
        if not arg:
            self.diag.error(self.path, line_no, "@video[…] needs a title")
            return
        self.blocks.append(
            {"type": "video", "url": url, "title": self._inline(arg, line_no)}
        )


# ---------------------------------------------------------------------------
# Article assembly
# ---------------------------------------------------------------------------


def build_article(
    path: Path, diag: Diagnostics, index: ResourceIndex
) -> Optional[Dict[str, Any]]:
    lines = read_lines(path)
    fm, body_start = parse_front_matter(lines, diag, path)

    for key in FRONT_MATTER_REQUIRED:
        if not fm.get(key, "").strip():
            diag.error(path, 1, f"front matter is missing a non-empty `{key}:`")

    article_id = fm.get("id", "").strip()
    if article_id and not ID_RE.match(article_id):
        diag.error(
            path,
            1,
            f"id {article_id!r} must be kebab-case ASCII "
            f"(^[a-z0-9]+(-[a-z0-9]+)*$)",
        )
    if article_id and article_id != path.stem:
        diag.warn(
            path,
            1,
            f"id {article_id!r} differs from the file name {path.stem!r} — "
            f"keeping them equal makes the corpus easier to navigate",
        )

    order = DEFAULT_ORDER
    if "order" in fm:
        raw_order = fm["order"].strip()
        try:
            order = int(raw_order)
        except ValueError:
            diag.error(path, 1, f"order must be an integer, got {raw_order!r}")

    updated = fm.get("updated", "").strip()
    if updated and not ISO_DATE_RE.match(updated):
        diag.warn(
            path, 1, f"updated {updated!r} is not ISO-8601 (YYYY-MM-DD)"
        )

    hero = fm.get("hero", "").strip()
    if hero and not index.sprite_exists(hero):
        diag.error(
            path,
            1,
            f"hero {hero!r} does not resolve to a file under Assets/Resources/ "
            f"(tried {', '.join(IMAGE_EXTENSIONS)})",
        )
        hero = ""

    blocks = ArticleParser(path, diag, index).parse(lines, body_start)
    if not blocks:
        diag.warn(path, 1, "article has no body blocks — it will render empty")

    words = 0
    for block in blocks:
        for field in ("text", "caption", "credit", "attribution", "title"):
            if field in block:
                words += len(plain_text(str(block[field])).split())
        for item in block.get("items", []):
            words += len(plain_text(item).split())

    if not article_id:
        return None

    # Front-matter prose is escaped but NOT marked up — the contract confines
    # inline markup to body blocks. Escaping still applies: `id` is regex-pinned
    # so it needs none, but a title/summary/category/credit is author text and
    # UI Toolkit `Label`s parse rich text by default.
    article: Dict[str, Any] = {
        "id": article_id,
        "title": escape_text(fm.get("title", "").strip()),
        "summary": escape_text(fm.get("summary", "").strip()),
        "category": escape_text(fm.get("category", "").strip()),
        "order": order,
        "wordCount": words,
        "blocks": [order_block(b) for b in blocks],
    }
    if hero:
        article["heroImage"] = hero
    hero_credit = fm.get("heroCredit", "").strip()
    if hero_credit:
        article["heroCredit"] = escape_text(hero_credit)
    if updated:
        article["updated"] = updated

    # Escaping invariant, checked on everything that reaches the JSON.
    for field in ("title", "summary", "category", "heroCredit"):
        if field in article:
            check_emitted_tags(str(article[field]), diag, path, f"front matter {field}")
    for block in article["blocks"]:
        for key, value in block.items():
            if isinstance(value, str):
                check_emitted_tags(value, diag, path, f"{block['type']}.{key}")
            elif isinstance(value, list):
                for entry in value:
                    check_emitted_tags(str(entry), diag, path, f"{block['type']}.{key}[]")

    return {k: article[k] for k in ARTICLE_FIELD_ORDER if k in article}


def order_block(block: Dict[str, Any]) -> Dict[str, Any]:
    unknown = set(block) - set(BLOCK_FIELD_ORDER)
    if unknown:  # pragma: no cover - guards a coding mistake, not author input
        raise AssertionError(f"block emitted fields outside the DTO union: {unknown}")
    return {k: block[k] for k in BLOCK_FIELD_ORDER if k in block}


# ---------------------------------------------------------------------------
# Image install (content/articles/images → Assets/Resources/Articles)
# ---------------------------------------------------------------------------


def fresh_meta_from(template: Path, dest_meta: Path) -> bool:
    """Clone a .meta with a fresh GUID so Unity imports the new file with the
    template's settings and no Editor trip (the populate_animals.py trick).
    Never clobbers an existing .meta — that one is Unity-managed."""
    if dest_meta.exists():
        return False
    if not template.exists():
        print(
            f"  ! no .meta template at {template}; Unity will generate one on "
            f"next focus",
            file=sys.stderr,
        )
        return False
    text = template.read_text(encoding="utf-8")
    text = re.sub(
        r"^guid: [0-9a-f]{32}", f"guid: {uuid.uuid4().hex}", text, count=1, flags=re.M
    )
    dest_meta.write_text(text, encoding="utf-8")
    return True


def install_images(verbose: bool) -> List[str]:
    """Copy every staged image into Assets/Resources/Articles/ and give it a
    Sprite .meta. Byte-identical destinations are left alone so Unity does not
    reimport, and so a no-op build really is a no-op."""
    if not CONTENT_IMAGES_DIR.is_dir():
        return []
    staged = [
        p
        for p in sorted(CONTENT_IMAGES_DIR.iterdir())
        if p.is_file() and p.suffix.lower() in IMAGE_EXTENSIONS
    ]
    if not staged:
        return []
    ARTICLE_IMAGES_DIR.mkdir(parents=True, exist_ok=True)
    fresh_meta_from(
        FOLDER_META_TEMPLATE,
        ARTICLE_IMAGES_DIR.parent / (ARTICLE_IMAGES_DIR.name + ".meta"),
    )
    actions: List[str] = []
    for src in staged:
        dest = ARTICLE_IMAGES_DIR / src.name
        if dest.exists() and dest.read_bytes() == src.read_bytes():
            if verbose:
                actions.append(f"  = Articles/{src.name} (unchanged)")
        else:
            shutil.copy2(src, dest)
            actions.append(f"  + Articles/{src.name}")
        if fresh_meta_from(
            SPRITE_META_TEMPLATE, dest.with_suffix(dest.suffix + ".meta")
        ):
            actions.append(f"  + Articles/{src.name}.meta (Sprite, fresh GUID)")
    return actions


# ---------------------------------------------------------------------------
# Build
# ---------------------------------------------------------------------------


def serialize(articles: List[Dict[str, Any]]) -> str:
    payload = {
        "schemaVersion": SCHEMA_VERSION,
        "_generated": GENERATED_NOTE,
        "articles": articles,
    }
    return json.dumps(payload, ensure_ascii=False, indent=2, sort_keys=False) + "\n"


def build(check_only: bool, verbose: bool) -> int:
    diag = Diagnostics()

    if not CONTENT_DIR.is_dir():
        print(f"No content directory at {CONTENT_DIR}", file=sys.stderr)
        return 1

    sources = sorted(p for p in CONTENT_DIR.glob("*.md") if p.name != "README.md")
    if not sources:
        print(f"No articles found in {CONTENT_DIR}", file=sys.stderr)
        return 1

    installed: List[str] = []
    if not check_only:
        installed = install_images(verbose)

    index = ResourceIndex(diag)

    articles: List[Dict[str, Any]] = []
    seen_ids: Dict[str, Path] = {}
    for path in sources:
        article = build_article(path, diag, index)
        if article is None:
            continue
        aid = article["id"]
        if aid in seen_ids:
            diag.error(
                path,
                1,
                f"duplicate id {aid!r} — already used by "
                f"{seen_ids[aid].relative_to(REPO_ROOT)}",
            )
            continue
        seen_ids[aid] = path
        articles.append(article)

    # Index order is the contract's: order asc, then title.
    articles.sort(key=lambda a: (a["order"], a["title"]))

    diag.report()
    if not diag.ok:
        # "nothing written" would be a lie when photos were staged: install_images()
        # deliberately runs BEFORE validation, because an `![](Articles/foo)` can only
        # be checked against a file that is already in Resources. So say what happened.
        staged = (
            f" ({len(installed)} staged photo(s) had already been installed — that step "
            f"runs first so image references can resolve)" if installed else ""
        )
        print(
            f"\n{len(diag.errors)} error(s) — no articles.json written{staged}. "
            f"Fix the files above and re-run.",
            file=sys.stderr,
        )
        return 1

    text = serialize(articles)
    total_blocks = sum(len(a["blocks"]) for a in articles)
    kinds: Dict[str, int] = {}
    for a in articles:
        for b in a["blocks"]:
            kinds[b["type"]] = kinds.get(b["type"], 0) + 1

    if verbose:
        for a in articles:
            per: Dict[str, int] = {}
            for b in a["blocks"]:
                per[b["type"]] = per.get(b["type"], 0) + 1
            shape = ", ".join(f"{k}×{v}" for k, v in sorted(per.items()))
            print(
                f"  {a['id']:<34} order={a['order']:<5} "
                f"{len(a['blocks']):>3} blocks, {a['wordCount']:>4} words "
                f"[{a['category']}]"
            )
            print(f"      {shape}")

    if check_only:
        stale = not OUTPUT_JSON.is_file() or OUTPUT_JSON.read_text(
            encoding="utf-8"
        ) != text
        if stale:
            # --check is the CI / pre-commit gate, and the failure it exists to catch
            # is exactly "edited a .md, forgot to regenerate": the app reads ONLY
            # articles.json, so a tree where the two disagree ships stale text to a
            # phone with nothing in any log to say so. A note that still exits 0 is
            # not a gate, so this is an error.
            print(
                f"articles-check FAILED — the sources are valid "
                f"({len(articles)} article(s), {total_blocks} block(s), "
                f"{len(diag.warnings)} warning(s)), but "
                f"{OUTPUT_JSON.relative_to(REPO_ROOT)} does not match them. "
                f"Run `make articles` and commit the result.",
                file=sys.stderr,
            )
            return 1
        print(
            f"articles-check OK — {len(articles)} article(s), "
            f"{total_blocks} block(s), {len(diag.warnings)} warning(s)"
        )
        return 0

    for line in installed:
        print(line)

    unchanged = OUTPUT_JSON.is_file() and OUTPUT_JSON.read_text(encoding="utf-8") == text
    if unchanged:
        print(f"  = {OUTPUT_JSON.relative_to(REPO_ROOT)} (unchanged)")
    else:
        OUTPUT_JSON.parent.mkdir(parents=True, exist_ok=True)
        with open(OUTPUT_JSON, "w", encoding="utf-8", newline="\n") as fh:
            fh.write(text)
        print(f"  + {OUTPUT_JSON.relative_to(REPO_ROOT)}")
    if fresh_meta_from(
        TEXTASSET_META_TEMPLATE, OUTPUT_JSON.with_suffix(OUTPUT_JSON.suffix + ".meta")
    ):
        print(f"  + {OUTPUT_JSON.name}.meta (TextAsset, fresh GUID)")

    kind_list = ", ".join(f"{k}×{v}" for k, v in sorted(kinds.items()))
    print(
        f"Wrote {len(articles)} article(s), {total_blocks} block(s), "
        f"{len(text.encode('utf-8'))} bytes"
    )
    print(f"  block types: {kind_list}")
    if diag.warnings:
        print(f"  {len(diag.warnings)} warning(s) — see above")
    return 0


# ---------------------------------------------------------------------------
# Self-test — the escaping contract, in memory, no filesystem
# ---------------------------------------------------------------------------


def self_test() -> int:
    diag = Diagnostics()
    p = Path("selftest.md")
    failures: List[str] = []

    def eq(got: Any, want: Any, label: str) -> None:
        if got != want:
            failures.append(f"{label}\n      got:  {got!r}\n      want: {want!r}")

    # Author angle brackets can never become tags; `&` and `>` are inert and are
    # emitted untouched (UI Toolkit decodes no entities — `&amp;` would print).
    LT = ESCAPED_LT
    eq(
        render_inline('Escreva <b>assim</b> & pronto', diag, p, 1),
        f"Escreva {LT}b>assim{LT}/b> & pronto",
        "escape: author tags are neutralised, `&` is left alone",
    )
    eq(
        render_inline("<color=red>x</color>", diag, p, 1),
        f"{LT}color=red>x{LT}/color>",
        "escape: rich-text tag is neutralised",
    )
    eq(
        render_inline('Foto: <autor> / <licença> (<fonte>)', diag, p, 1),
        f"Foto: {LT}autor> / {LT}licença> ({LT}fonte>)",
        "escape: the repo's credit convention survives as literal text",
    )
    # The injection the per-character wrapping exists to close: the noparse
    # region holds exactly one `<`, so an author's own `</noparse>` cannot end it.
    eq(
        render_inline("</noparse>", diag, p, 1),
        f"{LT}/noparse>",
        "escape: an author cannot close the noparse region",
    )
    # An author who types an entity gets the entity, literally — nothing decodes it.
    eq(render_inline("&lt;", diag, p, 1), "&lt;", "escape: entities are not decoded")
    eq(render_inline("a > b", diag, p, 1), "a > b", "escape: `>` is inert on its own")

    # Our own inline markup.
    eq(
        render_inline("**forte** e *leve* e _também_", diag, p, 1),
        "<b>forte</b> e <i>leve</i> e <i>também</i>",
        "emphasis",
    )
    # `<u>` is the whole of a link's visual treatment: an `<a href>` span gets
    # none of its own and USS cannot reach inside one.
    eq(
        render_inline("[o site](https://mergulhovirtual.dev)", diag, p, 1),
        '<a href="https://mergulhovirtual.dev"><u>o site</u></a>',
        "link is underlined",
    )
    eq(
        render_inline("[**forte**](https://a.dev/x_y_z)", diag, p, 1),
        '<a href="https://a.dev/x_y_z"><u><b>forte</b></u></a>',
        "link label takes emphasis; underscores in the URL are left alone",
    )
    eq(
        render_inline("veja https://a.dev/um_dois_tres agora", diag, p, 1),
        "veja https://a.dev/um_dois_tres agora",
        "bare URL underscores are not italics",
    )

    # A non-https link is an error, not a silent pass-through.
    d2 = Diagnostics()
    render_inline("[x](javascript:alert(1))", d2, p, 1)
    if not d2.errors:
        failures.append("non-https inline link should be an error")

    # So is one past UI Toolkit's 256-character <a href> limit, which truncates
    # silently and would ship a link that opens the wrong page.
    d2b = Diagnostics()
    long_url = "https://a.dev/" + "x" * MAX_LINK_HREF_CHARS
    render_inline(f"[x]({long_url})", d2b, p, 1)
    if not d2b.errors:
        failures.append("an over-long link href should be an error")
    d2c = Diagnostics()
    render_inline("[x](https://a.dev/" + "x" * (MAX_LINK_HREF_CHARS - 15) + ")", d2c, p, 1)
    if d2c.errors:
        failures.append(f"a link href at the limit should pass: {d2c.errors}")

    # The emitted-tag invariant must actually fire on a bad string.
    d3 = Diagnostics()
    check_emitted_tags("<color=red>", d3, p, "test")
    if not d3.errors:
        failures.append("check_emitted_tags should reject <color=red>")
    d4 = Diagnostics()
    check_emitted_tags(
        f'<b>a</b><i>b</i><a href="https://x.dev"><u>c</u></a> {ESCAPED_LT}d> & e > f',
        d4,
        p,
        "test",
    )
    if d4.errors:
        failures.append(
            f"check_emitted_tags should accept the allowed tag set: {d4.errors}"
        )
    # A bare `<` with no `>` after it matches no tag pattern at all — the case
    # the old finditer-based check could not see.
    d4b = Diagnostics()
    check_emitted_tags("a < b", d4b, p, "test")
    if not d4b.errors:
        failures.append("check_emitted_tags should reject an unescaped bare `<`")
    # A lone half of the escape sequence is not the escape sequence.
    d4c = Diagnostics()
    check_emitted_tags("<noparse>x</noparse>", d4c, p, "test")
    if not d4c.errors:
        failures.append("check_emitted_tags should reject a stray <noparse> span")

    # Block parsing, including the comment strip and the level-1 heading error.
    src = [
        "---",
        "id: t",
        "title: T",
        "summary: S",
        "category: C",
        "---",
        "<!-- nota do autor -->",
        "## Dois",
        "### Três",
        "Um parágrafo.",
        "",
        "- a",
        "- b",
        "",
        "1. um",
        "2. dois",
        "",
        "> [!info] Aviso",
        "",
        "> Citação",
        "> — Autor",
        "",
    ]
    d5 = Diagnostics()
    idx = ResourceIndex(d5)
    fm, start = parse_front_matter(src, d5, p)
    blocks = ArticleParser(p, d5, idx).parse(src, start)
    eq(
        [b["type"] for b in blocks],
        [
            "heading",
            "heading",
            "paragraph",
            "bulletList",
            "numberedList",
            "callout",
            "quote",
        ],
        "block sequence",
    )
    eq(blocks[0]["level"], 2, "## is level 2")
    eq(blocks[1]["level"], 3, "### is level 3")
    eq(blocks[5]["tone"], "info", "callout tone")
    eq(blocks[6].get("attribution"), "Autor", "quote attribution")
    if d5.errors:
        failures.append(f"clean source produced errors: {d5.errors}")

    d6 = Diagnostics()
    ArticleParser(p, d6, idx).parse(["# Título"], 0)
    if not d6.errors:
        failures.append("a level-1 heading in the body should be an error")

    d7 = Diagnostics()
    ArticleParser(p, d7, idx).parse(["@peixe[abc]"], 0)
    if not d7.errors:
        failures.append("an unknown directive should be an error")

    d8 = Diagnostics()
    parse_front_matter(["---", "id: x"], d8, p)
    if not d8.errors:
        failures.append("unterminated front matter should be an error")

    d9 = Diagnostics()
    parse_front_matter(["---", "autor: x", "---"], d9, p)
    if not d9.errors:
        failures.append("an unknown front-matter key should be an error")

    if failures:
        print("self-test FAILED:", file=sys.stderr)
        for f in failures:
            print(f"  - {f}", file=sys.stderr)
        return 1
    print("self-test OK — escaping, inline markup, blocks and front matter")
    return 0


def main() -> int:
    ap = argparse.ArgumentParser(
        description="Build Assets/Resources/articles.json from content/articles/*.md"
    )
    ap.add_argument(
        "--check",
        action="store_true",
        help="validate only; write nothing, exit non-zero on any error (CI mode)",
    )
    ap.add_argument(
        "--verbose", "-v", action="store_true", help="print per-article block counts"
    )
    ap.add_argument(
        "--self-test",
        action="store_true",
        help="run the in-memory parser/escaping assertions and exit",
    )
    args = ap.parse_args()
    if args.self_test:
        return self_test()
    return build(check_only=args.check, verbose=args.verbose)


if __name__ == "__main__":
    sys.exit(main())
