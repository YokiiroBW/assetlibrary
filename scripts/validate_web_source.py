#!/usr/bin/env python3
"""Enforce the first Web slice's read-only, bounded, and privacy-safe source boundary."""
from __future__ import annotations

import argparse
import re
import sys
from pathlib import Path


ROOT = Path(__file__).resolve().parents[1]
WEB_SOURCE = Path("apps/web/src")
WEB_TESTS = Path("tests/web")
IGNORED = {"dist", "node_modules", "playwright-report", "test-results"}
FORBIDDEN_PRODUCTION = re.compile(
    r"\b(?:localStorage|sessionStorage|indexedDB|dangerouslySetInnerHTML|WebSocket|EventSource)\b"
    r"|\b(?:authorization|bearer)\b"
    r'|["\'](?:upload|move|copy|rename|delete|trash)\.[^"\']+["\']',
    re.IGNORECASE,
)
SENSITIVE_LOG = re.compile(r"\bconsole\.(?:log|info|warn|error|debug)\s*\(", re.IGNORECASE)


def source_files(root: Path) -> list[Path]:
    base = root / WEB_SOURCE
    return sorted(
        path
        for path in base.rglob("*")
        if path.is_file() and not any(part in IGNORED for part in path.relative_to(base).parts)
    )


def validate(root: Path) -> list[str]:
    errors: list[str] = []
    files = source_files(root)
    if not files:
        return ["Web source directory is empty"]
    allowed_suffixes = {".css", ".ts", ".tsx"}
    for path in files:
        relative = path.relative_to(root).as_posix()
        if path.suffix not in allowed_suffixes:
            errors.append(f"unexpected Web source type: {relative}")
            continue
        text = path.read_text(encoding="utf-8")
        limit = 24_000 if path.suffix == ".css" else 20_000
        if len(text.encode("utf-8")) > limit:
            errors.append(f"Web source exceeds the per-file review bound: {relative}")
        if FORBIDDEN_PRODUCTION.search(text):
            errors.append(f"Web source contains forbidden state, auth, or write capability: {relative}")
        if SENSITIVE_LOG.search(text):
            errors.append(f"Web source must not log browser data: {relative}")
        if "fetch(" in text and path.name != "assetLinkClient.ts":
            errors.append(f"network access must stay in the AssetLink adapter: {relative}")

    combined = "\n".join(path.read_text(encoding="utf-8") for path in files)
    required = {
        'from "@assetlibrary/assetlink"': "generated AssetLink SDK import",
        "encodeAssetLinkMessage": "generated AssetLink encoder",
        "parseAssetLinkMessage": "generated AssetLink parser",
        "const pageSize = 100": "bounded page size",
        "AbortController": "request cancellation",
        "generation.current": "stale response guard",
        "useVirtualizer": "windowed list rendering",
        'maxLength={200}': "search input bound",
        'credentials: "same-origin"': "same-origin authentication transport",
        "message.request_id !== request.request_id": "response correlation check",
        "items.length > pageSize": "response page bound",
    }
    for marker, description in required.items():
        if marker not in combined:
            errors.append(f"Web source lacks {description}")
    if combined.count("fetch(") != 1:
        errors.append("Web source must have exactly one centralized fetch call")

    test_root = root / WEB_TESTS
    tests = "\n".join(
        path.read_text(encoding="utf-8")
        for path in sorted(test_root.glob("*.mjs"))
        if path.is_file()
    )
    for marker in (
        "width: 1440",
        "width: 390",
        "root-next",
        "hiddenLibraryName",
        "service_unavailable",
        "authentication_required",
        "old-result.jpg",
        "new-result.jpg",
    ):
        if marker not in tests:
            errors.append(f"Web browser regression lacks required evidence marker: {marker}")
    if re.search(r"\b(?:test|describe)\.only\s*\(", tests):
        errors.append("Web browser regression contains a focused-only test")
    return errors


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--root", type=Path, default=ROOT)
    args = parser.parse_args()
    errors = validate(args.root.resolve())
    if errors:
        for error in errors:
            print(f"ERROR: {error}", file=sys.stderr)
        return 1
    print("Web read-only source boundary passed.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
