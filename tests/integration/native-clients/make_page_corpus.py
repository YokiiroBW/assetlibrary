"""Copy 120 real synthetic images for native paging acceptance; never decode media."""
from __future__ import annotations

import argparse
import hashlib
import json
from pathlib import Path
import shutil
import tempfile

ROOT = Path(__file__).resolve().parents[3]
SOURCE = Path(__file__).resolve().parent / "fixtures/image-preview-v1"
NAMES = ("landscape.jpg", "landscape.png", "transparent.png")


def sha256(path: Path) -> str:
    with path.open("rb") as stream:
        return hashlib.file_digest(stream, "sha256").hexdigest()


def generate(output: Path) -> Path:
    output = output.absolute()
    if any(part.is_symlink() or part.is_junction() for part in (output, *output.parents)):
        raise ValueError("Output must not traverse a link or junction")
    output = output.resolve()
    if not any(root.resolve() in output.parents for root in (ROOT / ".runtime", Path(tempfile.gettempdir()))):
        raise ValueError("Output must be inside this worktree's .runtime or the system temporary directory")
    manifest = json.loads((SOURCE / "manifest.json").read_text(encoding="utf-8"))
    if manifest.get("kind") != "synthetic_preview_integration_inputs":
        raise ValueError("Only the checked-in synthetic corpus is allowed")
    entries = {entry["path"]: entry for entry in manifest["files"]}
    for name in NAMES:
        path = SOURCE / name
        entry = entries[name]
        if path.is_symlink() or not 0 < path.stat().st_size <= 33554432 or path.stat().st_size != entry["bytes"] or sha256(path) != entry["sha256"]:
            raise ValueError("Checked-in synthetic input no longer matches its manifest")
    if sum(entries[name]["bytes"] for name in NAMES) * 40 > 67108864:
        raise ValueError("Synthetic page corpus exceeds its total byte budget")
    output.mkdir(parents=True, exist_ok=False)
    files = []
    for index in range(120):
        source = SOURCE / NAMES[index % len(NAMES)]
        target = output / f"{index + 1:03}{source.suffix}"
        shutil.copyfile(source, target)
        expected = entries[source.name]
        if target.stat().st_size != expected["bytes"] or sha256(target) != expected["sha256"]:
            raise ValueError("Synthetic page copy failed its readback")
        files.append({"path": target.name, "bytes": expected["bytes"], "sha256": expected["sha256"], "mtime_ns": target.stat().st_mtime_ns})
    result = {"kind": "synthetic_preview_integration_inputs", "source_count": len(files),
              "generated_with": "Python standard library; byte copies of three checked-in synthetic images",
              "acceptance_status": "not_run", "files": files}
    (output / "manifest.json").write_text(json.dumps(result, indent=2) + "\n", encoding="utf-8")
    return output


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--output", type=Path, default=ROOT / ".runtime/corpus")
    options = parser.parse_args()
    print(json.dumps({"corpus": str(generate(options.output)), "files": 120, "synthetic": True}))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
