"""Build the real Windows components, then assemble an unsigned, hash-verified preview.

This entry point never installs/registers a component or invokes asset APIs.
"""

from __future__ import annotations

import argparse
import hashlib
import json
import os
from pathlib import Path
import shutil
import struct
import subprocess
import sys
import zipfile


ROOT = Path(__file__).resolve().parents[2]
VERSION = "0.3.0-preview.8"
OWNER = "AssetLibrary.Windows.Explorer"
PROJECTS = {
    "Setup": "apps/windows-client/Setup/AssetLibrary.Windows.Setup.csproj",
    "Host": "apps/windows-client/AssetHost/AssetLibrary.Windows.AssetHost.csproj",
    "Settings": "apps/windows-client/Settings/AssetLibrary.Windows.Settings.csproj",
}


def run(command: list[str]) -> None:
    subprocess.run(command, cwd=ROOT, check=True, timeout=1200)


def sha256(path: Path) -> str:
    with path.open("rb") as stream:
        return hashlib.file_digest(stream, "sha256").hexdigest()


def regular_files(root: Path):
    if root.is_symlink() or root.is_junction():
        raise ValueError("Input directory must not be a link or junction")
    for path in sorted(root.rglob("*")):
        if path.is_symlink() or path.is_junction():
            raise ValueError("Input contains a link or junction")
        if path.is_file():
            yield path


def merge_file(source: Path, destination: Path) -> None:
    if destination.exists():
        if sha256(source) != sha256(destination):
            raise ValueError(f"Different components publish conflicting bytes: {destination.name}")
        return
    destination.parent.mkdir(parents=True, exist_ok=True)
    shutil.copyfile(source, destination)
    if sha256(source) != sha256(destination):
        raise ValueError("Copy readback failed")


def pe_imports(path: Path) -> set[str]:
    """Read bounded PE header/import metadata without loading executable code."""
    with path.open("rb") as stream:
        header = stream.read(4096)
        if header[:2] != b"MZ":
            raise ValueError(f"Not a Windows executable: {path.name}")
        offset = struct.unpack_from("<I", header, 60)[0]
        if offset > 1024 * 1024:
            raise ValueError("PE header offset outside supported bound")
        stream.seek(offset)
        header = stream.read(4096)
        if header[:4] != b"PE\0\0" or struct.unpack_from("<H", header, 4)[0] != 0x8664:
            raise ValueError(f"Component must be x64 PE: {path.name}")
        sections = struct.unpack_from("<H", header, 6)[0]
        optional_size = struct.unpack_from("<H", header, 20)[0]
        if sections > 64 or struct.unpack_from("<H", header, 24)[0] != 0x20B:
            raise ValueError("Unsupported PE layout")
        import_rva = struct.unpack_from("<I", header, 24 + 120)[0]
        section_data = header[24 + optional_size:]

        def file_offset(rva: int) -> int:
            for number in range(sections):
                virtual_size, virtual_address, raw_size, raw_offset = struct.unpack_from("<IIII", section_data, number * 40 + 8)
                if virtual_address <= rva < virtual_address + max(virtual_size, raw_size):
                    return raw_offset + rva - virtual_address
            raise ValueError("Import address outside PE sections")

        if import_rva == 0:
            return set()
        table = file_offset(import_rva)
        names = set()
        for number in range(256):
            stream.seek(table + number * 20)
            descriptor = stream.read(20)
            if descriptor == b"\0" * 20:
                return names
            name_rva = struct.unpack_from("<I", descriptor, 12)[0]
            stream.seek(file_offset(name_rva))
            raw_name = stream.read(256).split(b"\0", 1)[0]
            names.add(raw_name.decode("ascii").lower())
        raise ValueError("Too many PE imports")


def validate_binaries(payload: Path) -> dict[str, list[str]]:
    imports = {}
    for name in ("AssetLibrary.Setup.exe", "AssetLibrary.Host.exe", "AssetLibrary.Settings.exe", "AssetLibrary.Explorer.dll"):
        imports[name] = sorted(pe_imports(payload / name))
    if any(name.startswith(("vcruntime", "msvcp")) for name in imports["AssetLibrary.Explorer.dll"]):
        raise ValueError("Explorer DLL requires dynamic VC runtime; production must use the approved /MT build")
    for runtime in ("coreclr.dll", "hostfxr.dll", "hostpolicy.dll"):
        if not (payload / runtime).is_file():
            raise ValueError(f"Self-contained Host/Settings runtime is missing: {runtime}")
    settings_resources = payload / "AssetLibrary.Settings.pri"
    if not settings_resources.is_file() or settings_resources.stat().st_size == 0:
        raise ValueError("Settings application resources are missing or empty: AssetLibrary.Settings.pri")
    return imports


