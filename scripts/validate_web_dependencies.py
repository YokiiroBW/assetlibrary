#!/usr/bin/env python3
"""Validate the Web lock, reviewed licenses, exact tools, and build budgets."""
from __future__ import annotations

import argparse
import base64
import binascii
import json
import re
import sys
from pathlib import Path


ROOT = Path(__file__).resolve().parents[1]
POLICY_PATH = Path("eng/web-dependency-policy.json")
PACKAGE_PATH = Path("apps/web/package.json")
LOCK_PATH = Path("apps/web/pnpm-lock.yaml")
EXACT_VERSION = re.compile(r"^[0-9]+(?:\.[0-9]+){2}(?:-[0-9A-Za-z.-]+)?$")


def load_object(path: Path) -> dict:
    value = json.loads(path.read_text(encoding="utf-8"))
    if not isinstance(value, dict):
        raise ValueError(f"expected a JSON object: {path}")
    return value


def lock_packages(lock: str) -> dict[str, set[str]]:
    try:
        package_section = lock.split("\npackages:\n", 1)[1].split("\nsnapshots:\n", 1)[0]
    except IndexError as error:
        raise ValueError("pnpm lock lacks package and snapshot sections") from error
    headers = list(
        re.finditer(
            r'^  (?:"([^"]+)"|([^ \t":\n][^:\n]*)):\n',
            package_section,
            re.MULTILINE,
        )
    )
    packages: dict[str, set[str]] = {}
    for index, match in enumerate(headers):
        coordinate = match.group(1) or match.group(2)
        assert coordinate is not None
        name, separator, version = coordinate.rpartition("@")
        if not separator or not name or not EXACT_VERSION.fullmatch(version):
            raise ValueError(f"invalid locked npm coordinate: {coordinate}")
        block_end = headers[index + 1].start() if index + 1 < len(headers) else len(package_section)
        block = package_section[match.end() : block_end]
        integrity = re.search(
            r"resolution:\s*(?:\{\s*)?integrity:\s*sha512-([A-Za-z0-9+/]+={0,2})",
            block,
        )
        if integrity is None:
            raise ValueError(f"locked npm package lacks SHA-512 integrity: {coordinate}")
        try:
            digest = base64.b64decode(integrity.group(1), validate=True)
        except (binascii.Error, ValueError) as error:
            raise ValueError(f"locked npm package has invalid SHA-512 integrity: {coordinate}") from error
        if len(digest) != 64:
            raise ValueError(f"locked npm package has invalid SHA-512 integrity: {coordinate}")
        packages.setdefault(name, set()).add(version)
    if not packages:
        raise ValueError("pnpm lock contains no registry packages")
    return packages


def expected_license(name: str, rules: list[dict]) -> str | None:
    exact = next((rule.get("license") for rule in rules if rule.get("exact") == name), None)
    if isinstance(exact, str):
        return exact
    prefixes = [
        (str(rule["prefix"]), str(rule["license"]))
        for rule in rules
        if isinstance(rule, dict) and "prefix" in rule and "license" in rule
        and name.startswith(str(rule["prefix"]))
    ]
    return max(prefixes, key=lambda item: len(item[0]))[1] if prefixes else None


def normalize_license(value: object) -> str | None:
    if isinstance(value, str):
        return value
    if isinstance(value, dict) and isinstance(value.get("type"), str):
        return str(value["type"])
    return None


def installed_licenses(root: Path, locked: dict[str, set[str]]) -> dict[tuple[str, str], str]:
    package_store = root / "apps/web/node_modules/.pnpm"
    if not package_store.is_dir():
        return {}
    found: dict[tuple[str, str], str] = {}
    for manifest_path in package_store.glob("*/node_modules/**/package.json"):
        try:
            manifest = load_object(manifest_path)
        except (OSError, UnicodeError, json.JSONDecodeError, ValueError):
            continue
        name = manifest.get("name")
        version = manifest.get("version")
        if (
            not isinstance(name, str)
            or not isinstance(version, str)
            or version not in locked.get(name, set())
        ):
            continue
        license_name = normalize_license(manifest.get("license"))
        if license_name is not None:
            found[(name, version)] = license_name
    return found


def validate_build_sizes(root: Path, policy: dict, required: bool, errors: list[str]) -> None:
    output = root / "apps/web/dist/assets"
    if not output.is_dir():
        if required:
            errors.append("required Web build output is missing")
        return
    javascript = sum(path.stat().st_size for path in output.glob("*.js"))
    css = sum(path.stat().st_size for path in output.glob("*.css"))
    budgets = policy.get("size_budgets_bytes", {})
    actual = {
        "javascript": javascript,
        "css": css,
        "javascript_and_css": javascript + css,
    }
    for name, size in actual.items():
        budget = budgets.get(name) if isinstance(budgets, dict) else None
        if not isinstance(budget, int) or budget <= 0 or size > budget:
            errors.append(f"Web build exceeds or lacks size budget: {name} ({size} bytes)")


