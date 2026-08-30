"""Deterministic, streaming fixtures for performance and failure tests."""

from .generator import (
    BYTES_100_GIB,
    GeneratorConfig,
    AssetRecord,
    generate_assets,
    write_manifest,
    summarize_records,
    validate_output_path,
    write_summary,
)
from .faults import FaultKind, FaultPlan, build_fault_plan

__all__ = [
    "BYTES_100_GIB", "GeneratorConfig", "AssetRecord", "generate_assets", "write_manifest",
    "summarize_records", "validate_output_path", "write_summary", "FaultKind", "FaultPlan",
    "build_fault_plan",
]
