# Artigos educativos — como escrever um

One file per article. `make articles` parses them and writes
`src/app/MergulhoVirtual/Assets/Resources/articles.json`, which is the **only** thing the app
reads — no Markdown parser ships in the APK. Fuller guide: [docs/articles-authoring.md](../../docs/articles-authoring.md).

> ⚠️ **The four `.md` files here are placeholder content written by an implementer, not by a
> biologist.** They exist so every block type has a test case and the screens have something real
> to render. They contain no statistics, no dates, no species-identification claims and no
> safety/first-aid instructions, deliberately. **Replace them with reviewed text before release.**

## Three steps

1. Copy any existing file to `content/articles/<slug>.md` and edit the front matter + body.
2. `make articles` — it validates everything and writes the JSON. Errors name the file and line.
3. Commit the `.md` **and** the regenerated `articles.json` (+ its `.meta` on the first run).

## Front matter

Required: `id` (kebab-case, the stable key — normally the file name), `title`, `summary` (one
sentence), `category` (free pt-BR text, groups the index). Optional: `hero` (a Resources sprite
path), `heroCredit`, `order` (int, ascending, default 1000), `updated` (`YYYY-MM-DD`).
**Any other key is an error** — front matter is strictly typed, so typos fail loudly.
HTML comments (`<!-- … -->`) may sit above the opening `---` and are never emitted.

## Body syntax

| Write this | You get |
|---|---|
| `## Texto` / `### Texto` | a heading (level 2 / 3). A single `#` is an **error** — the title is front matter. |
| prose separated by blank lines | a paragraph |
| `- item` or `* item` runs | a bullet list |
| `1. item` runs | a numbered list |
| `> [!info] Texto` (`warning`, `success`, `error`) | a coloured callout |
| `> Texto` then optionally `> — Autor` | a pull quote (+ attribution) |
| `![Legenda](Beaches/praia_do_sancho "Foto: Autor / Licença (Fonte)")` | an image; legenda and crédito may be empty (`![]( … "")`) |
| `@video[Título](https://…)` | a tap-to-play video card |
| `@especie[tiger_shark]` | a species card that opens the Espécie screen |
| `@praia[Sueste Beach]` | a beach card that opens the Praia screen |
| `**negrito**`, `*itálico*`, `[texto](https://…)` | inline, inside prose / headings / list items / callouts / quotes |

Directives and images must be **alone on their line**. An author's `<` is neutralised, so typing
`<b>` shows up as the literal text `<b>` — only `<b> <i> <u> <a href>` can ever reach the renderer.
You do **not** need to avoid `&` or `>`; they are written through untouched.

## The keys you cannot invent

- **Species** — exactly these five: `hammerhead`, `tiger_shark`, `lemon_shark`, `nurse_shark`,
  `reef_shark` (the `.asset` names in `Assets/Resources/Animals/`).
- **Beaches** — the `name` field of `Assets/Resources/places.json`, **not** `displayName`. Several
  differ: it is `Sueste Beach`, not `Baía do Sueste`. List them with
  `python3 -c "import json;[print(p['name']) for p in json.load(open('src/app/MergulhoVirtual/Assets/Resources/places.json'))]"`.
- **Images** — `Resources.Load` paths **without an extension**, e.g. `Beaches/praia_do_sancho` or
  `Animals/hammerhead`. Your own photos go in `images/` here; see that folder's README.

## Commands

- `make articles` — build and write. `make articles VERBOSE=1` adds per-article block counts.
- `make articles-check` — validate only, write nothing, non-zero exit on any error (CI / pre-commit).
- `python3 tools/build_articles.py --self-test` — the parser's own escaping assertions.
