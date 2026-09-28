# Handover — V2 redesign (UI Toolkit)

**For:** the next person on this, developer or Claude Code session, who was not here for any
of it. You know Unity and C#; you have no memory of the decisions, the dead ends, or which
"bugs" are actually faithful to the design.

**The plan of record is [DESIGN_IMPLEMENTATION.md](../DESIGN_IMPLEMENTATION.md).** It says what
gets built, in what order, against which Figma frames, with every number transcribed. This
document does not repeat it — it says where the work actually stands, how to run it, what is
deliberately unfinished, and what to do next.

Written 2026-09-28, against the working tree at that date. The test and screenshot numbers
below come from `src/app/MergulhoVirtual/Logs/*.xml` and `.shots/manifest.json` from the last
headless run; re-run them before trusting them.

---

## 1. Where things stand

Four of the seven delivery slices are done (§7 of the plan carries the detail):

| Slice | State |
|---|---|
| **0 — Foundations** (brand tokens, Inter, shape, icons) | done |
| **1 — Shell + Início** (`MdRouter`, nav bar, HomeScreen) | done |
| **2 — Praias** (landing + Praia detalhe + 8 new components) | done; **content file is empty** |
| **3 — Avistamentos** (Reportar form) | done **front-end only**; backend untouched |
| **4 — Mergulho (AR HUD)** | not started |
| **5 — SOS + states** | not started; **blocked** on real emergency numbers (Decision D3) |
| **6 — Retire uGUI** | not started (partly pre-empted — see §4) |

Verified green, headless, editor closed:

```
make ds-test        EditMode   402/402
make ds-test-play   PlayMode    21/21
make ds-shots                   78 PNGs written, 0 subjects skipped
make ds-compile                 0 errors
```

### Code-complete is not shippable here

Four independent things stand between this and a release, and none of them is code:

- **Beach content is blank.** `beaches_content.json` has **0 of 17** beaches with a risk level,
  best season, sighting peak, lifeguard hours or tips, and **3 of 17** with a species list.
  **14 of 17** beach detail pages therefore render as a hero photo, a title and a description;
  three of them (Conceição, Meio, Porto de Santo Antônio) have no editorial content at all.
  What *is* filled — `environmentTags` 11/17, `advisories` 7/17, `idealTide` 4/17 — was derived
  from `places.json` descriptions and the AR spawner list, not authored by anyone who knows
  the beaches. See §5.
- **SOS is placeholder.** No screen exists and no real phone numbers exist. Decision D3 is
  open and blocks Slice 5 outright.
- **Two icon sites still render a stand-in** for an asset the designer owes (§7).
- **Nothing has been run on a device.** Every green check above is an editor check. The AR
  overlay, the safe-area insets, Inter at real DPI, and a real photo upload against production
  App Check are all unverified. §8 has the checklist.

---

## 2. How to work on it

### The one rule newcomers get wrong: close the Unity Editor

`make ds-compile`, `ds-test`, `ds-test-play`, `ds-setup`, `ui-setup` and `ds-shots` all drive
Unity in `-batchmode`, which takes an **exclusive lock on the project directory**. With the
Editor open they fail — sometimes with a clear "project already open", sometimes with
something much less obvious. Close the Editor, run the target, reopen.

Only the Python targets (`make ds-tokens`, `make ds-icons`) are safe with the Editor open.

### The targets

| Target | What it does |
|---|---|
| `make ds-tokens` | Regenerates `_colors-*.uss` from `tools/design_system/brand-theme.json`. Python; never hand-edit the generated files. |
| `make ds-icons` | Re-subsets `MaterialSymbols.ttf` from `tools/design_system/material_symbols_icons.txt`. Python. |
| `make ds-setup` | Font assets + PanelSettings + GalleryScene. Run after re-subsetting icons or adding a font. |
| `make ui-setup` | Runs `AppUiBuilder.BuildHeadless` — builds/rewires the `AppUI` GameObject, `AppPanelSettings`, and `ScreenManager`'s fields into `MainScene`. Idempotent. |
| `make ds-test` | EditMode: token discipline + component structure + every ViewModel suite. |
| `make ds-test-play` | PlayMode: pointer/interaction suites. |
| `make ds-shots` | Renders every screen and gallery section to PNG. **This is the review loop** — see below. |
| `make ds-compile` | Import + compile check, no side effects. |

`ui-beaches-setup` is an alias of `ui-setup`, kept for muscle memory.

### `ui-setup` has a silent failure mode — know it before you add a screen

