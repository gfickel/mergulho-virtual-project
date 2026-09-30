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
| Conteúdo educativo (article index + reader) | ❌ nothing equivalent | **Not in V2 and not a slice.** The feature postdates the prototype, so §1.1 has no frame for it and there was nothing to transcribe. Built 2026-09-30 as two sub-screens reached from a card on Início — see §8.9. |

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

### 3.2 Extension tokens (M3 has no success, warning, or camera-overlay roles)

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

**Nine more roles were added the same way** — eight by Slice 4's AR HUD, one by the
2026-09-29 fidelity pass. They are **pre-blended composites, not alphas**: Unity blends in
linear colour space and Figma blends in sRGB, so a translucent value transcribed from a
Figma layer does not reproduce it (Appendix B, *Linear colour space*). Where the backdrop is
opaque and known — which every one of these is — authoring the composite is the *exact* fix
rather than an approximate one.

| Token (`--md-sys-color-…`) | Light | Dark | = what over what | Site |
|---|---|---|---|---|
| `camera-surface` | `#3A3F45` | same | opaque | back circle + beach pill |
| `camera-surface-dim` | `#050B14` | same | opaque | species info-card fill |
| `on-camera-surface` | `#FFFFFF` | same | opaque | every glyph and label on the HUD |
| `camera-outline` | `#2A3037` | same | white @0.15 over `camera-surface-dim` | info-card hairline |
| `camera-outline-dim` | `#54585D` | same | white @0.13 over `camera-surface` | control hairlines (pill, back) |
| `camera-divider` | `#262B33` | same | white @0.13 over `camera-surface-dim` | the card's internal rule |
| `camera-fill` | `#1E232C` | same | white @0.10 over `camera-surface-dim` | close-button disc fill |
| `camera-fill-outline` | `#40444C` | same | white @0.15 over `camera-fill` | close-button disc hairline |
| `inverse-on-surface-muted` | `#7F8288` | `#828891` | `inverse-on-surface` @0.5 over `inverse-surface` | inactive bottom-nav icon + label |

Four things about that table that are easy to undo by accident:

- **The whole `camera-*` family is identical in both themes**, deliberately. These surfaces
  float over the live camera, so the backdrop is whatever the lens is pointing at and is
  never the theme. `inverse-surface` would have been the obvious M3 role (in light it is
  the exact brand value) but it flips to a light grey in dark, rendering the card
  white-on-camera.
- **`camera-divider` and `camera-fill-outline` exist because one alpha served two
  backdrops.** White @0.13 sits on both `camera-surface` and `camera-surface-dim`; white
  @0.15 sits on both `camera-surface-dim` and `camera-fill`. An alpha can serve both, a
  pre-blend cannot — hence the split.
- **Each value is bonded to one backdrop.** Move `camera-surface` or `camera-surface-dim`
  and every composite has to be recomputed; they will not follow on their own the way an
  alpha would. They are named after the surface they are drawn *on*, not after how
  translucent they look — which is why, post-pre-blend, `camera-outline-dim` is *lighter*
  than `camera-outline`. That naming wart is kept on purpose.
- **`inverse-on-surface-muted` replaces Appendix A.1's "`inverse-on-surface` @ 50%".**
  Authored as an `opacity` it rendered **(183,184,185)** against V2's **(127,130,136)** —
  the inactive tabs read nearly as bright as the active one, erasing the selected-state
  signal. `MdNavigationBar`'s `--active` modifier now swaps ink **colour** rather than
  opacity. Unlike the camera family this token does differ per theme, because both the ink
  and the bar flip.

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

**Two rungs were added on the way through**: `title-large-increased` (24/700) and
`title-small-increased` (16/700). A.2 specifies those two rows as **ranges** — title-large
"20–24 / 700", title-small "15–16 / 600–700" — and the retune picked one end of each (20 and
15), leaving every V2 element specced at the other end to re-author its size in a screen or
component stylesheet; 16 is in fact the most frequently authored text size in the codebase.
The suffix follows `_shape.uss`'s `corner-*-increased` precedent (§3.5) rather than starting
a parallel vocabulary.

**Added, never re-pointed.** `title-large` is still 20 and `title-small` still 15 — moving
either would silently restyle every `Md*` component that inherits the brand through those
names, which is the one thing this file must never do. Each `-increased` rung moves the
**size only**, keeping its base rung's 700 face, so the two V2 sites that want 16 at the 600
face (the SOS rows and the beach-selector name) still override `-unity-font-definition`.

### 3.5 Shape

V2 radii: **8** (chips), **10** (icon tiles), **12** (small buttons, upload thumbs),
**14** (submit button), **16** (list cards, alert bar, nav pill, stat cards), **20** (feature
cards, CTA, AR info card), **24** (large content cards), **100/999** (pills).

Current `_shape.uss` carries the M3 corner scale (`extra-small` 4 → `extra-large` 28).
Retune the existing tokens to the list above rather than adding a second scale. Two rungs
were added rather than re-pointed, in the M3-2025-expressive style:
`corner-medium-increased` (14, the submit button) and `corner-large-increased` (20, feature
cards / CTA / AR info card).

**`corner-full` (1000px) is a CIRCLE token, not a pill token.** CSS resolves an over-large
`border-radius` by scaling every radius by one shared factor ≤ 1, which preserves the
stadium; **UI Toolkit instead clamps each axis independently** (`rx = min(r, width/2)`,
`ry = min(r, height/2)`), so on a 295×42 selector pill a 1000px radius resolves to
`rx 147.5 / ry 21` — a full ellipse, a lens. The token is only correct where width ==
height, where both clamps land on the same number. A stadium therefore needs
`r == height/2` **exactly**: larger re-opens the ellipse (rx keeps growing toward width/2),
smaller is a plain rounded rect. Two ways to get it, and the rule for choosing:

