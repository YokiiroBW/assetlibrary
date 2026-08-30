"""A bounded-memory synthetic asset manifest generator (Python 3.12 stdlib only)."""
from __future__ import annotations

import hashlib
import json
import os
import tempfile
from dataclasses import dataclass
from pathlib import Path
from typing import Callable, Iterator

SCHEMA_VERSION = "m0-007.asset-manifest.v1"
BYTES_100_GIB = 100 * 1024**3


@dataclass(frozen=True)
class GeneratorConfig:
    count: int = 1000
    seed: int = 0
    hot_directory_count: int = 100_000
    logical_huge_index: int = 0

    def __post_init__(self) -> None:
        if self.count < 0 or self.hot_directory_count < 0:
            raise ValueError("count and hot_directory_count must be non-negative")
        if self.logical_huge_index >= self.count and self.count:
            raise ValueError("logical_huge_index must refer to a generated record")


@dataclass(frozen=True)
class AssetRecord:
    schema_version: str
    record_type: str
    asset_id: str
    physical_relative_path: str
    extension: str
    logical_size_bytes: int
    modified_epoch_seconds: int
    fixture_hash: str
    directory_class: str


def _record(config: GeneratorConfig, index: int) -> AssetRecord:
    # Hash-derived fields avoid mutable PRNG state and make resume/retry exact.
    digest = hashlib.sha256(f"m0-007\0{config.seed}\0{index}".encode()).hexdigest()
    extensions = (".jpg", ".png", ".mp4", ".pdf", ".blend", ".flac")
    extension = extensions[index % len(extensions)]
    if index < config.hot_directory_count:
        directory = "hot-100k"
        name_index = index
    else:
        directory = f"nested/{index // 1000:04d}/{index % 1000:04d}"
        name_index = index % 1000
    # Deliberate repeated display names in separate directories; IDs remain unique.
    display_name = f"asset-{name_index:06d}"
    size = 1024 + ((index * 7919 + config.seed * 104729) % (8 * 1024 * 1024))
    if index == config.logical_huge_index:
        size = BYTES_100_GIB
    return AssetRecord(
        SCHEMA_VERSION, "asset", f"asset-{digest[:24]}",
        f"{directory}/{display_name}{extension}", extension, size,
        1_700_000_000 + ((index * 37 + config.seed) % 31_536_000),
        f"fixture-sha256:{digest}", directory,
    )


def generate_assets(config: GeneratorConfig, cancel: Callable[[], bool] | None = None) -> Iterator[AssetRecord]:
    """Yield records once, checking cancellation before each item."""
    for index in range(config.count):
        if cancel is not None and cancel():
            return
        yield _record(config, index)


def validate_output_path(path: str | os.PathLike[str], repository_root: str | os.PathLike[str] | None = None) -> Path:
    """Return a safe output path; reject escape and symlinked path components."""
    candidate = Path(path).expanduser()
    if not candidate.is_absolute():
        raise ValueError("output path must be absolute")
    root = Path(repository_root or Path(__file__).resolve().parents[3]).resolve()
    sandbox = (root / ".runtime" / "sandbox-storage").resolve()
    temp = Path(tempfile.gettempdir()).resolve()
    lexical_parent = candidate.parent
    probe = lexical_parent
    while probe != probe.parent and not probe.exists():
        probe = probe.parent
    cursor = probe
    while cursor != cursor.parent:
        if cursor.is_symlink():
            raise ValueError("symlink path components are not allowed")
        cursor = cursor.parent
    parent = lexical_parent.resolve(strict=False)
    allowed = parent == sandbox or sandbox in parent.parents or parent == temp or temp in parent.parents
    if not allowed:
        raise ValueError("output must be inside .runtime/sandbox-storage or the system temp directory")
    if candidate.exists() and candidate.is_symlink():
        raise ValueError("symlink output is not allowed")
    return candidate


def write_manifest(
    config: GeneratorConfig,
    output: str | os.PathLike[str],
    cancel: Callable[[], bool] | None = None,
) -> dict[str, int | str | bool]:
    """Atomically stream JSONL to a validated sandbox path and return its summary."""
    target = validate_output_path(output)
    target.parent.mkdir(parents=True, exist_ok=True)
    temp_path = target.with_name(f".{target.name}.partial")
    records = 0
    hot_directory_records = 0
    has_100gib_asset = False
    bytes_written = 0
    digest = hashlib.sha256()
    try:
        with temp_path.open("w", encoding="utf-8", newline="\n") as stream:
            for record in generate_assets(config, cancel):
                line = json.dumps(
                    record.__dict__, ensure_ascii=False, sort_keys=True,
                    separators=(",", ":"),
                ) + "\n"
                stream.write(line)
                encoded = line.encode("utf-8")
                digest.update(encoded)
                bytes_written += len(encoded)
                records += 1
                hot_directory_records += record.directory_class == "hot-100k"
                has_100gib_asset = has_100gib_asset or record.logical_size_bytes >= BYTES_100_GIB
            stream.flush()
            os.fsync(stream.fileno())
        cancelled = records < config.count
        if cancelled:
            temp_path.unlink(missing_ok=True)
            return {
                "schema_version": SCHEMA_VERSION, "records": records,
                "hot_directory_records": hot_directory_records,
                "has_100gib_asset": has_100gib_asset, "cancelled": True,
            }
        os.replace(temp_path, target)
    finally:
        temp_path.unlink(missing_ok=True)
    return {
        "schema_version": SCHEMA_VERSION, "records": records,
        "hot_directory_records": hot_directory_records,
        "has_100gib_asset": has_100gib_asset, "output_bytes": bytes_written,
        "manifest_sha256": digest.hexdigest(), "cancelled": False,
    }


def summarize_records(records: Iterator[AssetRecord]) -> dict[str, object]:
    count = 0
    logical = 0
    classes: dict[str, int] = {}
    hot = 0
    huge = False
    for item in records:
        count += 1
        logical += item.logical_size_bytes
        classes[item.extension] = classes.get(item.extension, 0) + 1
        hot += item.directory_class == "hot-100k"
        huge = huge or item.logical_size_bytes >= BYTES_100_GIB
    return {
        "schema_version": SCHEMA_VERSION,
        "records": count,
        "logical_size_bytes": logical,
        "extensions": classes,
        "hot_directory_records": hot,
        "has_100gib_asset": huge,
    }


def write_summary(
    summary: dict[str, object],
    output: str | os.PathLike[str],
    repository_root: str | os.PathLike[str] | None = None,
) -> Path:
    target = validate_output_path(output, repository_root)
    target.parent.mkdir(parents=True, exist_ok=True)
    temp_path = target.with_name(f".{target.name}.partial")
    try:
        with temp_path.open("w", encoding="utf-8", newline="\n") as stream:
            json.dump(summary, stream, ensure_ascii=False, sort_keys=True, indent=2)
            stream.write("\n")
            stream.flush()
            os.fsync(stream.fileno())
        os.replace(temp_path, target)
    finally:
        temp_path.unlink(missing_ok=True)
    return target
