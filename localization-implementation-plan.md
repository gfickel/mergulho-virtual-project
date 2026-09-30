# localization-implementation-plan.md

Implementation plan for **multi-language support** in the Mergulho Virtual Unity app.

**Written for:** the maintainer picking this up cold, or a Claude Code session with no
context from the conversation that produced it. It assumes [CLAUDE.md](CLAUDE.md) and does
not repeat it; it *does* carry the evidence for each decision, because the decisions below
were argued once and should not be re-argued from half the picture.

**Status: nothing implemented.** This is a plan only. Every count in it was measured
against the working copy on 2026-09-30; the method is stated wherever it matters, so a
later reader can re-measure rather than trust.

---

## 1. Settled decisions

### D1 — Hand-rolled locale catalog, **not** `com.unity.localization`

Three reasons, in order of weight:

1. **The assembly firewall.** `MergulhoVirtual.UI.asmdef` lists exactly one reference —
   `MergulhoVirtual.DesignSystem` — and `MergulhoVirtual.DesignSystem.asmdef` lists
   `"references": []` (verified by reading both files). ~90 % of the strings live inside
   `MergulhoVirtual.UI`, under **467 EditMode tests** (`grep -c "\[Test\]"` across
   [Assets/UI/Tests/Editor/](src/app/MergulhoVirtual/Assets/UI/Tests/Editor/)). A package
   reference would put an engine + Addressables dependency inside that assembly.
2. **The `Resources` collision.** The project is 100 % `Resources.Load` with **zero
   Addressables** — `com.unity.addressables` and `com.unity.localization` are both absent
   from [Packages/manifest.json](src/app/MergulhoVirtual/Packages/manifest.json) (verified
   by grep). Unity Localization 1.5.13 pulls Addressables in unavoidably: `catalog.bin`,
   `catalog.hash`, `settings.json`, `AddressablesLink/link.xml` and local bundles land in
   StreamingAssets, and its own docs instruct a manual Addressables content build before
   every player build. That is a whole second asset system for one feature.
3. **The repo already runs this idiom twice** — `make articles` (Markdown → typed JSON) and
   `make ds-tokens` / `ds-icons` (JSON/text → USS and a `.gen.cs`). A bespoke fifth way to
   generate an asset would be the odd one out; a third instance of the existing one is not.

**The honest counter-arguments, recorded so nobody re-litigates from half the picture:**

- **String-only localization would sidestep the `Resources` collision entirely.** String
  Tables carry their data inline; only *Asset* Tables hold GUIDs resolved through
  Addressables. The collision bites only if you localize assets.
- Unity's [UI Toolkit localization best-practice guide](https://docs.unity3d.com/6000.3/Documentation/Manual/best-practice-guides/ui-toolkit-for-advanced-unity-developers/localization.html)
  explicitly blesses `SetBinding` for **runtime-created C# elements**. This repo's
  no-UXML architecture is therefore on the package's documented happy path, not fighting it.
- Unity 6.7's package page describes `com.unity.localization` as *extending built-in
  localization*, with "Addressables support" listed as something the package **adds** —
  implying the built-in module may not need Addressables. ⚠️ **Unverified.**
  `6000.7/…/Manual/localization.html` 404s, so that is an inference from one doc bullet.
  Re-check it before letting it influence anything.
- `com.unity.localization` **2.0.0 is registry-only** and pinned to no shipping Unity 6
  release. 1.5.13's exact Addressables/Newtonsoft minimums are not published anywhere —
  the 1.5 changelog's last mention is "Addressables 1.25.0" at 1.5.5.

**The sharpest single reason** is that the problem is **two halves and the package answers
only one cleanly.** ~196 chrome strings fit String Tables well. ~2,590 words of *content*
are already keyed by machine key (beach `name`, `AnimalDef` filename, article `id`) and
would go to Asset Tables — i.e. precisely the Addressables-requiring half — which is worse
than per-locale sidecars the repo's data model already invites. A hybrid pays the full
Addressables tax and still leaves the larger half hand-written.

### D2 — Languages: **pt-BR (source) + en now, es likely later**

All three are one/other plural languages and all are covered by the shipped Inter TTFs, so:
**no CLDR plural engine, no font work, no RTL.** See §5 for the font measurement.

### D3 — Plain JSON is the source of truth

