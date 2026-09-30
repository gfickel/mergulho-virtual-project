# Handover — V2 redesign (UI Toolkit)

**For:** the next person on this, developer or Claude Code session, who was not here for any
of it. You know Unity and C#; you have no memory of the decisions, the dead ends, or which
"bugs" are actually faithful to the design.

**The plan of record is [DESIGN_IMPLEMENTATION.md](../DESIGN_IMPLEMENTATION.md).** It says what
gets built, in what order, against which Figma frames, with every number transcribed. This
document does not repeat it — it says where the work actually stands, how to run it, what is
deliberately unfinished, and what to do next.

Written 2026-09-28; **revised 2026-09-29**, after Slice 6, the `MvStateView` half of Slice 5,
and a Figma-fidelity + responsive pass that moved most of the design system. The test and
screenshot numbers below come from `src/app/MergulhoVirtual/Logs/ds-test-*.xml` and
`.shots/manifest.json` from the last headless run; re-run them before trusting them.

**Nothing in any of it has been run on hardware.** Every green check in this document is an
editor, batchmode or rendered-PNG check. §8 is the list of what that leaves unproven — and the
2026-09-29 work put items *onto* that list as well as taking items off it.

---

## 1. Where things stand

Six of the seven delivery slices are done and the seventh is half done (§7 of the plan carries
the detail):

| Slice | State |
|---|---|
| **0 — Foundations** (brand tokens, Inter, shape, icons) | done |
| **1 — Shell + Início** (`MdRouter`, nav bar, HomeScreen) | done |
| **2 — Praias** (landing + Praia detalhe + 8 new components) | done; **content file is empty** |
| **3 — Avistamentos** (Reportar form) | done **front-end only**; backend untouched |
| **4 — Mergulho (AR HUD)** | done in the editor; **the on-device check it rests on is still owed** |
| **5 — SOS + states** | **half done**: `MvStateView` built and wired (conditions card, failed sightings); **SOS not started**, blocked on real emergency numbers (Decision D3) |
| **6 — Retire uGUI** | done 2026-09-29, **except `MainScreen`** — see §4 |

Verified green, headless, editor closed (2026-09-29, `Logs/ds-test-editmode.xml` +
`Logs/ds-test-playmode.xml` + `.shots/manifest.json`):

```
make ds-test        EditMode   683/683   (177 DesignSystem + 305 UI + 201 Assembly-CSharp-Editor)
make ds-test-play   PlayMode    27/27
make ds-shots                   98 PNGs at 390x844, 0 subjects skipped
  + device presets              49 subjects x 5 widths -> .shots-devices/   (see §2)
make ds-compile                 0 errors
```

### The 2026-09-29 fidelity + responsive pass, and what it did to the risk picture

Most of the day's work was an audit of the implementation against the Figma frames, plus the
first render of every screen at five device widths. It is worth knowing as a *handover* fact
rather than a changelog one, because it cuts both ways:

**It removed risk.** The AR HUD's tap-passthrough bug was found and fixed *before* any device
test — `ObjectInteraction` read `Pointer.current` with no UI check at all, so a tap on the
species card, its ⨯, or a row of the open beach menu **also** fired the 0.2 m SphereCast behind
it (the ⨯ could close the card and immediately re-open it). It now panel-picks via
`RuntimePanelUtils.ScreenToPanel` + `panel.Pick` first (§8). Every screen was rendered at
360×640, 360×800, 390×844, 412×915 and 430×932; **nothing overflowed or clipped at any width**
and four 360 dp text-reflow defects were fixed (§6).

**It added risk, and this is the part that matters here.** Unity renders in **linear colour
space** (`ProjectSettings.asset` `m_ActiveColorSpace: 1`) while Figma blends in sRGB, so every
alpha transcribed off a Figma layer was compositing too light. Correcting it changed how a
number of overlays *look*, in places dramatically — the AR species card's hairlines and its ⨯
disc are now much fainter, which is faithful to the design and is nonetheless a real change in
feel that **no one has seen on a real screen, in daylight, over a live camera feed.** Separately
the icon font was re-subset at `FILL: 1`, so **every icon in the app changed face** from
outlined to solid. Both are now near the top of §8.

### Code-complete is not shippable here

Five independent things stand between this and a release, and none of them is code:

- **Beach content is blank.** `beaches_content.json` has **0 of 17** beaches with a risk level,
  best season, sighting peak, lifeguard hours or tips, and **3 of 17** with a species list.
  **14 of 17** beach detail pages therefore render as a hero photo, a title and a description;
  three of them (Conceição, Meio, Porto de Santo Antônio) have no editorial content at all.
  What *is* filled — `environmentTags` 11/17, `advisories` 7/17, `idealTide` 4/17 — was derived
  from `places.json` descriptions and the AR spawner list, not authored by anyone who knows
  the beaches. See §5.
- **SOS is placeholder.** No screen exists and no real phone numbers exist. Decision D3 is
  open and blocks the SOS half of Slice 5 outright (the `MvStateView` half shipped without it).
- **Two icon sites still render a stand-in** for an asset the designer owes (§6), and
  `MvStateView`'s illustration slot renders empty waiting for a second one (§9).
- **Two of the five 3D models are licensed CC BY-NC.** `hammerhead` (Jer Bot / CC BY-NC) and
  `reef_shark` (DigitalLife3D / CC BY-NC 4.0) — read the `modelCredit` field on each `.asset`.
  Non-commercial is fine for a free public-good app and is a **licensing problem the moment the
  app is distributed commercially**, in any store listing that charges or carries ads. Surfaced
  on 2026-09-29 while normalising the credit lines; nobody has decided about it (§5, §9).
- **Nothing has been run on a device.** Every green check above is an editor check. The AR
  overlay, the safe-area insets, Inter at real DPI, the newly-filled icon face, the
  colour-space-corrected overlays, and a real photo upload against production App Check are all
  unverified. §8 has the checklist.

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

**`ds-icons` before `ds-setup`, always.** `ds-icons` rewrites the TTF; `ds-setup` bakes the
TextCore SDF font asset *from* that TTF. Run them the other way round and every render still
shows the old glyphs, with nothing to tell you why. (This is exactly how the `FILL: 1` icon
change lands — §6.) Both must precede any screenshot run.