`AppUiBuilder.ScreenStylePaths` is a hardcoded list of every screen `.uss`, loaded onto the
panel root by `AppUiHost`. **A screen whose stylesheet is missing from that list still compiles,
still builds its visual tree, and renders completely unstyled — with no error anywhere.** Add
the sheet to the array in the same commit as the screen, then re-run `make ui-setup`, or you
will spend an hour debugging a screen that is fine.

(The screenshot harness does *not* share this failure: it loads every `.uss` under
`Assets/UI/Screens` by asset search. So a missing entry looks correct in `ds-shots` and wrong
in the app. Check both.)

### `make ds-shots` — the review loop

This is the single most useful thing in the workflow and the least obvious. It renders every
UI Toolkit screen, the app shell, and every design-system gallery section straight to PNG, in
about 20 seconds, with no device and no Editor.

```bash
make ds-shots                 # everything, light + dark  -> <repo>/.shots/
make ds-shots SHOT=home       # only subjects whose id contains "home"
make ds-shots DISPLAY=:0      # if :1 is not your X display
```

Output goes to `.shots/` (gitignored) plus a `manifest.json` recording the frame size, scale,
safe-area insets and the frozen clock.

Things to know:

- **It must NOT run with `-nographics`.** Rasterising a UI Toolkit panel needs a real graphics
  device, so this is the one Unity target in the Makefile that runs against a display. The
  Makefile defaults `DISPLAY` to `:1`; override it if that is wrong for your machine. (It still
  runs `-batchmode`, so the Editor still has to be closed.)
- **1 USS px = 1 dp = 1 Figma pt.** Each shot gets a throwaway `PanelSettings` at
  `ConstantPhysicalSize` / `referenceDpi = 160`, rasterised at 2×, so the PNGs line up pixel
  for pixel with the 2× Figma renders in `.figma-sync/png/`. Open them side by side.
- **Deterministic by construction.** Screens are built against `UiShotFixtures` — a frozen
  clock (`2026-09-15T17:30Z`), a fixed UTC−2 conversion for Noronha, fixed conditions and tide
  data, no GPS, no network, no backend. A PNG changes only when the UI changes. The beach and
  species *photos* do come from `Resources/`, which is fine: they are committed assets.
- **Safe area is passed in, not measured.** `Screen.safeArea` is meaningless in batchmode (the
  hidden window is 640×480), so the harness hands screens 47 dp top / 34 dp bottom explicitly.
  Override with `MV_SHOT_TOP_INSET` / `MV_SHOT_BOTTOM_INSET`. The fake status bar and home
  indicator the Figma frames draw are OS chrome and are deliberately not rendered.
- **`.shots/` is never cleaned.** The 8 `beaches-*.png` files sitting there are from the
  pre-Slice-2 `BeachesScreen` and are stale. Trust `manifest.json` (or the
  `UI-SHOTS-SUMMARY written=N skipped=N` line the Makefile prints), not `ls | wc -l`.

**Adding a subject.** Nothing is required: any `IAppScreen` in the `MergulhoVirtual.UI`
assembly that the table does not name gets a bare and a shell subject discovered automatically.
Declare it in the `Subjects` table in
`Assets/Editor/UiShots/UiScreenshotHarness.cs` only when it needs a variant — a taller frame
(`HeightDp`), a pre-navigated state (`VmMethod` / `VmArgs`), or a sequence of ViewModel calls
(`VmCalls`, which is how `report-filled` is driven through the form in the order a user would
tap it). Screens are resolved by simple type name and constructed by matching their constructor
parameters against `UiShotFixtures.Services`; a subject that cannot be built is **skipped with
a log line**, not fatal — so watch the `skipped=` count.

**When a ViewModel takes a new interface,** add a fake to `UiShotFixtures.Services` keyed by
that interface, or every screen using it starts silently skipping. The fixtures deliberately
mirror rather than share the EditMode test fakes: the tests want *empty* data to assert the
"—" fallbacks, the harness wants *plausible* data that exercises every row.

### Verification order per change

1. `make ds-compile`
2. `make ds-test` + `make ds-test-play`
3. `make ds-shots` and actually look at the PNGs
4. **Gallery first for visual bugs.** If a bug reproduces in `gallery-*.png` it is a component
   bug — fix the component and add a regression test. If it does not, it is screen wiring.
5. Device build for anything the editor cannot judge (§8).

---

## 3. The architecture as built

Enough to add a screen without reverse-engineering it.

### Assemblies — the boundary is compile-time, and one-way

