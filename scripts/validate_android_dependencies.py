#!/usr/bin/env python3
"""Check Android dependency integrity, runtime licenses, APK size and OSV advisories."""
from __future__ import annotations

import argparse
from datetime import datetime, timezone
import hashlib
import json
from pathlib import Path
import re
import urllib.request
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[1]
NS = {"v": "https://schema.gradle.org/dependency-verification"}
COMPONENT = re.compile(r"^[A-Za-z0-9_.-]+$")


def digest(path: Path) -> str:
    with path.open("rb") as stream:
        return hashlib.file_digest(stream, "sha256").hexdigest()


def check_source(root: Path) -> set[tuple[str, str, str]]:
    app = root / "apps/android"
    sdk = root / "packages/sdk/assetlink/kotlin"
    for name in ("gradle-wrapper.jar", "gradle-wrapper.properties"):
        native = app / "gradle/wrapper" / name
        baseline = sdk / "gradle/wrapper" / name
        if name.endswith("jar"):
            if digest(native) != digest(baseline):
                raise ValueError("Android wrapper differs from the already verified SDK wrapper")
        else:
            keys = ("distributionUrl=", "distributionSha256Sum=")
            selected = lambda path: [line for line in path.read_text(encoding="utf-8").splitlines() if line.startswith(keys)]
            if selected(native) != selected(baseline):
                raise ValueError("Android Gradle distribution differs from the pinned SDK distribution")
    if not (app / "app/gradle.lockfile").is_file():
        raise ValueError("Android resolved dependency lock is missing")
    metadata = ET.parse(app / "gradle/verification-metadata.xml").getroot()
    if metadata.findtext("v:configuration/v:verify-metadata", namespaces=NS) != "true":
        raise ValueError("Gradle metadata verification must stay enabled")
    if metadata.findall(".//v:trusted-artifacts/v:trust", NS):
        raise ValueError("Android artifacts must have checksums rather than trust bypasses")
    components = set()
    for component in metadata.findall("v:components/v:component", NS):
        identity = tuple(component.attrib[key] for key in ("group", "name", "version"))
        if any(not COMPONENT.fullmatch(value) for value in identity) or any("SNAPSHOT" in value.upper() for value in identity):
            raise ValueError("Android dependency identity is not an exact resolved coordinate")
        components.add(identity)
        artifacts = component.findall("v:artifact", NS)
        if not artifacts:
            raise ValueError(f"No verified artifacts for {identity}")
        for artifact in artifacts:
            checksums = artifact.findall("v:sha256", NS)
            if not checksums or any(not re.fullmatch(r"[0-9a-f]{64}", entry.get("value", "")) for entry in checksums):
                raise ValueError(f"Missing SHA256 for {identity} / {artifact.get('name')}")
    if not components:
        raise ValueError("Android verification metadata is empty")
    return components


def runtime_inventory(path: Path) -> list[tuple[str, str, str]]:
    items = set()
    for line in path.read_text(encoding="utf-8").splitlines():
        parts = tuple(line.split("\t"))
        if len(parts) != 3 or any(not COMPONENT.fullmatch(part) for part in parts) or parts[2] == "unspecified":
            raise ValueError("Runtime inventory must contain exact external Maven coordinates")
        items.add(parts)
    if not items:
        raise ValueError("Runtime dependency inventory is empty")
    return sorted(items)


def licenses(cache: Path, coordinate: tuple[str, str, str], policy: dict, seen: tuple = ()) -> dict:
    if coordinate in seen or len(seen) > 12:
        raise ValueError(f"Cyclic or excessive POM parent chain: {coordinate}")
    poms = list(cache.joinpath(*coordinate).glob("*/*.pom"))
    if not poms or len({digest(path) for path in poms}) != 1:
        raise ValueError(f"Missing or inconsistent cached POM: {coordinate}")
    pom = poms[0]
    document = ET.parse(pom).getroot()
    names = [element.text or "" for element in document.findall("./{*}licenses/{*}license/{*}name")]
    accepted = [policy["license_names"][name] for name in names if name in policy["license_names"]]
    if accepted:
        return {"coordinate": ":".join(coordinate), "declared_licenses": names, "accepted_licenses": accepted, "pom_sha256": digest(pom)}
    if names:
        raise ValueError(f"Runtime dependency license needs review: {coordinate} / {names}")
    parent = document.find("./{*}parent")
    if parent is None:
        raise ValueError(f"Runtime dependency has no license declaration: {coordinate}")
    parent_coordinate = tuple(parent.findtext("{*}" + key) or "" for key in ("groupId", "artifactId", "version"))
    if any(not COMPONENT.fullmatch(value) for value in parent_coordinate):
        raise ValueError(f"Unresolved parent license coordinate: {coordinate}")
    inherited = licenses(cache, parent_coordinate, policy, (*seen, coordinate))
    return {"coordinate": ":".join(coordinate), "pom_sha256": digest(pom), "inherited_from": inherited}


