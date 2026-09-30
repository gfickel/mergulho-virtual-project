# Showing the educational material in the app — options and research

**Researched 2026-09-29. No decision has been taken.** This is the evidence, not a plan. Seven
parallel research passes went into it (two repo recon, five external); everything here was either
read out of this repo or fetched live on that date. **Verification is flagged throughout** — ✅ means
directly verified by fetching the vendor's or Unity's own page that day, ⚠️ means the researcher
could not confirm it and it must not be cited onward as fact. Several ⚠️ items are decision-critical
and each has a short hands-on test in §11.

Every price is USD unless marked. FX used where a BRL figure appears: **USD 1 = BRL 5.2115**
(frankfurter.dev, 2026-09-29).

---

## 1. The premises this was researched against

Answered by the maintainer at the start, and they narrow the field hard:

| Question | Answer |
|---|---|
| State of the material | **Not written yet.** Mostly text, some images, a few videos. Must be **visually appealing**. |
| Who maintains it | **One biologist/educator, occasional edits.** |
| Offline | **All text and images must work offline. Videos may stream.** |
| Volume | **Medium — 10–30 sections.** |

Read together: the *app* must own the visual design (the author cannot be asked to lay anything
out), the content must be on the device before the user reaches a beach, and the authoring surface
has to be pleasant enough to use thirty times by someone who will never open Unity.

**The original idea was to ship a PDF.** §4 is why that cannot work here, and it is a harder "no"
than the usual usability argument.

---

## 2. Decision framing — three independent axes

"Show a PDF" is one answer to three separate questions, and a bad answer to each. Decide them
separately:

1. **Authoring** — where does the biologist type? (§7, §8)
2. **Transport** — how does content reach the phone and stay there offline? (§6)
3. **Rendering** — how does it appear on screen? (§5)

The single most useful structural finding in the whole exercise (§9.1) is that **the app must never
talk to a CMS directly**. Ingest server-side into the existing FastAPI + Firestore + GCS stack and
let Unity read only our own endpoints. That makes every vendor's API rate limit a property of *our
nightly ingest job* rather than of every user's phone, and it makes the authoring choice **swappable
without an app release**. Consequence: the authoring decision in §7 is reversible and therefore not
worth agonising over.

---

## 3. What this repo already has (recon findings)

- **There is no educational-material artefact in the repo.** `cartilha`, `apostila`, `aula`,
  `artigo`, `lesson`, `quiz` return **zero hits** repo-wide.
- **The client's own architecture doc already specifies this module and it has never been built.**
  [docs/mergulho_virtual_arquitetura.md](mergulho_virtual_arquitetura.md) §1.1.1 and §2.4: core
  information **offline**, enriched when connected with video, images and **documents**; content in
  **pt/en/es**; **audiodescription** downloadable after install; **first-aid content**. §1.1.4 asks
  for *"cadastro de novos recursos educacionais"* in the backend admin. **None of it exists** — no
  endpoint, no module, no config, no i18n.
- The same doc names **TRN** as the content owner (*"imagens e vídeos coletados pelo TRN e
  organizações parceiras ao longo de anos"*), **Projeto Tamar and ICMBio** as external sources, and
  — mentioned nowhere else in the repo — says the app *"também pode ser disponibilizado em **mesas
  interativas e totens**"*. If the kiosk deployment is real, it argues for a content format that can
  also render in a browser.
- **The contract puts content creation on the client.**
  [docs/proposta_prestacao_servico.md](proposta_prestacao_servico.md) §3.3 excludes *"criação de
  conteúdo textual/fotográfico de terceiros"*; §7 lists it as the Contratante's responsibility. No
  educational deliverable appears in any of the three stages.
- **`conteudos-educacionais`** (public GCS bucket, `videos/` prefix) holds **exactly one object**,
  referenced twice by mistake from `lemon_shark.asset`. No tooling, no Makefile target, no backend
  knowledge of it. Publishing is: encode H.264 `+faststart` → `gcloud storage cp` → hand-edit a
  `.asset` → rebuild the APK.
- `estimated_cost.md` sizes the eventual media plan at **~100 items / ~10 GB**, and warns that every
  streamed video costs **$0.12/GB** egress. See §9.2.
- **The only legitimate PDF in the project** is the DHN tide table, and Início already draws a
  **"Baixar a tábua de maré do mês"** link that is a deliberate logging no-op because the PDF is
  published nowhere. Hosting it in the bucket and `Application.OpenURL`-ing an `https://` URL closes
  that dead link. It is tracked in four places.

### 3.1 The D8 lesson — the most decision-relevant fact in this document

This project **already tried** "give the biologists a structured text file and a careful pt-BR
guide". [docs/beaches-content-todo.md](beaches-content-todo.md) is 534 lines, opens *"não exige
saber programar"*, and then explains escaped quotes and asks the author to *"peça para alguém rodar
`python3 -m json.tool`"*.

Measured fill rate of `beaches_content.json` today:

| Field | Filled |
|---|---|
| `riskLevel`, `bestSeason`, `sightingPeak`, `lifeguardHours`, `tips` | **0 / 17** each |
| `species[].tag`, `species[].behaviour` | **0 / 5** rows each |
| `AnimalDef` `approximateSize` / `diet` / `behaviour` | **0 / 15** strings |
| `species` | 3 / 17 |
| `idealTide` | 4 / 17 |
| `advisories` | 7 / 17 |
| `environmentTags` | 11 / 17 |

**Every non-blank field was machine-derived** from `places.json` or the scene's spawner list —
`_sources` proves it. Not one field was written by a human with domain knowledge.

**So the bar is not "a friendlier file format". The bar is that the author never sees a file.**
Any option in §7 that ends with a person editing JSON, YAML or Markdown source has already been
tested here and produced nothing.

Two corollaries worth keeping: the repo-wide editorial rule **"a blank beats an invention"** is
load-bearing and enforced in code (`BeachContentTokens` returns `Unknown`, never a guess; every
screen hides a missing block rather than printing a placeholder), and it was **sold to the client**
in [docs/relatorio_etapa_2.md](relatorio_etapa_2.md) as the reason the app can ship content-empty.

---

## 4. Why a PDF cannot work here

Not the usual usability complaint. Three independent blockers, in order of decisiveness:

1. **Unity cannot open a local PDF on either platform.** ✅ `Application.OpenURL` docs, Unity 6:
   Android — *"Application.OpenURL can no longer be used for opening local app files"* (use
   `FileProvider`); iOS — *"cannot be used for opening local files."* So *bundle a PDF for offline
   reading* requires either a native plugin we write (Android `FileProvider` + `ACTION_VIEW`, iOS
   QuickLook) or an in-Unity renderer — and ⚠️ **the researcher could not verify that any
   maintained, Unity-6, IL2CPP-safe, Android+iOS PDF renderer exists.** Treat that as unproven.
   A *remote* `https://` PDF does open — in the external browser, backgrounding the app and tearing
   down the AR session, with the browser's own error page instead of our `MvStateView` copy when
   offline. That fails the offline premise outright.
2. **WCAG 2.2 SC 1.4.10 Reflow (AA)** ✅ requires content to work at **320 CSS px** wide *"without
   loss of information or functionality, and without requiring scrolling in two dimensions."* A
   fixed A4 page at 360–430 dp cannot reflow. The criterion's exception covers content that
   genuinely *"require[s] two-dimensional layout"* — maps and data tables qualify; an illustrated
   article does not. **SC 1.4.5 Images of Text (AA)** ✅ is a second failure for any rasterised page.
3. **Nielsen Norman Group, *"PDF: Still Unfit for Human Consumption, 20 Years Later"*, 2020-08-09**
   ✅, verbatim: *"PDFs are typically large masses of text and images. The format is intended and
   optimized for print."* Recommendation: *"Do not use PDFs to present digital content that could
   and should otherwise be a web page."* Their prescription — an HTML gateway page with an optional
   PDF download for printing — is exactly §5.① plus §3's tide-table link.

Also lost with a PDF: dynamic type / OS font scaling, dark mode, deep links to a section, in-app
search, per-article offline download granularity (it is whole-file or nothing, on island
connectivity), inline video that plays, translation, screen readers on untagged PDFs — and the
entire design system, including the 360 dp responsive work and `make ds-shots`.

**Flipbook services (Publuu, Issuu) are the same failure with a page-flip animation**, and were
rated the worst option surveyed: page rasters, text-as-image, 10–30 MB before first paint, nothing
structured for the client. See §8.4.

**Where a PDF is right:** something meant to be printed. The DHN tide table, and nothing else.

---

## 5. Rendering — four options

### ① Native UI Toolkit article screens over a block list — RECOMMENDED

**We have already built this renderer without noticing.**
[EspecieScreen.cs](../src/app/MergulhoVirtual/Assets/UI/Screens/EspecieScreen.cs) (996 lines)
renders hero image → title → prose card → spec rows → 3D model → inline tap-to-play video cards →
credits, inside `AppScrollView`, from an engine-free data object, with `MvStateView` for the offline
state. That *is* an illustrated-article reader.

An `ArticleScreen` walking a `blocks[]` array and emitting `Label` / `Image` / `MdCard` /
`MvAlertBar` / `MvNumberedList` / `MvMediaCarousel` / video card needs **no new dependency, no new
licence, and no new attack surface**, and inherits the design tokens, light/dark parity,
`TokenDisciplineTests`, and `make ds-shots`.

Cost, in this project's own measured units: two screens (index + article) at the going rate of
**~400–1000 lines C# + ~250–480 lines USS each**, one loader cloned from `BeachContentLibrary`, one
interface + adapter in `UiServiceAdapters`, one backend converter.

**Why it is the only option that satisfies "must look visually appealing" given one non-designer
author:** the app supplies the design, so the author *cannot* make it ugly. And `make ds-shots`
renders the screen to PNG headlessly, so the biologist can be handed **a proof of exactly how their
text looks in the app** after every edit — the feedback loop the `beaches_content.json` attempt
never had.

**What UI Toolkit rich text can and cannot do** (✅ all from the Unity 6000.3 supported-tags table):
**35 tags** including `<b> <i> <u> <s>`, `<color> <alpha> <gradient> <mark>`, `<size> <font>
<font-weight>`, `<br> <align> <indent>`, **`<line-height>`**, `<nobr>`, `<sub> <sup>`, `<noparse>`,
**`<a href>`**, **`<link="ID">`**, **`<sprite>`**. Genuinely absent: `<material>`, `<page>`,
`<rotate>`, **lists, tables, `<hr>`**.

Three findings that matter:

- ⚠️→✅ **`<line-height>` exists as a rich-text tag.** CLAUDE.md says "no `line-height`" — correct
  for the **USS property** (still absent in 6000.3), **wrong for the tag**. Together with
  **`-unity-paragraph-spacing: <length>`**, which *is* a real inherited USS property, the documented
  V2 fixed-line-height gap is largely closable.
- **`<sprite>` cannot carry article photos.** It needs a build-time TextCore Sprite Asset atlas
  resolved through the **panel's** `TextSettings`, and both our PanelSettings have
  `textSettings: {fileID: 0}` — unset. It is a glyph-sized, baseline-aligned icon mechanism. Article
  images must be element blocks.
- **`<a href>` cannot be intercepted** — it is hardcoded in `TextEventHandler.ATagOnPointerUp` to
  call `Application.OpenURL` with no callback hook. For in-app cross-references use
  **`<link="ID">`** plus `PointerDown/Up/Move/Out/OverLinkTagEvent`, which carry `linkID` **and**
  `linkText`. Caveat: namespace **`UnityEngine.UIElements.Experimental`**, docs say *"still in the
  process of becoming stable"*, and both `<a>` and `<link>` have a **256-character limit**.

So: rich text for everything *inside* a paragraph, composition for block structure. That split is
right anyway — it keeps images out of the text generator and lets us control exactly which tags ever
reach it.

### ② WebView / hosted web article — wrong here, with one narrow exception

| | Licence / price | Rendering | Status |
|---|---|---|---|
| gree/unity-webview | Zlib, free ✅ | **Native overlay** — *"does not support these views in 3D"* ✅ | 2.7k ★, **436 open issues** ✅; last commit ⚠️ |
| Vuplex 3D WebView | **$179.99 Android alone** ✅, platforms sold separately | `Texture2D` + a Native 2D mode ✅ | Android v4.15.2, 2026-06-12 ✅; Unity 6 support ⚠️ |
| UniWebView 6.x | **$29.99 one-time** ✅ | Native overlay; **plus "Safe Browsing": SFSafariViewController on iOS, Chrome Custom Tabs on Android** ✅ | Unity 6 statement ⚠️ |

Why it is wrong for the primary content: a **native overlay** sits above *everything* including our
`sortingOrder 100` panel and `MdNavigationBar`, so the article cannot live inside the shell;
texture mode costs $179.99/platform. Either way we would run **two design systems** — M3 tokens in
USS everywhere else, CSS inside the frame, where `TokenDisciplineTests`, `ds-shots` and the whole
fidelity discipline stop at the boundary — and we would add a remote-code surface to an app whose
API is App Check-gated precisely so there is none. ⚠️ `file://`-from-`persistentDataPath` support
and build-size deltas are unverified for all three.

**The narrow exception, and it is good value:** UniWebView's Safe Browsing mode at **$29.99
one-time** is the right way to open *external* links (Instagram permalink, ICMBio, sources) as an
in-app sheet without backgrounding the app. Unrelated to the main content decision.

### ③ Designed pages exported as images — the honest fallback, now weaker

Near-zero engineering (a pager over `MvMediaCarousel`), perfect offline, guaranteed WYSIWYG, and
Canva's Connect API can export **per-page PNG** (40–25000 px, 1-indexed page selection, async
job + poll, 24-hour URLs, **500 exports/24 h per user**) ✅.