```
MergulhoVirtual.DesignSystem        components, tokens, themes. References nothing.
MergulhoVirtual.UI                  screens, ViewModels, domain formatting, routing.
                                    References MergulhoVirtual.DesignSystem ONLY.
Assembly-CSharp                     everything else: AR, GPS, Firebase, JobQueue,
                                    the legacy uGUI screens, and the adapters.
                                    Auto-references MergulhoVirtual.UI.
```

**`MergulhoVirtual.UI` may not reference `Assembly-CSharp`**, and Unity's asmdef graph enforces
it — the reference only flows the other way. That is the whole isolation story: a screen cannot
reach for `GPSHandler`, `ARSession`, `JobQueue` or Firebase even by accident. It talks to
interfaces in `Assets/UI/Interfaces/` (`IBeachCatalog`, `IConditionsService`, `ITideService`,
`IBeachContent`, `ISpeciesCatalog`, `IActiveBeach`, `IBeachOverride`, `IOnboardingState`,
`ISightingReports`, `IPhotoPicker`) and the mapping to the real engine types lives in exactly
one file, `Assets/Scripts/UI/UiToolkit/UiServiceAdapters.cs`, on the Assembly-CSharp side.

### The shell

```
MainScene (root)
└── AppUI                      UIDocument + AppUiHost   ← composition root
    └── MdRouter
        ├── screens container  exactly one IAppScreen visible
        └── MdNavigationBar    4 destinations, persistent
```