**`ds-setup` and `ds-shots` dirty the LFS-tracked font assets.** `Assets/DesignSystem/Fonts/*.asset`
grow whenever a new glyph is rasterised into the SDF atlas, so they show up modified after a
render you thought was read-only. They are regenerated output — commit them or discard them,
but do not go looking for the edit that caused it.
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
- **`.shots/` is never cleaned by the target.** Renamed or deleted subjects leave their old
  PNGs behind forever, and a stale file looks exactly like a current one. Trust
  `manifest.json` (or the `UI-SHOTS-SUMMARY written=N skipped=N` line the Makefile prints),
  not `ls | wc -l`. The directory was wiped by hand on 2026-09-29 — the 8 stale
  `beaches-*.png` from the pre-Slice-2 screen that this bullet used to warn about are gone —
  so it is clean *now*, which is not the same as self-cleaning.

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

### Rendering the device matrix — same harness, environment variables

There is **no separate make target.** `UiScreenshotHarness` reads `MV_SHOT_WIDTH`,
`MV_SHOT_HEIGHT`, `MV_SHOT_SCALE`, `MV_SHOT_TOP_INSET`, `MV_SHOT_BOTTOM_INSET`, `MV_SHOT_DIR`,
`MV_SHOT_THEMES` and `MV_SHOT_FILTER`, so a device frame is one line:

```bash
MV_SHOT_WIDTH=360 MV_SHOT_HEIGHT=640 MV_SHOT_TOP_INSET=24 MV_SHOT_BOTTOM_INSET=0 \
  MV_SHOT_THEMES=light MV_SHOT_DIR="$PWD/.shots-devices/android-360x640" make ds-shots
```

The five frames are the ones in **`DesktopReviewMode.Presets`**
(`Assets/Scripts/UI/UiToolkit/DesktopReviewMode.cs`), so the exe, `.shots/` and this matrix
agree on one list. Insets travel with the frame because they are a property of the device:

| Preset | Frame | Insets | Stands for |
|---|---|---|---|
| `android-360x640` | 360 × 640 | 24 / 0 | small 16:9 — the stress test |
| `android-360x800` | 360 × 800 | 24 / 0 | the common budget device |
| `v2ref-390x844` | 390 × 844 | 47 / 34 | what everything was transcribed at |
| `pixel-412x915` | 412 × 915 | 24 / 0 | Pixel 7/8 class |
| `iphone-430x932` | 430 × 932 | 47 / 34 | the large-notch case |

Output lands in **`.shots-devices/<preset>/`** (gitignored beside `.shots/`), light theme only,
49 subjects each. The two 360 presets render pixel-identically apart from viewport height.

**The thing to understand before reading any of it:** both PanelSettings use **Constant
Physical Size at 160 dpi**, so `1 USS px = 1 dp` and the layout does **not** scale on a
different phone — a narrower device simply gets less width and has to reflow. **Pixel
resolution is therefore a non-issue.** Usable dp width and safe-area insets are the entire
risk. [android-adaptivity.md](android-adaptivity.md) works that through, including the two DPI
failure modes and the one cheap on-device readout that would settle them; the 2026-09-29 pass
is the wider re-run of its Finding 1 (all 49 subjects × 5 frames, where it did 4 shell screens
× 3 frames).

### Verification order per change

1. `make ds-compile`
2. `make ds-test` + `make ds-test-play`
3. `make ds-shots` and **actually look at the PNGs.** Not optional after any change to
   flex direction, wrapping or typography: on 2026-09-29 a fully green suite (726 tests at the
   time) coexisted with a visibly broken Reportar screen — splitting "(opcional)" into its own
   label made the section title a flex-row child, it inherited `flex-shrink: 1`, "Se
   identifique" wrapped to two lines and collided with the "Seu nome" field. **Structure tests
   cannot see layout.** That is also the honest argument for §8: the automated gates prove less
   than their green count suggests.
4. **Gallery first for visual bugs.** If a bug reproduces in `gallery-*.png` it is a component
   bug — fix the component and add a regression test. If it does not, it is screen wiring.
5. Render the 360 dp frame for anything that adds text or a horizontal row (above).
6. Device build for anything the editor cannot judge (§8).

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
interfaces in `Assets/UI/Interfaces/` — currently `IBeachCatalog`, `IBeachContent`,
`IConditionsService`, `ITideService`, `IActiveBeach`, `IBeachOverride`, `ISpeciesCatalog`,
`IOnboardingState`, `ISightingReports`, `IPhotoPicker`, `IConnectivity`, `IArSelection`,
`ISpeciesModelViewer`, `IVideoPlayback` — and the mapping to the real engine types lives in
exactly one file, `Assets/Scripts/UI/UiToolkit/UiServiceAdapters.cs`, on the Assembly-CSharp
side. The last three are the interesting ones for §8: they are the only paths from a screen to
the AR raycast, the 3D turntable rig and the video decoder, **and all three are unexercisable
in batchmode**, so nothing behind them has ever run.

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
  that is the deliberate state of `AppRoutes.Sos` today. (`AppRoutes.Especie` was in that
  list until 2026-09-29, when Decision D1 landed as `EspecieScreen` — and it is the one
  route that carries a **payload**: `PraiaDetalheScreen.SpeciesRequested` raises a species
  key, `AppUiHost.OnSpeciesRequested` sets the ViewModel and only then pushes.)
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

## 4. What is left of uGUI after Slice 6

**`ScreenUI` holds exactly four GameObjects now:** `Panel` (the always-on debug overlay),
`SplashScreen`, `MainScreen` and `AboutScreen`. Slice 6 (2026-09-29) deleted `BottomNav`,
`BeachesScreen`, `RegisterScreen` and `AnimalsScreen` from `MainScene` together with their
controllers and Editor builders, and `AppUiBuilder.UnroutedLegacyScreens` went with them —
there is no list of screens-to-keep-deactivated any more. `ScreenManager` has had no screen
fields since Slice 1.

Also deleted, because their only user was one of those four objects: `ListItemView` +
`Assets/Prefabs/UI/ListItem.prefab`, `ConditionsCardView`, `TideSparkline`, `MaxWidthClamp`,
`BeachSelectorDropdown`, and the uGUI inline-video spawner pair `VideoSection` +
`VideoSectionBuilder`.

**Five things that look dead and are not** — check before you reach for any of them:

| Kept | Live consumer |
|---|---|
| `AnimalViewerRig` (scene root, inactive) | `EspecieScreen` via `UiServiceAdapters.SpeciesModelViewerAdapter`; wired by `AppUiHost.animalViewerRig`. Its construction was extracted out of the deleted `AnimalsScreenBuilder` into **`Assets/Editor/AnimalViewerRigBuilder.cs`** (`Tools > Mergulho Virtual > Create Animal Viewer Rig`) so it stays reproducible. Re-run `make ui-setup` after rebuilding it. |
| `VideoPlayerController`, `PointerHeldFlag`, `VideoRef` | the About screen's Instagram card (`InstagramWidgetBuilder`). UI Toolkit video goes through `IVideoPlayback` instead — do not resurrect the uGUI path. |
| `AspectCover` | `ScreenUI/SplashScreen/Image` and `ScreenUI/AboutScreen/Image`. |
| `UI/RoundedRect` shader + `RoundedRectCard.mat` | the Instagram card. (`MainScreen/ArTuning` was its other consumer until that subtree was deleted on 2026-09-29.) |
| `Assets/RenderTextures/AnimalViewer.renderTexture` | `AnimalViewerRig/ViewerCamera` — the descriptor template `SpeciesModelViewerAdapter` clones per species. |

