# DESIGN_IMPLEMENTATION.md

Implementation plan for the designer's **V2 prototype** — the `Protótipo` page of the
Figma file [`Protótipos`](https://www.figma.com/design/8e8pBsY0T5hpMdpwoinoXI/Prot%C3%B3tipos)
(file key `8e8pBsY0T5hpMdpwoinoXI`, last modified 2026-09-24).

**Written for:** whoever implements these screens in the Unity project (currently the
maintainer, plus Claude Code sessions). It assumes familiarity with
[CLAUDE.md](CLAUDE.md) and [design-system-implementation-plan.md](design-system-implementation-plan.md);
it does not repeat what those cover.

**Relationship to the other plans.** `design-system-implementation-plan.md` defines the
UI Toolkit + M3 component library (Phases 0–2, done) and the strangler migration (Phase 3,
started with Beaches). **This document is the content of Phase 3** — it says which screens
get built, in what order, against which design. It does not change the migration strategy:
old uGUI screens stay live until each new screen reaches parity.

> **Scope note — V1 is out.** The Figma file has two pages. `Wireframes` (6 frames, 375pt,
> low-fidelity greyscale) is **V1 and will not be implemented**. Everything below refers
> exclusively to the `Protótipo` page (14 frames, 390pt, full fidelity) = **V2**.

---

## 1. Source of truth and how to re-sync

The designs are read through the Figma REST API with a personal access token in
`~/.figma-token`. To refresh after the designer changes something:

```bash
tools/figma_fetch.sh
# -> .figma-sync/file.json          full node tree
# -> .figma-sync/png/<node-id>.png  2x frame renders
# -> .figma-sync/struct/<frame>.txt annotated layout tree per V2 frame
```

Two things the renders do **not** tell you, both load-bearing:

1. **The "Hoje" conditions card on Home is a flattened bitmap** in Figma (`image 13`), not
   a node tree. Its internals (Onda/Maré/Lua/Vento/Água rows + the tide curve) must be
   rebuilt from the existing `ConditionsCard` formatting in
   [BeachesViewModel.cs](src/app/MergulhoVirtual/Assets/UI/ViewModels/BeachesViewModel.cs)
   + [MdSparkline](src/app/MergulhoVirtual/Assets/DesignSystem/Components/MdSparkline/) —
   there is nothing to transcribe.
2. **`Custom Status Bar` and `Home Indicator Bar` are mockup chrome.** Every V2 frame draws
   a fake "9:30 / wifi / battery" bar and a home pill. Do **not** implement them — they are
   the OS. They are the reason each frame is 52dp taller at the top and 34dp at the bottom
   than the real content area. Safe-area insets handle this (see §3.6).

### 1.1 V2 frame inventory

| Frame | Node id | Size | What it is |
|---|---|---|---|
| Tela 7 | `29:2154` | 390×888 | **Início (Home)** — conditions card + 2×2 feature grid |
| Tela 6 | `15:746` | 390×1400 | Home **with first-run welcome card** |
| Tela 1 | `9:109` | 390×844 | **Praias — "você está em"** (current-beach summary) |
| Tela 2 | `4:61` | 390×1790 | **Praia — detalhe** (no top bar) |
| Tela 4 | `15:550` | 390×1888 | Praia — detalhe **+ top bar + floating SOS** |
| Tela 9 | `89:1930` | 390×1888 | Tela 4 with the **beach dropdown open** |
| Tela 8 | `31:3979` | 390×888 | **Mergulho (AR)** — camera + species info card |
| Tela 11 | `109:933` | 390×1494 | **Reportar avistamento — empty state** |
| Tela 12 | `109:1080` | 390×1631 | Reportar avistamento — **filled state** |
| Tela 3 | `9:180` | 390×1421 | Reportar — earlier order (size before species) |
| Tela 5 | `15:659` | 390×1415 | Reportar — earlier order (species before size) |
| Screen 14 - SOS | `36:8156` | 390×880 | **SOS** — speed dial + first aid |
| Screen 14 - SOS | `79:1304` | 390×880 | **Error state** ("Algo deu errado") |
| Screen 14 - SOS | `81:1403` | 390×880 | **Offline state** (same, wifi-off icon) |

**Tela 12 supersedes Tela 3 and Tela 5** — same screen, three orderings; 12 is the latest
(adds the "Seu nome" field and the populated media grid). Build 11 + 12.
**Tela 4 supersedes Tela 2** (adds the top bar). Build 4 + 9.

### 1.2 Unit mapping

Figma frames are 390pt wide. `AppPanelSettings` uses Constant Physical Size @160dpi, so
**1 USS px = 1 dp = 1 Figma pt**. Every number in this document transcribes literally —
no scaling.

> **Label metrics are zeroed — do not re-discover this.** Unity's default runtime theme
> gives every `Label` `padding: 4 2 4 1` and `margin: 4 4 2 2`: 14dp of invisible vertical
> and 9dp of invisible horizontal space that no Figma text node has. Transcribing a Figma
> gap on top of it doubles the gap, and the lost 9dp of measure costs whole wrapped LINES
> (the Home feature-card body wrapped to 4 where Figma fits 3). It is reset once, app-wide,
> in `Assets/DesignSystem/Tokens/_typography.uss` (`.unity-label { padding: 0; margin: 0 }`
> — the "Label metric reset" block). Consequences: (a) every gap you see is one a component
> or screen authored, so transcribe Figma gaps as-is; (b) **nothing may lean on the default
> metrics for spacing** — a new stack of labels needs explicit margins.

---

## 2. Screen inventory — what is new vs. what is a restyle

| V2 screen | Exists today? | Verdict |
|---|---|---|
| **Início (Home)** | ❌ nothing equivalent | **New screen.** Closest data source is the conditions/tide pair already wired for Beaches. |
| **Praias — "você está em"** | ⚠️ partly (`MainScreen` HUD place name) | **New screen** — becomes the Praias tab landing. |
| **Praia — detalhe** | ✅ `Assets/UI/Screens/BeachesScreen.cs` detail | **Restyle + major content expansion** (≈6 new content blocks). |
| **Mergulho (AR)** | ✅ `MainScreen` + `ObjectInteraction` popup | **Restyle of the HUD overlay.** AR session/spawning untouched. |
| **Reportar avistamento** | ✅ `RegisterScreen` (uGUI) | **Rebuild.** Form is ~4× richer; changes the upload contract. |
| **SOS** | ❌ | **New screen.** No nav slot — reached from the Home grid and the floating button on Praia detalhe. |
| **Error / offline state** | ❌ | **New shared component**, not a screen. |
| Animais catalog + 3D viewer | ✅ `AnimalsScreen` | **Not in V2.** See Decision D1. |
| Sobre + Instagram widget | ✅ `AboutScreen` | **Not in V2.** See Decision D2. |

---

## 3. The visual system

V2 is a **light theme with a dark chrome**: off-white page, white cards with 1px grey
borders, deep-navy for primary actions and the bottom bar, amber as the single accent.
This is a complete departure from the current tokens (generated from a turquoise seed
`#00A0B0`, and the app currently renders `Theme-Dark`).

### 3.1 Brand palette → M3 roles

Measured from the node tree (frequency-ranked; the `#6750A4 / #49454F / #1D1B20 / #CAC4D0 /
#FEF7FF` family are **unmodified Material kit defaults** the designer dragged in and never
restyled — treat them as "use our token here", not as brand colors).

| Brand color | Where it appears | M3 role |
|---|---|---|
| `#152336` navy | filled buttons, selected chips, icon tiles, CTA card, numbered bullets, tag pills | `primary` (`on-primary: #FFFFFF`) |
| `#FFC107` amber | nav active pill, "Reportar" button, icon glyphs on navy, "Ciência cidadã" heading | `secondary` (`on-secondary: #152336`) |
| `#FFFFFF` | all cards, top-bar pills | `surface` / `surface-container-lowest` |
| `#F9FAFB` | page background, "add more" tile | `background`, `surface-dim` |
| `#E5E7EB` | card borders, dividers, "Pendente" chip | `outline-variant` |
| `#6B7280` | secondary/caption text, placeholder text | `on-surface-variant` |
| `#374151` | body copy inside cards (tips, species behavior) | `on-surface` at reduced emphasis → use `on-surface-variant`; see A.1 |
| `#050B14` | bottom navigation bar, AR species card | `inverse-surface` |
| `#DC2626` | SOS button, call icons | `error` (`on-error: #FFFFFF`) |
| `#34C759` | "Risco: Baixo" badge | ⚠️ **no M3 role** → extension token |
| `#FFF7E6` / `#FEDF89` | lifeguard alert bar fill / border | ⚠️ **no M3 role** → extension token |

### 3.2 Extension tokens (M3 has no success/warning)

M3's role set has `error` but no `success` or `warning`. Two of the designs need both, so
the token vocabulary must be extended. **`_colors-*.uss` is generated and must not be
hand-edited**, so extensions get their own hand-maintained pair, following the existing
`_shape/_state/_motion` precedent:

```
Assets/DesignSystem/Tokens/_brand-light.uss   (new, hand-maintained)
Assets/DesignSystem/Tokens/_brand-dark.uss    (new, hand-maintained)
```

defining at minimum:

```
--md-sys-color-success / -on-success / -success-container / -on-success-container
--md-sys-color-warning / -on-warning / -warning-container / -on-warning-container
```

Three places must be updated in lockstep or the suite goes red:

1. `Theme-Light.tss` / `Theme-Dark.tss` — add the matching `@import` (the two import lists
   are tested for parity).
2. `TokenDisciplineTests.TokenFiles(theme)` — the file list is hardcoded; add the new pair,
   or `UsedTokens_AreAllDefined` will fail on every `var(--md-sys-color-success)`.
3. Both brand files must define the **identical** variable set
   (`LightAndDarkThemes_DefineIdenticalTokenSets`).

### 3.3 Regenerating the color tokens

Do **not** hand-edit `_colors-light.uss` / `_colors-dark.uss`. The brand palette is not a
tonal derivation of one seed (navy + amber are independent hues), so the seed path is wrong
here. Use the generator's JSON path, which reads `schemes.light` / `schemes.dark` verbatim:

```bash
tools/design_system/.venv/bin/python tools/design_system/generate_md3_tokens.py \
  --from-json tools/design_system/brand-theme.json
```

`brand-theme.json` (new, committed) is a Material Theme Builder-shaped export authored from
the table in §3.1. Every M3 role must be present in both schemes — the generator copies what
it's given and the discipline test compares the two sets.

**Dark scheme:** V2 only specifies light. Keep a dark scheme anyway (the token set must
exist in both files, and `Theme-Dark.tss` is what the gallery and the current Beaches host
use) — derive it from the same two brand hues rather than inventing a second palette, and
ship light. See Decision D5.

### 3.4 Typography — the project needs Inter

V2 is set in **Inter** at weights 400 / 500 / 600 / 700. The project ships Roboto only.
UI Toolkit resolves weight through a **separate font asset per weight**, so this is four
TTFs and four TextCore SDF assets:

```
Assets/DesignSystem/Fonts/Inter-Regular.ttf    -> Inter-Regular.asset     (400)
Assets/DesignSystem/Fonts/Inter-Medium.ttf     -> Inter-Medium.asset      (500)
Assets/DesignSystem/Fonts/Inter-SemiBold.ttf   -> Inter-SemiBold.asset    (600)
Assets/DesignSystem/Fonts/Inter-Bold.ttf       -> Inter-Bold.asset        (700)
```

Inter is SIL OFL — add the license next to the existing `LICENSE-Apache-2.0.txt`/`NOTICE.md`.
Generate the `.asset` files with `make ds-setup` (extend `FontAssetBuilder` to cover Inter),
editor closed.

The V2 sizes (11/12/13/14/15/16/18/20/24/26 px) do not line up with the M3 type scale
(11/12/14/16/22/24/28/32…). Recommendation: **keep the `.md-typescale-*` class names and
retune their values to Inter**, so there is one vocabulary and M3 components inherit the
brand automatically — rather than introducing a parallel `.mv-*` scale that components
would not pick up. Mapping in Appendix A.2. `_typography.uss` is hand-maintained, so this is
a direct edit.

### 3.5 Shape

V2 radii: **8** (chips), **10** (icon tiles), **12** (small buttons, upload thumbs),
**14** (submit button), **16** (list cards, alert bar, nav pill, stat cards), **20** (feature
cards, CTA, AR info card), **24** (large content cards), **100/999** (pills).

Current `_shape.uss` carries the M3 corner scale (`extra-small` 4 → `extra-large` 28).
Retune the existing tokens to the list above rather than adding a second scale.

### 3.6 Elevation — accepted fidelity loss

V2 uses drop shadows on most cards (`shadow:10` on cards and pills, `12` on the submit
button, `16` on the SOS button, `28` on the AR species card). **UI Toolkit has no
`box-shadow`** — this is a known, already-accepted limitation of the design system.

Mitigation: every shadowed surface in V2 *also* carries a `1px #E5E7EB` border, which is
what actually separates it from the `#F9FAFB` page. Render the border, drop the shadow. The
two places where the shadow is doing real work and a border is not enough:

- the **floating SOS button** over the hero photo → compensate with the existing `#B91C1C`
  2px stroke the design already has;
- the **AR species info card** over live camera → it already has `#FFFFFF@0.15` stroke +
  an opaque `#050B14` fill, which reads fine.

### 3.7 Icons — needs the designer

V2 uses **Flaticon** glyphs (`fi-sr-home`, `fi-sr-map-marker`, `fi-sr-mode-landscape`,
`fi-ss-doctor`, `fi-ss-call-outgoing`, `alert-triangle`, `arrow-left`, `chart-column`,
`compass`, `map-pin`, `mail`, `user`, `add_circle`, `close`) plus a **custom shark-fin icon**
for the Avistamentos tab.

The project renders Material Symbols from a subset font
([tools/design_system/material_symbols_icons.txt](tools/design_system/material_symbols_icons.txt)
→ `make ds-icons`). Two options:

- **(a) Map to Material Symbols** — `home`, `location_on`, `landscape`, `medical_services`,
  `call`, `warning`, `arrow_back`, `bar_chart`, `explore`, `mail`, `person`, `add_circle`,
  `close`. Cheap, no new pipeline, slightly off-brand. **The shark fin has no equivalent.**
- **(b) Get the SVG set from the designer** and subset a second icon font, or ship the fin
  as a sprite.

Recommendation: (a) for everything except the shark fin, which needs (b). Flag to the
designer early — it is the one asset nothing else can substitute. See Decision D7.

---

## 4. Navigation restructure

V2's bottom bar has **four** destinations; the app has five.

| | V2 | Today |
|---|---|---|
| 1 | **Início** (new) | AR |
| 2 | **Mergulho** (= today's AR) | Praias |
| 3 | **Praias** | Animais |
| 4 | **Avistamentos** (= Reportar, expanded) | Reportar |
| 5 | — | Sobre |

So **Animais and Sobre lose their tab**, and AR stops being the app's landing screen.
Both hold shipped, working features (the 3D viewer + inline video subsystem; the Instagram
widget that went live 2026-07-17). They must go somewhere — see Decisions D1 and D2.

**Bar spec** (identical across every V2 frame): container 390×98 `fill:#050B14`,
1px `#152336` top border; tab row 390×64, horizontal, `space-between`, `padding: 0 24`;
each tab 64 wide, vertical, `gap: 4`, centered. Active = 56×32 pill `padding: 4 16`,
`radius: 16`, `fill: #FFC107`, 24dp icon tinted `#152336`, label Inter 600 11px `#FFFFFF`.
Inactive = no pill, icon and label at `#FFFFFF` 50%, label Inter 500/600 11px. Below the row
sits the 34dp OS home-indicator area — that is safe-area padding, not a drawn element.

`MdNavigationBar` already implements the M3 pill-indicator shape and takes destinations from
code (`SetDestinations(IReadOnlyList<MdNavDestination>)`, `SelectionChanged`). What it needs
is the **inverse-surface** styling above, which is a USS change, not a component rewrite.

**Router.** Today `ScreenManager` (Assembly-CSharp, uGUI `SetActive`) owns navigation, with
`useUiToolkitBeaches` picking per-screen. With four UITK screens that stops scaling — the
DS plan already anticipates `MdRouter`. Introduce it in Slice 1 and let `ScreenManager` own
exactly one thing: whether the AR session runs (`useOptimizedPerformance` gating — see
CLAUDE.md "Performance gate"; **that logic must survive the migration intact**, it is what
keeps battery sane and AR resume sub-second).

Sub-screens reached *outside* the bar, which the router must also handle (with back):
**Praia detalhe** ← Praias · **SOS** ← Home grid + floating button · **Espécie detalhe**
← beach species card + AR info card · **Reportar** ← Praias CTA.

---

## 5. Data model gaps — read this before estimating

This is the largest risk in the plan. **Most of the beach detail screen is data the app
does not have.** `places.json` carries exactly five fields: `name`, `imageName`,
`description`, `photoCredit`, `points`.

### 5.1 Per-beach fields V2 needs that do not exist

| V2 element | Example value | Source today |
|---|---|---|
| Risk badge | "Risco: Baixo" | ❌ none |
| Environment tags | "Ambiente recifal", "Área de berçário", "Mar de fora", "Área do parque" | ❌ none |
| Best season | "Melhor época: Set - Fev" | ❌ none |
| Ideal tide | "Maré ideal: Baixa (até 14h)" | ❌ none |
| Sighting peak | "Pico de avistamento: Jan-Mar manhã" | ❌ none |
| Lifeguard hours | "Salva-vidas: Das 08h às 17h" | ❌ none |
| Species list (chips) | Tubarão-limão, Raias, Barracuda… | ⚠️ partial — `BeachSharkSpawner`'s Inspector list is per-beach but only covers modelled sharks |
| Per-beach species behavior | "Comportamento nessa praia: …" | ❌ none (`AnimalDef.description` is global, not per-beach) |
| Tips (3, numbered) | "Mantenha distância…" | ❌ none |
| Sighting count | "120 avistamentos registrados" | ⚠️ `/api/v1/avistamentos/count` is **global**, not per beach |
| Sighting gallery | photo + species + "Foto: Bianca Rangel" | ❌ no public endpoint; sighting images are private/signed |

**Recommendation — split geometry from editorial content.** Leave
[places.json](src/app/MergulhoVirtual/Assets/Resources/places.json) as the polygon source of
truth (it is generated by [tools/migrate_places_to_osm.py](tools/migrate_places_to_osm.py) and
should stay machine-owned), and add a hand-authored sibling:

```
Assets/Resources/beaches_content.json     # keyed by the exact places.json `name`
```

carrying risk, tags, season, ideal tide, peak, lifeguard hours, species keys, per-beach
behavior blurbs and tips. This ships the whole beach screen **with zero backend work**, and
keeps the two update cadences separate. The dynamic pair (count, gallery) stays backend and
can land later behind a "—" placeholder.

> ⚠️ **Naming mismatch.** V2 says "Baía do Sueste"; `places.json` says **"Sueste Beach"**.
> The file mixes Portuguese and English names (`Boldró Beach`, `Atalaia Beach`,
> `Sueste Beach`, `Praia do Sancho`…). `beaches_content.json` keys must match `places.json`
> **exactly** (the same constraint `BeachSharkSpawner` already has). Either normalize
> `places.json` to Portuguese display names — which touches the spawner list, the seed
> data, and the backend's `local` field — or keep the English keys and add a
> `displayName`. Decide before authoring content. See Decision D4.

### 5.2 Report-form gaps

The V2 form is much richer than `RegisterScreenController` + the
`POST /api/v1/avistamentos` contract:

| V2 field | Today |
|---|---|
| Species as **chips** | free-text `species_guess` |
| Size bucket (`<1m` / `1–2m` / `>3m`) | ❌ |
| Behavior **multi-select** (4 checkboxes) | ❌ |
| **Multiple** photos (grid + remove + add) | single photo |
| Name, email | ❌ |
| Profile (Turista / Condutor) | ❌ |
| "Seus avistamentos pendentes" list | ❌ — `JobQueue` has `GetStatus(jobId)` but **no enumerate-pending API** and keeps no success records |

Each new field needs: the `ReportSightingJob` `Data` struct + `SerializeData`/`DeserializeData`
round-trip, a `MultipartFormDataSection` in `Execute`, the backend form parameter, and the
Firestore document shape. **Multi-photo is not an additive change** — it alters the
blob layout (`originals/<registro>.<ext>` assumes one image per sighting) and the
idempotency semantics. Treat it as its own task. See Decision D6.

`JobQueue` needs a new read-only `IEnumerable<PendingJob> ListPending()` for the pending
feed. That is additive and safe (one file-per-job on disk already).

> ⚠️ **What Slice 3 actually shipped (2026-09-28): the client half only.** The paragraph
> above reads as one change spanning app and backend; it was split, deliberately. The
> `ReportSightingJob` round-trip, the multipart parts and `JobQueue.ListPending()` are done;
> **the backend was not touched.** `POST /api/v1/avistamentos` still declares only
> `photo`, `beach`, `timestamp`, `species_guess`, `notes`, so FastAPI silently discards the
> six new parts the app now sends — `species_key`, `size_bucket`, `behaviours` (repeated),
> `reporter_name`, `reporter_email`, `reporter_profile`. Nothing errors; the data simply
> never reaches Firestore. Finishing the round trip means adding those `Form()` parameters,
> extending the Firestore document shape, and regenerating the composite indexes for any
> field that becomes a list filter (the `2^N` budget in CLAUDE.md applies).
>
> Two consequences worth carrying forward: **`notes` is now dead on the client side** — the
> backend accepts it, but the V2 form has no free-text field, so `ReportViewModel.Submit()`
> never sets it; and **`reporter_email` is personal data in flight to a service that does not
> want it**, so it exists only in request logs. A privacy notice and a retention decision are
> owed *before* the backend starts persisting it.

### 5.3 SOS content

Phone numbers (Bombeiros, Hospital, ICMBio) and the first-aid articles are placeholders in
V2 — the tourniquet illustration is stock art. Real numbers and vetted first-aid copy are
required before this ships; wrong emergency numbers are worse than no SOS screen.
See Decision D3.

---

## 6. Design-system component gaps

Existing and reusable as-is (after retokenizing): `MdButton`, `MdChip`, `MdCard`,
`MdListItem`, `MdTextField`, `MdMenu`/`MdDropdown`, `MdNavigationBar`, `MdTopAppBar`,
`MdIcon`, `MdIconButton`, `MdSparkline`, `MdDialog`, `MdSnackbar`, `MdBottomSheet`,
`MdProgress`, `MdFab`.

Missing, needed by V2 — each ships code + USS + gallery entry + tests, per the DS
definition of done:

| # | Component | Used by | Notes |
|---|---|---|---|
| 1 | **`MdCheckbox`** | Reportar (behavior list) | Genuine M3 gap in the library. 18×18 box, `radius: 2`, checked = filled `primary` + white check, ≥48dp touch target. |
| 2 | **`MvTag`** (badge/pill) | risk badge, environment tags, "Pendente", "Área de berçário" | Small pill, `radius: 100`, `padding: 4–6 / 10–12`, semantic color variants (success / neutral / on-image). Could be an `MdChip` variant instead — prefer a new component; chips are interactive by contract. |
| 3 | **`MvAlertBar`** | "Salva-vidas: Das 08h às 17h" | Icon + text, `warning-container` fill, `radius: 16`. |
| 4 | **`MvOptionCard`** | Reportar (Turista / Condutor) | Large selectable card, icon over label, selected = 2px `primary` border. |
| 5 | **`MvMediaPicker`** | Reportar (photo grid) | Empty dashed state → 104×104 thumb grid with remove buttons + "add more" tile. |
| 6 | **`MvStateView`** | error / offline / empty | Illustration + title + body + action button. Drives frames `79:1304` / `81:1403`. |
| 7 | **`MvNumberedList`** | "Dicas de convivência" | 24dp numbered circle + body, `gap: 12`. Could be a `MdListItem` variant. |
| 8 | **`MvHeroHeader`** | Praia detalhe, AR | Full-bleed image + scrim + floating back button + pill selector + badge row. |
| 9 | **`MvMediaCarousel`** | "Galeria de avistamentos" | Horizontal 160×171 cards. UI Toolkit `ScrollView` with `horizontal` mode. |

`MvSpeedDialRow` (SOS call rows) and the feature-grid tile are thin enough to compose from
`MdCard` + `MdIcon` inside their screens — do not promote them until a second caller exists.

---

## 7. Delivery slices

Each slice is independently shippable and independently revertable, following the strangler
rule already in force: **the legacy uGUI screen stays live until the new one reaches parity**,
selectable by a `ScreenManager` toggle. Do not batch slices.

> **Status, 2026-09-28 — Slices 0, 1, 2 and 3 are done and green. Slices 4, 5 and 6 have not
> started.** Headless verification at that date: `make ds-test` **402/402**, `make ds-test-play`
> **21/21**, `make ds-shots` **78 PNGs, 0 skipped**. **Nothing has been run on a device**, so
> every claim below is an editor claim. See [docs/handover-v2-redesign.md](docs/handover-v2-redesign.md)
> for the working state, the known gaps and the device-verification checklist.
>
> The strangler *mechanism* changed shape in Slice 1 and no longer matches the paragraph
> above: there is no per-screen `ScreenManager` toggle any more. `ScreenManager` is only the
> AR performance gate; `MdRouter` owns navigation; the superseded uGUI screens (`BottomNav`,
> `BeachesScreen`, `AnimalsScreen`, `RegisterScreen`) stay in `MainScene` **deactivated and
> unrouted** until Slice 6 (`AppUiBuilder.UnroutedLegacyScreens`), while `MainScreen` (AR) and
> `AboutScreen` are still routed through `LegacyUguiScreen`.

### Slice 0 — Foundations *(blocks everything)* — ✅ **done**
- `brand-theme.json` + regenerate `_colors-*.uss`; add `_brand-*.uss` extension tokens.
- Update both `.tss` import lists and `TokenDisciplineTests.TokenFiles`.
- Inter TTFs + 4 SDF assets; retune `_typography.uss` (Appendix A.2) and `_shape.uss`.
- Icon decision (§3.7) applied to `material_symbols_icons.txt`; `make ds-icons` + `ds-setup`.
- **Done when:** `make ds-test` + `ds-test-play` green, and the gallery renders every
  component in the brand palette in both themes.
- ⚠️ This restyles the **existing** UITK Beaches screen on the way through. Expected.

### Slice 1 — Shell + Início — ✅ **done**
- `MdNavigationBar` restyled to the dark bar; `MdRouter`; `ScreenManager` reduced to the
  AR-session performance gate.
- `HomeScreen` (Tela 7) + welcome card (Tela 6): conditions card reusing the existing
  `BeachesViewModel` row formatting + `MdSparkline`; 2×2 feature grid.
- **Done when:** all four tabs route, Home shows live conditions, AR still runs on Mergulho.
- **As built:** the shell is `AppUiHost` + `MdRouter` + `IAppScreen`/`AppRoutes`, scaffolded
  by `AppUiBuilder` (`make ui-setup`). Two of the four tabs are UI Toolkit screens, Mergulho
  is still the uGUI AR HUD behind `LegacyUguiScreen`. "AR still runs on Mergulho" is verified
  only in the editor — the UITK panel sits at `sortingOrder = 100` over the uGUI canvas and
  that stacking has never been checked on hardware. Home also carries a fifth, full-width
  card for **Sobre** (Decision D2) that is not in the Figma frame, and its
  "Baixar a tábua de maré do mês" link is deliberately inert — there is no published DHN PDF
  to open (`AppUiHost.OnTideTableRequested`).

### Slice 2 — Praias — ✅ **done (code); ⚠️ content is empty**
- `beaches_content.json` + loader + `IBeachContent` in `Assets/UI/Interfaces/`.
- Praias landing (Tela 1) — replaces the current Beaches list as the tab root.
- Praia detalhe (Tela 4 + dropdown Tela 9) — restyle + the six new content blocks.
- Components 2, 3, 7, 8, 9 from §6.
- **Done when:** every beach in `places.json` renders with content, no placeholder strings;
  `useUiToolkitBeaches` legacy path deleted.
- **As built:** the legacy path is gone (`ScreenManager` no longer has screen fields at all)
  and every §6 component landed. The screens render **no placeholder strings** — they hide
  any block whose data is missing — but that is the opposite half of "every beach renders
  with content": `beaches_content.json` has **0/17** `riskLevel`, `bestSeason`,
  `sightingPeak`, `lifeguardHours` and `tips`, and **3/17** `species`. What is filled
  (`environmentTags` 11/17, `advisories` 7/17, `idealTide` 4/17) was *derived* from
  `places.json` descriptions and the AR spawner list, not authored by anyone with the
  knowledge. **This slice's "done when" is not met and cannot be met by code** — see
  Decision D8 and [docs/beaches-content-todo.md](docs/beaches-content-todo.md).

### Slice 3 — Avistamentos — ✅ **done (front end); ⚠️ backend unchanged**
- `MdCheckbox`, `MvOptionCard`, `MvMediaPicker`.
- `JobQueue.ListPending()`; `ReportSightingJob` field expansion; ~~backend form fields +
  Firestore shape + **index regeneration**~~ (`tools/generate_firestore_indexes.py` →
  `firebase deploy --only firestore:indexes` **before** the UI ships — see CLAUDE.md, and
  mind the 2^N index budget).
- Reportar (Tela 11 + 12).
- **Done when:** a multi-field sighting round-trips to the admin UI; retries stay idempotent.
- **As built:** deliberately front-end only. The struck-through line above was **not done** —
  no backend change, no Firestore shape change, no index regeneration. The app sends six new
  multipart parts that the endpoint does not declare and FastAPI drops on the floor (§5.2),
  so **"round-trips to the admin UI" is false today**: a sighting reaches Firestore with the
  same five fields it always had. Idempotency is unchanged and still holds. Multi-photo is
  out per D6 (`ReportViewModel.MaxPhotos = 1`). A separate fix in the same slice made the
  `JobQueue` load from disk at launch (`AppUiHost.Awake` → `SightingReportsAdapter`), so a
  sighting queued before an app kill now resumes — previously nothing instantiated the queue
  until the user submitted another report. That fix has EditMode coverage but has never run
  on a device.

### Slice 4 — Mergulho (AR HUD) — ⬜ **not started**
- Hero top bar (back + beach pill + dropdown) and the species info card (Tela 8) as a UITK
  overlay on the AR camera.
- Keep `ObjectInteraction` as the hit source; only the presentation moves.
- ⚠️ A UITK `UIDocument` over the AR feed needs a transparent root and correct sort order
  against the existing uGUI canvas — the same constraint `BeachesScreen` already solves with
  a transparent, `PickingMode.Ignore` root.
- **Done when:** tapping a shark opens the card on device; AR tracking unaffected.
- ⚠️ The constraint above is **still unverified**: the shell panel is already drawing over the
  AR HUD today (`AppUiBuilder.PanelSortingOrder = 100`, transparent `PickingMode.Ignore` root),
  but no one has confirmed on hardware that the nav bar draws above the AR camera and stays
  tappable. **Do that device check before starting this slice** — it is the assumption the
  whole slice is built on.

### Slice 5 — SOS + states — ⛔ **not started; blocked by D3**
- `MvStateView`; SOS screen with real numbers (`Application.OpenURL("tel:…")`).
- Wire error/offline states into the Instagram widget, conditions fetch, and sighting upload.
- `AppRoutes.Sos` already exists and is raised by the Início SOS tile and the floating SOS pill
  on Praia detalhe; with no screen registered, `MdRouter` logs one warning and does nothing.
  `MvStateView` is the one §6 component not built.

### Slice 6 — Retire uGUI — ⬜ **not started**
- Delete legacy screens; resolve Animais/Sobre per D1/D2; drop `ScreenManager`'s screen
  fields; remove the now-dead uGUI builders.
- Partially pre-empted by Slice 1: `ScreenManager`'s screen fields are already gone, and
  `BottomNav` / `BeachesScreen` / `AnimalsScreen` / `RegisterScreen` are already deactivated
  and unrouted. What remains is deleting them (and their builders) and giving Animais a home
  — D1 says "sub-screen from the species cards", and the "Saiba mais sobre a espécie" link
  that would push it already exists as an unregistered no-op.

---

## 8. Per-screen specs

Numbers are transcribed from the node tree. Only non-obvious structure is listed — full
dumps are in `.figma-sync/struct/*.txt` after a re-sync.

### 8.1 Início — Tela 7 / 6
```
page  fill #F9FAFB
└ Main Content Body   V, gap 16, padding 20/16/100/16
  ├ [Tela 6 only] Welcome card  V, gap 8, pad 16, #FFFFFF, r20, 1px #E5E7EB, close ⨯ top-right
  ├ Conditions card   234h, V, gap 8, pad 12, fill #152336, r20
  │   "Hoje" Inter 700 15 #FFF + chevron · "Baixar a tábua de maré do mês" Inter 600 10 #FFF
  │   rows Onda/Maré/Lua/Vento/Água + MdSparkline  (rebuild — bitmap in Figma)
  └ Feature grid      V, gap 12  ·  2 rows × 2 cards, gap 12
      card 173×138, V, gap 8, pad 12, #FFFFFF, r20, 1px #E5E7EB
        icon tile 36×36 #152336 r10, glyph 24dp #FFC107
        title Inter 700 15 #152336 · body Inter 400 12 #6B7280, gap 2
      Praias · Mergulho Virtual · Avistamentos · SOS
```
The 4th tile is named `Feature Card - Mapa Interativo` in Figma but labelled **SOS** — a
renamed leftover. Build SOS.

### 8.2 Praias landing — Tela 1
```
Hero 390×240  image + #152336@0.25 scrim
  badge row  H, gap 8 · pills pad 6/12, r100, #152336@0.85, Inter 600 12 #FFF
Main Content Body  V, gap 16, pad 20/16/100/16
├ Location card   V, gap 12, pad 16, #FFFFFF, r20, 1px #E5E7EB
│   "Você está em" Inter 600 13 ls0.5 #6B7280
│   <beach>        Inter 700 26 #152336
│   divider 1px #E5E7EB
│   "Fernando de Noronha, PE" Inter 400 14  ·  risk pill pad 4/12 r100 #34C759, Inter 700 12 #FFF
├ Stats row  H, gap 12 · two 173×77 cards, V gap 8, pad 16, r16
│   caption Inter 600 12 #6B7280 · value Inter 700 18 #152336
│   "Maré agora" / "Pico de avistamento"
└ CTA card   H, gap 12, pad 16, #152336, r20
    "Ciência cidadã" Inter 700 16 #FFC107 · body Inter 400 13 #FFFFFF
    button 84×36 pad 10/14 r12 #FFC107, "Reportar" Inter 700 13 #152336
```

### 8.3 Praia detalhe — Tela 4 (+ Tela 9 dropdown)
```
Hero 390×340  image + #152336@0.35 scrim
  Back 40×40 #FFFFFF r100 1px #E5E7EB   ·   Beach selector 295×42 pill:
    H, gap 12, pad 14/16, #FFFFFF, r100 — map-pin 20dp, name Inter 600 16 ls0.1, chevron 24dp
  badge row (as 8.2, #152336@0.80, Inter 500 12)
Main Content Body  V, gap 24, pad 24/16/120/16
├ Title row        name Inter 700 24 · "Fernando de Noronha, PE" Inter 500 13 #6B7280
│                  risk pill pad 6/12 r100 #34C759 Inter 600 12
├ Stats card       V, gap 16, pad 20, #FFFFFF, r24, 1px #E5E7EB
│    2-col: "Melhor Época"/"Maré Ideal" — label Inter 600 12, value Inter 700 18
│    1px #E5E7EB line
│    chart icon 24dp + "Avistamentos" Inter 600 12 / "<n> avistamentos registrados" Inter 600 15
├ Alert bar        H, gap 12, pad 12/16, #FFF7E6, 1px #FEDF89, r16, Inter 500 14
├ Species section  "Espécies comuns" Inter 700 18 + MdChip row (r8; selected #152336/#FFF)
├ Species card     V, #FFFFFF, r24, 1px #E5E7EB
│    media 358×160 + "Área de berçário" tag (#152336@0.80, Inter 600 11)
│    content pad 16, gap 12: name Inter 700 18 + binomial Inter 400 12 #6B7280 (baseline-aligned)
│    behavior Inter 400 14 #374151 · "Saiba mais sobre a espécie" Inter 600 14, right-aligned
├ Tips section     "Dicas de convivência" Inter 700 18
│    card pad 20, gap 16 — rows H gap 12: 24dp #152336 r100 circle w/ Inter 700 12 #FFF + Inter 400 14 #374151
└ Gallery section  "Galeria de avistamentos" Inter 700 18
     H scroll, gap 12 — 160×171 cards r16: image 160×120 + pad 10, gap 2
       species Inter 700 13 · "Foto: <name>" Inter 400 11 #6B7280
Floating SOS  97×52, pad 12/18, #DC2626, 2px #B91C1C, r999 — triangle 20dp + "SOS" 800 16 #FFF
```
Tela 9's dropdown is a stock M3 menu: 295 wide, `#FFFFFF`, `r16`, 56dp items → `MdMenu`.

### 8.4 Mergulho (AR) — Tela 8
```
full-bleed AR camera
Top bar   = same back + beach-selector pill as 8.3
species-info-card  342×176, V, pad 16, gap 12, fill #050B14, 1px #FFFFFF@0.15, r20
  close 28×28 #FFFFFF@0.10 1px #FFFFFF@0.15 r14
  title  Inter 700 20 #FFF  ·  binomial Inter 500 13 #FFF
  1px #FFFFFF@0.13 line
  3 rows, gap 10, space-between:
    label Inter 700 12 ls0.5 #FFF  /  value Inter 400 13 #FFF
    "Tamanho aprox." · "Dieta" · "Comportamento"
```
`AnimalDef` has no `diet` or `behavior` field — add both, or source from
`beaches_content.json`. Note the card is bottom-anchored above the nav bar, not a bottom sheet.

### 8.5 Reportar — Tela 11 (empty) / 12 (filled)
```
Header 390×72  H, gap 12, pad 16, 1px #E5E7EB bottom
  back 40×40 r100 · "Reportar avistamento" Inter 700 20
Form  V, gap 24, pad 20/16/100/16 — each section V, gap 10, label Inter 700 15 #152336
├ Espécie identificada   MdChip wrap row, gap 8
├ Tamanho aproximado     MdChip row — Menor que 1m / 1m - 2m / Maior que 3m
├ Comportamento observado  card r16 1px #E5E7EB — 4 rows 52h, pad 14, 1px divider,
│                          label Inter 400 14 + MdCheckbox 18×18 r2
├ Adicionar foto ou vídeo
│    empty:  dashed box, "+" · "Selecione arquivos do dispositivo" Inter 700 · "Limite: 20MB" 12 #6B7280
│    filled: card pad 16 — 104×104 thumbs r12, gap 8, wrap; remove 28×28 #000@0.25 r14;
│            "add more" tile #F9FAFB 1px #E5E7EB r12 + add_circle 24dp #6B7280
├ Se identifique (opcional)   two 44h fields, pad 12/16, #FFFFFF, r12, 1px #E5E7EB
│                             placeholder Inter 400 14 #6B7280 + trailing 20dp icon
├ Selecione seu perfil        two 174×93 cards r16, pad 16, gap 12 — icon 32dp over Inter 700 14
│                             selected = 2px #152336; unselected = 1px #E5E7EB
├ Submit  358×47, pad 14, #152336, r14 — "Enviar avistamento" Inter 700 16 #FFF
└ Seus avistamentos pendentes
     card pad 16, r16 — species Inter 700 16 + "Pendente" pill (#E5E7EB, Inter 700 11 #6B7280)
     "Hoje, 9:32 · Baía do Sueste" Inter 400 13 #6B7280
```

### 8.6 SOS — `36:8156`
```
Header  = 8.5 header, title "SOS"
Content V, gap 32, pad 24/16/120/16
├ "Discagem rápida" Inter 600 16
│   3 rows 358×60, H, gap 12, pad 14/16, #FFFFFF, r16, 1px #E5E7EB
│     32×32 #DC2626 r16 circle + 19dp white call glyph · label Inter 600 16
│     Bombeiros · Hospital · ICMBio
└ "Primeiros socorros básicos" Inter 600 16
    card pad 20, gap 16, r24 — title Inter 700 18 · body Inter 400 14 #6B7280 · image 318×160 r16
```
The nav bar in this frame shows **Praias** active — a copy-paste artifact. SOS has no tab;
keep the originating tab selected.

### 8.7 Error / offline — `79:1304` / `81:1403`
Centered illustration + 46dp status icon (`error` filled `#A7AFB6` / wifi-off), title
"Algo deu errado por aqui" Inter 700 18, body "Não conseguimos carregar as informações."
Inter 400 14 `#6B7280`, and a bottom-anchored 358×47 `#152336` r14 button
"Tentar novamente". Identical but for the icon → one component, two variants.

---

## 9. Verification

Per slice, in this order — the first three are headless and **require the editor closed**:

```bash
make ds-compile      # imports + compiles
make ds-test         # EditMode: TokenDiscipline + component + ViewModel suites
make ds-test-play    # PlayMode: interaction suites
```

Then, by hand:
- **Gallery first.** Reproduce any visual bug in `GalleryScene` before touching a screen —
  if it repros there it is a component bug (fix + regression test); if not it is screen wiring.
- **Device build.** Light-on-white contrast, safe-area insets, and the AR overlay cannot be
  judged in the editor. Follow the existing rule: build an Android Development Build and read
  Android Logcat.
- New ViewModels get EditMode tests as plain C# with fake services — keep the
  `BeachesViewModelTests` pattern, including injected `utcNow`/`toLocalTime` so tests stay
  timezone-proof.

---

## 10. Open decisions

These change what gets built and were not mine to make. **All but D3 were resolved by the
maintainer and are reflected in the code as of 2026-09-28**; the column below records the
decision that was actually taken, not the recommendation that was offered.

| # | Decision | Why it matters | Resolved (2026-09-28) |
|---|---|---|---|
| ✅ **D1** | Where do **Animais** + the 3D viewer + inline videos go? | Lose their tab in V2, but the subsystem works and is shipped. | **Sub-screen from the species cards; the standalone list is dropped.** Not built yet: `AppRoutes.Especie` has no screen, and the "Saiba mais sobre a espécie" link on the beach species card raises it as an **unregistered no-op**. `AnimalsScreen` is deactivated and unrouted in `MainScene`. |
| ✅ **D2** | Where does **Sobre** + the Instagram widget go? | Lost its tab; the widget shipped 2026-07-17. | **A Home entry** — a full-width card below the 2×2 grid (not in the Figma frame), pushing `AppRoutes.Sobre`, which routes to the **legacy uGUI AboutScreen** via `LegacyUguiScreen`. That keeps the shipped Instagram widget reachable with no rebuild. ⚠️ It has no back button and the nav bar cannot return to the tab it was pushed from in one tap — see the handover doc. |
| ⛔ **D3** | Are the **SOS** numbers and first-aid content real? | Wrong emergency numbers are actively dangerous. | **STILL OPEN. This blocks Slice 5 entirely** — nothing SOS-shaped may ship until the project/ICMBio vets the numbers and the first-aid copy. The SOS entry points exist and are deliberately inert. |
| ✅ **D4** | Normalize beach names to Portuguese, or add `displayName`? | `places.json` mixes "Sueste Beach" / "Praia do Sancho"; V2 shows "Baía do Sueste". Keys must match exactly across content, spawner, seed data and the backend `local` field. | **`displayName` added to `places.json`; the machine `name` stays the key** (spawner list, `beaches_content.json` keys, the backend `local` field). All 17 places carry one; 5 differ from the key. `BeachInfo.DisplayName` falls back to `Name` when blank. Never look anything up by `displayName`. |
| ✅ **D5** | Light-only, or keep a dark theme? | V2 specifies light only; the DS requires both token sets to exist. | **Ship light.** `AppPanelSettings.themeStyleSheet = Theme-Light`. Dark exists (both token sets are required and parity-tested) and the gallery keeps its toggle, but no screen was designed for it — `make ds-shots` renders dark shots only as a token-discipline canary. |
| ✅ **D6** | **Multi-photo** upload — in scope? | Changes the blob layout, idempotency, and the Firestore shape. Not additive. | **Single photo this slice; the grid UI is capped at 1** (`ReportViewModel.MaxPhotos = 1` → `MvMediaPicker.MaxItems`). Multi-photo is its own future slice. Replacing a photo is remove-then-add. |
| ✅ **D7** | Can the designer export the **icon set** (esp. the shark fin)? | Flaticon glyphs; the fin has no Material Symbols equivalent. | **Option (a) now: Material Symbols everywhere. The shark fin is still outstanding.** It is stood in for by `AppIcons.AvistamentosPlaceholder = "visibility"`, rendered by both the bottom-bar Avistamentos tab and the Início Avistamentos tile; a third stand-in (`"help"`) sits in the gallery's nav-bar demo. All three must move together when the SVG arrives. Plausible-looking marine glyphs (`waves`, `surfing`, `scuba_diving`, `pool`) are deliberately excluded from the icon subset so none of them can quietly become the shipped icon. |
| ✅ **D8** | Who authors `beaches_content.json` for **17 beaches**? | Risk, season, tips, species, lifeguard hours × 17. Blocks Slice 2's "done". | **The biologists / project team, via [docs/beaches-content-todo.md](docs/beaches-content-todo.md).** The schema shipped with the content left blank: a research-and-cite pass was offered and **declined** — an invented risk level or lifeguard hour is worse than a blank, and the screens are built to hide a missing block rather than print a placeholder. Slice 2 is code-complete and content-empty until that file is filled. |

---

## Appendix A — mapping tables

### A.1 Color

| Figma | Token | Note |
|---|---|---|
| `#152336` | `--md-sys-color-primary` | |
| `#FFFFFF` on navy | `--md-sys-color-on-primary` | |
| `#FFC107` | `--md-sys-color-secondary` | |
| `#152336` on amber | `--md-sys-color-on-secondary` | |
| `#F9FAFB` | `--md-sys-color-background` | |
| `#FFFFFF` | `--md-sys-color-surface` | |
| `#E5E7EB` | `--md-sys-color-outline-variant` | |
| `#6B7280` | `--md-sys-color-on-surface-variant` | |
| `#374151` | `--md-sys-color-on-surface-variant` | V2 uses two greys for body vs. caption; collapse to one unless the designer objects |
| `#050B14` | `--md-sys-color-inverse-surface` | nav bar, AR card |
| `#FFFFFF@0.50` | `--md-sys-color-inverse-on-surface` @ 50% | inactive tab |
| `#DC2626` | `--md-sys-color-error` | |
| `#34C759` | `--md-sys-color-success` | **extension** |
| `#FFF7E6` | `--md-sys-color-warning-container` | **extension** |
| `#FEDF89` | `--md-sys-color-warning` | **extension** |
| `#6750A4`, `#49454F`, `#1D1B20`, `#CAC4D0`, `#FEF7FF`, `#F7F2FA` | — | **Material kit defaults, not brand.** Replace with the tokens above wherever they appear. |

### A.2 Type — Inter, proposed `.md-typescale-*` retune

| Class | V2 usage | Size / weight |
|---|---|---|
| `headline-small` | location card beach name | 26 / 700 |
| `title-large` | screen titles ("SOS", "Reportar avistamento"), beach name | 20–24 / 700 |
| `title-medium` | section headers ("Espécies comuns", "Dicas de convivência") | 18 / 700 |
| `title-small` | card titles, form section labels, stat values | 15–16 / 600–700 |
| `body-large` | body copy in cards, list rows, inputs | 14 / 400 |
| `body-medium` | supporting copy, feature-card body | 12–13 / 400 |
| `label-large` | buttons, chips | 14 / 500–700 |
| `label-medium` | captions, stat labels, tags | 12 / 600 |
| `label-small` | nav labels, photo credits, small tags | 11 / 500–600 |

Letter-spacing is `0` almost everywhere in V2 (exceptions: `0.5` on "Você está em" and the
AR spec labels, `0.1` on the beach-selector name). M3's default tracking must be zeroed —
do not keep the Roboto values.

## Appendix B — fidelity caveats (accepted)

| V2 uses | UI Toolkit | Resolution |
|---|---|---|
| Drop shadows | no `box-shadow` | 1px border (already in the design) — §3.6 |
| Fixed line heights | no `line-height` | font metrics; verify long pt-BR strings on device |
| Text nodes with no inset | `Label` ships padding+margin | zeroed app-wide in `_typography.uss` — §1.2 |
| Font weight as a property | one asset per weight | 4 Inter SDF assets — §3.4 |
| `cubic-bezier()` easing | named easings only | nearest in `_motion.uss` |
| Status bar / home indicator | OS-owned | **do not build** — safe-area padding, §1 |

---

*Generated from Figma file `8e8pBsY0T5hpMdpwoinoXI` (`Protótipo` page), version `2402812393380097744`.*