- **`Assets/UI/Navigation/MdRouter.cs`** — owns which screen is visible, the sub-screen back
  stack, and edge-inset forwarding. Two kinds of navigation: **`Navigate(route)`** for a
  bottom-bar destination (clears the back stack, moves the bar's selection) and
  **`Push(route)` / `Back()`** for a sub-screen (the bar keeps the *originating* tab selected,
  because sub-screens have no tab). Screens are registered once and kept alive; switching
  toggles `display`, so per-visit work belongs in `OnEnter`/`OnExit`, never in a constructor.
  Navigating to an unregistered route logs one warning, returns `false` and changes nothing —
  that is the deliberate state of `AppRoutes.Sos` and `AppRoutes.Especie` today.
- **`Assets/UI/Navigation/IAppScreen.cs`** — the contract (`Key`, `Root`, `OnEnter`, `OnExit`,
  `SetEdgeInsets`), plus **`AppRoutes`** (four tab keys + `PraiaDetalhe`, `Sos`, `Especie`,
  `Sobre`), **`AppTabs.Default`** (the bar, in V2 order) and **`AppIcons`** (icon names that
  more than one place must agree on).
- **`Assets/Scripts/UI/UiToolkit/AppUiHost.cs`** — the composition root. Builds adapters and
  ViewModels in `Awake` (once — they survive every shell rebuild), builds the visual tree and
  registers screens in `OnEnable` (UIDocument tears the tree down on deactivate, so it must be
  rebuildable), applies safe-area insets, and reports the active route to `ScreenManager`.
  Screens raise `AppRoutes` keys; **the host decides `Navigate` vs `Push`** — so screens stay
  router-agnostic and unit-testable.
- **`Assets/Scripts/UI/UiToolkit/LegacyUguiScreen.cs`** — adapts a legacy uGUI panel GameObject
  to `IAppScreen`, so the router drives `MainScreen` (AR) and `AboutScreen` exactly like a UI
  Toolkit screen. Its `Root` is an empty, non-pickable element: it paints nothing and hit-tests
  nothing, so taps fall straight through the UITK panel to the uGUI canvas underneath. This is
  what makes the strangler workable; it lives in Assembly-CSharp because it is the only
  assembly allowed to see both worlds.
- **`Assets/Scripts/UI/ScreenManager.cs`** — **only the AR performance gate now.** It answers
  "should the AR session be running?" (yes on `AppRoutes.Mergulho` and during the splash, no
  elsewhere) and sets the frame-rate target. No screen fields, no `Show*()` methods, no
  `useUiToolkitBeaches` switch. Do not put navigation back into it.

### Screens

Built **in code, not UXML** — the APIs that carry the actual content (`MdSparkline.SetSamples`,
`MdDropdown.SetChoices`, `MvMediaPicker`, `MdIcon.Icon`) are code-only, so a UXML file would
hold nothing but empty boxes.

Each screen is `VisualElement` + `IAppScreen`, constructed with its ViewModel, with a sibling
`.uss` in `Assets/UI/Screens/`. Structure is always: a **transparent, `PickingMode.Ignore`
root** (so the nav-bar strip and anything the shell does not paint stay tappable) wrapping an
**opaque content container** that takes the top/left/right safe-area insets as *padding*.

**Safe area is applied by the host, not by `SafeAreaElement`** — a deliberate deviation.
`SafeAreaElement` would leave the status-bar strip transparent (showing raw camera feed), and
it skips editor panels entirely, which breaks the Device Simulator. `AppUiHost.ApplyEdgeInsets`
converts `Screen.safeArea` to panel units and pushes it through `MdRouter.SetEdgeInsets`; the
router gives top/left/right to the screen and absorbs the bottom into the nav bar's padding
(V2's 34 dp "home indicator" strip below the 64 dp tab row **is** that padding, not a drawn
element).

### Where pt-BR formatting lives

**`Assets/UI/Domain/`** — and only there. `ConditionsFormatter` (wave/tide/moon/wind/water/
freshness rows, shared by Home and Praias so the two can never disagree), `BeachContentFormatter`,
`BeachContentTokens`, `ReportFormatter`, `MoonPhase`. Plain C#, no `UnityEngine`, clock and
UTC→local conversion injected so tests are deterministic and timezone-proof.

A screen must never manufacture a user-visible string. That rule is why the stat labels ship
title-case instead of being `ToUpper()`-ed in the view (§6).

### Adding a screen — the checklist

1. `Assets/UI/ViewModels/YourViewModel.cs` — plain C#, no `UnityEngine`, injected clock. Add
   EditMode tests in `Assets/UI/Tests/Editor/` (that assembly is already in the Makefile's
   `-assemblyNames`).
2. New service? Interface + engine-free DTO in `Assets/UI/Interfaces/`; adapter in
   `UiServiceAdapters.cs`; fake in `UiShotFixtures.Services`.
3. `Assets/UI/Screens/YourScreen.cs` implementing `IAppScreen`, raising `AppRoutes` keys via
   an event rather than routing itself. Use `AppScrollView`, not `ScrollView` (§6).
4. `Assets/UI/Screens/YourScreen.uss` — **tokens only**; `TokenDisciplineTests` scans
   `Assets/UI/**/*.uss` and fails on a color literal (even inside a comment) or an undefined
   `var()`.
5. Add the route to `AppRoutes`, construct and register the screen in `AppUiHost.RegisterScreens`.
6. **Add the `.uss` to `AppUiBuilder.ScreenStylePaths` and re-run `make ui-setup`.**
7. `make ds-compile` → `ds-test` → `ds-shots`.

---

## 4. What Slice 6 still has to clean up

Slice 1 pre-empted part of it. Already done: `ScreenManager` has no screen fields, and
`BottomNav`, `BeachesScreen`, `AnimalsScreen` and `RegisterScreen` are deactivated and unrouted
in `MainScene` (see `AppUiBuilder.UnroutedLegacyScreens`). They stay in the scene by the
strangler rule until Slice 6 deletes them along with their uGUI builders.

Still routed, on purpose: `MainScreen` (the AR HUD, until Slice 4) and `AboutScreen` (Sobre,
per Decision D2 — this is what keeps the shipped Instagram widget reachable with no rebuild).

---

## 5. Content and data state

### Beaches

`Assets/Resources/beaches_content.json`, keyed by the `places.json` machine `name`:

| Field | Filled |
|---|---|
| `riskLevel` | 0/17 |
| `bestSeason` | 0/17 |
| `sightingPeak` | 0/17 |
| `lifeguardHours` | 0/17 |
| `tips` | 0/17 |
| `species` | 3/17 (Sancho, Baía dos Porcos, Cacimba do Padre) |
| `species[].behaviour` / `.tag` | 0 of 5 species rows |
| `environmentTags` | 11/17 *(derived)* |
| `advisories` | 7/17 *(derived)* |
| `idealTide` | 4/17 *(derived)* |

**A research-and-cite pass was offered and declined** (Decision D8). Filling this is the
biologists' job and the task list for them is [beaches-content-todo.md](beaches-content-todo.md)
— pt-BR, written for non-programmers, checkbox-per-field, and verified in sync with the JSON's
`_todo` arrays. Its standing rule is the right one: **a blank field beats an invented one**, and
the screens are built to hide a missing block rather than print a placeholder.

One stale spot in that doc: `places.json`'s `displayName` for the Porto beach was settled as
"Praia do Porto", but the doc still asks the question in two places.

### Beach names — D4

`places.json` now carries a `displayName` alongside the machine `name`. **`name` is the key**
— `BeachSharkSpawner`'s Inspector list, `beaches_content.json`, the backend's `local` field and
the seed data all agree on it. `displayName` is editorial, may be re-worded at any time, and
must never be used as a lookup key. Five of the 17 differ (`Sueste Beach` → "Baía do Sueste",
`Sharks Cove` → "Enseada dos Tubarões", and three others).

### Reportar — what reaches the backend

The app sends eleven multipart parts. The endpoint declares five.

| Part | Backend |
|---|---|
| `photo`, `beach`, `timestamp`, `species_guess` | accepted |
| `notes` | accepted — but **the V2 form has no notes field**, so nothing ever sets it |
| `species_key`, `size_bucket`, `behaviours`, `reporter_name`, `reporter_email`, `reporter_profile` | **silently dropped** |

FastAPI ignores undeclared form parts, so nothing errors — the data just never reaches
Firestore. This is deliberate (Slice 3 was scoped front-end only) and documented in
`ReportSightingJob`; the fields start persisting the moment the backend grows the matching
`Form()` parameters. Doing that also means extending the Firestore document shape and, for any
field that becomes a list filter, regenerating the composite indexes (mind the `2^N` budget in
CLAUDE.md).

**`reporter_email` needs a decision before the backend persists it.** It is personal data
collected in the UI and transmitted over the wire to a service that does not store it — today
it exists only in request logs. **A privacy notice and a retention policy are owed before
anyone adds `reporter_email: str = Form(None)`.** This is flagged in both `ISightingReports`
and `ReportSightingJob`; do not treat it as a one-line backend change.

### The species catalog has one wrong record

`Assets/Resources/Animals/reef_shark.asset` reads `displayName: Tubarão-bico-fino`,
`binomial: Carcharhinus acronotus` (blacknose shark), with a blacknose description — but its
`modelCredit` is *"Model 54A - Caribbean Reef Shark"*, the FBX and every texture in
`Assets/Models/reef_shark/` are `CRS`-prefixed (Caribbean Reef Shark), and
[sharks-noronha-models.md](sharks-noronha-models.md) lists the species as *Carcharhinus perezi*.
The binomial was added recently and contradicts the model, the credit and the project's own
species doc. `AnimalDef`'s own comment says the field is deliberately blank where identification
is open, because a wrong binomial is worse than none. **Someone who knows the species has to
say which it is**; the asset shows on both the Praia detalhe species card and (once D1 lands)
the Animais detail screen.

---

## 6. Known gaps and deliberate deviations

Read this section before "fixing" anything. Roughly half the visible oddities are the design.

### Bugs — fix these

**Submit button on Reportar is not full width.** V2 draws a 358 dp bar; it renders as a pill
hugging its label, centred. `ReportScreen.uss`'s `.mv-report__submit { align-self: stretch }`
does stretch `MdButton`'s transparent 48 dp touch-target root (the screen sheet is on the
document root, so it outranks the theme's `align-self: flex-start`), but the *visible* surface
is `.md-button__container`, which has no `flex-grow` and so hugs its content inside a
full-width invisible parent. The `.mv-report__submit .md-button__container` rule sets height
and radius but never width. Fix is `flex-grow: 1` on that container rule.

**`MdChip` selected is the wrong colour family.** It renders `secondary-container` (pale amber
`#FFDF9E`) with a navy label; V2's selected chip is **white on navy** (`primary`). Its radius
is `corner-small` (10) where V2 chips are 8. **This was deliberately not forked per screen** —
no screen USS overrides chip colours, only margins — because the same chip is used by Reportar
(species, size) *and* Praia detalhe (species), and the right fix is one `MdChip` restyle that
corrects both at once plus the gallery. `_shape.uss` already carries a note saying MdChip has
not been restyled. Resist patching it in `ReportScreen.uss`.

**`MdCard` is still radius 12** (`corner-medium`) where V2 cards are 16. Real design-system
debt, **but invisible today**: `MdCard` is instantiated nowhere in the app — only in the gallery
and its tests — because every V2 screen authors its own card surface at `corner-large`. Fix it
in the same pass as the `MdChip` restyle, and expect the gallery shots to change.

**`MdMenu`'s scrollbar is hidden by a workaround in the wrong place.** `MdMenu` builds a stock
`ScrollView`, and with 18 beaches the picker always overflows, so it would be the one place in
the app showing UI Toolkit's desktop scrollbar-with-steppers. `PraiasScreen.OpenBeachMenu`
queries the menu's `ScrollView` and sets `ScrollerVisibility.Hidden` from C# (scroller
visibility is an inline style USS cannot reach — §7). The code comment says it plainly: this
belongs in `MdMenu`, and is parked in the screen only because Slice 2 did not own the component.
Move it when you next touch `MdMenu`.