Still routed, on purpose: `AboutScreen` (Sobre, per Decision D2 — this is what keeps the
shipped Instagram widget reachable with no rebuild).

**`MainScreen` was not retired in Slice 6 — and is now an empty shell.** It was a third case
since Slice 4: not routed, but not unrouted either. Its `TopBar` is deactivated (that is what
`MergulhoScreen` replaced) and the rest of the subtree was switched on and off with the Mergulho
route by `AppUiHost.OnRouteChanged`, because it carried **ArTuning**, the on-beach panel that
tuned the AR stabilisation filters live. **That panel and its JSON persistence were deleted on
2026-09-29** (the decision was "drop", not "port"), so `MainScreen` now holds nothing but the
deactivated `TopBar` and the route toggle achieves nothing. Retiring it is unblocked and still
pending: delete the GameObject, `AppUiHost.legacyArOverlay` and its `OnRouteChanged` branch,
`AppUiBuilder`'s `mainScreen` lookup + `LegacyArOverlayChildrenToHide`, and
`ConditionsPillView`/`BeachNameView`. Do not put it back on the router either.

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

### The species catalog — what is blank and what is wrong

**`approximateSize`, `diet` and `behaviour` are blank on all five `AnimalDef` assets.** Slice 4
added the three fields for the AR info card's spec rows and mirrored them onto `SpeciesInfo`;
nobody has authored a value. Both consumers drop a row they have no value for, so **the AR
species card and the Espécie screen ship with no spec table at all** — name, binomial and
description only. Fifteen short strings (5 species × 3 fields) would light both up; they are
species facts, so they want the biologists, not a guess.

~~**`hammerhead.asset`'s `modelCredit` is not a credit line.**~~ — **fixed 2026-09-29.** It
held the full ~180-word Sketchfab *description* of the model (the Bimini photogrammetry
write-up, over 20 YAML lines) and rendered as the largest element on the Espécie page. All five
assets are now one line on the `photoCredit` convention — `Modelo: <author> / <licence>
(<source>)` — with no author name or licence identifier lost.

⚠️ **Normalising them surfaced a licensing problem: two of the five models are non-commercial.**
`hammerhead` is *Jer Bot / CC BY-NC (Sketchfab)* and `reef_shark` is *DigitalLife3D / CC BY-NC
4.0 (Sketchfab)*; the other three are CC BY 4.0. That is fine for a free public-good app and
**blocks any commercial distribution** — a paid listing, ads, or a sponsor build. It is a
project decision, not a code one: either keep the app non-commercial, obtain a commercial
licence from those two authors, or replace the two models. Nobody has been asked (§9).

**`lemon_shark.asset`'s two videos are the same file.** `"Tubarão-limão em ação"` and
`"Tubarão-limão (vídeo 2)"` both point at
`…/conteudos-educacionais/videos/reelsvideo.io_1780764982422.mp4`, so Espécie renders two cards
that play identical content. Either the second URL was never filled in or the entry is a
duplicate; lemon_shark is the only species with videos at all, so this is the whole of the
video content.

### The species catalog also has one wrong record

`Assets/Resources/Animals/reef_shark.asset` reads `displayName: Tubarão-bico-fino`,
`binomial: Carcharhinus acronotus` (blacknose shark), with a blacknose description — but its
`modelCredit` is *"Model 54A - Caribbean Reef Shark"*, the FBX and every texture in
`Assets/Models/reef_shark/` are `CRS`-prefixed (Caribbean Reef Shark), and
[sharks-noronha-models.md](sharks-noronha-models.md) lists the species as *Carcharhinus perezi*.
The binomial was added recently and contradicts the model, the credit and the project's own
species doc. `AnimalDef`'s own comment says the field is deliberately blank where identification
is open, because a wrong binomial is worse than none. **Someone who knows the species has to
say which it is**; the asset shows on the Praia detalhe species card, the Reportar species
chips, the AR info card and the Espécie screen.

---

## 6. Known gaps and deliberate deviations

Read this section before "fixing" anything. Roughly half the visible oddities are the design.

**A ~~struck-through~~ heading means the bug is fixed and the entry is kept on purpose** — either
because the cause recurs, or because someone comparing against an older screenshot needs to know
what moved. Nothing here is deleted just for being done.

### Bugs — the fixed ones and the ones still open

~~**Submit button on Reportar is not full width.**~~ — **fixed 2026-09-29.** Kept here because
the cause recurs for any full-width `MdButton`. V2 draws a 358 dp bar; it rendered as a pill
hugging its label, centred. `ReportScreen.uss`'s `.mv-report__submit { align-self: stretch }`
does stretch `MdButton`'s transparent 48 dp touch-target root (the screen sheet is on the
document root, so it outranks the theme's `align-self: flex-start`), but the *visible* surface
is `.md-button__container`, which had no `flex-grow` and so hugged its content inside a
full-width invisible parent. The `.mv-report__submit .md-button__container` rule set height
and radius but never width. The fix was `flex-grow: 1` on that container rule — **local to the
screen**, exactly as `MvStateView.uss` does for its own action, and deliberately *not* a change
to `MdButton`'s default, which would widen every hugging button in the app. The changed region
in all four `report-*.png` shots is exactly 358×47 dp at the 16 dp gutter and nothing else on
the screen moved. `PraiasScreen`'s "Ciência cidadã" CTA looks similar but is **not** the same
bug: V2 draws it as a 36 dp hugging pill, and it is correct as-is.

~~**Every pill in the app renders as an ELLIPSE, not a stadium.**~~ — **fixed 2026-09-29.**
`--md-sys-shape-corner-full` is `1000px`, and UI Toolkit clamps the corner radius **per axis**
(`rx = min(r, width/2)`, `ry = min(r, height/2)`) rather than scaling both by one factor the way
CSS does, so a 295×42 pill got 147×21 corners — a perfect ellipse. Every pill now carries an
explicit half-height radius (`MvHeroHeader`'s selector 21px, Praia detalhe's floating SOS 26px)
or, where the height is content-driven, `MdShape.KeepStadium`, which reads the resolved height
at runtime (`MvTag`). **The four surviving `corner-full` uses are genuinely square elements** —
`MdIconButton` 40×40, `MdCheckbox` 40×40, `MvNumberedList` 24×24, `MvMediaPicker`'s remove badge
28×28 — where the token produces a correct circle. Two new suites guard it: `ShapeDisciplineTests`
(EditMode, fails a `corner-full` on a known-wide element) and `StadiumCornersTests` (PlayMode,
covers `KeepStadium` including a height change). Verified by fitting rendered corner curvature
against a circle model, not by eye. **Kept in this section because the token is still `1000px`**
and reaching for it on a wide element will silently reproduce the bug.

