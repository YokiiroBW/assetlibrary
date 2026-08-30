"""Small executable oracle for the candidate manifest (no production dependency)."""
from __future__ import annotations

from typing import Any


class ManifestError(ValueError):
    pass


REQUIRED_LIMITS = (
    "max_request_bytes", "max_response_bytes", "max_artifact_bytes",
    "request_deadline_ms", "cpu_ms", "memory_bytes", "processes", "file_descriptors",
)
TRUST_CLASSES = {"official_signed", "side_loaded", "test_only"}


def validate_manifest(manifest: dict[str, Any]) -> dict[str, Any]:
    if not isinstance(manifest, dict) or manifest.get("manifest_version") != "1.0":
        raise ManifestError("unsupported manifest version")
    for field in ("provider_id", "provider_version", "api_versions", "capabilities", "trust_class", "library_scope", "permissions", "resource_limits"):
        if field not in manifest:
            raise ManifestError(f"missing manifest field: {field}")
    if not manifest["api_versions"] or any(not isinstance(v, str) for v in manifest["api_versions"]):
        raise ManifestError("api_versions must contain strings")
    if manifest["trust_class"] not in TRUST_CLASSES:
        raise ManifestError("unknown trust class")
    permissions = manifest["permissions"]
    if permissions.get("original_write") is not False:
        raise ManifestError("original_write is permanently false")
    if permissions.get("input_tokens_only") is not True:
        raise ManifestError("input_tokens_only is permanently true")
    limits = manifest["resource_limits"]
    for field in REQUIRED_LIMITS:
        value = limits.get(field)
        if not isinstance(value, int) or isinstance(value, bool) or value <= 0:
            raise ManifestError(f"invalid resource limit: {field}")
    if limits["max_request_bytes"] > 1024 * 1024 or limits["max_response_bytes"] > 1024 * 1024:
        raise ManifestError("message limit exceeds candidate maximum")
    if limits["max_artifact_bytes"] > 64 * 1024 * 1024:
        raise ManifestError("artifact limit exceeds candidate maximum")
    if permissions.get("network") and not permissions.get("approved_network_profile"):
        raise ManifestError("network requires an approved profile")
    return manifest


def negotiate_api(client_versions: list[str], provider_versions: list[str]) -> str | None:
    common = set(client_versions).intersection(provider_versions)
    if not common:
        return None
    return max(common, key=lambda value: tuple(int(part) for part in value.split(".", 1)))


def safe_mode_allows(manifest: dict[str, Any], *, safe_mode: bool) -> bool:
    """Safe mode permits official/signed candidates only."""
    return not safe_mode or manifest.get("trust_class") == "official_signed"


def result_is_l0_safe(result: dict[str, Any]) -> bool:
    return result.get("original_write") is False and result.get("asset_level") in {"L0", "L1", None}


def degrade_to_l0(asset: dict[str, Any], reason: str) -> dict[str, Any]:
    """Provider failure fallback: keep base browsing and external-open metadata."""
    if not reason or asset.get("original_write") is True:
        raise ManifestError("unsafe asset cannot enter L0 fallback")
    return {
        **asset,
        "asset_level": "L0",
        "browseable": True,
        "external_open": True,
        "provider_status": "degraded",
        "provider_failure": reason,
        "original_write": False,
    }