**Escaping a pushed screen costs two taps.** `MdNavigationBar.SelectedIndex` returns early when
the value is unchanged, so it raises no event — and while a sub-screen is pushed the bar still
shows the *originating* tab selected. Re-tapping that tab therefore does nothing; you have to
tap a different tab and come back. Praia detalhe hides this behind its hero back button.
**Sobre does not: it is a `LegacyUguiScreen` with no back affordance of its own**, so it is
currently the worst case. Resolves when pushed screens get a real back affordance (an
`MdTopAppBar` with a nav icon, the way `ReportScreen` already has one and hides it) — or, more
cheaply, by making the bar raise its event on a re-tap of the active destination.

**The `reef_shark` binomial contradicts its model** — see §5.

### Faithful to the design — leave these alone

- **Stat labels render title-case.** V2 uppercases "Maré agora", "Pico de avistamento",
  "Melhor Época", "Maré Ideal", "Avistamentos", "Você está em", "Pendente", "Tamanho aprox.",
  "Dieta" and "Comportamento" — but it does so with a Figma **text-case transform** while the
  text nodes themselves are title case. **UI Toolkit has no `text-transform`**, and uppercasing
  in the view would mean a screen manufacturing a user-visible string (§3). Both screen
  stylesheets document this as accepted. If the designer insists, the correct fix is to change
  the constants in the ViewModel, not to add `.ToUpper()` in the view.
  *Re-sync gotcha:* `.figma-sync/struct/*.txt` does **not** surface `textCase`. If you re-derive
  a spec from the struct dumps you will miss this again — check `style.textCase` in
  `.figma-sync/file.json`.