~~**`MdChip` selected is the wrong colour family.**~~ — **fixed 2026-09-29**, as the one
component restyle that could not wait: selected fill is now `primary` / `on-primary` (navy +
white, which V2 and the M3 spec agree on) instead of `secondary-container`, radius is
`corner-extra-small` (8), horizontal padding 12. The fix that mattered most was not cosmetic —
**`MdChip.cs` injected a check glyph into ANY selected chip regardless of `Kind`**, so an
*Assist* chip grew 18.5 dp the instant you tapped it and re-flowed the row under your finger.
It is now gated on `Kind == Filter`. ⚠️ The old asymmetric `9/17` padding pair was
**intentional** (it assumed a selected chip always carried an icon); removing the injection
broke that assumption, so it was re-derived as a symmetric no-icon pair plus a
higher-specificity `.md-chip--selected.md-chip--with-icon` rule — do not "simplify" it back.

**`MdCard` is still radius 12** (`corner-medium`) where V2 cards are 16. Real design-system
debt, **but invisible today**: `MdCard` is instantiated nowhere in the app — only in the gallery
and its tests — because every V2 screen authors its own card surface at `corner-large`. It is
what is left of the restyle pass now that `MdChip` and the pills are done; expect the gallery
shots to change.

**`MdMenu`'s scrollbar is hidden by a workaround in the wrong place.** `MdMenu` builds a stock
`ScrollView`, and with 18 entries the beach picker always overflows, so it would be the one
place in the app showing UI Toolkit's desktop scrollbar-with-steppers.
`PraiasScreen.OpenBeachMenu` queries the menu's `ScrollView` and sets `ScrollerVisibility.Hidden`
from C# (scroller visibility is an inline style USS cannot reach — §7). **Still true after the
2026-09-29 `MdMenu` restyle**, which fixed the menu being nearly invisible (it was
`surface-container` on a `background` page with no border, no shadow, and a `.md-menu__scrim`
class that was **styled in no `.uss` file in the project**; it is now `surface` + a 1 px
`outline-variant` + a real scrim) but did not move the scroller fix. Move it when you next touch
the component.

**Escaping a pushed screen costs two taps.** `MdNavigationBar.SelectedIndex` returns early when
the value is unchanged, so it raises no event — and while a sub-screen is pushed the bar still
shows the *originating* tab selected. Re-tapping that tab therefore does nothing; you have to
tap a different tab and come back. Praia detalhe hides this behind its hero back button.
**Sobre does not: it is a `LegacyUguiScreen` with no back affordance of its own**, so it is
currently the worst case. Resolves when pushed screens get a real back affordance (an
`MdTopAppBar` with a nav icon, the way `ReportScreen` already has one and hides it) — or, more
cheaply, by making the bar raise its event on a re-tap of the active destination.

**The `reef_shark` binomial contradicts its model** — see §5.

### Two changes that moved how the app LOOKS, on purpose

Both landed 2026-09-29, both are corrections rather than restyles, and both are the reason a
screenshot you remember from last week will not match one you take today. **Neither has been
seen on hardware** (§8).

**1. Every Figma-transcribed alpha was compositing too light, and has been corrected.** Unity
renders in **linear colour space** (`ProjectSettings.asset` `m_ActiveColorSpace: 1`): a USS
colour is sRGB, converted to linear, blended, converted back. **Figma blends directly in sRGB.**
So an overlay authored at the alpha the designer wrote did not reproduce the designer's
composite. The model was validated, not assumed — `camera-fill` white @0.10 over `#050B14` was
predicted at (90,90,92) and measured (90,91,92) in the render.

Two things decide how each site was fixed, and both matter if you touch one:

- **The direction depends on the overlay.** A *light* overlay on a dark ground needs its alpha
  cut ~3.5× (0.15 → 0.043); a *dark* overlay on a light ground needs it *raised* ~1.7×
  (0.25 → 0.43). Never assume "reduce".
- **Per-channel corrected alphas diverge**, so **no single alpha reproduces Figma exactly.**
  Where the backdrop is opaque and known, pre-blending to an opaque token is exact — that is why
  the `camera-*` family is now a set of literal hexes with their derivation tabulated in
  `Tokens/_brand-light.uss`, and why `camera-divider` and `camera-fill-outline` had to be added
  (`camera-outline` and `camera-outline-dim` each served two different opaque backdrops, and one
  alpha can serve both where one pre-blend cannot). Where the backdrop is arbitrary — a photo, a
  video frame — a real alpha is kept at the corrected value (`MvHeroHeader`'s scrim 0.35 → 0.505,
  compact 0.25 → 0.38).

⚠️ **The AR species card is where this is most visible and least verified.** Its hairline
(`camera-outline`), its internal rule (`camera-divider`) and its ⨯ disc (`camera-fill` +
`camera-fill-outline`) all got substantially **fainter** — `camera-outline` was rendering
(108,109,110) and is now `0x2A3037`. That is faithful to V2. It is also the opposite direction
from "make the HUD legible in sunlight", and the card sits over a live camera feed the
screenshot harness does not render at all. See §8.

The biggest single miss the sweep caught was **`MdNavigationBar`'s inactive destinations**:
`inverse-on-surface` at `opacity: 0.5` rendered (183,184,185) against the designer's measured
(127,130,136), so inactive tabs read nearly as bright as the active one and the selected-state
signal was all but gone. They now use a new `inverse-on-surface-muted` token, and `--active`
swaps ink **colour** rather than opacity.

Several alphas were deliberately **not** corrected, each for a stated reason in the source: M3
state layers (relative interaction feedback, not transcriptions), `MdDialog` / `MdBottomSheet`
scrims (M3 spec constants, and V2 has no dialog or sheet frame to match), `MdMenu`'s scrim (a
menu is not modal — under linear blending its 0.32 renders as the light non-modal veil a
dropdown actually wants; it coincides with `MdDialog`'s number while meaning something
different, so **do not unify the three**), and `MdSparkline`'s own defaults (the Figma card is a
flattened bitmap, so there is no authored alpha to transcribe).

