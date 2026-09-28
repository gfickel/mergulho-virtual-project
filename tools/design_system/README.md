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

`generate_md3_tokens.py` — seed color → full M3 light+dark scheme as USS variables
(`Tokens/_colors-light.uss` / `_colors-dark.uss`). Same HCT algorithm as Material
Theme Builder. Current seed: `#00A0B0` (placeholder Noronha turquoise until the
designer picks).

```bash
.venv/bin/python generate_md3_tokens.py --seed "#0A7E8C"          # new seed
.venv/bin/python generate_md3_tokens.py --from-json export.json    # designer's Theme Builder export
```

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
curl -sL -o roboto-android.zip https://github.com/google/roboto/releases/download/v2.138/roboto-android.zip
unzip -o roboto-android.zip Roboto-Regular.ttf Roboto-Medium.ttf Roboto-Bold.ttf
curl -sL -o MaterialSymbolsOutlined.ttf "https://github.com/google/material-design-icons/raw/master/variablefont/MaterialSymbolsOutlined%5BFILL%2CGRAD%2Copsz%2Cwght%5D.ttf"
curl -sL -o MaterialSymbolsOutlined.codepoints "https://github.com/google/material-design-icons/raw/master/variablefont/MaterialSymbolsOutlined%5BFILL%2CGRAD%2Copsz%2Cwght%5D.codepoints"
```

## Unity-side setup (`make ds-setup`, editor CLOSED)

Runs `DesignSystemSetup.SetupAll` headlessly: TTF → TextCore font assets,
dp-calibrated PanelSettings (1 USS px = 1 dp), and the GalleryScene — fully wired,
zero Inspector work. Also available in-editor under
**Tools > Mergulho Virtual > Design System**.

## Tests

`make ds-test` (EditMode: structure, properties, token discipline) and
`make ds-test-play` (PlayMode: synthetic pointer events → callbacks). Editor must be closed.