def query_osv(coordinates: list[tuple[str, str, str]]) -> list[dict]:
    results = []
    for start in range(0, len(coordinates), 100):
        batch = coordinates[start:start + 100]
        payload = {"queries": [{"package": {"ecosystem": "Maven", "name": f"{group}:{name}"}, "version": version} for group, name, version in batch]}
        request = urllib.request.Request("https://api.osv.dev/v1/querybatch", json.dumps(payload).encode(), {"Content-Type": "application/json"}, method="POST")
        with urllib.request.urlopen(request, timeout=30) as response:
            raw = response.read(8 * 1024 * 1024 + 1)
        if len(raw) > 8 * 1024 * 1024:
            raise ValueError("OSV response exceeds its bound")
        decoded = json.loads(raw)
        if not isinstance(decoded, dict):
            raise ValueError("OSV response must be an object")
        items = decoded.get("results")
        if not isinstance(items, list) or len(items) != len(batch) or any(not isinstance(item, dict) for item in items):
            raise ValueError("OSV did not return one result for every dependency")
        for coordinate, item in zip(batch, items, strict=True):
            if item.get("next_page_token"):
                raise ValueError("OSV result is incomplete; investigate before delivery")
            if set(item) - {"vulns", "next_page_token"} or not isinstance(item.get("vulns", []), list):
                raise ValueError("OSV returned an error or invalid vulnerability list")
            results.append({"coordinate": ":".join(coordinate), "vulnerabilities": item.get("vulns", [])})
    return results


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--root", type=Path, default=ROOT)
    parser.add_argument("--inventory", type=Path)
    parser.add_argument("--gradle-cache", type=Path)
    parser.add_argument("--apk", type=Path)
    parser.add_argument("--audit-output", type=Path)
    args = parser.parse_args()
    try:
        verified = check_source(args.root)
        optional = (args.inventory, args.gradle_cache, args.apk, args.audit_output)
        if any(optional) and not all(optional):
            raise ValueError("Full audit requires inventory, Gradle cache, APK and output paths")
        if all(optional):
            policy = json.loads((args.root / "eng/android-dependency-policy.json").read_text(encoding="utf-8"))
            coordinates = runtime_inventory(args.inventory)
            if not set(coordinates).issubset(verified):
                raise ValueError("Runtime inventory contains components missing from Gradle verification")
            license_evidence = [licenses(args.gradle_cache / "caches/modules-2/files-2.1", item, policy) for item in coordinates]
            if not 0 < args.apk.stat().st_size <= policy["apk_size_budget_bytes"]:
                raise ValueError("APK is empty or exceeds the reviewed size budget")
            advisories = query_osv(coordinates)
            report = {"checked_at": datetime.now(timezone.utc).isoformat(), "scope": policy["scope"], "licenses": license_evidence, "osv": advisories, "apk_bytes": args.apk.stat().st_size, "apk_sha256": digest(args.apk)}
            args.audit_output.parent.mkdir(parents=True, exist_ok=True)
            args.audit_output.write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")
            if any(item["vulnerabilities"] for item in advisories):
                raise ValueError("OSV reports vulnerable runtime dependencies; see audit output")
            print(f"Android runtime audit passed ({len(coordinates)} dependencies).")
        print(f"Android dependency integrity passed ({len(verified)} verified components).")
        return 0
    except (OSError, ValueError, KeyError, ET.ParseError) as error:
        print(f"ANDROID_DEPENDENCY_CHECK_FAILED: {error}")
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