⚠️ **One site is a genuine unknown, not a decision: `MvStateView`'s icon at `opacity: 0.62`.**
Its comment claims the composite lands within ~6/255 of the designed swatch — but it is
unknowable whether that was computed in sRGB (in which case 0.73 is right) or read off a
rendered PNG (in which case 0.62 already is). **Unlike every other site there is no recorded
Figma hex to check against.** This needs the designer's swatch, not a guess (§9).

**2. The icon set changed face — outlined to filled.** `AXIS_PINS` in
`tools/design_system/subset_material_symbols.py` went `FILL: 0` → `FILL: 1`. MdIcon had been
shipping Material Symbols **Outlined** where every V2 glyph is a **solid** mark; measured amber
ink coverage in the Home feature tiles was 8/8/13 % against Figma's 25/16/33 %. Validated
offline with fontTools first: **advance widths are byte-identical (960/960 on every glyph)**, so
there is no layout shift and no `MdIconGlyphs.gen.cs` churn — but **48 of 95 icons gain ink, up
to 4×** (`visibility` 10.5 → 34.5 %, `location_on` 14.6 → 37.0 %). This is a change to *every
icon in the app* and it has only ever been seen in a PNG. `wght` (100–700) is the second lever
if the filled face still reads too light; measure the FILL=1 result before reaching for it.

### 360 dp — what was fixed, what is accepted, and the one thing still wrong

The 2026-09-29 pass was the first time any screen was rendered below 390 dp beyond the four
shell frames in [android-adaptivity.md](android-adaptivity.md). **412 and 430 are shippable
as-is** — 16 dp gutters held everywhere, both grids split the extra width evenly, hero overlays
and bottom-anchored elements were correct under both inset pairs, and no screen stylesheet
contains a fixed-width offender. **At 360, nothing overflowed or clipped either**; every failure
was text reflow, and four were fixed:

1. **`MvHeroHeader`'s selector pill** — 98 dp of fixed chrome left only a 170 dp label slot at
   360. Chrome trimmed to 88 dp (inner gutters 16→14, pin gap 8→6, chevron gap 12→8) **and** the
   label reduced 16→15 px. Truncation went 2 names → 1 at 360 and 1 → 0 at 390. The deviation is
   documented in full as "deviation 4" in the component's own header.
2. **`PraiaDetalheScreen`'s species name row** — `Tubarão-bico-fino` + `Carcharhinus acronotus`
   measured 316.8 dp against 294 dp of content, and `align-items: flex-end` bottom-aligned the
   binomial to the name's *last* line, stranding "fino" beside it. The common name is now pinned
   whole (`flex-shrink: 0` + `nowrap`); the decorative binomial ellipsises instead.
3. **`ConditionsFormatter`'s Maré row** — the space inside `(2.2 m)` is now U+00A0, so the string
   can no longer break as `… (2.2` / `m)`. This also fixed a pre-existing wrap at 390.
4. **`BeachContentFormatter` / the Praias tide stat** — value 18 px → 16 px
   (`title-small-increased`) **and** an NBSP after the `·`. At 18 px the separator was stranded
   in three of four cases, including at 390.

⚠️ **Two of those are deliberate deviations from the transcribed spec at 390 and want designer
sign-off** (§9): the hero pill label is **15 px** where V2 says 16, and the Praias stat value is
**16 px** where V2 says 18. Both are in-ladder sizes and both fix pre-existing 390 dp wraps — but
they are type *reductions*, and small type is exactly what degrades on a bright beach.

⚠️ **"Praia da Cacimba do Padre" still truncates at 360**, as `Praia da Cacimba do P…` — one of
the 17 names. The visible prefix was checked to be unique among all of them, so it is legible
rather than ambiguous, but it is the one known unresolved reflow.

**Accepted at 360, not bugs:** the Reportar size-chip row wraps to two rows (chips need 343.4 dp
against a 328 dp box; it degrades gracefully and every way to close the gap costs more than it
buys — chip padding 12→10 still wraps at 331.4); the Praias location name wraps to two lines;
and the Home feature-grid icon misalignment widens to ~12.5 dp, which is a consequence of the
deliberate `justify-content: center` over unequal body line counts.

⚠️ **A flag that was raised and then retracted — do not re-raise it.** The **Lua row looks like
it has the same dangling-`·` bug and does not.** Word wrap takes the last break that *fits*, not
the first: `Gibosa Minguante · 100%` is only 168.9 dp inside the 212 dp column, so the break
lands after the percentage and the separator stays mid-line. An NBSP there would be a no-op, and
actively harmful if the column ever narrowed (it would force a line starting with a leading
`·`). A comment on `ConditionsFormatter.Moon` records this, so the next person who measures
236.1 > 212 and reaches for the same fix stops.

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
- ~~**"Saiba mais sobre a espécie"**~~ — **no longer inert.** Decision D1 landed 2026-09-29:
  the link raises `PraiaDetalheScreen.SpeciesRequested` with the species key and opens
  `EspecieScreen` (description, 3D turntable, inline videos, credits). Two things about it are
  still open: **it has no Figma frame**, so the layout is designed rather than transcribed and
  wants a designer's review (DESIGN_IMPLEMENTATION.md §8.8); and **the 3D viewer and the video
  playback have never run** — neither can be exercised in batchmode (no rig, and Unity's Linux
  VideoPlayer cannot decode H.264), so both need an Android build. §4 also lists the AR info
  card as a second entry point; that one is NOT wired, because Tela 8 draws no such link.
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
  stand-in — `"help"`, at `GalleryController.cs:553` — sits in the gallery's nav-bar demo and has
  drifted from the other two; fix all three when the SVG arrives.
  *Correction, 2026-09-29:* it has been said (here and in CLAUDE.md) that plausible marine glyphs
  are "excluded from the icon subset so none can quietly become the shipped icon". **That is not
  true of the font** — `waves`, `pool`, `scuba_diving` and `surfing` are all in
  `tools/design_system/material_symbols_icons.txt` and all ship. The guarantee is real but it
  lives one level up, at the **consumer**: both sites read the single constant
  `AppIcons.AvistamentosPlaceholder`, so the icon can only change in one place. Fix the claim,
  not the file.

### Screenshot-harness artifacts (not app bugs)

- `shell-praia-detalhe-*.png` shows **no active tab**, because the harness `Navigate`s straight
  to the sub-screen instead of `Push`ing from Praias. In the app, Praias stays lit.
- `.shots/` is not self-cleaning, so a renamed or deleted subject leaves its PNG behind
  indefinitely and it looks identical to a current one. (The 8 pre-Slice-2 `beaches-*.png` this
  bullet used to name were cleared by hand on 2026-09-29.) Trust `manifest.json`.
