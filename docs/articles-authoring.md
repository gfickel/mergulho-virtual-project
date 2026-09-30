# Authoring educational articles

"Show the educational material" is three independent decisions —
**authoring** (where does the biologist type?), **transport** (how does it reach the phone and stay
there offline?) and **rendering** (how does it appear?) — and
[educational-content-options.md](educational-content-options.md) §2 insists on deciding them
separately. This document covers the **authoring** front door that shipped first: Markdown files in
the repo, parsed at build time. The rendering answer (native UI Toolkit screens over typed block
JSON) and the transport answer (bundled in `Resources`) are §13's recommendation, and neither is
affected if the front door is later replaced.

## The pipeline

```
content/articles/*.md          ← the maintainer edits these (one file per article)
content/articles/images/*.jpg  ← optional: photos the article brings with it
        │
        │  make articles   →  tools/build_articles.py   (Python 3, stdlib only, no venv)
        │                     • parses the constrained Markdown subset
        │                     • validates every reference (species, beach, image, URL)
        │                     • neutralises author `<`, then inserts only <b> <i> <u> <a href>
        │                     • installs staged photos + a Sprite .meta
        ▼
src/app/MergulhoVirtual/Assets/Resources/articles.json   ← GENERATED, never hand-edited
src/app/MergulhoVirtual/Assets/Resources/Articles/*.jpg  ← installed photos
        │
        │  Resources.Load  +  JsonUtility
        ▼
ArticleLibrary  →  ArticleCatalogAdapter (IArticleCatalog)  →  ArticlesViewModel / ArticleViewModel
        ▼
ArticlesScreen (index)  ·  ArticleScreen (one article)
```

