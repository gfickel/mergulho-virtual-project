#!/usr/bin/env python3
"""Subset the Material Symbols variable font to the icons the app actually uses.

Reads the icon list from material_symbols_icons.txt, resolves codepoints from
the official .codepoints file, pins the variable axes (FILL=1, GRAD=0, opsz=24,
wght=400 — see AXIS_PINS), and writes:

  - Assets/DesignSystem/Fonts/MaterialSymbols.ttf   (the shipped subset font)
  - Assets/DesignSystem/Components/MdIcon/MdIconGlyphs.gen.cs  (name -> glyph map)

Adding an icon = add its name to material_symbols_icons.txt, rerun this script,
then rerun the font-asset generation (make ds-setup).

Source files (fonts_src/) are downloaded once from google/material-design-icons;
rerun the curl commands in tools/design_system/README.md if they're missing.
"""

import sys
from datetime import date
from pathlib import Path

from fontTools import subset
from fontTools.ttLib import TTFont
from fontTools.varLib.instancer import instantiateVariableFont

HERE = Path(__file__).resolve().parent
REPO_ROOT = HERE.parents[1]
SRC_FONT = HERE / "fonts_src/MaterialSymbolsOutlined.ttf"
SRC_CODEPOINTS = HERE / "fonts_src/MaterialSymbolsOutlined.codepoints"
ICON_LIST = HERE / "material_symbols_icons.txt"
OUT_FONT = REPO_ROOT / "src/app/MergulhoVirtual/Assets/DesignSystem/Fonts/MaterialSymbols.ttf"
OUT_CS = REPO_ROOT / "src/app/MergulhoVirtual/Assets/DesignSystem/Components/MdIcon/MdIconGlyphs.gen.cs"

# Variable-axis pins for the instance we ship. The source face is the *Outlined*
# variable font, whose FILL axis (0..1) morphs each glyph between a hairline outline
# and a SOLID mark — it is a fill toggle, not a different icon family, so there is
# nothing else to download.
#
# FILL=1, NOT 0 — THIS WAS THE BUG, AND IT AFFECTED EVERY ICON IN THE APP
#  The pin shipped at 0, so MdIcon rendered Material Symbols Outlined: thin wireframes.
#  Every V2 glyph is a SOLID mark. Measured amber ink coverage inside the Home feature
#  tiles was 8% / 8% / 13% against the design's 25% / 16% / 33% — a third to a half of
#  the ink, on top of the marks reading 4-8dp smaller — so V2's bold amber badges came
#  out as outlines. Same cause on the navigation bar. FILL=1 is the fix; it changes no
#  codepoint and no advance width, so MdIconGlyphs.gen.cs and every layout are stable.
#
# THE OTHER THREE ARE DELIBERATE AND SHOULD NOT MOVE IN THE SAME CHANGE
#  GRAD  0   grade, range -50..200: a fine stroke-emphasis trim. Near-irrelevant once
#            the glyph is solid; leave it as the neutral value.
#  opsz  24  optical size, range 20..48. Consumers render at 14-38dp and cluster on
#            24, which this matches. Pinning 20 would thicken the small ones slightly.
#  wght  400 weight, range 100..700. This is the SECOND lever on ink coverage. If the
#            marks still read light after FILL=1 lands, raise this — but measure the
#            FILL=1 result first, because changing both at once makes the result
#            un-attributable.
#
# Remaining known gap, and it is NOT fixed here: Material Symbols draw on a 24-unit
# grid inside an em box that carries padding V2's Figma vectors do not, so a glyph at
# font-size N renders visibly smaller than N dp of mark. That is a per-consumer
# `font-size` bump, not an axis pin.
#
# The instance keeps the source's name records ("Material Symbols Outlined") on
# purpose: nothing resolves this face by family name — MdIcon.uss points at the
# generated .asset path — and rewriting them would churn the font asset for no gain.
AXIS_PINS = {"FILL": 1, "GRAD": 0, "opsz": 24, "wght": 400}


def read_icons() -> list[str]:
    icons = []
    for line in ICON_LIST.read_text().splitlines():
        line = line.strip()
        if line and not line.startswith("#"):
            icons.append(line)
    return sorted(set(icons))


def read_codepoints() -> dict[str, int]:
    table = {}
    for line in SRC_CODEPOINTS.read_text().splitlines():
        name, _, hexcode = line.strip().partition(" ")
        if name and hexcode:
            table[name] = int(hexcode, 16)
    return table


def main() -> None:
    for f in (SRC_FONT, SRC_CODEPOINTS):
        if not f.exists():
            sys.exit(f"error: {f} missing — see tools/design_system/README.md for download steps")

    icons = read_icons()
    table = read_codepoints()
    missing = [i for i in icons if i not in table]
    if missing:
        sys.exit(f"error: not in codepoints file (typo?): {', '.join(missing)}")
    selected = {name: table[name] for name in icons}

    font = TTFont(SRC_FONT)
    instantiateVariableFont(font, AXIS_PINS, inplace=True)

    options = subset.Options()
    options.layout_features = []          # icons need no OpenType features
    options.name_IDs = [1, 2, 3, 4, 6]    # keep basic naming records
    options.notdef_outline = True
    subsetter = subset.Subsetter(options)
    subsetter.populate(unicodes=list(selected.values()))
    subsetter.subset(font)

    OUT_FONT.parent.mkdir(parents=True, exist_ok=True)
    font.save(OUT_FONT)

    lines = [
        "// GENERATED by tools/design_system/subset_material_symbols.py — DO NOT HAND-EDIT.",
        "// Icon set: tools/design_system/material_symbols_icons.txt "
        f"({len(selected)} icons, generated {date.today().isoformat()}).",
        "using System.Collections.Generic;",
        "",
        "namespace MergulhoVirtual.DesignSystem",
        "{",
        "    /// <summary>Icon name → Material Symbols glyph for the shipped subset font.</summary>",
        "    public static class MdIconGlyphs",
        "    {",
        "        public static readonly IReadOnlyDictionary<string, string> Map =",
        "            new Dictionary<string, string>",
        "        {",
    ]
    for name, cp in selected.items():
        lines.append(f"            {{ \"{name}\", \"\\U{cp:08X}\" }},")
    lines += [
        "        };",
        "    }",
        "}",
        "",
    ]
    OUT_CS.parent.mkdir(parents=True, exist_ok=True)
    OUT_CS.write_text("\n".join(lines))

    size_kb = OUT_FONT.stat().st_size / 1024
    print(f"wrote {OUT_FONT.relative_to(REPO_ROOT)} ({size_kb:.0f} KB, {len(selected)} icons)")
    print(f"wrote {OUT_CS.relative_to(REPO_ROOT)}")


if __name__ == "__main__":
    main()
