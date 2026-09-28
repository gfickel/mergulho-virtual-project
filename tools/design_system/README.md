# Design-system tooling

Pipelines feeding `src/app/MergulhoVirtual/Assets/DesignSystem/`. Everything visual is
regenerable — when the designer delivers a new palette or needs new icons, rerun a
script; never hand-edit generated files.

## Setup (one-time)

```bash
cd tools/design_system
python3 -m venv .venv
.venv/bin/pip install materialyoucolor fonttools
```

## Color tokens (`make ds-tokens`)

`generate_md3_tokens.py` → `Tokens/_colors-light.uss` / `_colors-dark.uss` (never
hand-edit those). The source of truth is **`brand-theme.json`**, a hand-authored
Material Theme Builder-shaped export of the V2 brand palette
(DESIGN_IMPLEMENTATION.md §3.1 / §3.3 / Appendix A.1). The seed path is *wrong* for
this project — navy `#152336` and amber `#FFC107` are independent hues, not a tonal
derivation of one seed — so `make ds-tokens` uses `--from-json`:

```bash
.venv/bin/python generate_md3_tokens.py --from-json brand-theme.json   # what make ds-tokens runs
.venv/bin/python generate_md3_tokens.py --seed "#0A7E8C"               # algorithmic, unused here
```

`success` / `warning` are not M3 roles; they live in the hand-maintained
`Tokens/_brand-light.uss` / `_brand-dark.uss` pair instead (§3.2). Changing those
means touching both files, both `.tss` import lists, and
`TokenDisciplineTests.ThemeColorFiles` — all three are tested.

`--spec 2021` (default) matches m3.material.io docs; `--spec 2025` switches to M3 Expressive.

## Icon subset (`make ds-icons`)

`subset_material_symbols.py` — subsets the 10.7 MB Material Symbols variable font to
the icons listed in `material_symbols_icons.txt` (→ ~13 KB `MaterialSymbols.ttf`) and
generates the C# name→glyph map (`MdIconGlyphs.gen.cs`).

**Adding an icon:** find its name at https://fonts.google.com/icons → add the name to
`material_symbols_icons.txt` → `make ds-icons` → `make ds-setup` (refreshes the font asset).

Source fonts live in `fonts_src/` (gitignored-safe to delete); re-download with:

```bash
cd fonts_src
# Inter — the brand face (SIL OFL 1.1). Static TTFs, one per weight, because UI Toolkit
# resolves font weight through a separate font asset (DESIGN_IMPLEMENTATION.md §3.4).
curl -sL -o Inter-4.1.zip https://github.com/rsms/inter/releases/download/v4.1/Inter-4.1.zip
unzip -o -j Inter-4.1.zip 'extras/ttf/Inter-Regular.ttf' 'extras/ttf/Inter-Medium.ttf' \
    'extras/ttf/Inter-SemiBold.ttf' 'extras/ttf/Inter-Bold.ttf'
# Roboto — stock-M3 reference face, no longer used by the shipped type scale.
curl -sL -o roboto-android.zip https://github.com/google/roboto/releases/download/v2.138/roboto-android.zip
unzip -o roboto-android.zip Roboto-Regular.ttf Roboto-Medium.ttf Roboto-Bold.ttf
curl -sL -o MaterialSymbolsOutlined.ttf "https://github.com/google/material-design-icons/raw/master/variablefont/MaterialSymbolsOutlined%5BFILL%2CGRAD%2Copsz%2Cwght%5D.ttf"
curl -sL -o MaterialSymbolsOutlined.codepoints "https://github.com/google/material-design-icons/raw/master/variablefont/MaterialSymbolsOutlined%5BFILL%2CGRAD%2Copsz%2Cwght%5D.codepoints"
```

## Unity-side setup (`make ds-setup`, editor CLOSED)

Runs `DesignSystemSetup.SetupAll` headlessly: TTF → TextCore font assets (Inter
Regular/Medium/SemiBold/Bold + Roboto + MaterialSymbols; the TTFs themselves are committed
under `Assets/DesignSystem/Fonts/`, `fonts_src/` is only the download cache),
dp-calibrated PanelSettings (1 USS px = 1 dp), and the GalleryScene — fully wired,
zero Inspector work. Also available in-editor under
**Tools > Mergulho Virtual > Design System**.

