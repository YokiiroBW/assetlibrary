#!/usr/bin/env python3
"""Export native resources from the existing authoritative Web semantic colors."""
from __future__ import annotations

import argparse
import json
import re
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
SOURCES = {
    "light": "apps/web/src/styles/base.css",
    "dark": "apps/web/src/styles/responsive.css",
}
KEYS = {
    "ink", "muted", "line", "surface", "surface-strong", "accent",
    "accent-soft", "on-accent", "danger", "positive", "folder", "focus",
}
OUTPUT = ROOT / "packages/ui/workspace-theme.json"


def render() -> str:
    themes = {}
    for theme, source in SOURCES.items():
        css = (ROOT / source).read_text(encoding="utf-8")
        colors = dict(re.findall(r"--([a-z-]+):\s*(#[0-9a-fA-F]{6});", css))
        if set(colors) != KEYS:
            raise ValueError(f"Unexpected semantic token set in {source}")
        themes[theme] = colors
    return json.dumps({
        "generated_by": "scripts/export_native_theme.py; do not edit",
        "sources": SOURCES,
        "themes": themes,
    }, ensure_ascii=False, indent=2) + "\n"


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--check", action="store_true")
    args = parser.parse_args()
    expected = render()
    if args.check:
        if not OUTPUT.exists() or OUTPUT.read_text(encoding="utf-8") != expected:
            print("Native theme differs from Web semantic colors; run scripts/export_native_theme.py")
            return 1
    else:
        OUTPUT.write_text(expected, encoding="utf-8")
    print("Native semantic theme is current.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
