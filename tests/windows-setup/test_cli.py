"""Exercise the actual self-contained installer using synthetic files and a temp-only registry adapter."""

import argparse
import hashlib
import json
from pathlib import Path
import subprocess
import tempfile


def make_package(root: Path, version: str) -> Path:
    package = root / ("package-" + version)
    payload = package / "payload"
    payload.mkdir(parents=True)
    entries = []
    for name in ("AssetLibrary.Setup.exe", "AssetLibrary.Host.exe", "AssetLibrary.Settings.exe", "AssetLibrary.Explorer.dll"):
        data = ("Synthetic fixture only: " + name).encode("utf-8")
        (payload / name).write_bytes(data)
        entries.append({"path": name, "size": len(data), "sha256": hashlib.sha256(data).hexdigest()})
    (package / "AssetLibrary.Setup.exe").write_bytes((payload / "AssetLibrary.Setup.exe").read_bytes())
    manifest = {"formatVersion": 1, "owner": "AssetLibrary.Windows.Explorer", "version": version,
                "rid": "win-x64", "files": entries}
    (package / "manifest.json").write_text(json.dumps(manifest), encoding="utf-8")
    return package


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--setup", required=True, type=Path)
    parser.add_argument("--report", required=True, type=Path)
    args = parser.parse_args()
    reports = []
    with tempfile.TemporaryDirectory(prefix="AssetLibrary-setup-cli-") as temporary:
        root = Path(temporary)
        isolated = root / "sandbox"
        first = make_package(root, "0.3.0-preview.1")
        second = make_package(root, "0.3.0-preview.2")
        for command, package, expected in (
            ("install", first, "installed"),
            ("status", first, "registered_current_process_view"),
            ("install", first, "installed"),
            ("install", second, "installed"),
            ("uninstall", second, "uninstalled"),
            ("status", second, "not_installed"),
            ("uninstall", second, "uninstalled"),
        ):
            result = subprocess.run([str(args.setup.resolve()), command, "--package", str(package),
                                     "--sandbox", str(isolated), "--quiet"], capture_output=True,
                                    text=True, encoding="utf-8", check=False, timeout=30)
            if result.returncode != 0:
                raise AssertionError(f"{command} failed: {result.returncode} {result.stderr}")
            response = json.loads(result.stdout)
            assert response["report"]["status"] == expected, response
            reports.append({"command": command, "status": expected, "exitCode": result.returncode})
        (first / "payload/AssetLibrary.Host.exe").write_bytes(b"damaged")
        result = subprocess.run([str(args.setup.resolve()), "install", "--package", str(first),
                                 "--sandbox", str(isolated), "--quiet"], capture_output=True,
                                text=True, encoding="utf-8", check=False, timeout=30)
        assert result.returncode == 1
        assert json.loads(result.stderr)["code"] == "hash_mismatch"
        reports.append({"command": "install-corrupted", "status": "hash_mismatch", "exitCode": 1})
    args.report.parent.mkdir(parents=True, exist_ok=True)
    evidence = {"passed": len(reports), "failed": 0, "syntheticPayload": True, "realHkcuUsed": False,
                "setupSha256": hashlib.sha256(args.setup.read_bytes()).hexdigest(), "steps": reports}
    args.report.write_text(json.dumps(evidence, indent=2) + "\n", encoding="utf-8")
    print(json.dumps(evidence))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