## Tests

`make ds-test` (EditMode: structure, properties, token discipline) and
`make ds-test-play` (PlayMode: synthetic pointer events → callbacks). Editor must be closed.

## Screenshots (`make ds-shots`, editor CLOSED)

Renders the UI Toolkit screens and the component gallery straight to PNG, so the UI can
be looked at without a device build. Output lands in the gitignored `.shots/` at the repo
root, one deterministically-named file per subject and theme, plus a `manifest.json`:

```bash
make ds-shots                 # everything, both themes (~20 s, ~54 PNGs)
make ds-shots SHOT=home       # only subjects whose id contains "home"
make ds-shots SHOT=gallery-mdchip
```

Also in-editor: **Tools > Mergulho Virtual > Design System > Capture UI Screenshots**.

Implementation: [`Assets/Editor/UiShots/UiScreenshotHarness.cs`](../../src/app/MergulhoVirtual/Assets/Editor/UiShots/UiScreenshotHarness.cs)
(subject table at the top — adding a screen is one line) plus
[`UiShotFixtures.cs`](../../src/app/MergulhoVirtual/Assets/Editor/UiShots/UiShotFixtures.cs)
(the frozen clock + fake services that make a shot change only when the UI changes).

What the frames are:

* **390 × 844 dp at 2×** (780 × 1688 px) by default — the V2 `Protótipo` frame size, and
  `1 USS px = 1 dp = 1 Figma pt`, so a shot overlays the 2× renders in `.figma-sync/png/`
  directly. Tall variants (`home-tall`, `beaches-detail-tall`) set their own dp height.
* **Safe area is applied, chrome is not.** 47 dp top / 34 dp bottom insets are passed to
  `IAppScreen.SetEdgeInsets` exactly as the runtime host does (`Screen.safeArea` is
  meaningless in batchmode). The fake status bar / home indicator in the Figma frames are
  OS chrome — deliberately not drawn (DESIGN_IMPLEMENTATION.md §1).
* **`shell-*` shots include the bottom `MdNavigationBar`** (they go through `MdRouter`);
  the bare `home` / `beaches` shots are the screen alone. Use `shell-*` when comparing a
  whole V2 frame.
* **`gallery-<section>-*` shots are cropped to each section's natural height**, so a
  component's whole variant matrix is one image.

This is **not** a golden-image test — nothing asserts on the pixels. It is a look-at-it
tool. Knobs (all optional env vars, read by the harness): `MV_SHOT_DIR`, `MV_SHOT_FILTER`,
`MV_SHOT_WIDTH`, `MV_SHOT_HEIGHT`, `MV_SHOT_SCALE`, `MV_SHOT_TOP_INSET`,
`MV_SHOT_BOTTOM_INSET`, `MV_SHOT_THEMES` (`light`, `dark`, or `light,dark`).

**`ds-shots` is the one Unity target that must not pass `-nographics`** — it needs a real
graphics device to rasterise the panels — so it needs an X display. The Makefile defaults
to `DISPLAY=:1`; your environment's `DISPLAY` wins, or override with
`make ds-shots DISPLAY=:0`.

### Adding a screen to the run

One line in the `Subjects` table:

```csharp
new Subject("sos", ShotKind.Screen, "SosScreen"),
new Subject("shell-sos", ShotKind.Shell, "SosScreen") { Route = AppRoutes.Sos },
```

The screen is found by **simple type name** in `MergulhoVirtual.UI` and built by matching
its constructor parameters (and its ViewModel's, recursively) against
`UiShotFixtures.Services`, the frozen clock and the sprite loader — so a screen that does
not exist yet, or whose constructor grew a parameter with no fake, is **skipped with a log
line naming what was missing** instead of failing the run. When that happens, add the fake
to `UiShotFixtures.Services`. `HeightDp`, `VmMethod`/`VmArgs` (a ViewModel call applied
after `OnEnter`, e.g. `ShowDetail`) and `Route` are the per-subject knobs.