- `.shots-devices/*` is **light theme only** and one frame per subject — the dark theme is only
  rendered at 390 dp. Nothing is known about dark at any other width; nothing suggests it
  differs, since the reflow is a function of dp width and not of palette.

---

## 7. Project-wide footguns

These cost real time. All are already solved; the solutions are invisible unless you know they
exist, and each is easy to undo by accident.

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

**4. A USS alpha is not the alpha Figma drew.** Unity blends in linear space, Figma in sRGB, so
a transcribed alpha always composites too light — and the correction's *direction* depends on
whether the overlay is lighter or darker than its ground. Worked through in §6; the reason it
belongs here too is that it is invisible, it applies to every new overlay anyone adds, and the
natural instinct ("the designer wrote 0.35, so write 0.35") is the wrong one. Pre-blend to an
opaque token where the backdrop is known; correct the alpha where it is not.

**5. A `#RRGGBB` literal inside a USS *comment* fails `TokenDisciplineTests`** exactly like a
real declaration would — the check scans raw file text and cannot tell a comment from a rule.
Write hex in comments as `0xRRGGBB`. This broke the build once; 28 comment literals across four
stylesheets had to be rewritten.

---

## 8. What to do next, in order

### 1. Build to a device — still the top of the list, and the list got *longer*

Slice 4 shipped a UI Toolkit overlay on the live AR camera **on an assumption nobody has
tested**: the shell's panel is at `sortingOrder = 100` over the uGUI `ScreenUI` canvas, with a
transparent `PickingMode.Ignore` root so taps fall through to the AR scene. In the editor that
is fine. On hardware it is unverified, and the whole screen rests on it — if the stacking or
hit-testing is wrong, Slice 4's approach has to change.

**The 2026-09-29 pass changed this list in both directions**, and the handover point is that
the net movement was *not* downward. It retired most of the layout-and-reflow questions (§D
below, all answered in headless renders), and it found and fixed one real AR bug before any
device saw it — but it also **changed how the app looks** in ways only a PNG has ever judged,
which is now the top of the list.

**What the automated gates prove, and what they do not.** 683 EditMode tests, 27 PlayMode and
343 rendered PNGs is real coverage of structure, formatting, token discipline and reflow. It
is *not* coverage of appearance: on 2026-09-29 a fully green suite (726 tests at that moment)
coexisted with a Reportar screen whose section title had wrapped into the field below it. And
**the screenshot harness renders no camera feed at all** — the AR HUD's background in every
shot is flat black — so nothing about the one screen that is composited over live video has
ever been seen composited over anything.

#### A. The AR HUD — highest risk, and now less certain than it was

- [ ] ⚠️ **Is the species card still legible over bright, sunlit, moving water?** This is the
      item that got *worse* on 2026-09-29. The card ships opaque because V2's 77 %-plus-blur
      cannot be reproduced — and the linear-colour-space correction (§6) then made its chrome
      **substantially fainter**: the card hairline, its internal rule and the ⨯ disc all
      dropped (`camera-outline` was rendering (108,109,110), now `0x2A3037`). That is faithful
      to Figma and it is the opposite direction from "readable on a beach at midday". Judge it
      outdoors, on real water, not indoors. If the chrome vanishes, the answer is a
      camera-specific override — not undoing the colour-space correction, which is right.
- [ ] **The AR camera is visible through the screen.** Everything but the control strip and the
      species card must be transparent; `mergulho-light.png` shows black where the camera
      should be.
- [ ] **Tapping a shark opens the card.** Exercises a path that had never worked before Slice 4:
      `BeachSharkSpawner` → `ArSpeciesTarget.Attach` (which adds the collider the prefabs do not
      have) → `ObjectInteraction`'s sphere cast → `ArSelectionAdapter` → `MergulhoViewModel`.
      Any one of those links failing looks identical from the outside: nothing happens.
      `ObjectInteraction.verbose` logs every cast.
- [ ] **Tapping the card, its ⨯, the pill or the back arrow does NOT also hit the animal behind
      it** — and tapping open water still *does* reach the AR scene. **Confirm a fix, no longer
      discover a bug:** `ObjectInteraction` read `Pointer.current` with no `EventSystem` check
      and no UI Toolkit panel pick, so every tap on the HUD also fired the 0.2 m SphereCast
      behind it and the ⨯ could close the card and immediately re-open it. It now panel-picks
      (`RuntimePanelUtils.ScreenToPanel` + `panel.Pick`) before raycasting, with an optional
      `uiPanelSource` `UIDocument` wired by `AppUiBuilder` and a cached `FindAnyObjectByType`
      fallback. Open water still passes through because the HUD root, hero and dock are
      `PickingMode.Ignore` by design — **that is the half most likely to be wrong on hardware**,
      so test both directions, not just the card.
- [ ] **No stray uGUI chrome is left on the AR route.** The "AJUSTE AR" pill and its panel were
      deleted on 2026-09-29, so nothing at 72 % of the screen height on the right should draw or
      accept a tap any more; `ScreenUI/MainScreen` is an empty shell that still toggles with the
      route.
- [ ] **Nav bar over AR.** On the Mergulho route, the bar draws above the camera feed *and* is
      tappable; the AR HUD underneath still receives its own taps.

#### B. What the 2026-09-29 pass changed, that only a PNG has judged

Every item here is new to this checklist. None of it is a bug report — each is a deliberate
change whose *effect on a real screen* is unknown.

- [ ] ⚠️ **Every icon in the app changed face**, outlined → filled (`FILL: 1`). 48 of 95 glyphs
      gain ink, up to 4× (`visibility` 10.5 → 34.5 % coverage). Advance widths are
      byte-identical, so nothing moved — but the whole app's iconography now reads heavier.
      Look at the bottom bar and the Início feature tiles first.
- [ ] ⚠️ **The colour-space-corrected overlays**, beyond the AR card: the hero scrims
      (0.35 → 0.505, compact 0.25 → 0.38), the Home sparkline fill and baseline, the freshness
      line, `MvTag--on-image`, `MvMediaPicker`'s remove badge. All are lighter-over-photo or
      darker-over-light cases where the correction went in opposite directions; the sanity check
      is that hero text stays readable over the *brightest* beach photo in `Resources/`.
- [ ] **The bottom bar's inactive/active contrast.** Inactive destinations moved from
      `inverse-on-surface` @0.5 to the `inverse-on-surface-muted` token, which is the single
      largest colour change in the pass — the selected-state signal had been nearly erased.
      Confirm the selected tab is obvious at arm's length.