def validate(root: Path, require_build_artifacts: bool = False) -> list[str]:
    errors: list[str] = []
    try:
        policy = load_object(root / POLICY_PATH)
        package = load_object(root / PACKAGE_PATH)
        lock = (root / LOCK_PATH).read_text(encoding="utf-8")
        locked = lock_packages(lock)
    except (OSError, UnicodeError, json.JSONDecodeError, ValueError) as error:
        return [str(error)]

    if policy.get("version") != 1:
        errors.append("Web dependency policy version must be 1")
    if package.get("packageManager") != policy.get("package_manager"):
        errors.append("Web pnpm version differs from dependency policy")
    expected_node = policy.get("node_version")
    if package.get("engines", {}).get("node") != f">={expected_node} <25":
        errors.append("Web Node.js engine must pin the approved Node 24 patch floor")
    manifest = policy.get("manifest", {})
    for section in ("dependencies", "devDependencies"):
        expected = manifest.get(section) if isinstance(manifest, dict) else None
        if package.get(section) != expected:
            errors.append(f"Web {section} differs from the reviewed exact manifest")
    for section in ("dependencies", "devDependencies"):
        values = package.get(section, {})
        if not isinstance(values, dict):
            continue
        for name, version in values.items():
            if name == "@assetlibrary/assetlink":
                if version != "link:../../packages/sdk/assetlink/typescript":
                    errors.append("Web may link only the repository-generated AssetLink SDK")
            elif not isinstance(version, str) or not EXACT_VERSION.fullmatch(version):
                errors.append(f"Web dependency is not an exact stable version: {name}")
            elif version not in locked.get(name, set()):
                errors.append(f"Web direct dependency lock drift: {name}")

    if re.search(r"\b(?:https?|git|github):", lock, re.IGNORECASE):
        errors.append("Web pnpm lock contains a non-registry source")
    if lock.count("link:../../packages/sdk/assetlink/typescript") != 2:
        errors.append("Web lock must contain exactly one local AssetLink SDK specifier and resolution")

    allowed = set(policy.get("allowed_licenses", []))
    rules = policy.get("locked_package_license_rules", [])
    if not isinstance(rules, list):
        rules = []
    for name in locked:
        license_name = expected_license(name, rules)
        if license_name is None:
            errors.append(f"locked npm package lacks a reviewed license: {name}")
        elif license_name not in allowed:
            errors.append(f"locked npm package uses an unapproved license: {name} / {license_name}")
    for (name, _), actual_license in installed_licenses(root, locked).items():
        reviewed = expected_license(name, rules)
        if reviewed is not None and actual_license != reviewed:
            errors.append(f"installed npm license differs from review: {name} / {actual_license}")

    reviews = policy.get("direct_dependency_review", [])
    if not isinstance(reviews, list):
        errors.append("Web policy lacks direct dependency reviews")
    else:
        expected_reviews = {
            "Node.js": str(expected_node),
            "pnpm": str(policy.get("package_manager", "")).removeprefix("pnpm@"),
        }
        for section in ("dependencies", "devDependencies"):
            for name, version in package.get(section, {}).items():
                if name != "@assetlibrary/assetlink":
                    expected_reviews[name] = version
        actual_reviews: dict[str, str] = {}
        for review in reviews:
            if not isinstance(review, dict):
                errors.append("Web dependency review must be an object")
                continue
            name = review.get("name")
            version = review.get("version")
            if not isinstance(name, str) or not isinstance(version, str):
                errors.append("Web dependency review requires a name and exact version")
                continue
            if name in actual_reviews:
                errors.append(f"duplicate Web dependency review: {name}")
            actual_reviews[name] = version
            if review.get("license") not in allowed:
                errors.append(f"direct dependency has an unapproved license: {name}")
            if not isinstance(review.get("scope"), str) or not review["scope"]:
                errors.append(f"direct dependency lacks a reviewed scope: {name}")
            if not str(review.get("security_updates", "")).startswith("https://"):
                errors.append(f"direct dependency lacks an HTTPS update source: {name}")
        if actual_reviews != expected_reviews:
            errors.append("Web direct dependency reviews differ from the exact manifest and toolchain")

    validate_build_sizes(root, policy, require_build_artifacts, errors)
    return errors


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--root", type=Path, default=ROOT)
    parser.add_argument("--require-build-artifacts", action="store_true")
    args = parser.parse_args()
    errors = validate(args.root.resolve(), args.require_build_artifacts)
    if errors:
        for error in errors:
            print(f"ERROR: {error}", file=sys.stderr)
        return 1
    suffix = " with build budgets" if args.require_build_artifacts else ""
    print(f"Web dependency, lock, and license policy passed{suffix}.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