`Assets/Resources/strings.pt-BR.json`, `strings.en.json`. No PO/gettext — translation here
is machine output plus the maintainer's own review, not an agency workflow, and a `.po`
toolchain buys nothing for that. **No compile step for the strings themselves: the file the
app loads is the file a human edits.**

### D4 — `make strings` exists but does **not** translate

Two jobs only:

- **(a) Generate `StringKeys.gen.cs`** so keys are compile-time constants rather than
  stringly-typed runtime misses. Exact precedent:
  [MdIconGlyphs.gen.cs](src/app/MergulhoVirtual/Assets/DesignSystem/Components/MdIcon/MdIconGlyphs.gen.cs),
  whose first line is `// GENERATED by tools/design_system/subset_material_symbols.py — DO NOT HAND-EDIT.`
- **(b) Validate parity** — every pt-BR key present in every other locale; matching
  placeholder arity (`{0}`/`{1}` counts per key, which is what catches a translator
  dropping an argument); no orphan keys unreferenced in C#.

Non-zero exit on any failure, mirroring `make articles-check` as a CI / pre-commit gate.
Stdlib Python only, no venv, **editor may stay open** — same class as `make articles`
(see [Makefile:142-146](Makefile#L142)), *not* the `ds-*` class that needs the editor closed.

### D5 — How the catalog reaches the formatters: **trailing optional parameter with ambient fallback**

This is the pattern the codebase already uses for time.
[ConditionsFormatter.cs:68-71](src/app/MergulhoVirtual/Assets/UI/Domain/ConditionsFormatter.cs#L68-L71):

```csharp
public static string Tide(TideData t, Func<DateTime, DateTime> toLocalTime)
{
    if (!t.Valid) return NoValue;
    var toLocal = toLocalTime ?? (d => d.ToLocalTime());
```

So formatters gain `IStringCatalog strings = null` as a **trailing** arg and do
`var s = strings ?? Loc.Current;`. Because it is trailing and optional, **every existing
call site and every existing test compiles unchanged.** `AppUiHost` sets `Loc.Current` once
at startup from the adapter.

⚠️ **The string *constants* have no parameter list to extend.**
[StateViewCopy.cs](src/app/MergulhoVirtual/Assets/UI/Domain/StateViewCopy.cs) holds 9 of
them; [BeachContentFormatter.cs:30-32](src/app/MergulhoVirtual/Assets/UI/Domain/BeachContentFormatter.cs#L30)
(`RiskPrefix`, `LifeguardPrefix`, `BehaviourPrefix`) and
[ArticleFormatter.cs:52](src/app/MergulhoVirtual/Assets/UI/Domain/ArticleFormatter.cs#L52)
are more. Each must either read `Loc.Current` directly (a property, `=>`, not a `const`)
or become a method. **A `const string` cannot be localized at all** — the C# compiler
inlines it into every call site, so callers in other assemblies keep the old value even
after a recompile of the declaring one. Converting `const` → `static` property is part of
phase 2 and is not optional.

### D6 — Test migration: option (a) now, (b) opportunistically

- **(a)** A `[SetUp]` loads the real pt-BR table; the existing assertions pass with **zero
  edits**, still asserting the same Portuguese but sourced from the table. They stay
  meaningful because what they actually test is *which key the branch picks* (rising vs
  falling tide, zero/one/many sightings, Hoje vs Ontem vs date) and the **argument
  formatting** (times, decimals, the NBSP). What is lost: the test no longer pins the copy
  — the committed JSON does, backed by `make strings-check` and `make ds-shots`.
- **(b)** Tests pass a synthetic fixture catalog (`"tide.rising" → "RISING {0:HH:mm} ({1:0.0} m)"`),
  which makes them locale-agnostic and able to catch a **wrong-key** bug that (a) cannot,
  at the cost of rewriting the assertions. **Convert a file to (b) when you are already
  touching it** — never as a standalone sweep.

### D7 — Content: per-locale sidecars keyed by the machine keys that already exist

| Sidecar | Keyed by | Replaces / sits beside |
|---|---|---|
| `places.<lang>.json` | `name` | `description` + `displayName` in [places.json](src/app/MergulhoVirtual/Assets/Resources/places.json) |
| `beaches_content.<lang>.json` | `name` | prose fields in [beaches_content.json](src/app/MergulhoVirtual/Assets/Resources/beaches_content.json) |
| `species_content.<lang>.json` | `AnimalDef` asset filename | prose fields on the `.asset` YAML |
| `content/articles/<lang>/*.md` → `articles.<lang>.json` | `id` | [articles.json](src/app/MergulhoVirtual/Assets/Resources/articles.json) |

Sidecars keep the ~40 KB polygon blob and the prefab/scale wiring out of every translation.
The base file stays the machine-key + geometry + wiring record; the sidecar is pure prose.

⚠️ **Species prose moves OUT of the `AnimalDef` `.asset` YAML** into
`species_content.<lang>.json`, leaving the `.asset` as prefab / scale / image / binomial
wiring only. Reason: the alternative is **5 more hand-maintained YAML files per locale in a
format Unity rewrites on save**, plus an `--lang` flag on
[tools/populate_animals.py](tools/populate_animals.py). Lifting it needs **one** adapter
change (`SpeciesCatalogAdapter` in
[UiServiceAdapters.cs](src/app/MergulhoVirtual/Assets/Scripts/UI/UiToolkit/UiServiceAdapters.cs))
instead. `binomial` deliberately stays on the asset: a scientific name is the same in every
locale, and putting it in a translation file invites someone to "translate" it.

### D8 — Articles: localize the pipeline, translate nothing yet

All four shipped articles are confirmed implementer-written placeholders (each opens with an
`error` callout saying so) and will be replaced with reviewed text. Translating them now
would be translating text that is about to be deleted.

⚠️ **When `articles.en.json` is absent or empty, fall back to the pt-BR corpus** rather than
showing an empty library. An English reader seeing disclaimer-flagged Portuguese articles
beats a dead tab. `IArticleCatalog.IsAvailable`
([IArticleCatalog.cs:472](src/app/MergulhoVirtual/Assets/UI/Interfaces/IArticleCatalog.cs#L472))
semantics stay **untouched** — it means "the build is broken", never "you are offline", and
a missing *translation* is neither.

**Free win:** the cross-reference validators in
[tools/build_articles.py](tools/build_articles.py) need **no change**. `@praia[…]` resolves
against `places.json` `name` and `@especie[…]` against `AnimalDef` filenames — both machine
keys — and the screens render the catalog's `DisplayName`, so translated articles link to
translated beaches automatically.

### D9 — Article `id`s stay pt-BR slugs

`tubaroes-de-noronha` stays `tubaroes-de-noronha` in every locale. It is cosmetic, and it is
the route payload — renaming one is a breaking change, exactly as CLAUDE.md records for
`AnimalDef` asset names and `places.json` `name`.

### D10 — Plurals stay explicit in C#

Separate keys (`sightings.zero` / `.one` / `.other`), selected by an `if` in the formatter,
not a templating engine. Honest for pt/en/es, and it **fails loudly** rather than silently
if anyone ever adds Polish or Russian — a missing `.few` key is a `strings-check` failure,
where a naive `{count, plural, …}` engine would quietly pick the wrong form.

### D11 — Number and date formatting travels with the **locale record**

Decimal separator, group separator, the 12 month names, "Hoje"/"Ontem" — all fields of the
locale record, not derived from `CultureInfo`. Keeps the suite deterministic, and it matches
what [ArticleFormatter.cs:122-126](src/app/MergulhoVirtual/Assets/UI/Domain/ArticleFormatter.cs#L122)
already does by hand.

### D12 — Locale selection: `systemLanguage` seeds, `PlayerPrefs` persists

The pattern `OnboardingPrefKeys`
([IOnboardingState.cs:26](src/app/MergulhoVirtual/Assets/UI/Interfaces/IOnboardingState.cs#L26))
already uses, including the explicit `Save()` — Unity only flushes prefs on a clean quit.

⚠️ **`SystemLanguage` has a single `Portuguese` member** (no pt-BR/pt-PT split; Chinese is
the only variant-split language in the enum). So it is good enough to *seed* a first launch
and must **never** be the stored value. Store the locale record's own id.

**The in-app language switcher UI is explicitly out of scope for this plan** and will be
designed separately.

---

## 2. Two defects to fix as part of this work, not separately

### ⚠️ The decimal separator is already wrong for pt-BR

The app ships `2.2 m` / `1.5 m` where Brazilian Portuguese wants `2,2 m`. It is deliberate —
[BeachContentFormatter.cs:34-35](src/app/MergulhoVirtual/Assets/UI/Domain/BeachContentFormatter.cs#L34):

```
/// <summary>pt-BR thousands grouping ("1.200 avistamentos"). Decimals stay
/// invariant ("2.2 m") to match ConditionsFormatter's existing output.</summary>
```

So the grouping separator was localized and the decimal separator was not, for consistency
with a sibling that was also not localized. **Fixing the comma and introducing a locale are
the same edit** — the decimal separator is a field of the locale record (D11), and doing it
any earlier means a second pass over the same call sites.

Affected format strings, all of them: `{0:0.0} m` and `{0:0} s`
([ConditionsFormatter.cs:38-40](src/app/MergulhoVirtual/Assets/UI/Domain/ConditionsFormatter.cs#L38)),
both tide strings (`:75`, `:81`), `{0:0.0} m · {1}`
([BeachContentFormatter.cs:128](src/app/MergulhoVirtual/Assets/UI/Domain/BeachContentFormatter.cs#L128)),
and `{0:0.#} MB` ([ReportFormatter.cs:104](src/app/MergulhoVirtual/Assets/UI/Domain/ReportFormatter.cs#L104)).

⚠️ The NBSP in `({1:0.0} m)` is load-bearing and documented at length in
`ConditionsFormatter`'s own doc comment — it is what stops "m)" being stranded on line two at
360 dp. **Carry it into the translated string** and make `strings-check` treat ` `
as significant, or the fix silently regresses in every locale but the source.

### ⚠️ CLAUDE.md's stated reason for avoiding `CultureInfo` is factually wrong on Unity 6

[ArticleFormatter.cs:117-120](src/app/MergulhoVirtual/Assets/UI/Domain/ArticleFormatter.cs#L117)
hand-codes the month names because *"IL2CPP can strip culture data out of a player build."*
**Verified directly against this project's editor** at `~/Unity/Hub/Editor/6000.3.14f1`:

- `Editor/Data/il2cpp/libil2cpp/os/Android/Locale.cpp:33-38` reads the real device locale via
  JNI — `Locale.getDefault()` then `toLanguageTag()`.
- `…/icalls/mscorlib/System.Globalization/Generated/CultureInfoTablesNet_4_0.h:8` declares
  `#define NUM_CULTURE_ENTRIES 339`, with `pt-BR` present in its string pool.

Those tables are **compiled C++ inside libil2cpp — the managed linker cannot strip them at
any `managedStrippingLevel`.** So `new CultureInfo("pt-BR")` works on device.

The explicit-formatting approach is **still correct** — determinism for a 467-test suite is a
better reason than the one that is written down — but the recorded *reason* is a myth and is
currently steering design. Re-check on Unity 6.8, which swaps Mono for CoreCLR.
**This should eventually be corrected in CLAUDE.md itself**; it is not corrected by this plan
(which edits no other file).

### A free test win worth taking in phase 1

The formatter tests never set a non-invariant culture, so deleting an `InvariantCulture`
argument keeps the suite green *today* — i.e. the suite cannot currently catch a culture
leak. A `[SetUp]` forcing `CultureInfo.CurrentCulture` to pt-BR turns ~70 existing assertions
into a **locale-independence guard at zero authoring cost**. Do it in phase 1, before
anything moves.

---

## 3. Three rule violations to clean up on the way through

All three verified against the working copy:

| Site | What | Why it is a violation |
|---|---|---|
| [HomeScreen.cs:258-262](src/app/MergulhoVirtual/Assets/UI/Screens/HomeScreen.cs#L258-L262) | `"Onda"`, `"Maré"`, `"Lua"`, `"Vento"`, `"Água"` passed to `AddConditionsRow` | Breaks the repo's **"screens must not build user-visible strings"** rule. They belong beside the row *values* in `ConditionsFormatter`, which already owns all five. |
| [MdRouter.cs:44-47](src/app/MergulhoVirtual/Assets/UI/Navigation/MdRouter.cs#L44-L47) | `"Início"`, `"Mergulho"`, `"Praias"`, `"Avistamentos"` in `RouterTab.Default` | Same rule, in the navigation layer. `Default` is a convenience table; the labels should come from the catalog with `Default` as the shape. |
| [MvMediaPicker.cs:73-74](src/app/MergulhoVirtual/Assets/DesignSystem/Components/MvMediaPicker/MvMediaPicker.cs#L73-L74) | `DefaultEmptyTitle` = `"Selecione arquivos do dispositivo"`, `DefaultHint` = `"Limite de tamanho: 20MB"` | **DesignSystem ships no copy by contract** (`MvStateView` is the stated reference for this). Both are already overridable and `ReportViewModel` already declares its own copies — so deleting the defaults costs nothing and removes the only Portuguese in the component library. |

These are the whole cleanup. Every other screen literal is an icon name or a log message
(verified by dumping every string literal in `UI/Screens` + `UI/Navigation` and reading the
59 hits: 5 in HomeScreen, 4 in MdRouter, the rest icons and `[MdRouter]` log text).

---

## 4. Measured inventory

Method for the C# counts: a Python pass over every `.cs` file, extracting `"…"` literals
outside comment lines, discarding pure-punctuation strings, dotted/slashed machine keys and
`md-`/`mv-`/`unity-` USS class names, then hand-classifying the residue. Reproduce it or
re-measure — do not trust these numbers a year from now.

| Area | Count | Notes |
|---|---:|---|
| `Assets/UI/Domain/` | **108** | Every one user-visible or a locale-sensitive format pattern |
| `Assets/UI/ViewModels/` | **70** | 81 raw literals − 6 report option keys − 5 icon names |
| Elsewhere | **18** | HomeScreen 5, MdRouter 4, MvMediaPicker 2, `VideoPlayerController` 6, `GalleryPicker` 1 |
| **Runtime total** | **≈196** | |

Per-file in `Domain/`: **ConditionsFormatter 33** (8 cardinals + 8 moon phases + 5
"Atualizado" variants + formats), **ArticleFormatter 26** (incl. 12 month names),
**BeachContentFormatter 19**, **ReportFormatter 11**, **StateViewCopy 9**,
**SpeciesMediaFormatter 6** (11 raw − 3 icon names − 2 duration formats),
**SpeciesCardFormatter 4**. `ArticleTokens` (14 literals) and `BeachContentTokens` (6) are
**wire vocabulary, not copy** — they must stay untranslated, as their doc comments say — and
`MoonPhase.cs` holds no literals at all.

**Tests.** `Assets/UI/Tests/Editor/` holds **207 assertion lines** containing a pt-BR literal
(counted by matching lines that contain both an `Assert`/`Is.EqualTo`/`StringAssert` token and
an accented or Portuguese-word literal) and **373 pt-BR literals overall** — the gap is
fixture data, not assertions. Heaviest by assertion line: ArticleFormatterTests 37,
BeachDetailViewModelTests 19, ConditionsFormatterTests 19, BeachesViewModelTests 15,
BeachContentFormatterTests 14, HomeViewModelTests 13, ReportFormatterTests 11,
ReportViewModelTests 11. Note `ArticlesScreenTests` / `ArticleScreenTests` look heavy by
literal count (30 / 28) but are **fixture-heavy, not assertion-heavy** (4 / 7) — they need no
work under option (a) and little under (b).

**Content words** (counted by parsing each JSON / YAML field):

| Source | Words | Notes |
|---|---:|---|
| `places.json` | **756** | 17 beaches: 703 description + 53 `displayName`. `name` (53w) is a machine key and is **not** translated |
| `beaches_content.json` | **156** | Very sparsely filled: `riskLevel`, `bestSeason`, `sightingPeak`, `lifeguardHours`, `tips` are empty on **all 17**; `idealTide` 4/17, `species` 3/17, `advisories` 7/17, `environmentTags` 11/17 |
| `AnimalDef` assets | **324** | 5 species: 257 description, 62 credit lines, 5 `displayName`. Credits are attribution — only the `Foto:` / `Modelo:` prefix is translatable, so ≈**262** truly |
| `articles.json` | **1,352** | 4 articles, all placeholder (D8) |
| **Total** | **≈2,590** | |

---

## 5. Fonts — no work needed, and why

All **8** TextCore font assets in
[Assets/DesignSystem/Fonts/](src/app/MergulhoVirtual/Assets/DesignSystem/Fonts/) (4 Inter,
3 Roboto, MaterialSymbols) are `m_AtlasPopulationMode: 1` (Dynamic) with
`m_ClearDynamicDataOnBuild: 1` and empty glyph tables — so **zero glyphs ship
pre-rasterised**; every glyph renders on demand from the TTF at runtime. A new language
therefore costs **no asset regeneration**, only whatever the TTF's cmap covers.

Measured directly from the shipped TTF cmap tables: each Inter face covers **2,852
codepoints** — Latin-1 Supplement 95/96, Latin Extended-A 127/128, Latin Extended-B 207/208,
Cyrillic 248/256, Greek 105. **CJK, Arabic, Hebrew and Devanagari are entirely absent (0
codepoints).** Adding one of those means a new TTF, a new font asset, and either a
`-unity-font-definition` swap in `_typography.uss` or a fallback chain — a separate project,
not a line item here.

⚠️ `make ds-shots` grows those atlas files as it rasterises new glyphs, and they are Git-LFS
tracked. **Expect them dirty after any screenshot run in a new locale** — that is
regenerated output, not an edit (CLAUDE.md already records this).

---

## 6. Phasing — five phases, riskiest first

### Phase 1 — The seam, pt-BR only, no translations

**All the risk lives here. Do it before committing to a language list.**

Changes: a `Locale` record + `IStringCatalog` + a static `Loc` ambient holder in
`Assets/UI/Domain/` (they are engine-free plain C#, so they belong in
`MergulhoVirtual.UI`, not `Assembly-CSharp`); a `StringCatalogAdapter` in
[UiServiceAdapters.cs](src/app/MergulhoVirtual/Assets/Scripts/UI/UiToolkit/UiServiceAdapters.cs)
doing the `Resources.Load<TextAsset>("strings.pt-BR")` + `JsonUtility` parse; `Loc.Current`
set once in `AppUiHost.Awake`; `tools/build_strings.py` + `make strings` / `make strings-check`;
`StringKeys.gen.cs`; the `[SetUp]` re-point in every affected test file; and the
`CultureInfo.CurrentCulture` guard `[SetUp]`.

**Acceptance:** `make ds-test` still reports **874/874** EditMode (use
`python3 tools/summarize_test_results.py …`, never the attribute grep — CLAUDE.md's warning
applies). `make strings-check` exits 0. **`make ds-shots` must be re-rendered and looked at**
— a green suite proves nothing about layout, and this phase changes where every string comes
from.

⚠️ **Footguns:** `JsonUtility` cannot deserialise a top-level array *or* a
`Dictionary<string,string>`, which is why `places.json` needs its `{"places": …}` wrapper —
so the table must be `{"locale":"pt-BR", "entries":[{"k":…,"v":…}]}` and get folded into a
dictionary by the adapter, exactly as `ArticleLibrary` does. And: the adapter runs in
`Assembly-CSharp` but the *interface* must live in `MergulhoVirtual.UI` or the formatters
cannot see it — the dependency is one-way and load-bearing.

**Mechanical vs judgement:** the seam is mechanical. The **key naming scheme is judgement**
and is the one thing that is expensive to change later, because `StringKeys.gen.cs` bakes it
into every call site. Decide it once, here.

### Phase 2 — Extract the ~196 C# literals

Largely scriptable: a script can find every literal and emit the table; **a human must name
every key.** Do the decimal separator (§2) and the three rule violations (§3) in this phase,
because all three touch the same call sites.

⚠️ Convert every `public const string` of copy to a `static` property in the **same edit**
that moves its value — a `const` left behind is inlined into callers and silently keeps the
old value (D5).

**Acceptance:** `make ds-test` green; `make strings-check` reports zero orphan keys (that is
the check that proves the extraction was complete, not the test suite); `make ds-shots`
re-rendered — the decimal-comma change moves text width on the conditions card, the Praias
tide stat and the report photo-size error, all of which are at or near a documented wrap
boundary at 360 dp.

### Phase 3 — Content sidecars

Order: **articles first** (easiest — the pipeline already exists and takes a directory), then
beaches, then lift species prose out of YAML. Each is a loader change plus an adapter change;
each degrades to the pt-BR base file when its sidecar is missing (D8).

⚠️ `make articles` must learn `content/articles/<lang>/` without breaking the flat layout for
pt-BR, and `make articles-check`'s **stale-JSON byte comparison** has to run per locale or a
stale `articles.en.json` ships silently — which is precisely the failure that gate exists for.

⚠️ Lifting species prose is the only *destructive* step in the plan: it deletes fields from 5
`.asset` files. Commit the sidecar and the YAML edit together, and check
`SpeciesCatalogAdapter`, the Praia detalhe species cards, the Reportar chips, the AR card and
`EspecieScreen` in one `make ds-shots` pass — five consumers, one change.

**Acceptance:** `make articles && make articles-check` both clean; `make ds-test` green;
`make ds-shots SHOT=praia && make ds-shots SHOT=especie && make ds-shots SHOT=conteudo`.

### Phase 4 — Locale selection + persistence

`Application.systemLanguage` seeds first launch; an explicit choice persists in `PlayerPrefs`
with an explicit `Save()`. **No switcher UI** (D12) — the stored value is set by test code
and by nothing else until that UI is designed.

**Acceptance:** `make ds-test` green. There is no visual surface to render.

### Phase 5 — Translate

Machine translation, maintainer review, commit. Purely content work; no code changes if
phases 1–4 were done right. That is the test of whether they were.

**Acceptance:** `make strings-check` clean for every locale (parity + placeholder arity), and
`make ds-shots` at **360 dp** in the new locale — German-length or Spanish-length strings in
a layout tuned to Portuguese is exactly where the wraps documented in CLAUDE.md's
narrow-phone section come back. Use `MV_SHOT_WIDTH=360 MV_SHOT_HEIGHT=640` with an
**absolute** `MV_SHOT_DIR` (CLAUDE.md records why a relative one lands somewhere unexpected).

---

## 7. What this plan does not cover

- **The in-app language switcher UI.** Designed separately; D12 stops at the stored value.
- **The operator admin HTML** in [src/backend/templates/](src/backend/templates/) — 7
  templates, ~772 pt-BR word tokens (321 distinct). It is Cloudflare-Access-gated,
  operator-only, and both operators read Portuguese.
- **Instagram captions** — user-generated pass-through from the Graph API, never a
  translatable resource.
- **`/api/v1/*` error `detail=` strings.** Verified: all six are English
  (`"Idempotency-Key header is required"`, `"photo is empty"`, `"timestamp must be ISO 8601 / RFC 3339"`,
  `"no cached Instagram image"`, `"no Instagram post cached yet"`), developer-facing, and never
  displayed by the app. ⚠️ Note the one that looks like a counter-example is not:
  `"Avistamento não encontrado"` appears 4× in
  [avistamentos_admin.py](src/backend/api/endpoints/avistamentos_admin.py#L132) — the *admin*
  router, behind Cloudflare Access, not `/api/v1`.
- **AR telemetry strings.** `ArStabilizationController.BuildTelemetry()` emits Portuguese
  (`"Fusão aguardando GNSS…"`, `"(bússola — ande ~8 m p/ refinar)"`), but CLAUDE.md records
  that it has **no on-screen consumer** — it is a log line for the maintainer. Leave it.

**Text baked into images — no live risk, one revival risk.** Nine legacy PNGs under
`Assets/Images/UI/` carry English text (`buttons_about`, `buttons_animals`, `buttons_ar`,
`buttons_beaches`, `ui_about`, `ui_animals`, `ui_ar`, `ui_beaches`, `ui_register`). Each was
checked by extracting its `.meta` GUID and grepping `MainScene.unity`: **all nine have zero
references**, orphaned by Slices 1–6. So there is no text-in-image problem today — only the
risk of someone reviving one. If that ever happens, the localization answer is to re-draw it
as an `MdIcon` + `Label`, not to ship nine more PNGs per locale.

---

## 8. Open questions

1. **Key naming scheme** (phase 1, and the expensive one). Mirror the C# shape
   (`conditions.tide.rising`) or the screen shape (`home.conditions.tide.rising`)? The first
   survives a screen being rebuilt; the second makes an orphan-key report readable. Not
   decided.
2. **Does the built-in localization module really avoid Addressables?** D1's third
   counter-argument rests on one Unity 6.7 doc bullet against a 404ing manual page. If it is
   true, a string-only package path becomes genuinely cheap and D1 deserves one more look
   *before* phase 2 (after which the extraction is done and the point is moot).
3. **Does `en` get its own article corpus, or share pt-BR forever?** D8 defers this by making
   the fallback safe; the answer arrives when the placeholder articles are replaced.
4. **Should `displayName` be localized at all?** Several are proper nouns
   (`Praia do Sancho`) that an English reader is better served seeing untranslated — but
   `Enseada dos Tubarões` arguably is not. Per-entry judgement, not a policy.
5. **Does `es` actually happen?** D2 assumes it might. If it definitively will not, nothing in
   the plan changes — which is the point of designing for it now rather than deciding.