- [ ] ⚠️ **`MvStateView`'s status glyph at `opacity: 0.62`** — genuinely unknown, not a judgement
      call (§6, §9). Worth an eye on device, but the real answer is the designer's swatch.
- [ ] ⚠️ **The two deliberate 390 dp type reductions** — the hero selector label at 15 px (V2:
      16) and the Praias tide stat at 16 px (V2: 18). Both fix real wraps; both are *smaller*
      type, and small type is exactly what degrades outdoors. Read them in sunlight before
      asking the designer to sign off (§9).
- [ ] **`MdChip` selected**, now navy + white with no injected check glyph. The glyph injection
      grew a chip 18.5 dp on tap and re-flowed the row under the user's finger — worth one tap
      on the Reportar species and size rows to confirm nothing shifts.

#### C. Five slices of editor-only work, still awaiting a first real check

- [ ] **Safe-area insets on a notched phone.** The opaque page surface paints under the status
      bar / notch (not a transparent strip showing camera), content clears it, and the bar's
      bottom padding matches the home indicator. (Both inset pairs — 47/34 and 24/0 — are
      rendered now, but from *injected* numbers; `Screen.safeArea` itself has never been read on
      a real device.)
- [ ] **AR resume is sub-second entering Mergulho**, and the frame-rate unlock happens leaving
      it (`ScreenManager` pauses `ARSession` and raises `Application.targetFrameRate` to the
      display rate on non-AR routes). Check battery/thermals on a long session too.
- [ ] **Inter legibility at real DPI**, now that *wrapping* is covered headlessly (§D). The
      plan accepts the loss of `line-height`, so the remaining question is purely optical
      density: the Home feature-card bodies, the conditions rows and the Praia detalhe advisory
      bar are the dense spots.
- [ ] **The `Screen.dpi` question.** `1 USS px = 1 dp` only holds if Unity's Android backend
      reports Android's bucketed `densityDpi` rather than the panel's physical DPI. It is a
      *mechanism* question, so one phone answers it for all of them — and
      [android-adaptivity.md](android-adaptivity.md) has the four-line `GeometryChangedEvent`
      log to drop behind a debug flag on the build you are making anyway.
- [ ] **A real photo pick → upload returns 200** against the production backend with App Check
      enforced. Exercises `GalleryPicker` on the device's gallery, the EXIF-preserving byte
      copy, `Idempotency-Key`, and the App Check header path end to end.
- [ ] **Queue a sighting, kill the app, relaunch.** The upload should resume and the pending
      feed should show it. This was a real bug — nothing instantiated `JobQueue` at launch, so a
      queued sighting sat on disk until the user submitted *another* one; it is fixed by
      constructing the reports adapter in `AppUiHost.Awake`. It has EditMode coverage
      (`ListPending_SurvivesARestart`) and **has never run on hardware**. While you are there:
      a *retrying* row now repaints on the host's 60 s tick (`ISightingReports.Changed` never
      fires on a transient retry, so the attempt count and "Tentando de novo" / "Sem conexão"
      label used to freeze at whatever `OnEnter` computed) — leave the tab open through a
      failed attempt and watch it update.
- [ ] **The Espécie screen's 3D turntable and inline video.** Neither can be exercised in
      batchmode — there is no rig in a headless panel, and Unity's Linux `VideoPlayer` cannot
      decode the H.264 the educational clips are encoded as. The viewport fit was rewritten on
      2026-09-29 (it had been fitting the animal's longest dimension against the camera's
      *vertical* FOV, so a shark filled ~14 % of the box) and the second visit to a species used
      to show the placeholder instead of the model. **All of that is unverified.**

#### D. Retired from this list by the 2026-09-29 headless pass — and the limit of that

Recorded so the work is not redone, and so the distinction stays sharp: these were answered in
**rendered PNGs, not on hardware.** A render proves layout; it does not prove the device.

- [x] **Does the layout survive a narrower phone?** Every screen rendered at 360×640, 360×800,
      390×844, 412×915 and 430×932. **Nothing overflowed, clipped, or scrolled horizontally at
      any width.** 412 and 430 are clean as-is; 360 needed four text-reflow fixes, all applied
      (§6). No screen stylesheet contains a fixed-width offender.
- [x] **Do both safe-area configurations lay out correctly?** 47/34 and 24/0 both rendered
      across all 49 subjects; hero overlays and bottom-anchored elements correct in both.
      (The *measurement* of the real inset remains item C.1.)
- [x] **Does pixel resolution matter?** No. Constant Physical Size at 160 dpi means a
      higher-resolution phone gets a sharper image of the same dp layout. Only dp width and
      insets matter — which is what the five presets cover.
- [x] **Does the AR HUD's ⨯ work?** It did not, and it now does — found by reading
      `ObjectInteraction` rather than by tapping a phone. Still needs the device confirmation in
      §A, because the *fix* is the untested part.

#### E. Newly owed, and nobody has decided

- [ ] ⚠️ **OS font scale is not inherited — accessibility gap, no decision taken.** Both
      PanelSettings are `ConstantPhysicalSize` at `referenceDpi 160`, which scales the panel by
      `Screen.dpi / 160` and by nothing else. Nothing in the project reads Android's font-scale
      or display-size setting (grep: there is no reference to it anywhere in `Assets/`), so a
      user who enlarges system text almost certainly sees **no change in this app**. That is a
      real accessibility gap for an outdoor app with 11–13 px supporting text, and it is a
      *product* decision as much as a technical one: honouring font scale means every fixed
      `height` in the design system becomes a `min-height` (`MvOptionCard` has already been
      through exactly that conversion) and every stadium radius becomes `MdShape.KeepStadium`.
      Confirm the behaviour on device first — it is one setting toggle — then decide.

### 2. Then, in rough priority order

1. **Unblock the SOS half of Slice 5** — chase Decision D3 (SOS numbers and vetted first-aid
   copy). It is the only slice with a hard external dependency, so start the ask early even if
   you build something else meanwhile. The states half is done and needs nothing from anyone
   except `MvStateView`'s illustration asset (§9).
2. **Finish the component-restyle pass.** `MdChip` and the pill/stadium bug were done on
   2026-09-29 (§6); what is left is `MdCard` (radius 12 → 16, invisible today because nothing in
   the app instantiates it) and moving the `MdMenu` scroller fix out of `PraiasScreen`. Smaller
   than it was, same rule: one pass, one set of regression tests, gallery shots updated.
3. **Get the designer's answers on the three items the 2026-09-29 pass opened** — the
   `MvStateView` opacity swatch, the two 390 dp type reductions, and a look at the filled icon
   face (§9). All three are cheap to act on and all three are currently guesses.