- **The floating SOS pill overlaps page content.** It is a floating action button: a sibling of
  the content container, anchored bottom-left above the nav bar, with the page scrolling behind
  it. V2 draws it the same way. It is not a layout bug. (It is also inert — see below.)
- **The nav bar shows the wrong active tab on the Figma SOS frame.** That is a copy-paste
  artifact in the design file (§8.6 of the plan). SOS has no tab; the router deliberately keeps
  the originating tab selected over a pushed screen.
- **No drop shadows.** UI Toolkit has no `box-shadow`. Every shadowed V2 surface also carries a
  1 px border, which is what actually separates it from the page; the border is rendered and the
  shadow dropped (§3.6 of the plan). The floating SOS pill keeps its 2 px darker-red stroke.
- **"SOS" renders at weight 700**, not V2's 800 — the shipped Inter set is 400/500/600/700.
- **The risk badge is dark green on green, not white on green.** V2's white-on-`#34C759` is
  about 2.2:1 and fails WCAG, so Slice 0 authored an accessible `on-success` (`#052E16`) and
  `MvTag` consumes the token rather than the swatch. If the designer insists on white it is one
  line in `_brand-light.uss` / `_brand-dark.uss`, not a fork in the component. Moot today: no
  beach has a risk level.
- **Scrollbars are hidden everywhere** (`AppScrollView`) — mobile convention, and no V2 frame
  has one. Touch, wheel and keyboard scrolling are unaffected.
- **Home has a fifth, full-width "Sobre" card** below the 2×2 grid that is not in any Figma
  frame. Decision D2 — Sobre lost its tab and had to go somewhere; full-width so it reads as its
  own section rather than an orphaned half-row.
- **Nothing draws a status bar or home indicator.** Those are OS chrome in the mockups
  (§1 of the plan); safe-area padding handles the space.
- **Missing content is hidden, not placeholdered.** A beach with no tips renders no tips
  section, not an empty card. That is why `praia-detalhe-empty-*.png` is the shot worth looking
  at.

### Wired but inert, on purpose

- **The SOS tile (Home) and the floating SOS pill (Praia detalhe)** raise `AppRoutes.Sos`,
  which has no screen. `MdRouter` logs one warning and changes nothing. Slice 5 / Decision D3.
- **"Saiba mais sobre a espécie"** on the beach species card raises `AppRoutes.Especie`, same
  deal. Decision D1 — Animais becomes a sub-screen reached from here; the standalone list is
  dropped.
- **"Baixar a tábua de maré do mês"** on Home's conditions card is visible and does nothing.
  The DHN tide table is a PDF the project parses offline and publishes nowhere: there is no URL
  to open, and inventing one would ship a dead link. `AppUiHost.OnTideTableRequested` logs it.
  Decide with the project: host the PDF (a public object in the `conteudos-educacionais` bucket,
  same pattern as the educational videos) or drop the link.
