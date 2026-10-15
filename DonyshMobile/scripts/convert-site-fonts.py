"""Unpack the site's own WOFF2 fonts into TrueType for native MAUI controls.

Run with a Python environment containing fonttools and brotli.
The original font names, glyphs, license metadata and weight information are preserved.
"""
from pathlib import Path
from fontTools.ttLib import TTFont

root = Path(__file__).resolve().parents[2]
source = root / "HesabYar.Web" / "wwwroot" / "fonts"
destination = root / "DonyshMobile" / "DonyshMobile" / "Resources" / "Fonts"

for weight in ("Regular", "SemiBold", "Bold", "ExtraBold"):
    original = source / f"YekanBakh-{weight}.woff2"
    with TTFont(original, recalcTimestamp=False) as font:
        font.flavor = None
        target = destination / f"YekanBakh-{weight}.ttf"
        font.save(target)
        with TTFont(target) as unpacked:
            assert font.getBestCmap() == unpacked.getBestCmap()
        if weight == "Regular":
            android = root / "DonyshMobile" / "DonyshMobile" / "Platforms" / "Android" / "Resources" / "font" / "donysh_yekan.ttf"
            android.parent.mkdir(parents=True, exist_ok=True)
            font.save(android)
        print(f"Converted {target.name}")