**No Markdown parser ships in the APK.** The build step *is* the renderer; the app only ever reads
one typed JSON file with `JsonUtility`, the same forgiving-loader pattern as `beaches_content.json`.
That is [§6.1](educational-content-options.md#61-parse-at-build-time-ship-typed-json)'s whole
argument, and the operative half of it is the second benefit: **malformed authoring fails in CI
rather than on a phone at a beach.**

## Block schema

`articles.json` is a top-level **object** (`JsonUtility` cannot read a top-level array) with
`schemaVersion`, a `_generated` banner and `articles[]`. Each article carries `id`, `title`,
`summary`, `category`, `order`, `wordCount`, `blocks[]`, plus `heroImage` / `heroCredit` / `updated`
when present. Every block is a **flat struct with a string discriminator** — the `JobEnvelope`
pattern — because `JsonUtility` cannot do polymorphism. Unused fields are omitted and land as C#
defaults.

| Authored | Emitted block |
|---|---|
| `## Texto` / `### Texto` | `{"type":"heading","level":2\|3,"text":"…"}` |
| prose between blank lines | `{"type":"paragraph","text":"…"}` |
| `- item` / `* item` runs | `{"type":"bulletList","items":["…"]}` |
| `1. item` runs | `{"type":"numberedList","items":["…"]}` |
| `> [!info\|warning\|success\|error] Texto` | `{"type":"callout","text":"…","tone":"info"}` |
| `> Texto` (+ `> — Autor`) | `{"type":"quote","text":"…","attribution":"…"}` |
| `![Legenda](Pasta/arquivo "Crédito")` | `{"type":"image","src":"…","caption":"…","credit":"…"}` |
| `@video[Título](https://…)` | `{"type":"video","url":"…","title":"…"}` |
| `@especie[tiger_shark]` | `{"type":"speciesRef","key":"tiger_shark"}` |
| `@praia[Sueste Beach]` | `{"type":"beachRef","name":"Sueste Beach"}` |

The complete field union, and nothing else: `type`, `level`, `text`, `items`, `tone`,
`attribution`, `src`, `caption`, `credit`, `url`, `title`, `key`, `name`. Fields are emitted in
that order so the file is byte-stable across runs.

### Escaping — the load-bearing part

**Only `<` is touched, and it is not HTML-escaped.** UI Toolkit's text generator decodes no named
HTML entities, so `&amp;` renders as the five literal characters `&amp;` — this was measured from a
render, not assumed. The only character that can open a tag is `<`; `&` and `>` are inert on their
own and are emitted exactly as the author typed them.

Each author `<` is emitted as `<noparse><</noparse>` — a supported Unity 6000.3 tag that suspends
tag parsing for its contents — which renders a visible `<` that cannot open anything. Wrapping is
**per character**, and that is what makes it provably injection-proof: an author typing `</noparse>`
becomes `<noparse><</noparse>/noparse>`, whose noparse region holds exactly one `<`, so the rest is
literal text and nothing can escape the region. This all runs **before** any of our tags are
inserted; links are then pulled out to sentinels before the emphasis regexes run, because a URL may
legitimately contain `_` or `*`.

The only tags that may appear in the output are `<b>`, `<i>`, `<u>` and `<a href="https://…">`.
**The build asserts this on every emitted string** with a left-to-right scan: every `<` must begin
one of those tags or the exact escape sequence, and nothing else — which also catches a bare `<`
with no `>` after it, the most dangerous case and the one a `<[^>]*>` regex cannot see.
`tools/build_articles.py --self-test` pins the behaviour in memory, including a literal
`Foto: <autor> / <licença> (<fonte>)`, which is exactly the repo's credit convention and therefore
the realistic way an author produces angle brackets.

**Links are underlined, and that is the whole of their visual treatment.** An `<a href>` span gets
no colour and no underline of its own, and USS cannot reach inside one — so an un-underlined link
reads as ordinary prose and the reader cannot tell it is tappable. The emitter therefore writes
`<a href="…"><u>texto</u></a>`. `<u>` carries no colour, so it does not smuggle a literal past the
token-discipline rule the way `<color=…>` would. A URL longer than **256 characters** is a hard
error: UI Toolkit truncates `<a>`/`<link>` there silently, which would ship a link that opens the
wrong page.

Front-matter prose (`title`, `summary`, `category`, `heroCredit`) goes through the same
neutralisation, for uniformity and because a UI Toolkit `Label` parses rich text by default. It is
**not** marked up — inline `**bold**` and links are confined to body blocks.

## Validation — what each error means

Every one of these is a hard error naming the file and the line; nothing is written until they are
all gone (`make articles` refuses to write, exactly like `make articles-check`).

| Error | Why it is an error |
|---|---|
| missing/empty `id`, `title`, `summary`, `category` | the index cannot render a card without them |
| unknown front-matter key | front matter is strictly typed, so `titel:` fails loudly instead of silently dropping the title |
| `id` not kebab-case, or duplicated | `id` is the stable route key; a collision would make one article unreachable |
| a level-1 `#` heading in the body | the title comes from front matter; two H1s is a structural mistake |
| a heading deeper than `###` | there is no block for level 4+ |
| unknown `@directive[…]`, or one not alone on its line | a typo'd directive would otherwise be emitted as literal prose |
| `image`/`hero` `src` that resolves to no file | `Resources.Load` returns null silently on device — the classic "blank card, no error" failure |
| `@especie[key]` with no `Animals/<key>.asset` | `SpeciesCatalogAdapter` logs nothing for a key it does not have; the card just never appears |
| `@praia[name]` not an exact `places.json` `name` | **`name`, not `displayName`** — the resolver, the spawner and the backend all key on `name`, and several differ (`Sueste Beach` vs `Baía do Sueste`) |
| a `@video` or inline-link URL that is not `https://` | keeps `javascript:` and friends out of `<a href>`, and cleartext HTTP off Android |
| an inline-link URL over 256 characters | UI Toolkit truncates `<a>`/`<link>` there silently, so the link would open the wrong page |
| unterminated front matter | otherwise the whole article silently becomes front matter |
| `---` used as a horizontal rule | not in the block vocabulary; use a `##` heading |
| an unknown callout tone | only `info`, `warning`, `success`, `error` have a style |

Warnings (build still succeeds): an article with no body blocks, a paragraph over 1200 characters,
an `updated` that is not `YYYY-MM-DD`, an `id` that differs from the file name.

## Installing an article's own photos

Drop the file in `content/articles/images/` and reference it as `Articles/<name>` (no extension).
`make articles` copies it to `Assets/Resources/Articles/` and, **when no `.meta` exists yet**,
clones one from `Animals/tiger_shark.jpg.meta` with a fresh 32-hex GUID — the
`tools/populate_animals.py` trick — so Unity imports it as a **Sprite** with no Editor trip. The
folder's own `Articles.meta` is cloned from `Animals.meta` the same way.

Caveats, all deliberate:

- An existing `.meta` is **never** clobbered: once Unity has seen the file, that `.meta` is
  Unity-managed and its GUID is referenced elsewhere.
- A byte-identical destination is left alone, so a no-op build triggers no Unity reimport.
- `make articles-check` copies nothing but still treats a staged file as resolvable, so CI passes
  before the install has happened.
- The four placeholder articles deliberately do **not** use this path — they reuse photos already
  imported under `Beaches/` and `Animals/`, so the mechanism is exercised by its own test rather
  than by committed binaries.

## Later: Google Docs

This Markdown front door is not the intended long-term authoring surface.
[educational-content-options.md](educational-content-options.md) §7Ⓐ recommends **Google Docs with
an Apps Script "Publicar" button**: the biologist writes in the tool they would use anyway,
`InlineImage.getBlob()` hands over real image bytes with `getAltDescription()` as the caption/credit
channel, and one `UrlFetchApp` multipart POST delivers the article plus every photo. It is roughly
80 lines of Apps Script and needs no OAuth verification (a service account, plus `@OnlyCurrentDoc`).

The reason it costs no app release to switch: **it lands on this same `articles.json`.** The block
schema above is the contract, the app reads nothing else, and §9.1's rule — the app must never talk
to a CMS directly — means the ingest is always ours. So the front door can become Google Docs, or
Pages CMS, or a Sanity project, and the only thing that changes is who writes the JSON. Whatever
replaces `build_articles.py` must keep two properties: the same escaping discipline (neutralise
author `<` first, emit only `<b> <i> <u> <a href>`) and the same reference validation, because both exist to stop bad
content reaching a phone where nobody can debug it.

## See also

- [content/articles/README.md](../content/articles/README.md) — the short how-to for authors.
- [educational-content-options.md](educational-content-options.md) §2, §6.1, §7Ⓐ, §13.
- `docs/beaches-content-todo.md` — the sibling content backlog, and the evidence behind this
  project's standing rule that **a blank beats an invention**.