- **The Avistamentos icon is a placeholder.** V2 draws a **custom shark fin** and Material
  Symbols has no equivalent, so `AppIcons.AvistamentosPlaceholder = "visibility"` stands in —
  deliberately neutral, so it reads as "not final". It is rendered by **two cross-referenced
  sites that must flip together**: the bottom-bar tab (`AppTabs.Default`) and the Início feature
  tile (`HomeViewModel.AvistamentosIconPlaceholder`, an alias of the same constant). A **third**
  stand-in — `"help"` — sits in the gallery's nav-bar demo and has drifted from the other two;
  fix all three when the SVG arrives. Plausible-looking marine glyphs (`waves`, `surfing`,
  `scuba_diving`, `pool`) are explicitly excluded from the icon subset so none of them can
  quietly become the shipped icon.

### Screenshot-harness artifacts (not app bugs)

- `shell-praia-detalhe-*.png` shows **no active tab**, because the harness `Navigate`s straight
  to the sub-screen instead of `Push`ing from Praias. In the app, Praias stays lit.
- `.shots/` still holds 8 `beaches-*.png` from the pre-Slice-2 screen. Stale, not regressions.

---

## 7. Three project-wide footguns

These three cost real time. All are already solved; the solutions are invisible unless you know
they exist, and each is easy to undo by accident.

**1. Unity's default `Label` metrics are non-zero, and are now reset app-wide.** The default
runtime theme gives every `Label` `padding: 4 2 4 1` and `margin: 4 4 2 2` — 14 dp of invisible
vertical and 9 dp of invisible horizontal space that no Figma text node has. Left in place it
inflates every stack of rows (the Home conditions card measured 31 dp per 17 dp row) and eats
9 dp of every wrapping label's measure, which costs whole **lines** (the Home feature-card body
wrapped to 4 where Figma fits 3). It is zeroed once, for the whole app, in the "Label metric
reset" block of `Assets/DesignSystem/Tokens/_typography.uss`. Two consequences: **transcribe
Figma gaps as-is**, because every gap you see is one a component or screen authored; and
**nothing may lean on the default metrics** — a new stack of labels needs explicit margins.

**2. `ScrollView` scroller visibility is an inline style that USS cannot beat.** `ScrollView`
writes its scrollers' `display` as an inline style on every layout pass (that is how
`ScrollerVisibility.Auto` works), and inline styles win over stylesheets — so hiding
`.unity-scroller` with `display: none` does nothing. The supported way to say "no scrollbar,
still scrollable" is `ScrollerVisibility.Hidden` from C#. `Assets/UI/Controls/AppScrollView.cs`
is a three-line subclass that makes it the default; **use `new AppScrollView()`, never
`new ScrollView()`**. `MdMenu` is the one place that still builds a stock one (§6).

**3. `flex-basis: 0` means *width* in a row and *height* in a column.** The grid cells set
`flex-grow: 1; flex-basis: 0` to split their **row** evenly. The full-width Sobre card is a
child of the **column** body, where the same `flex-basis: 0` means "height 0" — with
`min-height` also dropped, nothing held the card open and it collapsed to its padding, clipping
its own title. Any element that moves between a row context and a column context has to reset
`flex-grow`/`flex-basis`. The comment on `.mv-feature-card--wide` in `HomeScreen.uss` is the
worked example.

---

## 8. What to do next, in order

### 1. Build to a device — before Slice 4

Slice 4 puts a UI Toolkit overlay on the live AR camera, and it builds on an assumption nobody
has tested: the shell's panel is already at `sortingOrder = 100` over the uGUI `ScreenUI`
canvas, with a transparent `PickingMode.Ignore` root so taps fall through to the AR HUD. In the
editor that is fine. On hardware it is unverified. **Verify it before you build on it** — if the
stacking or hit-testing is wrong, Slice 4's whole approach changes.

While you are there, four slices' worth of editor-only work gets its first real check:

- [ ] **Nav bar over AR.** On the Mergulho route, the bar draws above the camera feed *and* is
      tappable; the AR HUD underneath still receives its own taps.
- [ ] **Safe-area insets on a notched phone.** The opaque page surface paints under the status
      bar / notch (not a transparent strip showing camera), content clears it, and the bar's
      bottom padding matches the home indicator.
- [ ] **AR resume is sub-second entering Mergulho**, and the frame-rate unlock happens leaving
      it (`ScreenManager` pauses `ARSession` and raises `Application.targetFrameRate` to the
      display rate on non-AR routes). Check battery/thermals on a long session too.