- **Height authored in USS** — every pill in the library but one. Write the literal
  half-height on the same rule as the height it halves (`height: 42px; border-radius: 21px`,
  as `MvHeroHeader`'s selector does). No runtime cost, still overridable by a screen
  stylesheet, and `ShapeDisciplineTests` pins the pair so they cannot drift apart.
- **Height driven by content** — font metrics, wrapping, OS font scale — so it cannot be
  known at author time: call `MdShape.KeepStadium(element)`, which re-pins the radius to
  half the *resolved* height on every `GeometryChangedEvent`. `MvTag` is the only current
  user; the cost is an inline style, which outranks every stylesheet, so it is a decision
  about the component rather than a tweak.

`ShapeDisciplineTests` fails the build if `corner-full` appears on a rule that is not a
declared square, so a pill cannot quietly regress into a lens again. **The four surviving
uses are all genuine circles**: `MdIconButton` 40×40, `MdCheckbox` 40×40, `MvNumberedList`
24×24, `MvMediaPicker`'s remove badge 28×28. Earlier revisions of this document and of
CLAUDE.md listed "every pill renders as an ellipse" as a live gap; it was fixed on
2026-09-29 and verified by fitting the rendered corner curvature against a circle model
(measured left-edge insets matched an r=40px circle within antialiasing, nowhere near the
75.8px an ellipse would give).

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

**The subset ships the FILLED face (`FILL: 1`) since 2026-09-29.** Material Symbols is a
variable font with a `FILL` axis (0–1) that the subsetter pins (`AXIS_PINS` in
[subset_material_symbols.py](tools/design_system/subset_material_symbols.py)). It had been
pinned at `FILL: 0`, so every glyph in the app rendered **Outlined** while **every V2 glyph
is a solid mark** — a whole-set mismatch, not a per-icon one. Measured amber ink coverage in
the Home feature tiles: V2 **25 / 16 / 33 %**, implementation **8 / 8 / 13 %**, i.e. marks
4–8dp smaller carrying a third to half the ink. Instancing to `FILL: 1` was validated
offline with fontTools before the change: the axis exists, instancing is clean, and
**advance widths are byte-identical (960/960 on every glyph)** — so there is no layout shift
and `MdIconGlyphs.gen.cs` keeps every codepoint (regenerating it changes only its date
comment). **48 of the 95 icons gain ink, up to 4×**: `visibility` 10.5 → 34.5 %,
`location_on` 14.6 → 37.0 %. `wght` (100–700) is the *second* lever and is deliberately
untouched until the FILL result has been looked at on screen.

**Order is load-bearing: `make ds-icons` THEN `make ds-setup`.** The TextCore font asset is
baked from the TTF, so skipping the second step leaves every render showing outlined glyphs
out of the stale atlas.

**What is and is not in the subset — a correction.** `waves`, `surfing`, `scuba_diving` and
`pool` **are** in
[material_symbols_icons.txt](tools/design_system/material_symbols_icons.txt) (lines 103–110,
in the sea/diving block). Earlier revisions of this document, of Decision D7 and of CLAUDE.md
claimed they were "deliberately excluded from the subset so none of them can quietly become
the shipped icon"; that was never true of the font. **The intent holds at the *consumer*
level instead**: both sites that need the Avistamentos glyph read the single constant
`AppIcons.AvistamentosPlaceholder` (= `visibility`) in
[IAppScreen.cs](src/app/MergulhoVirtual/Assets/UI/Navigation/IAppScreen.cs), and the icon
list carries a TODO block naming the four candidates and saying not to substitute one. Fix
the claim, not the file — those glyphs have legitimate uses.

### 3.8 Responsive — dp width, not pixel resolution

V2 is drawn at one width (390dp) and the app was built, rendered and reviewed at that width
alone until 2026-09-29. It is now checked at **five device widths**.

**The mechanism decides what the risk is.** Both `PanelSettings` assets use **Constant
Physical Size @160 dpi** (§1.2), so **1 USS px = 1 dp** on every device and the layout does
**not** scale to fit a different phone: a narrower device simply gets **less width** and
must reflow. The consequence is worth stating flatly — **pixel resolution is a non-issue**
(a 1080p and a 1440p phone of the same physical size get identical layout), and **usable dp
width plus safe-area insets are the entire risk surface**.

**Workflow — no new rendering code.** `UiScreenshotHarness` was already environment-driven:
`MV_SHOT_WIDTH`, `MV_SHOT_HEIGHT`, `MV_SHOT_SCALE`, `MV_SHOT_TOP_INSET`,
`MV_SHOT_BOTTOM_INSET`, `MV_SHOT_DIR`, `MV_SHOT_THEMES`, `MV_SHOT_FILTER`, all read in
`UiScreenshotHarness.ReadOptions()`. All but the last are set in front of `make ds-shots`;
the filter is the exception, because the Makefile assigns `MV_SHOT_FILTER` from `SHOT=`
itself and an inherited value would be overwritten. The device list comes from
`DesktopReviewMode.Presets`
([Assets/Scripts/UI/UiToolkit/DesktopReviewMode.cs](src/app/MergulhoVirtual/Assets/Scripts/UI/UiToolkit/DesktopReviewMode.cs)),
which already existed but only drove a runtime hotkey picker in a Windows review build —
**no screen had ever been rendered at those sizes headlessly**. Output goes to
`.shots-devices/<preset>/`, gitignored on the same rule as `.shots/` and `.arsim/`.

| Preset | dp | Safe-area top / bottom | What it is |
|---|---|---|---|
| Android 360×640 | 360×640 | 24 / 0 | small 16:9 — the stress test |
| Android 360×800 | 360×800 | 24 / 0 | the most common budget device |
| V2 ref 390×844 | 390×844 | 47 / 34 | the design target; what §8 transcribes |
| Pixel 412×915 | 412×915 | 24 / 0 | |
| iPhone 430×932 | 430×932 | 47 / 34 | |

The two 360 presets render **pixel-identically** apart from viewport height, so they are one
result, not two.

**Verdict.**

- **412 and 430 are shippable as-is.** 16dp gutters held on every screen at every width;
  both grids split the extra width exactly evenly; hero overlays, bottom-anchored elements
  and both safe-area configurations are correct. No screen stylesheet contains a fixed-width
  offender.
- **360 needed four fixes**, below. **Nothing overflowed or clipped at any width** — every
  single failure was text reflow.

**The four 360dp fixes.**

1. **`MvHeroHeader` selector pill.** 98dp of fixed chrome left a 170dp label slot at 360
   (200 at 390). Chrome trimmed to **88dp** (inner gutters 16 → 14, pin gap 8 → 6, chevron
   gap 12 → 8) **and** the label taken from V2's 16/600 to **15/600**, giving a 210dp slot
   at 390 and 180dp at 360. Both halves were needed: the trim alone left "Enseada dos
   Tubarões" 2.7dp of clearance at 360 and "Praia da Cacimba do Padre" **0.4dp** at 390 —
   inside the measurement error bar. At 15/600 all 17 `displayName`s fit at 390 with ≥13dp
   to spare and 16 of 17 fit at 360. ⚠️ "Praia da Cacimba do Padre" still ellipsises at 360
   to `Praia da Cacimba do P…`; the visible prefix was checked to be unique among all 17
   names, so it stays identifiable.
2. **`PraiaDetalheScreen` species name row.** `Tubarão-bico-fino` (18/700, 161.6dp) + 8 +
   `Carcharhinus acronotus` (13/400, 147.2dp) = 316.8dp against a 324dp content box at 390
   and **294dp at 360** — and `align-items: flex-end` bottom-aligned the binomial to the
   name's *last* line, stranding "fino" beside the scientific name as if it belonged to it.
   The common name is now pinned whole (`flex-shrink: 0` + `nowrap`) and the binomial —
   decorative, and one tap away in full on the Espécie screen — ellipsises instead, to
   "Carcharhinus acro…" with the genus intact.
3. **`ConditionsFormatter` Maré row.** The space inside the parenthesised height is now
   U+00A0. The label column is a fixed 68dp, leaving the value 242dp at 390 and **212dp at
   360**, while the two strings measure 229.6dp (rising) and 257.0dp (falling) — so the
   falling string already wrapped at 390 and both broke at the space inside "(2.2 m)",
   stranding "m)" alone on line two, which reads as truncation rather than wrap. ⚠️
   **Verified, not assumed**: TextCore's word-wrap save-state test in
   `TextGeneratorParsing` excludes `k_NoBreakSpace` (U+00A0) from break points, all four
   shipped Inter TTFs carry the glyph, and its advance width is identical to U+0020 — so the
   single-line case is byte-identical. Write it as the `\u00A0` escape (`\x00A0m`
   mis-lexes).
4. **Praias "Maré agora" stat.** Value taken from `title-medium` (18/700) to
   **`title-small-increased`** (16/700) **and** an NBSP added after the separator in
   `BeachContentFormatter.TideNow`. The two stat cards split the row, so the value box is
   139dp at 390 and 124dp at 360, while "1.4 m · descendo" measures 147.4dp at 18 and
   131.1dp at 16: at the authored size the falling tide wrapped **even at 390**. No type
   size alone fixes both widths (15/700 would clear 360 by 1.2dp, inside the error bar), and
   content-sizing the cards was out because the sibling stat renders `—` on all 17 beaches.

**Accepted at 360 — not bugs.**

- **The Reportar size-chip row wraps to two rows.** The three chips need 343.4dp; the
  content box is 358 at 390 and **328 at 360**. It degrades gracefully (`flex-wrap` plus the
  deliberate `margin-bottom: -8px`; the page grows 17dp) and loses nothing. Every way to
  close the 15.4dp gap costs more than it buys: chip padding 12 → 10 still wraps
  (331.4 > 328), and 9dp a side would put every chip in the app 3dp off V2's own
  `pad 6/12/6/12`.
- **The Praias location name wraps to two lines** (`white-space: normal`, nothing lost).
- **Home feature-grid icon misalignment widens to ~12.5dp**, caused by the documented,
  deliberate `justify-content: center` over cards whose bodies have unequal line counts.

**⚠️ Two deliberate 390dp deviations from the transcribed spec** — the hero pill label at
**15px** where V2 says 16, and the Praias stat value at **16px** where V2 says 18. Both are
in-ladder sizes, and both fix wraps that were **already present at the 390 design width**,
so they are not purely a 360 concession. Flagged for designer sign-off as **Decision D9**.

**⚠️ One flag that was raised and then retracted — recorded so it is not re-raised.** The
Lua row was reported as having the same dangling-`·` defect as Maré and **does not**. A
greedy line-break simulator (validated against two independently observed renders first)
showed that word wrap takes **the last break that fits, not the first**: "Gibosa Minguante ·
100% iluminada" is 236.1dp and does overflow the 212dp column at 360, but the run up to the
percentage is only 168.9dp, so the break lands *after* "100%" and the separator stays
mid-line. Binding it would be a no-op, and actively harmful if the column ever narrowed (it
would force a line starting with a leading `·`). A comment on `ConditionsFormatter.Moon`
records this. The Onda / Vento / Água rows were cleared at their **maximum emittable**
values, not their fixture values — 122.7 / 91.6 / 43.7dp against the 212dp column, all
bounded by construction (1–3 digits plus a ≤2-character cardinal), so they cannot regress on
content.

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
definition of done. **All nine are now built**: 1–5 and 7–9 landed with Slices 2 and 3,
and `MvStateView` (row 6) landed 2026-09-29 — the table is kept as the record of what
each one is for.

| # | Component | Used by | Notes |
|---|---|---|---|
| 1 | **`MdCheckbox`** | Reportar (behavior list) | Genuine M3 gap in the library. 18×18 box, `radius: 2`, checked = filled `primary` + white check, ≥48dp touch target. |
| 2 | **`MvTag`** (badge/pill) | risk badge, environment tags, "Pendente", "Área de berçário" | Small pill, `radius: 100`, `padding: 4–6 / 10–12`, semantic color variants (success / neutral / on-image). Could be an `MdChip` variant instead — prefer a new component; chips are interactive by contract. |
| 3 | **`MvAlertBar`** | "Salva-vidas: Das 08h às 17h" | Icon + text, `warning-container` fill, `radius: 16`. |
| 4 | **`MvOptionCard`** | Reportar (Turista / Condutor) | Large selectable card, icon over label, selected = 2px `primary` border. |
| 5 | **`MvMediaPicker`** | Reportar (photo grid) | Empty dashed state → 104×104 thumb grid with remove buttons + "add more" tile. |
| 6 | **`MvStateView`** | error / offline / empty | Illustration + title + body + action button. Drives frames `79:1304` / `81:1403`. **Built 2026-09-29** (component + USS + gallery + tests) and wired into the Início conditions card and the Avistamentos failed rows — see Slice 5. Its **illustration slot ships empty**: V2's line-art shark is a raster in the Figma file with no export in this repo, so `SetIllustration` is ready and nothing is substituted for it until the designer supplies the asset. |
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

> **Status, 2026-09-29 — Slices 0, 1, 2, 3, 4 and 6 are done and green. Slice 5 is half
> done: the `MvStateView` half shipped; the SOS half has not started and stays blocked on
> Decision D3.** Headless verification at that date:
> `make ds-test` **683/683**, `make ds-test-play` **27/27**, `make ds-shots` **98 PNGs, 0 skipped**,
> plus 49 subjects × 5 device presets into `.shots-devices/` (§3.8). **Nothing has been run on a device**, so
> every claim below is an editor claim. See [docs/handover-v2-redesign.md](docs/handover-v2-redesign.md)
> for the working state, the known gaps and the device-verification checklist.
>
> The strangler *mechanism* changed shape in Slice 1 and no longer matches the paragraph
> above: there is no per-screen `ScreenManager` toggle any more. `ScreenManager` is only the
> AR performance gate and `MdRouter` owns navigation. The superseded uGUI screens (`BottomNav`,
> `BeachesScreen`, `AnimalsScreen`, `RegisterScreen`) spent one slice in `MainScene`
> deactivated and unrouted, and **Slice 6 deleted them**. `AboutScreen` is still routed through
> `LegacyUguiScreen` (D2), and `MainScreen` is kept alive on the Mergulho route by
> `AppUiHost.OnRouteChanged` for the ArTuning panel alone — **and that panel was removed on
> 2026-09-29**, so `MainScreen` is now an empty shell and the toggle is vestigial.

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

### Slice 4 — Mergulho (AR HUD) — ✅ **done (editor); ⛔ the device check is still owed**
- Hero top bar (back + beach pill + dropdown) and the species info card (Tela 8) as a UITK
  overlay on the AR camera.
- Keep `ObjectInteraction` as the hit source; only the presentation moves.
- ⚠️ A UITK `UIDocument` over the AR feed needs a transparent root and correct sort order
  against the existing uGUI canvas — the same constraint `BeachesScreen` already solves with
  a transparent, `PickingMode.Ignore` root.
- **Done when:** tapping a shark opens the card on device; AR tracking unaffected.
- **As built:** `Assets/UI/Screens/MergulhoScreen.cs` + `.uss`, `MergulhoViewModel`,
  `SpeciesCardFormatter`, and a new `IArSelection` interface adapted to `ObjectInteraction` by
  `UiServiceAdapters.ArSelectionAdapter`. The screen paints nothing but its two surfaces (the
  `MvHeroHeader` control strip and the bottom-anchored card); everything else is transparent
  and `PickingMode.Ignore`, so taps fall through to the AR scene.
- **Three things the hit path needed that had never existed.** `ObjectInteraction` was not in
  `MainScene` at all, its `targetName` ("Tubarão Martelo") matched no object the spawner ever
  creates, and the five shark Prefab Variants carry **no colliders**. So tap-to-info had never
  worked in any form. It now resolves the hit through a new `ArSpeciesTarget` component that
  `BeachSharkSpawner` attaches (with a fitted `BoxCollider`) to every instance it spawns, keyed
  by the prefab name — which is already the AnimalDef asset name. `AppUiBuilder` puts the
  component on `Beach Shark Spawner`.
- **`AnimalDef` grew `approximateSize` / `diet` / `behaviour`** for the card's three spec rows,
  mirrored onto `SpeciesInfo`. **All three are blank on all five assets** (Decision D8) and the
  card drops a row it has no value for, so the shipped card is name + binomial until someone
  authors them.
- **`ScreenUI/MainScreen` is no longer a routed screen**; `AppUiBuilder` deactivates its
  `TopBar` (the presentation this slice replaced) and `AppUiHost.OnRouteChanged` keeps the rest
  of the subtree alive on the Mergulho route, for the uGUI **ArTuning** pill + panel alone.
  *(ArTuning was subsequently removed on 2026-09-29, leaving `MainScreen` empty.)*
  That retires `ConditionsPillView` — V2's Tela 8 has no conditions pill, the legacy bar has no
  safe-area awareness and overlaps the new strip, and its content is the Início card's.
- ⚠️ **The device check this slice was built on was never done**: the shell panel draws over the
  AR HUD at `AppUiBuilder.PanelSortingOrder = 100` with a transparent `PickingMode.Ignore` root,
  and nobody has confirmed on hardware that the nav bar draws above the camera and stays
  tappable, that taps reach the sharks through the panel, or that a tap opens the card at all.

### Slice 5 — SOS + states — 🟡 **half done: states shipped 2026-09-29; SOS still blocked by D3**
- `MvStateView`; SOS screen with real numbers (`Application.OpenURL("tel:…")`).
- Wire error/offline states into the Instagram widget, conditions fetch, and sighting upload.
- **States: done.** `MvStateView` is built to the DS definition of done (component + USS +
  gallery section + tests, §6 row 6) and wired into two real failures: the **Início conditions
  card**, which swaps the rows for an error/offline state with a retry that actually re-fetches,
  and the **Avistamentos pending feed**, where a permanently-failed report gets a "Tentar
  novamente" action backed by the new `JobQueue.Requeue`. Its **illustration slot is empty** —
  the designer owes V2's line art; `SetIllustration` is ready and nothing is invented for it.
  The Instagram widget is still uGUI (`AboutScreen`, D2) and was not touched.
- **SOS: not started, and nothing SOS-shaped exists.** `AppRoutes.Sos` already exists and is
  raised by the Início SOS tile and the floating SOS pill on Praia detalhe; with no screen
  registered, `MdRouter` logs one warning and does nothing. Decision **D3** blocks it outright —
  no numbers and no vetted first-aid copy may be invented.

### Slice 6 — Retire uGUI — ✅ **done 2026-09-29 (except `MainScreen`, deliberately)**
- Delete legacy screens; resolve Animais/Sobre per D1/D2; drop `ScreenManager`'s screen
  fields; remove the now-dead uGUI builders.
- **As built.** `ScreenUI/BottomNav`, `BeachesScreen`, `RegisterScreen` and `AnimalsScreen`
  were deleted from `MainScene`, together with `BeachesScreenController`,
  `BeachSelectorDropdown`, `RegisterScreenController`, `AnimalsScreenController`,
  `AnimalViewerInput`, `RegisterScreenBuilder`, `AnimalsScreenBuilder` and the components
  whose only user was one of those objects (`ListItemView` + `Assets/Prefabs/UI/ListItem.prefab`,
  `ConditionsCardView`, `TideSparkline`, `MaxWidthClamp`, `VideoSection` + `VideoSectionBuilder`).
  `AppUiBuilder.UnroutedLegacyScreens` went with them. `ScreenManager`'s screen fields had
  already gone in Slice 1.
- **Four things that look dead and were kept**, each with a live consumer: `AnimalViewerRig`
  (rendered by `EspecieScreen` via `SpeciesModelViewerAdapter`; its construction was extracted
  into the new `AnimalViewerRigBuilder.cs` so it stays reproducible), `VideoPlayerController` +
  `PointerHeldFlag` + `VideoRef` (the About screen's Instagram card), `AspectCover` (Splash +
  About), and `UI/RoundedRect` + `RoundedRectCard.mat` (the Instagram card + ArTuning).
- **`ScreenUI` now holds only `Panel`, `SplashScreen`, `MainScreen` and `AboutScreen`.**
  `MainScreen` was deliberately NOT retired *in this slice*: it still hosted **ArTuning**, the
  on-beach panel that tuned the AR stabilisation filters live. That decision was taken on
  2026-09-29 — **drop, not port**: ArTuning and its JSON persistence were deleted, so
  `MainScreen` is now an empty shell (one deactivated `TopBar`) whose own deletion is unblocked
  but not yet done. Sobre stays on `AboutScreen` per D2.
- Verified headless **at that point** (the banner above carries the current numbers):
  `ds-compile` clean, `ds-test` **618/618**, `ds-test-play` **26/26**,
  `ds-shots` **92 PNGs / 0 skipped** with 91 of 92 byte-identical to the pre-deletion run
  (the 92nd is the indeterminate progress indicator's animation phase). No
  `m_Script: {fileID: 0}` anywhere in `MainScene.unity`.

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

**As built — six places the transcription above is not what ships**, each corrected against
the render on 2026-09-29:

- **The conditions card's real content inset is 40dp, not the declared 12.** The frame says
  `pad:12/12/12/12`, but it is `xCENTER` and **every child is 310dp wide inside a 358dp
  box** (`Hoje` 310×18, `image 13` 310×181 — node `29:2154`), so the design's content
  actually starts 16 (page gutter) + 24 = **40dp** from the screen edge. Transcribing the
  declared 12 alone hugged the text to the card's border, uniquely on this card. The screen
  authors `padding: 12px 24px` — 12 top/bottom is correct as declared.
- **Header margin 6 → 12.** The measured ink-to-ink gap from "Hoje" down to the "Onda" row
  came out at 11.5dp against the frame's 20.5; the heading read as part of the row block
  rather than as a heading over it. The sparkline's `flex-grow` absorbs the difference, so
  the card still lands on 234.
- **Two 48dp touch targets.** The title group ("Hoje" + chevron) is the live navigation into
  Praias and measured ~21.5dp; the tide link measured its own 14dp line box. The link is
  **two elements** for this reason: V2 underlines it, UI Toolkit has no `text-decoration`,
  so the rule is a 1px bottom border on the label's own box — any height beyond the line box
  pushes the rule off the text. The label keeps its box and an otherwise-invisible wrapper
  carries the height and the `Clickable`.
- **Chevron 18 → 26px.** Material Symbols' em box carries padding the Figma vector does not,
  so an 18px glyph draws 5.5×9dp of ink where V2's chevron is 7.5×12.
- **The chart ink is `on-primary`, not `inverse-primary`.** `inverse-primary` is a
  stock-M3 pale blue (`#B9C7E0`) that appears nowhere in Appendix A.1; every vector mark the
  designer drew on this card is white or amber.
- **Every alpha on this card is linear-space corrected** (Appendix B): sparkline fill
  0.14 → **0.025**, baseline 0.4 → **0.19**, the "Atualizado:" line 0.7 → **0.49**, and the
  `MvStateView` the card swaps in on a failed fetch 0.5 → **0.27** (icon) / 0.75 → **0.57**
  (body). The sparkline's **label** alpha 0.75 is deliberately left alone — it renders
  `0xE1E1E2` where the Figma bitmap's labels peak at `0xE5ECF2`, so the render already *is*
  the design and correcting it would move away from V2.

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

**As built:**

- **The CTA button needs `align-self: center` on the screen side.** `MdButton.uss` declares
  `align-self: flex-start` on its own root — deliberately, a button must not stretch — and a
  child's `align-self` beats the parent's `align-items`, so the 48dp target was pinned to
  the top of the 71dp content box, leaving the visible pill `(71 − 48) / 2 = 11.5dp` above
  centre. Fixed here rather than in the component, which is right for every other button.
- **The "Maré agora" value is 16/700, not V2's 18/700**, and its separator is bound with a
  no-break space. Both are §3.8 fixes and both also fix a wrap that was already present at
  the 390 design width; the size is Decision **D9**, awaiting sign-off.
- **The location name wraps to two lines at 360dp** and that is accepted — nothing is lost.

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
It needed real work to be visible at all: it was drawing `surface-container` (`#F3F4F6`) on
a `background` (`#F9FAFB`) page with no border and no shadow, so it read as a faint smudge.
It is now `surface` + a 1px `outline-variant`, with a real scrim (`.md-menu__scrim` was
styled in **no** `.uss` file in the project), item labels at 14/500 ls 0, and the selected
row on `surface-container-high` rather than `secondary-container` — Tela 9 draws no amber
row.

**As built, four deviations from the transcription above:**

- **The species name row does not wrap; the binomial ellipsises.** See §3.8 fix 2 for the
  measurements. The cost of pinning the name is that a future common name wider than ~286dp
  would overflow rather than wrap; the longest today is 161.6dp, so there is ~124dp of
  headroom, and if it is ever spent the row should gain `flex-wrap: wrap` rather than get
  `white-space: normal` back.
- **The binomial is `body-medium` italic (13/400 slanted), not the frame's upright 12/400.**
  A user reaches the same datum on this card, on the AR species card and on the Espécie page
  within one tap, and a scientific name that changes size and slant on the way reads as
  three different kinds of thing. The slant is synthesised by TextCore — the shipped Inter
  set has no italic face.
- **"Saiba mais sobre a espécie" carries a 48dp target.** It measured ~19dp and is the only
  route to the Espécie screen. The height is taken with `min-height: 48px` plus
  `margin-top: -17px`, so the *visible* gap to the behaviour paragraph is still the design's
  12dp (−17 + 29 = 12); the target overlaps the paragraph's last line, which is inert text.
- **The photo credit has no `opacity`.** The 0.8 that used to be there measured **3.29:1**,
  under WCAG AA's 4.5:1; `on-surface-variant` at 11px measures **4.90:1** on the card. The
  hero selector pill's chrome and label size also changed — §3.8 fix 1.

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
`AnimalDef` now has `approximateSize`, `diet` and `behaviour` (Slice 4) — all blank on every
asset, so the card renders name + binomial and drops the spec table until they are authored.
The card is bottom-anchored above the nav bar, not a bottom sheet.

Two things the struct dump does not surface and the implementation therefore decided
explicitly: the card is **opacity 0.77 + a 24dp `BACKGROUND_BLUR`** in Figma (§3.6 calls it an
opaque fill, which the file contradicts) — UI Toolkit has no backdrop filter and 77% over an
*unblurred* camera is worse than either, so it ships opaque; and the binomial is **italic**,
which it now is. The hero controls are `#3A3F45` with a `#FFFFFF@0.125` hairline here, not the
white pill of Tela 4 — same `MvHeroHeader`, restyled by the screen through the new `camera-*`
extension tokens in `Tokens/_brand-*.uss` (identical in both themes: the backdrop is a lens,
not a theme).

**Three more things this screen settled on 2026-09-29:**

- **The three spec labels are baked UPPERCASE** in `SpeciesCardFormatter`
  (`TAMANHO APROX.` / `DIETA` / `COMPORTAMENTO`). The Figma text nodes are title case under
  a text-case transform, and the transform is the thing the reader sees; UI Toolkit has no
  `text-transform`, and a screen may not manufacture a user-visible string — every one of
  them lives in a formatter and is pinned by a test — so the case is baked into the constant. The 0.5px tracking on these labels is an
  all-caps device and only reads correctly against all-caps text. `ReportScreen`'s
  `"PENDENTE"` pill was settled the same way — the render wins over prose.
- **Every hairline on the card is now a distinct pre-blended token**, because one Figma
  alpha sat on three different opaque backdrops: `camera-outline` (white @0.15 over the card
  fill) for the card's own edge, `camera-outline-dim` (white @0.13 over the lighter control
  surface) for the pill and back button, `camera-divider` (white @0.13 over the card fill)
  for the card's internal rule — which had been borrowing `camera-outline-dim` and rendering
  **four times brighter than V2** — and `camera-fill` / `camera-fill-outline` for the close
  disc and its edge. §3.2 has the table; Appendix B has the reason.
- ⚠️ **AR taps used to pass straight through the UI.** `ObjectInteraction` read
  `Pointer.current` raw, with no `EventSystem` check and no UI Toolkit panel pick, so a tap
  on the species card, on its ⨯, or on any row of the open beach menu **also** fired the
  0.2 m `SphereCast` behind it — the ⨯ could close the card and immediately re-open it. It
  now panel-picks via `RuntimePanelUtils.ScreenToPanel` + `panel.Pick` before raycasting,
  through an optional `[SerializeField] UIDocument uiPanelSource` that `AppUiBuilder` wires
  (falling back to a cached `FindAnyObjectByType`). Taps on open water still reach the AR
  scene, because the HUD root, the hero strip and the dock are `PickingMode.Ignore` by
  design — which is exactly why a pick test, not a blanket "is the pointer over UI" test,
  was the right answer.

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

**As built:**

- **Chips.** `MdChip`'s selected state was `secondary-container` — pale amber — where V2 and
  the M3 spec both say navy `#152336` on white; it is now `primary` / `on-primary`.
  Horizontal padding went 16 → **12** (the Figma state layer is `pad:6/12/6/12`) and the
  radius `corner-small` (10) → **`corner-extra-small`** (8, V2's chip rung). ⚠️ The
  component was also **injecting a check glyph into any selected chip regardless of
  `Kind`**, contradicting its own doc comment — so an *Assist* chip grew **18.5dp on tap**
  and re-flowed the row under the user's finger. The injection is now gated on
  `Kind == Filter`; the old asymmetric `9/17` padding pair that silently assumed "selected
  ⇒ has icon" was re-derived as a symmetric `13/13` no-icon pair plus a
  higher-specificity `.md-chip--selected.md-chip--with-icon` rule, so source order no longer
  decides the result.
- **The whole behaviour row is the toggle**, not just the checkbox — only the 48dp box at
  the far right answered before. The row's right padding is **1px**, not the frame's 14:
  `MdCheckbox` is a 48dp root centring an 18dp box, so `(48 − 18) / 2 = 15dp` of that side
  is already empty and `1 + 15` lands the box's edge at the frame's 16dp from the card edge.
- **"(opcional)" is its own muted 12/400 label** beside the 15/700 heading, in a row with
  `align-items: flex-end` (UI Toolkit has no baseline alignment; flushing the bottoms is the
  closest approximation of the shared baseline Figma draws). ⚠️ **That split caused a
  regression the fully green suite did not catch**: the title became a flex-row child,
  inherited `flex-shrink: 1` against its base `white-space: normal`, wrapped "Se
  identifique" onto two lines, and collided with the "Seu nome" field below. Both labels are
  now pinned `flex-shrink: 0` + `white-space: nowrap` — **the house pattern for this
  failure**, and the same one `PraiaDetalheScreen`'s species row needed.
- **The pending pill reads `"PENDENTE"`.** The Figma renders uppercase and the prose above
  says "Pendente"; the render wins, as it does for the AR spec labels (§8.4).
- **The pending feed is re-rendered on the host's 60s freshness tick.**
  `ISightingReports.Changed` fires only on `Success` and `PermanentFailure` — **never on a
  transient retry** — so without the tick a retrying row's state, attempt count and
  "Tentando de novo" / "Sem conexão" label stayed frozen at whatever `OnEnter` computed, and
  a working queue read as a stuck one. The cost is that `ListPending`/`ListFailed` become a
  polled path, roughly once a minute while that tab is open.
- **The size-chip row wraps to two rows at 360dp** and that is accepted — §3.8.

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

### 8.8 Espécie — **no frame; designed, not transcribed**

The one screen in the app with **no Figma source**. §1.1 has no species page, and Decision
D1 settles only *where* the Animais content goes, not what it looks like. Built 2026-09-29
against the rules "reuse Praia detalhe's idioms, invent no new visual pattern, invent no
content". **Everything below is a proposal awaiting the designer**, and the fastest way to
review it is `make ds-shots SHOT=especie` (six PNGs: `especie`, `especie-no-video`,
`especie-playing`, `shell-especie`, each light + dark).

Structure, top to bottom, every block gated on presence exactly as §8.3 is:
`MvHeroHeader` **compact (240dp)** + back button, no selector · common name (24/700) over
the binomial (13/500 italic) · description card · spec card (the §8.4 rows, via the same
`SpeciesCardFormatter`) · **"Modelo 3D"** — 260dp viewport + a gesture hint · **"Vídeos"** —
one 200dp poster card per clip, tap to play, amber seek track, `m:ss / m:ss` · photo and
model credits as muted footer text, no card and no heading.

Measured over the five shipped `AnimalDef`s: name, binomial, photo, description and both
credits are filled for **all five**; the 3D model for **all five**; the three spec rows for
**none** (D8); videos for **one** (lemon_shark). So the honest common page is hero → name →
description → 3D model → credits.

Of the two content defects that measurement found, **one is fixed and one is not.** All
five `modelCredit`s were normalised on 2026-09-29 to the one-line
`Modelo: <author> / <licence> (<source>)` shape the existing `photoCredit`s already used —
`hammerhead`'s had been holding the model's full ~180-word Sketchfab **description** (camera
rigs, Blender, a PIT tag number, funders) across 20 YAML lines and rendering verbatim as the
**largest element on the page**. No author name or licence identifier was lost, and no
`.asset` was renamed (the file name is the species key). Still open:
**lemon_shark's two `videos` entries point at the same URL**, so the one species with videos
shows two cards playing identical content. ⚠️ Normalising the credits also surfaced a
licensing question that is not a UI matter: **`hammerhead` and `reef_shark` are CC BY-NC**,
which is a problem if the app is ever distributed commercially, however the credit is
worded.

**Five implementation findings from the 2026-09-29 pass:**

- **Credits carry no `opacity`.** The 0.8 that used to be there measured **2.68:1** against
  **4.65:1** without it, on a 14-line block — and attribution is a CC BY-SA *licence
  condition*, not decoration, so dimming it is the wrong instinct twice over. The same
  change was made to Praia detalhe's photo credit (3.29:1 → 4.90:1).
- **The pause affordance stays on screen while a clip plays.** The overlay used to be hidden
  entirely on play, which left a playing card with **zero controls** and meant
  `SpeciesMediaFormatter`'s "Pausar" / `pause` output — produced for exactly this state —
  was never drawn. It now stays up at 0.8 opacity: the media surface *is* the pause target,
  and a target with nothing on it is one the user has to guess at.
- **The seek track toggles `visibility`, not `display`.** Toggling `display` made the 14dp
  strip appear on play and vanish on stop, reflowing the title, the clock, the next card and
  the credits **under the finger that had just tapped**. A hidden element is not picked, so
  dragging a non-playing card's track still does nothing. The track is 14dp of touch over a
  3dp rule — a 3dp seek target is unhittable with a thumb.
- **The video well is `surface-container-high`, not `camera-surface-dim`**, and the overlay
  carries an `--over-frame` state class that flips the action glyph and label `primary` ↔
  white. The overlay has **two possible backdrops** — the light empty well and an arbitrary
  decoded video frame — which need opposite ink and which USS cannot tell apart, so the
  screen says which is underneath and the stylesheet owns every value. The hero scrim is
  also suppressed here: the species photo is the subject, not a backdrop for controls.
- **The 3D viewport fits whichever axis binds.** The port from the uGUI viewer fitted the
  animal's **longest** dimension against `Camera.fieldOfView`, which is the **vertical**
  field — the short axis of a 358×260 viewport. A 4 m shark lying horizontally therefore
  landed at the distance where its length filled the *height*, covering ~63 % of the width
  and ~22 % of the height, i.e. **~14 % of the box**, and no amount of viewport height would
  have fixed it because the mismatch is structural. Fitting both axes and taking the larger
  distance frames it at ~84 % of the width. Two companion fixes: `SetViewportSize` now
  re-fits when a model is already mounted, and `UnmountModel` resets the cached viewport
  size — without which the **second** visit to a species showed the placeholder instead of
  the model.

**Entry point carries a payload.** `PraiaDetalheScreen.SpeciesRequested` raises the species
key (the `AnimalDef` asset name); `AppUiHost` sets `EspecieViewModel.ShowSpecies` and only
then pushes, and does not navigate at all when the key is unknown. A bare `AppRoutes.Especie`
could not say *which* species. The §4 second entry point — the AR info card — is **not**
wired: Tela 8 draws no such link and adding one to a transcribed screen is a design decision,
not an implementation one.

Two decisions a designer should overrule if they disagree: the **compact hero** (340dp would
cover-crop ~40% of the width off a Wikipedia lead shot, i.e. the animal's head or tail), and
the **200dp video poster** (the clips are vertical reels, so any landscape poster letterboxes;
this is the compromise). Both are called out in the screen's `.uss`.

### 8.9 Conteúdo educativo — **not a V2 screen at all**

**This is not a slice and it does not have a number.** §7's slices are the V2 rebuild; the
educational article library is a **new feature that postdates the prototype**, so §1.1 has no
frame for it, §2's inventory lists it as "not in V2", and nothing here was transcribed from
anything. It is recorded in this file only because it added **two more UI Toolkit screens to
the same shell** — and therefore two more surfaces a designer has never seen. The feature
itself (the Markdown → block-JSON pipeline, the escaping rule, the loader's degradation, the
`@especie`/`@praia` key traps) is documented in **CLAUDE.md → Architecture → Educational
article library**, not here; this section is only the design status.

Built 2026-09-30: `ArticlesScreen` (`AppRoutes.Conteudos`, the index) and `ArticleScreen`
(`AppRoutes.Conteudo`, the reader), both **sub-screens** pushed from a full-width card on
Início — the bottom bar's four destinations are fixed, so this is the D2 arrangement again,
not a fifth tab. §4's navigation restructure is unchanged.

**Everything visual in both is a proposal awaiting the designer**, built under §8.8's rules:
reuse the idioms Início and Praia detalhe already established (16dp gutters, white outlined
cards on the off-white page, `title-medium` section headings, muted `label-small` credits),
invent no new visual pattern, and invent no copy (every string comes from a formatter). The
review is `make ds-shots SHOT=conteudo` — the filter is a substring match, so that one command
catches all five subjects, **ten PNGs, and they are not a comparison, they ARE the design
review**: the index, three articles chosen because no single one exercises every block type
(the five-in-a-row species references; the no-hero path; the
numbered-list/callout/beachRef/uncaptioned-figure set), and `shell-conteudos` — the index with
the nav bar up, which is what a pushed sub-screen actually looks like — each light+dark.

Three things a designer may well overrule, all of them called out at the code:

1. **The index card shows a full photo credit under every cover.** A credit is a *licence
   condition* wherever its photo appears, so it rides with the cover — but in a browse list of
   four covers it is visibly noisy. ⚠️ **This is a licence question, not a styling one**: the
   fixes that look obvious are all forbidden (credits never take `opacity`, and shortening one
   drops the author or the licence identifier that satisfies the condition). The real options
   are keep it, shrink it further, or drop covers from the index. **Open.**
2. **The index's back affordance is an `MdIconButton`, not an `MvHeroHeader`** — an index has
   no cover photo, and a hero with no image is a 240–340dp empty box (its foreground is
   absolutely positioned, so it cannot collapse to content). The reader has the same problem
   for the one shipped article with no `hero` and solves it the same way, with a plain
   back-button row.
3. **The chrome copy is invented**, and flagged `invented` at each declaration in
   `ArticleFormatter` / `StateViewCopy`: the index title and subtitle, the four callout tone
   labels ("Informação" / "Atenção" / "Recomendação" / "Perigo"), the quote dash, the
   beach-reference label, and both empty/error states. Every one is a one-liner to change.
   The reading estimate's 200 wpm is an assumption, not a measurement of this audience.

⚠️ **The four shipped articles are placeholder content written by an implementer, not a
biologist** — fixtures so every block type has a case, deliberately carrying no statistics, no
dates, no identification claims and **no safety or first-aid instructions** (which keeps them
clear of D3). They must be replaced with reviewed text, or deleted, before release. **Each of the four opens
with an `error` callout saying so, in the app** (added 2026-09-30 — a render review found the
disclaimer on only one of the four; the other three carried it as a source comment the build
never emits).

One icon was added to the subset for this feature: **`menu_book`**, for the Início card and the
index header — so `material_symbols_icons.txt` now carries **96** names, and §3.7's "48 of the
95 icons gain ink" measurement was taken before it (the +1 changes none of that, since advance
widths are identical per glyph). Nothing in the existing 95 reads as "articles to read", and
D7's rule against a plausible-but-wrong substitute applies generally, not only to the shark
fin.

---

## 9. Verification

Per slice, in this order — every `make` target below is headless and **requires the editor
closed**:

```bash
make ds-icons        # only if material_symbols_icons.txt or AXIS_PINS changed…
make ds-setup        # …and then ALWAYS this: the font asset is baked from the TTF (§3.7)
make ds-compile      # imports + compiles
make ds-test         # EditMode: TokenDiscipline + component + ViewModel + AR suites
make ds-test-play    # PlayMode: interaction suites
make ds-shots        # render every screen + gallery section to .shots/ (needs a DISPLAY)
```

**Current state (2026-09-29, last recorded headless run in `Logs/ds-test-*.xml`):
683/683 EditMode, 27/27 PlayMode, `ds-shots` 98 PNGs / 0 skipped.** The EditMode progression
over the 2026-09-29 fidelity pass is **658 → 683** (+25); PlayMode reads 27 both before and
after, but its *composition* changed — `StadiumCornersTests` is new (§3.5) — so the equal
total is a coincidence, not a no-op.

`ds-test` covers three assemblies — `MergulhoVirtual.DesignSystem.Tests.Editor`,
`MergulhoVirtual.UI.Tests.Editor` and `Assembly-CSharp-Editor` (the job-queue, geocoding and
AR-stabilisation suites) — so one command covers the whole tree.

⚠️ **To read the count back out of the result file, count `test-case` elements** —
`grep -c "<test-case " Logs/ds-test-editmode.xml`, or read `total=` / `passed=` off the root
`<test-run>`. **Do not grep `result="Passed"`**: NUnit writes that attribute on every
`<test-suite>` rollup and on `<test-run>` itself as well as on each case, so the naive grep
over-counts by the number of fixtures plus one (683 cases + 65 suites + 1 run = 749, a number
that was briefly and wrongly reported as a test count).

**Then the multi-device render** (§3.8). The harness is environment-driven, so this needs no
new code and no new target — set the preset in front of the existing one, per device:

```bash
MV_SHOT_WIDTH=360 MV_SHOT_HEIGHT=640 MV_SHOT_TOP_INSET=24 MV_SHOT_BOTTOM_INSET=0 \
MV_SHOT_DIR=.shots-devices/360x640 MV_SHOT_THEMES=light make ds-shots
```

The five presets are in `DesktopReviewMode.Presets`; 360×640 is the stress test and 390×844
is the design reference. **Look at the PNGs** — which is not a formality:

- ⚠️ **A fully green suite coexisted with a visibly broken screen.** The "Se identifique"
  collision (§8.5) passed every test in the suite. Structure tests assert that elements exist
  and carry the right classes; they cannot see layout. **Render and look** after any change
  to flex direction, typography or wrapping.
- **Gallery first for component bugs.** Reproduce a visual bug in `GalleryScene` before
  touching a screen — if it repros there it is a component bug (fix + regression test); if
  not it is screen wiring.
- **Device build.** Light-on-white contrast on a real panel, safe-area insets, and the AR
  overlay's stacking and hit-testing cannot be judged in the editor at all. Build an Android
  Development Build and read Android Logcat.
- New ViewModels get EditMode tests as plain C# with fake services — keep the
  `BeachesViewModelTests` pattern, including injected `utcNow`/`toLocalTime` so tests stay
  timezone-proof.

Two gotchas that will cost a run each:

- **A `#RRGGBB` literal in a USS *comment* fails `TokenDisciplineTests`** exactly like a real
  declaration — its `HexColor` regex scans raw file text and strips no comments. Write hex in
  comments as `0xRRGGBB`; the stylesheets already do.
- **`ds-setup` and `ds-shots` rewrite the LFS-tracked font assets**
  (`Assets/DesignSystem/Fonts/*.asset`) because rendering new glyphs grows the SDF atlas.
  Expect them dirty; they are regenerated output, not edits.

---

## 10. Open decisions

These change what gets built and were not mine to make. **D1–D8 were resolved by the
maintainer and are reflected in the code as of 2026-09-28, except D3, which is still open**;
the column below records the decision that was actually taken, not the recommendation that
was offered. **D9 and D10 are new, opened by the 2026-09-29 fidelity pass**, and both need
the designer rather than the maintainer.

| # | Decision | Why it matters | Resolved (2026-09-28) |
|---|---|---|---|
| ✅ **D1** | Where do **Animais** + the 3D viewer + inline videos go? | Lose their tab in V2, but the subsystem works and is shipped. | **Sub-screen from the species cards; the standalone list is dropped.** **Built 2026-09-29** as `Assets/UI/Screens/EspecieScreen.cs` + `EspecieViewModel` + `SpeciesMediaFormatter`, registered at `AppRoutes.Especie`. The 3D turntable and the inline videos came with it, behind two new interfaces (`ISpeciesModelViewer`, `IVideoPlayback`) adapted to the existing `AnimalViewerRig` and a `VideoPlayer` by `UiServiceAdapters`. The "Saiba mais sobre a espécie" link now raises a **species key** (`PraiaDetalheScreen.SpeciesRequested`), not a bare route — see §8.8. `AnimalsScreen` was deleted in Slice 6, along with its controller, its viewer input and its builder; the `AnimalViewerRig` it used to own was kept and now has its own `AnimalViewerRigBuilder`. ⚠️ **The screen has no Figma frame and was designed, not transcribed — it needs designer review.** |
| ✅ **D2** | Where does **Sobre** + the Instagram widget go? | Lost its tab; the widget shipped 2026-07-17. | **A Home entry** — a full-width card below the 2×2 grid (not in the Figma frame), pushing `AppRoutes.Sobre`, which routes to the **legacy uGUI AboutScreen** via `LegacyUguiScreen`. That keeps the shipped Instagram widget reachable with no rebuild. ⚠️ It has no back button and the nav bar cannot return to the tab it was pushed from in one tap — see the handover doc. |
| ⛔ **D3** | Are the **SOS** numbers and first-aid content real? | Wrong emergency numbers are actively dangerous. | **STILL OPEN. This blocks Slice 5 entirely** — nothing SOS-shaped may ship until the project/ICMBio vets the numbers and the first-aid copy. The SOS entry points exist and are deliberately inert. |
| ✅ **D4** | Normalize beach names to Portuguese, or add `displayName`? | `places.json` mixes "Sueste Beach" / "Praia do Sancho"; V2 shows "Baía do Sueste". Keys must match exactly across content, spawner, seed data and the backend `local` field. | **`displayName` added to `places.json`; the machine `name` stays the key** (spawner list, `beaches_content.json` keys, the backend `local` field). All 17 places carry one; 5 differ from the key. `BeachInfo.DisplayName` falls back to `Name` when blank. Never look anything up by `displayName`. |
| ✅ **D5** | Light-only, or keep a dark theme? | V2 specifies light only; the DS requires both token sets to exist. | **Ship light.** `AppPanelSettings.themeStyleSheet = Theme-Light`. Dark exists (both token sets are required and parity-tested) and the gallery keeps its toggle, but no screen was designed for it — `make ds-shots` renders dark shots only as a token-discipline canary. |
| ✅ **D6** | **Multi-photo** upload — in scope? | Changes the blob layout, idempotency, and the Firestore shape. Not additive. | **Single photo this slice; the grid UI is capped at 1** (`ReportViewModel.MaxPhotos = 1` → `MvMediaPicker.MaxItems`). Multi-photo is its own future slice. Replacing a photo is remove-then-add. |
| ✅ **D7** | Can the designer export the **icon set** (esp. the shark fin)? | Flaticon glyphs; the fin has no Material Symbols equivalent. | **Option (a) now: Material Symbols everywhere. The shark fin is still outstanding.** It is stood in for by `AppIcons.AvistamentosPlaceholder = "visibility"`, rendered by both the bottom-bar Avistamentos tab and the Início Avistamentos tile; a third stand-in (`"help"`) sits in the gallery's nav-bar demo. All three must move together when the SVG arrives. ⚠️ **Correction (2026-09-29):** earlier revisions of this cell said the plausible-looking marine glyphs (`waves`, `surfing`, `scuba_diving`, `pool`) were "deliberately excluded from the icon subset". They are **not** — all four ship, in the sea/diving block of `material_symbols_icons.txt` (lines 103–110), and they have legitimate uses. What actually prevents one becoming the Avistamentos icon is the single shared constant plus the TODO block in the icon list; the guarantee is at the consumer, not in the font. Separately, the subset now pins `FILL: 1` (§3.7), so every glyph in the app is the filled face V2 draws. |
| ✅ **D8** | Who authors `beaches_content.json` for **17 beaches**? | Risk, season, tips, species, lifeguard hours × 17. Blocks Slice 2's "done". | **The biologists / project team, via [docs/beaches-content-todo.md](docs/beaches-content-todo.md).** The schema shipped with the content left blank: a research-and-cite pass was offered and **declined** — an invented risk level or lifeguard hour is worse than a blank, and the screens are built to hide a missing block rather than print a placeholder. Slice 2 is code-complete and content-empty until that file is filled. |
| ⛔ **D9** | Two **type sizes below the transcribed spec** — sign off or overrule. | The hero beach-selector label and the Praias "Maré agora" value are the two places where the implementation renders a smaller size than V2 specifies. | **OPEN, needs the designer.** The selector pill label is **15/600** (V2: 16/600) and the Praias stat value **16/700** (V2: 18/700). Both are in-ladder sizes; both were taken down one rung because at the authored size a string wrapped or truncated **at the 390dp design width**, not merely on a narrow phone — V2's own catalogue never fitted the pill it was drawn in ("Praia da Cacimba do Padre" measures 209.6dp at 16/600 in a 200dp slot), and "1.4 m · descendo" wrapped the stat card at 18/700. The alternatives are all worse or bigger: shrink the chrome further (already trimmed 10dp), shorten the `displayName`s, or redesign the two cards. Measurements and the alternatives considered are in §3.8. |
| ⛔ **D10** | What is the **`MvStateView` status glyph's actual colour** in V2? | It is the one remaining alpha in the app whose correction cannot be decided from the evidence in the repo. | **OPEN, needs the designer's swatch.** The glyph renders `on-surface-variant` at `opacity: 0.62`, and its comment claims the composite lands within ~6/255 per channel of the designed colour. It is unknowable whether that "~6/255" was computed in sRGB — in which case the linear-space correction applies and the right value is ~0.73 — or read off a rendered PNG, in which case 0.62 is already correct and changing it moves away from the design. Unlike every other corrected site, **no Figma hex was recorded** for this glyph (Appendix A.1 maps the design's two body greys and stops; this is a third, lighter grey). Ask for the swatch; until then it is left as-is, which is the smaller of the two possible errors. See Appendix B. |

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
| `#FFFFFF@0.50` | `--md-sys-color-inverse-on-surface-muted` | inactive tab. ⚠️ **Not `inverse-on-surface` + `opacity: 0.5`** — that renders (183,184,185) against V2's (127,130,136). Pre-blended **extension** token, §3.2 |
| `#DC2626` | `--md-sys-color-error` | |
| `#34C759` | `--md-sys-color-success` | **extension** |
| `#FFF7E6` | `--md-sys-color-warning-container` | **extension** |
| `#FEDF89` | `--md-sys-color-warning` | **extension** |
| `#6750A4`, `#49454F`, `#1D1B20`, `#CAC4D0`, `#FEF7FF`, `#F7F2FA` | — | **Material kit defaults, not brand.** Replace with the tokens above wherever they appear. |
| AR HUD surfaces, hairlines and fills | `--md-sys-color-camera-*` (8 tokens) | **extension**, identical in both themes, pre-blended composites — full table in §3.2 |

**⚠️ The off-brand-looking hues in `brand-theme.json` are deliberate — do not "clean them
up".** This table lists the roles the designer **measured**, which is why
`primary-container`, `inverse-primary`, `tertiary-container` and `on-tertiary-container` are
absent from it; their absence has been misread as "unbranded leftovers from a seed pass"
more than once. They are not. Measured in CIELAB:

| Role | Value | L | C | h |
|---|---|---|---|---|
| `primary` | `#152336` | 13.4 | **14.2** | **273.2** |
| `primary-dim` | `#0E1C2F` | 10.0 | 14.6 | 274.9 |
| `on-primary-fixed-variant` | `#3A475C` | 29.9 | 14.1 | 272.6 |
| `primary-container` | `#D5E3FD` | 89.9 | **14.2** | **272.0** |
| `inverse-primary` | `#B9C7E0` | 79.9 | 14.0 | 271.5 |
| `secondary` | `#FFC107` | 81.5 | 83.2 | 83.5 |
| `tertiary-container` | `#FFE5B4` | 92.0 | 27.2 | 86.2 |
| `on-tertiary-container` | `#3F2E00` | 20.1 | 28.8 | 84.1 |

`primary-container` sits at *exactly* the brand navy's chroma and hue — it is a light tone
on the **same constant-chroma ramp** as `primary`, `primary-dim` and
`on-primary-fixed-variant`, which is what a hand-authored tonal ramp looks like. A Material
Theme Builder seed pass would have forced chroma up to ~48 and could not land on C ≈ 14 five
times running. The tertiary pair sits on the amber hue, matching `brand-theme.json`'s own
stated rule ("tertiary = the amber hue at reduced chroma"). They look pale because the brand
navy *is* low-chroma, not because anybody forgot to restyle them. (The table above is
CIELAB, measured from the shipped hex; `brand-theme.json`'s `description` states the same
relationships in **HCT** — navy hue 258.5, amber hue 86.1 — so the two sets of hue angles
are not meant to match numerically.)

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
| `title-large-increased` | **added rung** — the hero beach name (Tela 9) | 24 / 700 |
| `title-small-increased` | **added rung** — primary button label, `MvStateView` action, Praias stat value | 16 / 700 |

The two `-increased` rungs exist because the rows above them are **ranges** and the retune
had to pick one end; see §3.4 for why they were *added* rather than re-pointed, and §3.5 for
the `corner-*-increased` precedent the names follow. V2 sizes with no class of their own,
where a screen stylesheet overrides the nearest rung: 16/600 (SOS rows, the beach-selector
name), 13/700 (small pill buttons), 10/600 (micro tags).

Letter-spacing is `0` almost everywhere in V2 (exceptions: `0.5` on "Você está em" and the
AR spec labels, `0.1` on the beach-selector name). M3's default tracking must be zeroed —
do not keep the Roboto values.

## Appendix B — fidelity caveats (accepted)

| V2 uses | UI Toolkit | Resolution |
|---|---|---|
| **sRGB alpha blending** | **linear-space blending** | **every transcribed alpha must be corrected — B.1 below** |
| Drop shadows | no `box-shadow` | 1px border (already in the design) — §3.6 |
| Fixed line heights | no `line-height` | font metrics; verify long pt-BR strings on device |
| Text nodes with no inset | `Label` ships padding+margin | zeroed app-wide in `_typography.uss` — §1.2 |
| Font weight as a property | one asset per weight | 4 Inter SDF assets — §3.4 |
| `cubic-bezier()` easing | named easings only | nearest in `_motion.uss` |
| Uniformly-scaled `border-radius` | per-axis clamping | a stadium needs `r == height/2` exactly — §3.5 |
| `text-transform` | none | bake the case into the formatter constant — §8.4 |
| `text-decoration: underline` | none | 1px bottom border on the label's own box — §8.1 |
| Backdrop blur (AR card, `BACKGROUND_BLUR` 24) | none | ship the fill opaque — §8.4 |
| Status bar / home indicator | OS-owned | **do not build** — safe-area padding, §1 |

### B.1 Linear colour space — every alpha transcribed from Figma is wrong until corrected

**The mechanism.** Unity renders in **Linear** colour space
(`ProjectSettings/ProjectSettings.asset` → `m_ActiveColorSpace: 1`): a USS colour is
converted sRGB → linear, blended there, and converted back. **Figma blends directly in
sRGB.** So the same layer, at the same alpha, over the same backdrop, composites to a
different colour in the two tools — always landing **too light** in Unity. This is not a
rounding difference: white at 0.15 over the AR card's `#050B14` rendered **(108,109,110)**
where V2 shows **(42,48,55)**.

**It is a validated model, not a theory.** Predicting the composite from the linear-blend
maths and comparing against the rendered PNG agreed to **±1 per channel** on every case
tested — e.g. `camera-fill`, white @0.10 over `#050B14`, predicted (90,90,92) and measured
(90,91,92).

**Two consequences decide the fix, and both are counter-intuitive:**

1. **The direction depends on the overlay, not on the rule.** A **light overlay on a dark
   backdrop** must have its alpha cut hard — white @0.15 over `#050B14` needs roughly
   0.022–0.032 to reproduce Figma, a 5–7× reduction. A **dark overlay on a light backdrop**
   must have its alpha **raised** — the hero scrim (brand navy over a beach photo) went
   0.35 → **0.505** and 0.25 → **0.38**. **Never assume "reduce".**
2. **The per-channel corrections diverge**, so **no single alpha reproduces Figma exactly.**
   The same white-@0.15-over-`#050B14` case wants 0.022 / 0.026 / 0.032 on R/G/B. That gives
   the decision rule:

> **If the backdrop is opaque and known, pre-blend to an opaque token — that is exact.**
> **If the backdrop is arbitrary (a photo, a video frame, live camera), keep a real alpha at
> the corrected value — that is the best available approximation.**

The `camera-*` family and `inverse-on-surface-muted` (§3.2) are the pre-blended case. The
approximate case is small and enumerated:

| Site | Authored (Figma) | Ships |
|---|---|---|
| `MvHeroHeader` scrim, 340dp hero | 0.35 | **0.505** |
| `MvHeroHeader` scrim, 240dp compact hero | 0.25 | **0.38** |
| `MvTag--on-image` surface | 0.80 | **0.90** |
| `MvMediaPicker` remove badge fill | 0.25 | **0.47** |
| `HomeScreen` sparkline fill / baseline / freshness | 0.14 / 0.4 / 0.7 | **0.025 / 0.19 / 0.49** |
| `HomeScreen` state-view icon / body | 0.5 / 0.75 | **0.27 / 0.57** |
| `EspecieScreen` model-placeholder icon | 0.4 | **0.15** |

**Cost, and the one way this rots.** A pre-blended value is **bonded to one backdrop**: move
the surface underneath it and every composite must be recomputed by hand, because unlike an
alpha it will not follow. That is why the tokens are named after the surface they are drawn
*on*.

**Deliberately NOT corrected — with the reason, so nobody "finishes the job".** The
correction applies to **numbers transcribed from a Figma layer**. It does not apply to
numbers that mean something else, and applying it to these would make them worse:

- **M3 state-layer opacities `0.08 / 0.10 / 0.12`** (`--md-sys-state-*`, ~30 sites) —
  relative interaction feedback, not a transcription of anything.
- **`MdDialog` and `MdBottomSheet` scrims, both 0.32** — the same defect is present (the
  effective sRGB dim is only ~0.157; they would need ~0.58 to deliver M3's 0.32), but they
  are **M3 spec constants and V2 has no dialog or sheet frame** to match against. One line
  each if that ever changes.
- **`MdBottomSheet`'s drag handle at 0.4** — M3 spec constant.
- **`MdMenu`'s scrim at 0.32 — kept deliberately, and it is not the same number.** A menu is
  not modal (stock M3 gives it no scrim, and V2's Tela 9 dims nothing), and under linear
  blending 0.32 delivers only a ~0.16 effective dim — which is exactly the light,
  non-modal veil a dropdown wants. It coincides numerically with `MdDialog`'s 0.32 while
  **meaning something different**; do not unify the three.
- **`MdSparkline`'s component-default fill 0.15 and baseline 0.6** — §8.1 records the
  conditions card as a flattened bitmap in Figma, so there is **no authored alpha to
  transcribe**; these were eye-tuned.
- **The sparkline's screen-side label alpha, 0.75** — it renders `0xE1E1E2` and the Figma
  bitmap's labels peak at `0xE5ECF2`, so **the render already is the design**; correcting it
  would move away from V2.
- **`.mv-report__pending-retry:disabled` at 0.45** — M3's disabled vocabulary, and it dims a
  subtree containing a `primary` label; correcting it to ~0.23 would leave navy text at the
  edge of legibility. If the linear rule should reach the disabled states, it belongs in the
  state tokens, not in one screen.
- ⚠️ **`MvStateView`'s status glyph at 0.62 — genuinely ambiguous, and the one open
  question.** Its comment says the composite lands within ~6/255 per channel of the designed
  swatch, but it is unknowable whether that was computed in sRGB (→ the correction applies
  and ~0.73 is right) or read off a rendered PNG (→ 0.62 is already right and correcting it
  makes it worse). **Unlike every other site, no Figma hex was recorded for it.** Left as-is
  and raised as **Decision D10** — ask the designer for the swatch.

---

*Generated from Figma file `8e8pBsY0T5hpMdpwoinoXI` (`Protótipo` page), version `2402812393380097744`.*