Acceptable **only** under all of these conditions: authored at **phone aspect (e.g. 1080×1920), not
A4**; a handful of pages, not a library; each page ships a **plain-text alternative** in the JSON
for search and accessibility; understood as a visual *supplement*. A4 pages as the main format is
§4 wearing a hat.

Two findings that weakened it since the first pass:
- ✅ **Canva free plans "cannot upscale fixed-dimension designs beyond 1.125×"**, so a free-plan
  author designing at 540×960 cannot cleanly deliver 1080×1920. It needs Canva Pro.
  ⚠️ Canva for Nonprofits (existence in 2026, Brazilian *associação*/OSCIP eligibility, and whether
  it includes pro-quality exports) is **unverified** — canva.com returned 403 to four fetch attempts.
- ✅ The Apps SDK Content Querying API is **richtext only** (*"images and videos… explicitly
  unsupported"*), cannot read positions, and *"the only supported target is the current page"* — no
  multi-page querying. **Canva gives page rasters, scriptably; it does not give text.** Reading
  order and image↔caption pairing cannot be reconstructed.
- ✅ Google Slides is similar but worse: `files.export` supports **PPTX, ODP, PDF, TXT only** —
  *"PNG and SVG are not supported for presentations or individual slides"*. The only per-slide raster
  is `presentations.pages.getThumbnail`: PNG only, max `LARGE` = 1600 px, **30-minute URLs**, and it
  *"counts as an expensive read request for quota purposes."*

### ④ PDF — see §4.

### 5.1 Markdown on device — cheaper than assumed, and probably unnecessary

**We already ship a Markdown parser.** ✅ `com.unity.dt.app-ui` **2.2.2** — still in
`Packages/manifest.json`, still resolved, referenced by **zero** C# files — contains
`Runtime/Markdown/Plugins/Markdig.dll` = **Markdig 0.37.0, the netstandard2.1 build, 474,624 bytes**,
asmdef `includePlatforms: []` (all platforms, not editor-only), gated on the scripting define
`APPUI_ENABLE_MARKDOWN`, with BSD-2 attribution already declared in App UI's Third Party Notices.
So Markdown costs **no new dependency and no new licence obligation**.

**Its own `MarkdownView` is still not the answer**, for three reasons:
1. ✅ **It does not render images at all** — the renderer's doc comment: *"Image links emit their alt
   text and the URL as plain text — fetching remote bitmaps is out of scope."* `![Tubarão](foto.jpg)`
   renders as that literal string. No video either.
2. ✅ It emits App UI components styled with `--appui-*` tokens, which `TokenDisciplineTests` rejects.
3. ✅ `sealed` with `internal` members (`InternalsVisibleTo` only for App UI's own assemblies), and no
   table renderer registered — tables parse then silently drop. **An image renderer cannot be added
   from our assembly.**

The right use would be the DLL plus ~10 of our own object renderers emitting `Md*`/`Mv*` components.
Reference implementation: **`UnityGuillaume/MarkdownRenderer`** (v1.2.0, 2025-07-25, Unity Companion
License) — editor-only, but the **only implementation found anywhere that handles both images and
video** (`![](x.mp4)` → pooled `VideoPlayer` + RenderTexture, the same approach as our
`VideoPlaybackAdapter`), with only ~10 `UnityEditor` call sites to port.

Constraints if we ever go there: use the **ns2.1** build only (✅ ns2.0 throws
`FileNotFoundException` in Unity — it references `System.Runtime.CompilerServices.Unsafe`, for which
Unity ships no shim, per App UI's own 2.2.2 changelog); **do not vendor Markdig source** (✅ 1.4.0
uses file-scoped namespaces across 313 files, `required` members and collection expressions; Unity
6.3 is **C# 9 only**); and parse **once on load, cached** — never during scroll, which also mitigates
Markdig's two unresolved AOT crash reports (both MAUI/Xamarin, not IL2CPP, both fast-scrolling parse
storms).

⚠️ **One thing to test before relying on it:** `Markdig.dll.meta` serialises `Any: enabled: 1` with
**`Editor: enabled: 0`**, which reads as "excluded from the Editor" — so `MarkdownView` might work in
a device build but **not in Play mode or `make ds-shots`**.

**But §6.1 probably makes this moot:** if the build step is the renderer, no Markdown parser ships in
the APK at all, and this whole subsection becomes optional.

**Negative result worth recording:** OpenUPM has **no runtime Markdown renderer** — 4,052 packages
enumerated, two contain "markdown", both editor-only. CommonMark.NET is abandoned (0.15.1,
**2017-02-20**).

### 5.2 A standing risk: the Advanced Text Generator

Unity is replacing the text generator under UI Toolkit, and this lands regardless of what we choose:

| Version | ATG status |
|---|---|
| 6.0 | opt-in |
| **6.3 (ours)** | **default for Editor UI**; runtime still standard |
| 6.4 | default for IMGUI |
| **6.5** | **default at runtime** |

Unity: *"we eventually plan on removing the ability to opt-out."*

- ✅ **Safe on the one documented breaking requirement:** ATG does not support *static* font assets,
  and our Inter/MaterialSymbols assets are TextCore **dynamic** SDF generated from TTFs by
  `make ds-setup`.
- ⚠️ **The critical unknown: whether `<link>` pointer events survive under ATG.** Unity 6.0's ATG
  page listed *"Rich text tags"* and *"Events"* as unsupported; the 6.3 page narrowed that to two
  items, and nobody in the 6.5 announcement thread mentions link tags. **If we build tappable
  in-paragraph links, smoke-test with `-unity-text-generator: advanced` before 6.5.**
- ⚠️ Forum-reported, not corroborated in the issue tracker: `<gradient>` broken in 6.3–6.6, fixed in
  6.7 (our `MdSparkline` paints via Painter2D, so no exposure), and `<rotate>` now erroring.
- ✅ Two smaller doc facts: rich text tags are **not supported in `TextField`**, and `cursor: link`
  does not work at runtime.

---

## 6. Transport — bundle the text, download the rest

The premise ("text and images offline, videos streamed") maps onto machinery **we already own and
have never called**:

- **[FileDownloadJob.cs](../src/app/MergulhoVirtual/Assets/Scripts/Jobs/FileDownloadJob.cs) is a
  complete durable downloader with zero production callers.** ✅ `DownloadHandlerFile` to
  `DestPath + ".partial"` with `removeFileOnAbort`, the watchdogged `Job.Send()` (never a bare
  `SendWebRequest`), optional **SHA-256 verification**, transient-vs-permanent classification feeding
  two backoff schedules, atomic finalize via `File.Replace(partial, DestPath, null)` / `File.Move`.
  It is already registered in `JobQueue.EnsureInitialized()`. This is the single biggest piece of free
  machinery for a content-pack feature.
- **Ship `articles.json` + the essential images in `Resources`** so the very first launch works with
  no network. Budget context: `Assets/Resources/` is **71 MB**, of which **58 MB is two ONNX models**;
  all 19 shipped JPEGs total **~13 MB**. A few more MB of article imagery is free by comparison.
  (One existing outlier worth fixing while in there: `praia_do_sancho.jpg` is **4928×3264, 8.73 MB** —
  73 % of the Beaches folder — clamped to 2048 at import by `maxTextureSize`.)
- **Download updates and heavier assets as a versioned pack**, so content changes without an app
  release.
- **Videos keep streaming from the public bucket**, as they do now. ⚠️ If educational clips need to
  survive offline, the Trello backlog already names the fix: download once via `FileDownloadJob` to
  `persistentDataPath` and point `VideoPlayer.url` at the local `file://` path. ✅ Caveat recorded in
  CLAUDE.md: `FileDownloadJob` does **not** HTTP-resume — each retry restarts at byte 0 — so a
  multi-MB pack on island connectivity wants `Range:` + `DownloadHandlerFile(append: true)`, and the
  CDN must be verified to return **206**, not 200 (a 200 would silently corrupt the file).

**Publishing pipeline: copy [services/instagram.py](../src/backend/services/instagram.py).** ✅ It
already implements exactly the right loop — poll source → normalize → download assets to disk via
tmp+rename → **publish the Firestore doc last, so any failure leaves the previous version serving**
(the last-known-good guarantee) — on a 30-minute asyncio loop, with `upload_bytes` and
`resize_image_preserving_exif` beside it. An `services/articles.py` is the same shape.

**Serving it:** ✅ a public read endpoint is cheap. `/` already serves un-gated HTML; App Check is
attached at **router level** (`api_router = APIRouter(prefix="/api/v1",
dependencies=[Depends(verify_app_check)])`), so exempting one route means a third router; Cloudflare
Access is scoped only to `/avistamentos` and `/telemetria`; and `PUBLIC_BASE_URL` already exists to
hand absolute URLs to the client. The Instagram endpoints are the precedent — read-only,
cache-backed, `Cache-Control: public, max-age=300`, upstream never touched in the request path.

**Two image constraints to design around** (✅ both):
- `ImageConversion.LoadImage` accepts **PNG, JPG or EXR only** and decodes **uncompressed** (JPG →
  `RGB24`, PNG → `ARGB32`). A 1280×960 JPG costs ~**3.7 MB of texture RAM**. Call
  **`Texture2D.Compress()`** (transcodes to ETC/EAC on Android/iOS), cap decoded size, and release
  textures on `OnExit`. Note also the panel's `m_MaxSubTextureSize: 64` — anything larger is **not
  atlased**, so N photos on one page is N texture bindings, batched 8 at a time.
- **No native WebP or AVIF.** A plugin would be needed; the ~25 % saving is not worth it. **Ship
  JPEG** through the existing `resize_image_preserving_exif`.

**What the design system already accepts:** ✅ `MvMediaCarousel`'s `MvMediaItem` carries **either** a
`Sprite` **or** a `Texture2D` (*"a downloaded photo arrives as a texture, a bundled one as a
sprite"*), and `MvHeroHeader` has both `SetImage` overloads. The precedent for local-file → UITK
`Image` is `ReportScreen.cs:815-829`.

**The one thing we do not have** is a generic remote-image cache. The repo's entire texture-decode
surface is **three lines** (`InstagramPostWidget.cs:177`, `:259`, `ReportScreen.cs:822`) and nothing
uses `DownloadHandlerTexture`. `InstagramPostWidget` is a *pattern* to re-derive (~80 lines:
cache-first → App-Check GET → change-detect by exact JSON string equality → atomic persist → hide the
section entirely on failure-with-no-cache), **not a class to reuse** — it is a uGUI-bound
MonoBehaviour hardcoded to two `/latest-post` paths.

**Established `persistentDataPath` conventions to follow:** `jobs/` + `jobs/failed/` (one file per
job), `sightings/<guid>.<ext>`, `instagram/{latest.json,media.jpg}`,
`conditions_<sanitized-beach>.json`. Atomic overwrite has **two accepted spellings** in the repo and
CLAUDE.md warns that having two is how the wrong one gets copied — match `FileDownloadJob`'s
`File.Replace`/`File.Move` pair. And every loader **logs once and degrades, never throws into a
screen** (`BeachContentLibrary`: missing file, malformed JSON, keyless entry, duplicate key, unknown
token — worst case an empty catalog that renders like an all-blank entry).

### 6.1 Parse at build time, ship typed JSON

The strongest form of the recommendation: make the **build step** (GitHub Action, or the FastAPI
ingest) the renderer. Parse Markdown/DOCX/CMS-JSON → emit a JSON array of typed blocks alongside
pre-resized images:

```
{type:"heading", level, text}
{type:"paragraph", text}            // inline <b>/<i>/<link> emitted by OUR converter, never authored
{type:"image", src, caption, credit}
{type:"video", url, title}
{type:"callout", tone, text}
{type:"numberedList", items[]}
{type:"speciesRef", key}            // → push AppRoutes.Especie
{type:"beachRef", name}
```

Four benefits: the app ships no parser; malformed authoring fails in CI rather than on a phone at a
beach; we control exactly which inline tags reach the text generator; and the on-device path stays
`JsonUtility` + the existing forgiving-loader pattern.

⚠️ **`JsonUtility` cannot do polymorphism, dictionaries, or top-level arrays.** A block list must
therefore be a **flat struct with a string type discriminator and optional fields** — exactly the
`JobEnvelope` pattern already used for `Job` subclasses (`SerializeData`/`DeserializeData` + a
`typeFactories` registry). Also: unknown JSON fields are silently ignored, which is what lets
`beaches_content.json` carry `_comment`/`_todo`/`_sources` markers; and DTO field names must match
keys exactly, hence the snake_case DTOs.

**Consider Portable Text's shape as the schema** — ✅ an **open spec**
(github.com/portabletext/portabletext), not Sanity-proprietary: an array of blocks each with
`_type`, a `style`, a `children` array of spans, and `markDefs`, extensible with custom types.
Adopting the shape costs nothing and makes every existing Markdown→PT and CMS→PT converter available.
Counter-argument for a build-time pipeline: ✅ `portable-text-to-markdown` was **deprecated in
December 2025**, and Markdown-producing sources are trivially parseable at build time, so this is a
mild preference either way.

---

## 7. Authoring — the axis that decides success

Re-read §3.1 first. Then:

### Ⓐ Google Docs + an Apps Script "Publicar" button — recommended first move

The material is not written yet and **they will draft it in Docs or Word regardless**, so this adds
no step to their workflow at all. Two verified mechanisms make it decisive:

1. ✅ **`InlineImage.getBlob()` returns the real image bytes — no expiry, no 10 MB cap, no second API
   call** — plus `getAltDescription()` / `getAltTitle()`, which is the caption/credit channel
   (right-click → Alt text). And `UrlFetchApp.fetch(url, {method:'post', payload:{...}})`
   *"automatically defaults to either `application/x-www-form-urlencoded` or `multipart/form-data`"*
   when the payload object contains Blobs — Google's own example puts a Blob in the object. So:
   **biologist writes → clicks *Publicar* in the Doc's own menu → FastAPI receives one multipart POST
   with the article JSON and every photo's durable bytes.** ~80 lines of Apps Script.
   Quotas ✅: UrlFetch **20,000/day** (consumer) or 100,000, **50 MB per call**, 6-minute runtime,
   90 min/day of trigger time. We need about ten calls.
2. ✅ **A service account sidesteps Google's OAuth verification entirely.** Type
   `863458035684-compute@developer.gserviceaccount.com` (the existing VM SA) into the Doc's ordinary
   Share box as Viewer and the backend reads it via ADC — *"you can directly share individual files
   with the service account's email address using the standard UI."* The alternative, an OAuth app
   asking each biologist for `drive.readonly`, is a **restricted** scope requiring restricted-scope
   verification and **possibly a paid annual CASA security assessment**. For a solo maintainer this
   is the largest single operational saving in this document.
   ⚠️ The trap in the same doc: *"IAM roles configured in the Google Cloud Console don't grant access
   to Google Workspace assets"* — granting the SA a Drive IAM role does nothing. It must be shared
   with, by email. Domain-wide delegation is **not** needed.
   ⚠️ The April 2025 service-account change (new SAs get no Drive storage and cannot *own* My Drive
   items) is about **ownership, not access**; the consequence that matters — SAs can still be granted
   access to user-owned files — is verified by the 2026-09-03 guide. Primary announcement unreachable.

A happy accident: ✅ **Google Docs has no `onEdit`/`onChange` trigger** (those are Sheets-only; Docs
has only installable `onOpen` + time-driven). Auto-publish-on-save is impossible, so **the button *is*
an editorial gate** and half-written paragraphs cannot ship. Contrast Notion, which has no notion of
"publish" at all.

✅ **Google Workspace for Nonprofits is free and Brazil is explicitly eligible** (190+ countries,
verified through Goodstack): **$0/user/month up to 2,000 users**. Discounted upgrades: Business
Standard $3.50/user/mo annual, Business Plus $6.16.

**Do NOT rely on the export routes without reading this:**
- ✅ Drive export MIME types for Documents (2026-09-03): `.docx`, `.odt`, `.rtf`, `.pdf`,
  `text/plain`, **`text/html`**, **`application/zip`** (HTML + images as files),
  `application/epub+zip`, **`text/markdown`**. Markdown export shipped **2024-07-16**, personal
  accounts included. **Export is capped at 10 MB** and `Range` is unsupported; the `exportLinks`
  field on the File resource has no such cap.
- ✅ **The native Markdown export is NOT clean**: headings come out as `# **Heading**`, **images are
  inlined as base64 data URIs** (one photo turned 1,111 bytes into a **313 KB** file), lists run
  together, and **tab names become spurious top-level headings**.
- ✅ **The HTML export is the notorious soup**, measured on a live 3-paragraph doc: `class="c0".."c8"`
  generated per document with zero semantics, every text run wrapped in a `<span>` (8 spans for 3
  paragraphs), bold/italic only as `font-weight:700` in generated classes, the title a
  `<p class="title">` not an `<h1>`, **every link rewritten through
  `google.com/url?q=…&sa=D&source=editors&ust=…&usg=…`**, and **180 KB total of which ~99 % is
  JavaScript**. Headings *do* survive as real `<h1>` with stable `id="h.<slug>"` anchors under
  `div.doc-content`, so it is parseable — just ugly. (The Docs API's `textStyle.link.url` gives the
  raw URL, which is a real advantage of the API over the export.)
- ⭐ **The clean batch route is DOCX → `mammoth`** ✅ (python-mammoth **v1.13.0, 2026-09-26**, BSD-2,
  and it explicitly names Google Docs as a source). It is **semantic, not visual** —
  *"intentionally disregards … fonts, text size, and colors"* — maps styles to elements
  (`p[style-name='Heading 1'] => h1:fresh`), and its **`convert_image` handler** receives an object
  with `.open()` → bytes and `.content_type`, so a custom handler writes straight to the bucket,
  content-hashed. Alternative: **Pandoc 3.12** (2026-09-29, GPL-2.0-or-later) with
  `--extract-media`. ✅ There is **no maintained Python "Google Docs → Markdown" library** —
  `gdoc2md`, `gdocs2md`, `google-docs-to-markdown` are all absent from PyPI. Don't look for one.

**Three footguns that would silently corrupt content** (✅ all three):
1. **Always pass `includeTabsContent=true`** and walk `tabs[].documentTab.body`. Without it,
   *"the text fields in the Document Resource (e.g. `document.body`) will be populated with content
   from **the first tab only**"* and *"`document.tabs` will be empty and content from other tabs
   won't be returned."* With it, the legacy fields are *"left as empty"* — strictly either/or. The day
   a biologist adds a second tab, half the article vanishes with **no error**. Note `body`,
   `inlineObjects`, `positionedObjects`, `lists`, `namedStyles`, `headers`, `footers` and `footnotes`
   are all now documented as **"Legacy field."**
2. **Set `suggestionsViewMode` explicitly.** The default (`DEFAULT_FOR_CURRENT_ACCESS`) *"allows
   viewing the document with all suggestions inline"* — unaccepted review edits would leak into
   published articles. ⚠️ The other enum spellings did not render; verify before hardcoding.
3. **Teach exactly one rule: use the built-in heading styles.** `paragraph.paragraphStyle`
   `namedStyleType` (`TITLE`/`SUBTITLE`/`HEADING_1`…`6`/`NORMAL_TEXT`) plus `headingId`, which is
   *"empty [if] this paragraph is not a heading"* — so manually-bolded big text is invisible to the
   parser. If authors use Word, this becomes the **`.dotx` with exactly five styles** (Title,
   Heading 1, Body, Caption, Quote), **text boxes forbidden** (their contents and anchors are lost),
   and fifteen minutes of hand-holding. Neither mammoth nor Pandoc has a heuristic for
   bold-and-16pt-instead-of-Heading-1. **A training problem, not a tooling problem.**

**Other API shapes worth knowing** (✅): `textRun`s never cross paragraph boundaries but a paragraph
holds many, so **runs must be concatenated**; **lists have no container** — every item is a paragraph
carrying `bullet: {listId, nestingLevel, …}`, so group consecutive paragraphs by `listId` then look up
`document.lists[listId].listProperties.nestingLevels[nestingLevel]` for `<ol>` vs `<ul>`; tables
recurse (`tableRows[].tableCells[].content[]` is itself `StructuralElement[]`); indices are **UTF-16
code units**. **Video needs no rule** — a pasted YouTube or GCS URL becomes a smart chip arriving as
a first-class **`richLink`** with `{title, uri, mimeType?}` both *"always present"*.
✅ `imageProperties.contentUri` has a *"default lifetime of **30 minutes**"*, is pre-authorised (no
auth header needed), and *"anyone with the URI effectively accesses the image as the original
requester"*; `sourceUri` *"can be empty"* — and **is** empty for device-uploaded photos, i.e. exactly
the biologists'.

✅ **Offline is documented** (unlike Notion): Chrome or Edge, the Google Docs Offline extension, and
per-file *More → Available offline*. Safari and Firefox excluded; ⚠️ mobile apps not addressed.

⚠️ **"Publish to the web" (`/pub`)** was tested: HTTP 200, **CORS reflects the Origin** (contradicting
the common claim it is CORS-blocked), `cache-control: private, max-age=300`,
`x-robots-tag: noindex`, `?embedded=true` still ships 6 `<script>` tags and the same class soup and
link rewriting. Usable as a zero-auth fallback only; it is a rendered page, not a content API, and the
`/d/e/2PACX-…/pub` identifier is *different* from the file ID so it cannot be cross-referenced with
Drive.

⚠️ **Apps Script authorisation warning:** use the **`@OnlyCurrentDoc`** annotation, which *"force[s]
the authorization dialog to ask only for access to files in which the … script is used."* Whether
`documents.currentonly` + `script.external_request` avoid the "unverified app" screen entirely is
**not documented** — test once with a biologist's account; worst case they click through
*Advanced → Go to (unsafe)* one time, ever.

### Ⓑ Pages CMS — the structured alternative, and the only one clearing every bar

✅ Hosted free at **app.pagescms.org**, **MIT** throughout, v2.1.8 (2026-06-08).

- ✅ **Collaborators are invited by email and explicitly do not need a GitHub account** — the docs say
  to use them *"when someone needs to edit content or media but does not have a GitHub account"* — and
  sign-in is **email OTP** (v2.1.7 release note: *"Simplified collaborator invite sign-in with OTP
  verification"*). Repo owners sign in via a GitHub App.
- ✅ **Real WYSIWYG** rich-text field with a `switcher` to source mode, **output `markdown` (default)
  or `html`**, inline image upload via the `media` option (folder, extension whitelist,
  `rename: false/true/"safe"/"random"`), media manager with drag-and-drop and full-text search.
  Fields: string, text, code, rich-text, boolean, number, date, uuid, object, **block**, image, file,
  reference, select.
- ✅ **The permission split is exactly right:** collaborators edit content and media; they **cannot**
  manage config files, collaborators, or the cache. Granular permissions are "Coming Soon."
- ✅ **$0** — *"100 % free, whether you want to use the online version, deploy it for free on Vercel,
  or self-host it."* No paid tier exists.
- Optional self-host is Next.js + **PostgreSQL** — **do not put it on the e2-micro**; use the hosted
  app and spend zero RAM.

**Why it is genuinely competitive with Ⓐ:** typed fields *guide* the author, which is precisely what
§3.1 shows was missing. **Risks:** single maintainer, cadence slowed since June 2026, a free hosted
service with no SLA and no revenue model — all cushioned by MIT + a documented self-host path + the
content being plain Markdown in **our** repo. ⚠️ **UI localisation is undocumented; assume
English-only**, which is the one axis where Docs and Sveltia both beat it for a Brazilian author.

### The trade between Ⓐ and Ⓑ

| | Ⓐ Google Docs | Ⓑ Pages CMS |
|---|---|---|
| Adoption friction | **None** — the tool they already use, in Portuguese | One new (English) web app, email+OTP login |
| Structure | **None.** No schema; drift is guaranteed; we own a forgiving validator forever | **Typed fields**, so the author is guided and cannot omit a slot silently |
| Versioning | Docs revision history | **Git**, diffable and reviewable |
| Cost | $0 (likely institutionally free) | $0 |
| Risk | Google changes an export format | Single maintainer, no SLA |

**Start with Ⓐ**, because the backend converter and the app are identical either way — so switching
front doors later costs **no app release and no schema change**. That swappability (§2, §9.1) is the
reason this decision does not need to be got right first time.

### Ⓒ Escape hatches, in preference order

- **Prismic Starter, $10/mo** — ✅ **Slices** are the one mechanism surveyed where *the author's
  visual composition **is** the structured output*: we define a slice library (hero, photo+caption,
  video card, species callout), the author stacks and fills them, and the composition is the JSON
  array the app iterates. Free tier is **1 user** (fatal for two people), so the real number is
  **$10/mo ($120/yr)** for 3 users — by far the gentlest paid cliff in this document. Unlimited
  documents on every tier, 4M API calls/mo, imgix images. ⚠️ Slice *definitions* live in our
  codebase, so authors fill slices but cannot invent structures without a code change. ⚠️ Export
  gotcha: the Import-Export tool **only exports images in *published* documents** — archive via the
  API, not the export button.
- **Sanity free** — ✅ best free tier surveyed: **20 seats**, 10,000 docs, 1M CDN + 250k API
  requests/mo, **100 GB assets and 100 GB bandwidth**, unlimited locales, free Studio hosting, and
  **permanent CDN asset URLs** (no re-host step — the single biggest practical advantage over Notion).
  Reads need **no SDK**: `GET https://{projectId}.apicdn.sanity.io/v{YYYY-MM-DD}/data/query/{dataset}`
  is a plain `UnityWebRequest` + `JsonUtility`. Google/GitHub/email login on free. Growth is
  **$15/seat/mo**.
  ⚠️ **Two sharp edges that matter for a volunteer team:** free datasets are **public only** (anyone
  with the project ID reads everything, drafts included), and the free plan has **only Administrator
  and Viewer roles** — so every biologist who can write is a **full Administrator** able to delete
  content types and wipe datasets. Viewer is free but cannot edit. The fix is $15/seat/mo.
- **Decap CMS + Decap Turbo** — ✅ **Decap is actively maintained; the "it's stalled" claim is
  stale**: `main` commits through **2026-09-22**, npm **3.16.2/3.16.3**, `3.17.0-beta.0`, stewarded by
  PM TechHub with EU co-funding. **Turbo** (public preview **2026-09-14**) removes the GitHub-account
  wall: *"Editors log in with a Turbo account instead of a Git host account"*, git calls happen
  server-side, and *"every save is a real commit in your GitHub or GitLab repo."* **€0 for 1 site +
  1 seat; Pro €19/mo for 5 seats** (then €10/site, €6/seat; adds an S3-compatible media library).
  ⚠️ Two weeks old, public preview, no SLA, small agency — but the downside is losing a UI, not data.
  Its `richtext` widget is **beta** and outputs a raw Markdown string; admin UI has a generic **`pt`**
  locale.
- **Sveltia CMS** — ✅ the best free git-backed *editor*, **MIT**, v0.224.0 shipped 2026-09-29 (multiple
  releases per day is normal), **"feature complete" announced 2026-09-10**, v1.0 targeted "late 2026",
  335+ Decap issues resolved, and the **only option surveyed shipping a dedicated `pt-BR` admin UI**
  (plus mobile-browser support). ⚠️ **Bus factor 1** (Kohei Yoshino, solo, no corporate backer), still
  0.x, and the no-GitHub-account path is a **third-party** Cloudflare Worker
  (`hollesse/sveltia-cms-cloudflare-access-auth`, MIT, 2026-09-10) where Cloudflare Access does email
  OTP and a **shared bot account** commits — **losing per-author history**. GitHub also needs an OAuth
  proxy (use the official `sveltia/sveltia-cms-auth` Worker, MIT, pushed 2026-09-21) because GitHub
  has no client-side PKCE yet.
- **CloudCannon, $55/mo, no free tier** — ✅ the **best non-technical-author experience of anything
  surveyed**: "Client Sharing" gives an outside editor a site-specific password at
  `yoursite.com/update` — no CloudCannon account, no GitHub account, no Google login. True
  drag-and-drop visual editing with live preview, on a plain repo of Markdown+frontmatter, no
  lock-in. **Disqualified on budget only. Buy this if grant funding appears.**

---

## 8. Everything ruled out, with the reason

Keep this section: the reasons are what stop an option being re-proposed in six months.

### 8.1 Ruled out on RAM — they will not run on the 1 GB e2-micro

The VM already runs one uvicorn worker plus `cloudflared`. ✅ Documented minimums:

| | Minimum | Recommended | Note |
|---|---|---|---|
| **Strapi** | **2 GB** | 4 GB+ | The **admin-panel build alone** wants ~2 GB, so it fails before a login screen. Node v22/v24/v26 only. ⚠️ *"Strapi does not support MongoDB (or any NoSQL), nor any 'Cloud Native' databases (e.g. Amazon Aurora, Google Cloud SQL)"* — rules out Cloud SQL. **No free Cloud tier**; Starter $35/mo. |
| **Payload v3** | **2 GB** | 4 GB+ | Next.js plugin with a server-rendered admin; OOM-on-build is a common complaint. ⚠️ Cloud appears **discontinued for new projects** after the 2025 Figma acquisition (pricing pages 404). No GUI schema builder — every model change is code + redeploy. |
| **Baserow** | **2 GB** | 4 GB+ | Multi-service stack in one image (backend, web frontend, Celery, Caddy, its own Postgres + Redis). |
| **NocoDB** | 2 GB official | 8 GB prod | Architecturally lighter; ⚠️ ~1 GB reports are anecdotal. Licence changed 2026-01-09: AGPL-3.0 → **"Sustainable Use License"**, fair-code, **not OSI** (free self-host and internal commercial use retained; reselling as a hosted service prohibited). |
| **Ghost** | *"at least 1 GB"* alone | 2 GB practical | MySQL 8 eats several hundred MB first. SQLite mode ~512 MB **as sole tenant**, which is not our case. |
| **Directus** | **512 MB** | 1 vCPU / 2 GB | ✅ The **only** Family-2 self-host whose documented minimum fits — but Docker + Node + a DB on the remainder is genuinely tight, and one image upload from the OOM killer taking down the production API. |

⚠️ **Directus's licence is no longer BSL 1.1.** It is now the **Monospace Sustainable Core License
1.0 (MSCL-1.0-GPL)**: free for *"internal organizational use"*, non-commercial education and research;
prohibits *"Competing Use"* **and** *"mov[ing], chang[ing], disabl[ing] or circumvent[ing] the license
key functionality"* — **there is license-key machinery in the product now**. Converts to **GPL-3.0 on
the fourth anniversary** of each release. There is **no revenue threshold in the licence**; the $5M
figure lives in the **Open Innovation Grant** on the pricing page (free self-hosted for orgs under
**$5M revenue and 50 employees** — which this project clears easily; Cloud $99/mo under the grant).
Running it for our own content is covered, but it is **not OSI open source any more**.

### 8.2 Ruled out on price cliffs

✅ First paid tier, where the free tier is real but the escape is not affordable:

| | Free tier | First paid tier |
|---|---|---|
| **Contentful** | ⚠️ conflicting: 10 users / 25 content types / **10,000 records** / 100k calls, *or* 2 users / 25,000 records | ⚠️ **$300/mo ("Lite")** *or* **$489/mo ("Team")** — sources disagree; Vendr median actual spend **$54,173/yr** |
| **Contentstack** | 1 stack, 3 users, 100k calls/mo | **$299/mo** |
| **Hygraph** | **3 seats** (best free seat count), 1,000 entries, 500k ops, **unlimited asset storage** | ⚠️ **$99/mo** annual (one source claimed $399) — and **GraphQL-only** with an editor reviewers call *"functional but complex"*: wrong shape for biologists |
| **Storyblok** | **1 seat** (+$15/mo for a 2nd without leaving free), 20,000 stories, 100k calls, **no overage possible** | **$99/mo** ($90.75 annual, 5 seats). ✅ **Best visual editor surveyed** — Bridge live preview, click the rendered block, drag-drop reorder. Free forever if there is exactly one author. |
| **DatoCMS** | **300 records**, 2 editors, **200 MB file storage** (≈40–60 article photos) | **€149/mo** annual / €199 monthly. EUR-only officially |
| **Directus Cloud** | Core $0: 3 seats, 25 collections | **$499/mo** annual / $599 monthly — the worst cliff in this document |
| **TinaCMS** | **2 users** | Team **$24/mo per project**; ⚠️ its **API is Business-only ($249/mo)**; self-host needs your own GraphQL backend + **Redis or MongoDB** + your own auth — the heaviest self-host story in the git-backed family |
| **Ghost(Pro)** | — | ⚠️ Starter **$18/mo** (has moved $9→$15→$18) — and ✅ **no custom content types or custom fields, still, in 2026.** The Koenig editor is the best *writing* experience surveyed; the data model is hard-coded to Posts/Pages/Tags/Authors/Tiers. Wrong fit against our typed `beaches_content.json`. |

✅ **Out of the conversation entirely:** **Ceros** — Vendr median **$45,000/yr** (84 purchases);
**Foleon** — median **$21,000/yr**, and its API is **Enterprise-only**; **Turtl** — contact-sales, no
API mentioned.

### 8.3 Ruled out on architecture

- ⚠️→✅ **Shorthand — the trap on this list.** Purpose-built for "an NGO publishes an illustrated
  story", nonprofit discounts exist, free for universities on application, **and no published price
  anywhere** (*"Free to create. Pay when you publish"*; Vendr has no entry). But: **there is no
  content API** — the publishing API *publishes a built bundle*, so we can never get structured fields
  out. The embed is **not an iframe** (it renders into the host page), **only one story per page**, and
  **background media stops sticking if any parent has `overflow: hidden`** — i.e. inside every scroll
  container we own. We would ship a heavy scroll-effect WebView fighting our own AR and pinch
  gestures, with zero data. **Don't make the sales call.**
- ✅ **Framer — dead end.** *"Plugins can read and write to the Framer CMS"* — CMS access exists **only
  from plugins executing inside the Framer editor**. The "Server API" automates *publishing*; "Fetch"
  lets a Framer site consume *external* APIs. **No public read endpoint.**
- ✅ **Softr** is a front end, not a source — its API is a *management* API. If content is in Airtable,
  read Airtable and delete Softr; and Airtable is not an illustrated-article editor either.
- ✅ **Carrd** — one-page site builder, **no API, no CMS**, Pro $19/**year**.
- ✅ **Readymag** — best discount found (**50 % off for registered NPOs**, so Advanced ≈ $29.25/mo) spent
  on a **canvas-positioned static bundle with no content API**. Right discount, wrong axis.
- ✅ **Publii** — stores content as **HTML in a local SQLite database** and renders whole websites; its
  "Git Repository Sync" syncs *Publii's own site data between the author's machines*, not per-article
  Markdown. Wrong output shape, and no remote multi-author collaboration.
- ✅ **Front Matter CMS** — very active (v10.12.0, 2026-08-21) and **requires local VS Code**. Multiple
  sources say non-developer content creators are a scenario to avoid with it.
- ✅ **Static CMS — dead.** Repo `archived: true` on **2024-09-09**, last release v4.3.0 (2024-04-26).
- ✅ **New Git Gateway configurations.** Netlify **Identity's** deprecation was **reversed 2026-02-19**
  (*"Netlify Identity will continue as a supported authentication option"*) — but **Git Gateway was not
  part of that reversal**: *"Git Gateway is deprecated. … While we will keep fixing any major security
  issues that arise, we will no longer fix bugs in the functionality of Git Gateway."* Still Beta, no
  sunset date. It is the only no-GitHub-account Decap path needing nothing extra from us, and it is
  bug-frozen by its vendor. Turbo exists because of this.
- ✅ **Notion.** Right adoption story — plausibly the lingua franca of Brazilian NGO teams, pt-BR fully
  supported — and the worst API surveyed, for four independent reasons:
  1. **Images expire in 1 hour**: *"Each time you fetch a Notion-hosted file, it includes a temporary
     public url valid for 1 hour"* and *"Don't cache or statically reference these URLs."* So every
     photo **must** be mirrored server-side. **The belief that 2025's File Upload API fixed this is a
     misreading** — `FileUpload.expiry_time` is the *upload-session* deadline, not the download URL; no
     changelog or doc extends the 1-hour download expiry. Only `external` files *"never expire"*.
  2. ⚠️ **Offline is entirely undocumented.** Fourteen plausible help slugs all **404**, and offline is
     absent from the desktop page, the mobile page, system requirements and the release notes. The
     failure mode that matters — 2,000 words written on flaky 3G that silently never sync — is exactly
     what is unaddressed. **Disqualifying on its own for Noronha.**
  3. **The free block cliff:** *"Free workspaces with more than one member have a limit of 1,000
     lifetime blocks"* and *"Deleting blocks does not restore capacity."* An illustrated article is
     100–300 blocks → **~4–8 articles and the workspace is permanently bricked** (403
     `restricted_resource`). *"Guests do not count as members"*, so the workable shape is one sole
     owner + biologists as guests (≤10) — but then Free's **5 MiB per-file cap** rejects ordinary phone
     photos (3–8 MB), so one Plus seat (~$8/mo) is needed anyway. ⚠️ And **whether the API works on
     Free at all is contradictory in Notion's own materials** (pricing table lists Public API under
     Plus+; the dev docs describe Free-workspace API behaviour in detail).
  4. **Recursive fetches**: *"Returns only the first level of children"* — one article with columns,
     toggles and nested lists is **dozens of round-trips**, at **180 req/min (avg 3/s)** on non-Business
     plans (600/min on Business/Enterprise), plus a per-workspace shared budget whose `Retry-After`
     *"can be longer than a minute."*
  Also: `column_list`/`column` are content-free wrappers that triple recursion **and** ✅ *"There are
  no columns on mobile. Any column structure that you created on desktop will be collapsed"* — so the
  layout an author builds is not even what a phone shows them. There is **no notion of "publish"**:
  every keystroke is live. Free page history is **7 days**. ⚠️ No maintained C#/.NET SDK exists (a
  non-issue, since Unity must never call it).
  ✅ Its **ToS explicitly permits** serving API content to end users (*"copy and use any data and
  materials from the Service that are accessed via the API via your Integration in order to make the
  functionality of the Integration available to third parties"*), prohibits **scraping / the private
  API** (so no `react-notion-x`, no `notion-py` cookie route), and prohibits **training AI/ML on
  Integration Data** (our ONNX camera inference is unrelated and unaffected).
  ✅ **Notion for Nonprofits is 50 % off Business only** → $10/seat/mo, i.e. Plus's price with
  Business's rate limit. Clever, and it fixes none of 1–4.
  ✅ Maintained libraries, if ever needed: `@notionhq/client` **5.27.0 (2026-09-29)**, Python
  `notion-client` **3.1.0**, ⭐ **`ultimate-notion` 0.10.1 (2026-06-28)** (the maintained Python
  converter — but it does **not** re-host images and shifts heading levels). **Dead:** `notion-to-md`
  v4 abandoned in alpha since 2025-07-20, `@tryfabric/martian` (published artefact 2022-05-24),
  `notion2md`, `md2notion`, `notional`, `notion-py`.

### 8.4 Ruled out as an authoring surface for prose: Google Sheets and the flipbooks

**Sheets cannot carry images at all** — this is firmer than "awkward". ✅ `CellData` is
`{userEnteredValue, effectiveValue, formattedValue, userEnteredFormat, effectiveFormat, hyperlink,
note, textFormatRuns, dataValidation, pivotTable, dataSourceTable, dataSourceFormula, chipRuns}` —
**no image field; the word "image" appears zero times on that page.** An `=IMAGE()` formula is
readable only via `valueRenderOption=FORMULA` + regex (and requires the author to *already* have a
hosted URL — exactly the work we were sparing them); an image inserted **in** a cell is not represented
in `CellData`, and ✅ the Apps Script path is a dead end too (`CellImage`/`OverGridImage`:
`getUrl()` is **deprecated** with *"for most newly inserted images, the source URL is unavailable
regardless how the image is inserted"*, and `getContentUrl()` is *"tagged to the requester's
account"*). Add: CSV and gviz drop **all** rich formatting; long prose in a cell is miserable to write;
row order *is* article order so an accidental sort silently reflows it; and there is no preview.

**Flipbooks — the worst option surveyed.** ✅ Publuu: **no free tier** (14-day trial), $7/$24/$49/$89
per month, no API listed. Issuu: Basic free = 5 documents / 10 pages / 50 MB; Starter **$21/mo**;
Unlimited **$188/mo**; Teams from **$417/mo**; ⚠️ its `api.issuu.com/v2` endpoints and plan
requirement unverified. A flipbook is **page rasters in a page-flip viewer**: at 390 dp an A4 page
needs pinch-zoom-and-pan for every paragraph, the text is an image (no reflow, no selection, no screen
reader, no font scaling — for a Portuguese conservation NGO that means a low-vision or older user
**cannot read it at all**), 10–30 MB downloaded before first paint on island data, and the developer
gets an iframe URL. If someone asks for a "magazine feel", that is a **visual-design** request to
satisfy in our own UI, not a format request.

### 8.5 Sheets IS right for the flat reference data

`beaches_content.json` is a flat table of short fields for 17 rows, with vocabularies the file already
declares. A sheet with a **locked `name` column**, **Data Validation dropdowns** on
`riskLevel`/`idealTide` (so a token cannot arrive as four spellings of "médio"), an explicit **`order`**
column and a **`status`** column the backend filters on makes the invalid-token case *impossible*
rather than merely logged — and [docs/beaches-content-todo.md](beaches-content-todo.md) becomes the
sheet itself.

Mechanics (✅ unless noted):
- **Prefer the Sheets API v4 with the existing service account** (or a restricted API key — now
  doc-backed: *"This authentication method is used to anonymously access publicly available data, such
  as Google Workspace files shared using the 'Anyone on the Internet with this link' sharing
  setting."* Read-only; keys cannot write). Quotas are **10× tighter than Docs**: 300 reads/min/project,
  **60/min/user/project**.
- **Even better: an Apps Script web app deployed "Execute as me"** — then *"the script always executes
  as you, the owner"*, so **the sheet needs no sharing change at all**. `/exec` **302-redirects to
  `script.googleusercontent.com`** (follow redirects), cold starts 1–3 s. ⚠️ The "Who has access:
  Anyone" anonymous wording is on neither official page; check the editor UI.
- **`gviz` / `export?format=csv` work on a merely link-shared sheet without publish-to-web** (verified
  on two sheets; `/pub?output=csv` and `/pubhtml` return **401**, v4 without credentials **403**, CORS
  reflects the Origin). But **don't build on them**: Google Charts' last release was **April 2023**
  ("Frozen Charts Version 52"), the Query Language reference is last-updated **2024-07-10**, **Google
  archived the public issue tracker on 2026-04-18**, and the support page still claims *"new releases
  every few months"* — now false. *It will keep working; nobody is home.*
- ⚠️ **The one real risk:** a **March 2025 Google security change affected *newly created* sheets'**
  third-party connections (pre-existing sheets kept working; a vendor fix shipped 2025-04-29). **Both
  probes used Google's own long-lived sample sheets, so nothing verified a sheet created in 2026.**
  See §11.2. ("Execute as me" Apps Script makes this moot.)
- Footguns: **ragged rows** — *"Empty trailing rows and columns are omitted"*, and **gviz `out:csv`
  pads to the full column count while `values.get` does not**, so gviz CSV is the better target for
  dumb uniform parsing; `tqx=out:tsv-excel` **silently returns JSON** (only `json`/`csv`/`html` are
  real); the **`/*O_o*/`** prefix is an undocumented anti-JSON-hijacking guard — **strip defensively by
  finding the first `(`**, never pattern-match the literal; **gviz error responses omit `reqId`** (issue
  2371, filed 2016, never fixed, repo now archived) so always check `status` in the parsed payload;
  `out:html` applies **gviz's own** styling, not the sheet's; cells are `{"v":…, "f":…}` where `f` is
  number/date formatting, **not** rich text.
- ⚠️ **Sheets API overage is planned to *"incur charges to your Google Cloud billing account later in
  2026"*** (same note on Docs and Drive). Our billing account is open, so overage would **silently
  bill rather than hard-fail. Cache aggressively.**
- Rich text *is* recoverable via `spreadsheets.get(includeGridData=true)` → **`textFormatRuns`** +
  `hyperlink`, by walking offsets — but it yields no headings, lists, blockquotes or captions.
  **Telling authors to type `**negrito**` is simpler and more predictable.**
- **Don't buy a Sheets-to-API proxy.** ✅ SheetDB free = **500 req/mo**, then $29.99/mo; Sheety free =
  **200 req/mo AND 100 rows**; Sheet Best has **no free tier**; NocoDB API free = 300 req/mo and
  *"your API endpoints will go on sleep mode if you are a free user"*; Sheetson's own page contradicts
  itself on row limits. And **Stein is the cautionary tale**: site returns 200, ⚠️ **repo has 28
  commits, last one 2020-03-02 — six years** — with no sunset notice. We already have a backend.

### 8.6 If content ever lives in Firestore instead

- ✅ **Retool** is the answer: first-party Firebase integration, free tier *"Unlimited web & mobile
  apps, 500 workflow runs/month, Up to 5 users"*, a few hours to build a table + form, and **if Retool
  vanishes the data is untouched**. ⚠️ Paid pricing unread.
- ⚠️ **Rowy** is the *purpose-built* tool (spreadsheet UI directly over our own Firestore, data never
  leaves our GCP project, generous free tier, Pro $12/seat/mo, open source) — **but its repo's last
  commit is 2024-11-24** and the newest releases are from 2023, one titled *"…+ Introducing
  BuildShip"*. The team moved on. Self-host the OSS build only, eyes open.
- ⚠️ **Baserow / NocoDB / Directus are NOT Firestore admin UIs** — each ships its **own** SQL database
  and would become a second source of truth to sync. Flagged because they dominate searches for this
  question.
- The **Firebase console** is a document-tree editor with no table view, no validation and no bulk
  edit; **Firefoo** is a developer tool. Neither is an authoring surface.

---

## 9. Free wins available regardless of the decision

### 9.1 Never let the app talk to a CMS
Ingest server-side into FastAPI + Firestore + GCS; Unity reads only our own endpoints. This buys
immunity to a third party's uptime, immunity to asset-URL expiry policy (the thing that kills the
Notion path outright), **a CMS we can swap without an app release**, and it makes every free-tier API
quota a property of our ingest job rather than of every user's phone.

### 9.2 Move the media bucket to Cloudflare R2
✅ GCS free tier is 5 GB storage + **1 GB/month egress**, then **$0.12/GB**. R2 is **10 GB storage and
$0 egress at any volume** (paid storage $0.015/GB-mo), we already own the Cloudflare account and
`mergulhovirtual.dev`, and ✅ **Cloudflare removed ToS §2.8 and explicitly permits serving video and
images hosted on Cloudflare services including R2**. Against `estimated_cost.md`'s ~100 items / ~10 GB
plan, this is the difference between $0 and a metered bill.

⚠️ **Do not confuse this with Cloudflare's free CDN**, whose service-specific terms *do* let Cloudflare
disable CDN access for serving *"video or a disproportionate percentage of pictures, audio files, or
other large files"* without a paid service (applies to Free, Pro and Business). Different product.
**R2 is fine; proxying a non-Cloudflare media origin through the free CDN is not.**

### 9.3 Host the DHN tide-table PDF and close the dead link
The one legitimate PDF. Public object in the bucket + `Application.OpenURL` on an `https://` URL makes
Início's **"Baixar a tábua de maré do mês"** work (`AppUiHost.OnTideTableRequested` is currently a
logging no-op).

### 9.4 Free head start on species content — the iNaturalist API
✅ Keyless, verified by live probe: `GET https://api.inaturalist.org/v1/taxa/56766?locale=pt-BR`
(*Sphyrna mokarran*) returns `preferred_common_name` = **"Tubarão-martelo-gigante"**, a **638-character
pt-BR `wikipedia_summary`** containing `<b>`/`<i>` — **exactly the tags UI Toolkit supports** —
`wikipedia_url`, `conservation_status` **"CR" (IUCN Red List)**, and 8 `taxon_photos` each with
`license_code` and a ready-made `attribution` string. Search results also carry `default_photo`,
`observations_count`, `ancestry`, `rank`. This fills `AnimalDef.binomial`/`description`/`photoCredit`
the way `tools/populate_animals.py` already does for Wikipedia.
⚠️ **Filter on `license_code`** — many photos are CC-BY-NC or all-rights-reserved, the same trap as the
two CC BY-NC 3D models already flagged in the handover.

### 9.5 Free build machinery
✅ **GitHub Actions does not consume minutes at all on a public repo** with standard runners; a private
repo on the Free plan gets **2,000 minutes/month** and 500 MB of artifact storage, against a content
build measured in seconds. **Host the editor (or an OAuth proxy) on Cloudflare** — Workers/Pages free
is 100,000 requests/day, 128 MB, with **no non-commercial restriction**. ⚠️ **Vercel Hobby is
"non-commercial personal use only"** — *"All commercial usage … requires either a Pro or Enterprise
plan"*, though *"Asking for Donations does not fall under commercial usage."* Cloudflare avoids the
question.

### 9.6 The cheapest content wins already sitting there
`lemon_shark`'s duplicate video URL (one line of YAML), the `reef_shark` identity mismatch
(`Tubarão-bico-fino` / `Carcharhinus acronotus` against a "Caribbean Reef Shark" credit and `CRS`-
prefixed assets — someone who knows the species must say which it is), the 15 blank `AnimalDef` spec
strings, and the 132 `_todo` beach fields.

---

## 10. Footguns for whoever implements this

- ⚠️ **Git LFS × the GitHub Contents API.** A browser-based CMS commits through the REST Contents API,
  which may write **raw binaries instead of LFS pointers** — silently defeating LFS, which this repo
  already uses for ONNX models, native binaries and the TextCore font assets. GitHub's docs are
  **silent** on the interaction. **Test with one image before onboarding anyone, and keep the content
  repo separate from the Unity repo** so LFS rules cannot collide.
- ✅ **Contents API size limits:** files **≤1 MB** fully supported; **1–100 MB** only via raw/object
  media types (the `content` field comes back empty); **>100 MB not supported at all.** Article photos
  at 1–3 MB are fine; **videos must stay in the bucket as URLs**, matching the existing
  `AnimalDef.videos` / `VideoRef` pattern.
- ✅ **Adding a screen has two silent-failure traps.** Its `.uss` must be listed in
  `AppUiBuilder.ScreenStylePaths` followed by `make ui-setup`, or there is **no error, no warning, just
  a completely unstyled screen**; and `TokenDisciplineTests` scans `Assets/UI/**/*.uss` as **raw
  text**, so a `#RRGGBB` literal **inside a comment** fails the build. Write hex in USS comments as
  `0xRRGGBB` (the house convention).
- ✅ **The linear-colour-space rule applies to every alpha transcribed from a design.** See CLAUDE.md —
  a light overlay on a dark backdrop needs its alpha cut ~3.5×, a dark overlay on a light backdrop
  needs it **raised** ~1.7×, and no single alpha reproduces Figma exactly because per-channel
  corrections diverge.
- ✅ **A green test suite cannot see layout.** Every design-system test is a *structure* test. After any
  change to flex direction, typography, padding or wrapping, **render the screens and look at them**:
  `make ds-shots` is the check, not `make ds-test`.
- ✅ **Screens are constructed once and kept alive** — the router toggles `display` — so per-visit work
  belongs in `OnEnter`/`OnExit` (including `scroll.scrollOffset = Vector2.zero`). Copy `EspecieScreen`'s
  payload-carrying push: the host sets the ViewModel **before** `router.Push(...)`, and an unresolved
  key **does not navigate at all**.
- ✅ **`.mv-*__body` needs `flex-shrink: 0`**, or a short viewport compresses cards instead of
  scrolling. And `AppScrollView` (35 lines) exists because scroller visibility **is not a USS property**
  — `ScrollView` writes it as an inline style every layout pass.
- ✅ **Nothing in the app renders a second paragraph today.** There is no `\n` in any string in
  `Assets/UI/`; every body-copy block is one `Label` holding one paragraph. Paragraph rhythm is
  `margin-bottom` on siblings (`> * { margin-bottom: 24px }`), since USS has no `gap` and no
  `line-height` — though see §5 on `-unity-paragraph-spacing` and the `<line-height>` tag.
- ✅ **Label metrics are reset app-wide** (`.unity-label { padding: 0; margin: 0 }`) because Unity's
  default theme adds 14 dp of invisible vertical and 9 dp of horizontal space. Any new stack of labels
  must author its own spacing and must not lean on the default metrics.
- ✅ **Video has no poster concept.** `EspecieScreen`'s card shows a grey well + glyph until the first
  decoded frame; nothing downloads a still. If articles want thumbnails, that is new work. And
  ⚠️ **playback cannot be verified on Linux** — Unity's Linux `VideoPlayer` cannot decode H.264, so
  `Failed` is the only reachable state in this editor. Android build required.
- ✅ **`make ds-shots` must NOT pass `-nographics`** (it needs a real graphics device) and it **churns
  the LFS-tracked font assets** whenever it rasterises a new glyph. Expect them dirty; they are
  regenerated output, not edits.

---

## 11. Open tests — each a few minutes, each changes a real decision

1. ⚠️ **`Markdig.dll.meta` has `Editor: enabled: 0`** — does the bundled parser work in Play mode and
   `make ds-shots`, or only in a device build? **Only matters if we choose on-device parsing**; §6.1
   removes it from the critical path.
2. ⚠️ **Does a sheet *created in 2026*, link-shared, still serve `gviz` and `/export?format=csv`?**
   Create one → "Anyone with the link → Viewer" → `curl` both. Moot if we use "Execute as me" Apps
   Script. (§8.5)
3. ⚠️ **Does Drive's non-zip `text/html` export inline base64 or emit `googleusercontent` URLs?** One
   `curl` on a real article with a photo. Reports conflict; the researcher had no doc with images.
4. ⚠️ **Does the Apps Script authorisation dialog show the "unverified app" warning** for
   `documents.currentonly` + `script.external_request` with `@OnlyCurrentDoc`? Test on a biologist's
   own account.
5. ⚠️ **Do `<link>` pointer events survive the Advanced Text Generator?** Smoke-test with
   `-unity-text-generator: advanced`. Only needed for tappable in-paragraph cross-references, and only
   before Unity 6.5.
6. ⚠️ **Does a browser CMS commit break Git LFS?** (§10, first bullet.) Test with one image before
   onboarding authors.
7. ⚠️ If Notion is ever reconsidered: **is the API available on the Free plan**, and **does offline
   actually work?** A throwaway free workspace settles the first in ten minutes; the second needs two
   deliberate flaky-network trials.

---

## 12. Unverified — do not cite these onward

Consolidated from all seven passes. The research sessions exhausted their web-search budgets, so
several peripheral items rest on a single fetch or on inference.

- **Any PDF renderer for Unity** — Paroxe, PDFium/Docnet under IL2CPP, MuPDF bindings, `androidx.pdf`,
  iOS PDFKit via Unity, and any Unity plugin for Android `FileProvider` or iOS QuickLook. **Nothing
  verified. Treat "there is an asset for it" as unproven.**
- **WebView specifics** — gree's last-commit date; Vuplex's iOS price, bundle price and explicit Unity
  6 statement; UniWebView's latest release date and Unity 6 statement; `file://`-from-
  `persistentDataPath` support and build-size deltas for all three; app-store review implications of a
  remote-content WebView.
- **Contentful's free-tier limits and first paid price** — `contentful.com` returned HTTP 429 /
  "Vercel Security Checkpoint" on every attempt; archive.org and three review sites were unreachable.
  Two third-party sources disagree on tier name, free user count and price.
- **Hygraph Professional price** — `hygraph.com/pricing` timed out three times at 300 s. $99/mo annual
  corroborated by two secondary sources; one claimed $399.
- **Keystatic Cloud's "no GitHub account needed" claim** — the docs state it; the mechanism and whether
  per-user repo write access is still required are undocumented. Self-hosted GitHub OAuth definitely
  needs a GitHub account. (Otherwise Keystatic is healthy: MIT, last push 2026-09-28, `@keystatic/core`
  v0.6.9, and **v0.6.5 deliberately fixed pt-BR translations**. Needs a Node runtime — Cloudflare Pages
  free works; **Vercel Hobby is non-commercial**.)
- **Payload Cloud discontinued** — inferred from 404s plus a Figma-acquisition notice.
- **TinaCMS's exact licence** — open-source core, but the repo `LICENSE` was not read.
- **Canva for Nonprofits** — existence in 2026, Brazilian *associação*/OSCIP eligibility, and whether
  it includes pro-quality exports. canva.com returned 403 to four attempts including with a browser UA.
- **Shorthand's price** — apparently deliberately unpublished; Vendr has no entry.
- **Field-guide precedents** — Merlin Bird ID's bird packs, Seek's offline claims, eBird's offline
  queueing, Audubon, the NPS app's offline downloads, Bloomberg Connects' curator CMS: **all blocked by
  403/404.** ✅ **Only iNaturalist was verified**, from its own repo: `realm` 20.2.0 + `@realm/react`,
  `react-native-mmkv` 3.3.3, `@tanstack/react-query` — i.e. **a local database as the UI's source of
  truth**, a network cache above it, and a large on-device model + taxonomy **bundled out-of-band from
  source control**. That is the same architecture §6 recommends, arrived at independently.
- **Notion** — free-plan API availability, offline behaviour, BRL pricing, pt-BR localisation date, and
  the MSA / Personal Use ToS that §1 of the Developer Terms incorporates by reference.
- **Google** — Docs API `contentUri` expiry wording beyond the Java model mirror, 2026 quality of the
  non-zip HTML export, Drive/Docs/Sheets quota specifics, `SuggestionsViewMode` enum spellings, and
  whether a bare API key reads a link-shared **sheet** in practice (doc-backed but not executed).
- **Sveltia v0.224.0's date** — GitHub omitted the year; inferred from "29 Sep" against today.
- **Pages CMS UI localisation** — no docs found; assume English-only.
- **Ghost(Pro) Starter $18/mo** — the price has moved $9 → $15 → $18; confirm at decision time.
- **DatoCMS in USD** — EUR-only officially; any USD figure is an FX conversion.
- **Third-party Sheets proxies** — Sheety/Stein/SheetBest/Sheetson/NoCodeAPI liveness verified, several
  prices not.
- **Google Charts "maintenance mode"** — no explicit vendor statement exists; the case in §8.5 is
  circumstantial but conclusive.

---

## 13. Where this leaves it

> **Implemented 2026-09-30 — the rendering half and a Markdown front door; not the download pack, not R2, not Google Docs.**
>
> **Built:** the two **native UI Toolkit screens** (index + reader, `AppRoutes.Conteudos` / `Conteudo`, sub-screens reached from a card on Início) **over typed block JSON parsed at build time** — `tools/build_articles.py` compiles `content/articles/*.md` into `Assets/Resources/articles.json`, so **no Markdown parser ships in the APK** and malformed authoring fails on a developer's machine with a file name and a line number (§6.1's whole argument). Transport is the **bundled** half only: text and images ride in `Resources`, and a video block hands its URL straight to the player from the existing public GCS bucket `conteudos-educacionais`. The **authoring front door is Markdown files in this repo**, not a CMS — see [articles-authoring.md](articles-authoring.md), and the **Educational article library** section of [CLAUDE.md](../CLAUDE.md) for the pipeline, the escaping rule and the footguns.
>
> **Not built:** the **versioned update pack over `FileDownloadJob`** — nothing downloads an article, so **every content change still needs an app release**; **Cloudflare R2** (media is still the GCS bucket, §9.2 unchanged); and the **Google Docs + Apps Script "Publicar" button** with its service-account read path (§7 Ⓐ), along with the Google Sheets beach table (§8.5). §9.1 is what keeps those cheap to add later: the app never talks to a CMS, so the front door is swappable without an app release, and the block JSON the screens read does not change when it is swapped.
>
> ⚠️ **The four shipped articles are placeholder content written by an implementer, not a biologist** — fixtures so every block type has a case, carrying no statistics, no dates, no identification claims and no safety or first-aid instructions. Note this cuts *against* §3.1's "a blank beats an invention": prose signed by nobody is exactly the invention that principle warns about, which is why each of the four now opens with an in-app `error` callout saying so, and why the corpus must be replaced with reviewed text — or deleted — before release.

**Recommended shape, unchanged across all seven research passes and now fully specified:**

> **Native UI Toolkit article screens (index + article) over typed block JSON**, parsed at **build
> time** so no parser ships in the APK · **text and essential images bundled in `Resources`, updates
> downloaded as a versioned pack via `FileDownloadJob`**, videos streamed from the public bucket ·
> **hosted on Cloudflare R2** for free egress · **authored in Google Docs with an Apps Script
> "Publicar" button and a service-account read path**, with **Google Sheets for the flat beach table**
> · and **Pages CMS, Prismic or Sanity as a known escape hatch the app would never notice.**

Two premises that make it safe to start before every question is closed: the app never talks to a CMS
(§9.1), so the front door is swappable without an app release; and **a blank beats an invention**
(§3.1), so the screens can ship with whatever content exists and fill in over time — which is already
how this project sold the beach screens to the client.

**Related reading in-repo:** [docs/mergulho_virtual_arquitetura.md](mergulho_virtual_arquitetura.md)
§1.1.1, §1.1.4, §2.4 (the client's own spec for this module) ·
[docs/beaches-content-todo.md](beaches-content-todo.md) (the authoring guide whose fill rate is the
evidence in §3.1) · [docs/handover-v2-redesign.md](handover-v2-redesign.md) §5, §6, §9 ·
[DESIGN_IMPLEMENTATION.md](../DESIGN_IMPLEMENTATION.md) §10 decisions **D3** (SOS/first-aid, still
open and the nearest thing to educational content in scope) and **D8** (who authors content).