4. ~~**The submit-button width fix**~~ — **done 2026-09-29** (§6).
5. **A back affordance for pushed screens**, which also resolves the two-tap escape.
6. **Delete `ScreenUI/MainScreen`.** ArTuning — its last reason to exist — was dropped on
   2026-09-29, so the shell and its route toggle (`AppUiHost.legacyArOverlay`,
   `AppUiBuilder.LegacyArOverlayChildrenToHide`, `ConditionsPillView`, `BeachNameView`) can all
   go. Once they do, uGUI is down to the splash, the debug overlay and Sobre.

The backend half of Slice 3 (persisting the six dropped fields) is deliberately **not** on this
list ahead of the privacy decision on `reporter_email` (§5). Nor is the **CC BY-NC model
licensing** question (§5) — but that one is a *distribution* blocker rather than an engineering
task, so it wants asking early even though nothing is waiting on the answer today.

---

## 9. Open questions

### For the designer

Three of these are new on 2026-09-29 and are cheap to act on — they are marked **NEW**. All
three are currently guesses that shipped, which is exactly the kind of thing this document
exists to keep visible.

- **NEW — `MvStateView`'s status-glyph opacity: 0.62 or 0.73?** The only site in the whole
  colour-space sweep (§6) with **no recorded Figma hex to check against**. The code comment
  claims the composite lands within ~6/255 of the designed swatch, but whether that was
  computed in sRGB (→ 0.73 is right) or read off a rendered PNG (→ 0.62 already is) is
  unknowable from here. **One swatch settles it.** Do not let anyone "reason it out" —
  every other site in that sweep had a measured colour to land on and this one does not.
- **NEW — sign-off on two type reductions** (DESIGN_IMPLEMENTATION.md's Decision D9). The hero
  selector label is **15 px** where V2 says 16, and the Praias "Maré agora" value is **16 px**
  where V2 says 18. Both are in-ladder sizes, both were needed to stop pre-existing wraps *at
  the 390 dp design width* (not only at 360), and both make outdoor-legibility slightly worse.
  If either is refused, the alternative is not "put the size back" — it is to shorten the
  string or re-spec the row.
- **NEW — the icon set is now the FILLED face.** `FILL: 1`, which is what V2 actually draws;
  48 of 95 glyphs gained ink, up to 4×. Nothing moved (advance widths are identical) but the
  whole app reads heavier. Worth one look-over, and it supersedes half of the "Material
  Symbols substitutes" concern at the bottom of this list.
- **The shark-fin icon.** The long-pole asset. Nothing else can substitute it; three stand-in
  sites are waiting on it (§6). *Note for whoever chases it:* the old claim that marine glyphs
  were excluded from the icon subset to stop one becoming the shipped icon is **not true of the
  font** — `waves`, `pool`, `scuba_diving` and `surfing` all ship. The single-constant
  consumer is what actually holds the line.
- **`MvStateView`'s illustration.** V2's error/offline frames (`79:1304` / `81:1403`) draw a
  line-art shark-in-the-waves behind the status glyph; it is a raster in the Figma file with no
  export in this repo. The component's slot is built and `SetIllustration(Sprite|Texture2D)` is
  ready — it renders **empty** until the asset arrives, and no stand-in art was invented. An
  export at ~261×286 dp @2× drops straight in.
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
- **NEW — will this app ever be distributed commercially?** Two of the five 3D models are
  **CC BY-NC** (`hammerhead`, `reef_shark` — §5). Non-commercial covers a free public-good app
  and does not cover a paid listing, ads, or a sponsor build. Three ways out — stay
  non-commercial, license those two models commercially from their authors, or replace them —
  and the cheapest one to choose is whichever gets chosen *before* anyone commits to a
  distribution model.
- **NEW — `lemon_shark`'s second video.** Both of its `videos` entries point at the same URL,
  so the only species in the app that has any video renders two identical cards. Either supply
  the second clip or delete the entry; one line of YAML either way.
- **Beach content** — [beaches-content-todo.md](beaches-content-todo.md), 17 beaches. Slice 2
  is code-complete and content-empty until this lands (§5).
- **The `reef_shark` species mismatch** — is it *Carcharhinus acronotus* or *C. perezi*, and
  does the 3D model match whichever it is? (§5)
- **`approximateSize` / `diet` / `behaviour` for the five species** — 15 short strings, blank
  on every asset, which is why the AR info card and Espécie render no spec table (§5).
- **`reporter_email`** — is the app collecting it at all, and if so with what notice and what
  retention? Needed before the backend persists it (§5).
- **The DHN tide table** — publish the PDF, or drop the link from Home (§6).

---

## 10. What is not verified here

Stated plainly so it is not mistaken for confidence:

- **Nothing on a device.** Everything in this document is editor, batchmode and screenshot
  evidence. That did not change on 2026-09-29; the volume of evidence grew and its *kind* did
  not.
- **The screenshot harness renders no camera feed.** Every `mergulho-*.png` is the HUD over flat
  black. So the one screen that exists to be composited over live video has never been seen
  composited over anything — including after the change that made its chrome fainter (§6, §8A).
- **The colour-space corrections were computed and validated against renders, not observed on a
  screen.** The model is sound (predicted (90,90,92) vs measured (90,91,92), agreeing to ±1 per
  channel on every case tested) and it predicts *pixels*, not perception. Whether a 0x2A3037
  hairline reads on a phone held up to the sea is a different question, and an unanswered one.
- **The five device frames are renders, not devices.** Insets were injected, not measured;
  `Screen.dpi` was never read; touch was never used. They prove the layout reflows, which is
  what they were built to prove.
- **A green suite is not a correct screen.** 683 EditMode + 27 PlayMode tests passed while a
  screen was visibly broken (§2). Structure, formatting and token discipline are well covered;
  layout is covered only by looking.
- The **Figma frames** were read from the cached `.figma-sync/` snapshot (file version
  `2402812393380097744`, last modified 2026-09-24), not re-fetched. Run `tools/figma_fetch.sh`
  if the designer has moved since.
- The **test and screenshot counts** are from the last logged run on 2026-09-29
  (`Logs/ds-test-editmode.xml`, 683 across three assemblies); they are not continuously enforced
  by CI (there is no CI for the Unity project). ⚠️ **If a number here disagrees with another
  document, re-run — do not reconcile on paper.**
- **Pixel-level fidelity against the Figma renders was still not exhaustively measured.** The
  2026-09-29 pass audited alphas, icon ink coverage, type sizes and reflow, which is a large
  bite of it, and worked from the frames that had measurable properties; the flattened-bitmap
  regions of the Figma file (the conditions card's chart, the state-view illustration) cannot be
  audited that way at all.