def assemble(output: Path, components: dict[str, Path], shell: Path, notices: list[Path], version: str = VERSION) -> Path:
    if output.exists():
        raise ValueError("Output already exists; choose a new output path to preserve previous artifacts")
    if not notices or any(not path.is_file() for path in notices):
        raise ValueError("Supply applicable Visual Studio/Windows SDK license or notice files with --notice")
    output.mkdir(parents=True)
    payload = output / "payload"
    payload.mkdir()
    for component in components.values():
        for source in regular_files(component):
            if source.suffix.lower() != ".pdb":
                merge_file(source, payload / source.relative_to(component))
    merge_file(shell, payload / "AssetLibrary.Explorer.dll")
    for number, notice in enumerate(notices, 1):
        merge_file(notice, payload / "notices" / f"{number}-{notice.name}")
    merge_file(ROOT / "infra/windows-client/THIRD-PARTY-NOTICES.md", payload / "notices/THIRD-PARTY-NOTICES.md")
    imports = validate_binaries(payload)
    merge_file(payload / "AssetLibrary.Setup.exe", output / "AssetLibrary.Setup.exe")
    manifest = {
        "formatVersion": 1,
        "owner": OWNER,
        "version": version,
        "rid": "win-x64",
        "files": [
            {"path": path.relative_to(payload).as_posix(), "size": path.stat().st_size, "sha256": sha256(path)}
            for path in regular_files(payload)
        ],
    }
    (output / "manifest.json").write_text(json.dumps(manifest, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    shutil.copyfile(ROOT / "infra/windows-client/INSTALL.zh-CN.md", output / "安装说明.md")
    revision = subprocess.check_output(["git", "rev-parse", "HEAD"], cwd=ROOT, text=True).strip()
    evidence = {"version": version, "rid": "win-x64", "unsignedPreview": True, "sourceCommit": revision,
                "manifestSha256": sha256(output / "manifest.json"), "imports": imports}
    (output / "build-evidence.json").write_text(json.dumps(evidence, indent=2) + "\n", encoding="utf-8")
    archive = output.parent / (output.name + ".zip")
    if archive.exists():
        raise ValueError("Archive already exists; refusing to replace it")
    with zipfile.ZipFile(archive, "w", compression=zipfile.ZIP_DEFLATED, compresslevel=6) as bundle:
        for file in regular_files(output):
            bundle.write(file, file.relative_to(output))
    archive.with_suffix(".zip.sha256").write_text(f"{sha256(archive)}  {archive.name}\n", encoding="ascii")
    return archive


def build(args) -> tuple[dict[str, Path], Path]:
    build_root = args.build_root.resolve()
    build_root.mkdir(parents=True, exist_ok=True)
    components = {}
    lock_root = (ROOT / "infra/windows-client/locks").resolve()
    for name, project in PROJECTS.items():
        properties = ["-p:RuntimeIdentifier=win-x64", "-p:SelfContained=true", f"-p:Version={VERSION}",
                      f"-p:AssetLibraryReleaseLockRoot={lock_root}"]
        # Settings pins Microsoft.NETCore.App in its project: a global runtime
        # version would also override its Windows SDK framework reference.
        if name != "Settings":
            properties += ["-p:RuntimeFrameworkVersion=10.0.11"]
        if name == "Setup":
            properties += ["-p:PublishSingleFile=true", "-p:IncludeNativeLibrariesForSelfExtract=true"]
        if name == "Host":
            properties += ["-p:AssetLibraryProductionHost=true"]
        run([args.dotnet, "restore", project, "--locked-mode", "--runtime", "win-x64", *properties])
        destination = build_root / "publish" / name
        if destination.exists():
            raise ValueError("Publish directory already exists; use a fresh --build-root")
        run([args.dotnet, "publish", project, "--configuration", "Release", "--runtime", "win-x64",
             "--self-contained", "true", "--no-restore", "--output", str(destination), *properties])
        components[name] = destination
    native = build_root / "shell"
    run([args.cmake, "-S", str(ROOT / "apps/windows-shell"), "-B", str(native),
         "-G", "Visual Studio 17 2022", "-A", "x64"])
    run([args.cmake, "--build", str(native), "--config", "Release", "--target", "AssetLibraryExplorer", "--parallel", "2"])
    return components, native / "Release/AssetLibrary.Explorer.dll"


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--output", required=True, type=Path)
    parser.add_argument("--build-root", type=Path, default=ROOT / ".runtime/windows-package-build")
    parser.add_argument("--dotnet", default=os.environ.get("DOTNET_HOST_PATH", "dotnet"))
    parser.add_argument("--cmake", default="cmake")
    parser.add_argument("--notice", action="append", type=Path, default=[])
    parser.add_argument("--assemble", action="store_true", help="Assemble already-published real components")
    parser.add_argument("--setup-dir", type=Path)
    parser.add_argument("--host-dir", type=Path)
    parser.add_argument("--settings-dir", type=Path)
    parser.add_argument("--shell-dll", type=Path)
    args = parser.parse_args()
    try:
        if args.assemble:
            values = (args.setup_dir, args.host_dir, args.settings_dir, args.shell_dll)
            if any(value is None for value in values):
                parser.error("--assemble requires --setup-dir, --host-dir, --settings-dir and --shell-dll")
            components = dict(zip(PROJECTS, values[:3]))
            shell = args.shell_dll
        else:
            components, shell = build(args)
        archive = assemble(args.output.resolve(), components, shell, args.notice)
        print(json.dumps({"archive": str(archive), "sha256": sha256(archive), "unsignedPreview": True}))
        return 0
    except (OSError, ValueError, subprocess.SubprocessError, struct.error) as error:
        print(f"Package build failed: {error}", file=sys.stderr)
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