- [ ] **Inter legibility and pt-BR wrapping at real DPI** — the plan accepts the loss of
      `line-height`, so long strings are the risk. Worth checking the Home feature-card bodies,
      the conditions rows and the Praia detalhe advisory bar specifically.
- [ ] **A real photo pick → upload returns 200** against the production backend with App Check
      enforced. Exercises `GalleryPicker` on the device's gallery, the EXIF-preserving byte
      copy, `Idempotency-Key`, and the App Check header path end to end.
- [ ] **Queue a sighting, kill the app, relaunch.** The upload should resume and the pending
      feed should show it. This was a real bug — nothing instantiated `JobQueue` at launch, so a
      queued sighting sat on disk until the user submitted *another* one; it is fixed by
      constructing the reports adapter in `AppUiHost.Awake`. It has EditMode coverage
      (`ListPending_SurvivesARestart`) and **has never run on hardware**.

### 2. Then, in rough priority order

1. **Unblock Slice 5** — chase Decision D3 (SOS numbers and vetted first-aid copy). It is the
   only slice with a hard external dependency, so start the ask early even if you build
   something else meanwhile.
2. **Slice 4 — Mergulho AR HUD**, once the device check passes.
3. **A component-restyle pass**: `MdChip` (selected colours + radius 8) and `MdCard` (radius
   16), plus moving the `MdMenu` scroller fix into `MdMenu`. One pass, one set of regression
   tests, gallery shots updated.
4. **The submit-button width fix** (§6) — a one-line change worth doing next time you open
   `ReportScreen.uss`.
5. **A back affordance for pushed screens**, which also resolves the two-tap escape.
6. **Slice 6** — delete the dead uGUI screens and builders, and give Animais its home per D1.

The backend half of Slice 3 (persisting the six dropped fields) is deliberately **not** on this
list ahead of the privacy decision on `reporter_email` (§5).

---

## 9. Open questions

### For the designer

- **The shark-fin icon.** The long-pole asset. Nothing else can substitute it; three stand-in
  sites are waiting on it (§6).
- **The shark watermark in Home's lower third.** Present in the Figma render of Tela 7, not
  implemented. Is it decorative and droppable, or does it need to ship (and as what asset)?
- **The tide curve is teal.** The "Hoje" conditions card is a flattened bitmap in Figma, and its
  curve is a teal/cyan that appears nowhere in the approved palette — the implementation renders
  it from tokens instead. Which token should it be, or should teal enter the palette?
- **Risk-badge contrast.** V2's white on `#34C759` is ~2.2:1 and fails WCAG AA. The app uses an
  accessible dark green instead. Confirm, or supply a darker green for the badge.
- **The size buckets leave a hole.** "Menor que 1m" / "1m - 2m" / "Maior que 3m" — a 2.5 m shark
  has no bucket. Intentional, or should it be "2m - 3m" as a fourth chip / "Maior que 2m" as the
  third?
- Lower priority: the nav-bar and feature-tile glyphs are Material Symbols substitutes for the
  Flaticon set (Decision D7 option a) — worth a look-over for anything that reads wrong.

### For the project / biologists

- **SOS phone numbers and first-aid copy** (Decision D3). **Blocks Slice 5 entirely.** Wrong
  emergency numbers are worse than no SOS screen.
- **Beach content** — [beaches-content-todo.md](beaches-content-todo.md), 17 beaches. Slice 2
  is code-complete and content-empty until this lands (§5).
- **The `reef_shark` species mismatch** — is it *Carcharhinus acronotus* or *C. perezi*, and
  does the 3D model match whichever it is? (§5)
- **`reporter_email`** — is the app collecting it at all, and if so with what notice and what
  retention? Needed before the backend persists it (§5).
- **The DHN tide table** — publish the PDF, or drop the link from Home (§6).

---

## 10. What is not verified here

Stated plainly so it is not mistaken for confidence:

- **Nothing on a device.** Everything in this document is editor, batchmode and screenshot
  evidence.
- The **Figma frames** were read from the cached `.figma-sync/` snapshot (file version
  `2402812393380097744`, last modified 2026-09-24), not re-fetched. Run `tools/figma_fetch.sh`
  if the designer has moved since.
- The **test and screenshot counts** are from the last logged run on 2026-09-28; they are not
  continuously enforced by CI (there is no CI for the Unity project).
- **Pixel-level fidelity against the Figma renders was not measured.** The gaps in §6 are ones
  visible by eye in `.shots/` or provable from the source; a careful side-by-side of every frame
  would very likely find more.
