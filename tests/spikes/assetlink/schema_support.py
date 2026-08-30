"""Small executable contract oracle for the AssetLink M0 schema subset.

This is intentionally not a general JSON Schema implementation. It implements
every keyword used by contracts/assetlink and keeps cross-message semantics
explicit for the M0 protocol candidate.
"""

from __future__ import annotations

import json
import re
from pathlib import Path
from typing import Any, Iterator

MAX_UINT64 = (1 << 64) - 1
UINT64_PATTERN = re.compile(r"0|[1-9][0-9]*")
VERSION_PATTERN = re.compile(r"[1-9][0-9]*\.[0-9]+")


class ContractError(ValueError):
    """Raised when a wire value violates the executable M0 contract."""


def load_json(path: Path) -> Any:
    return json.loads(path.read_text(encoding="utf-8"))


def parse_uint64(value: object) -> int:
    if not isinstance(value, str) or UINT64_PATTERN.fullmatch(value) is None:
        raise ContractError("uint64 must be a canonical decimal string")
    if len(value) > 20:
        raise ContractError("uint64 exceeds 20 decimal digits")
    parsed = int(value)
    if parsed > MAX_UINT64:
        raise ContractError("uint64 exceeds 18446744073709551615")
    return parsed


def parse_version(value: str) -> tuple[int, int]:
    if VERSION_PATTERN.fullmatch(value) is None:
        raise ContractError(f"invalid protocol version: {value}")
    major, minor = value.split(".", 1)
    return int(major), int(minor)


def negotiate_versions(
    client_offers: list[str],
    server_preference: list[str],
) -> str | None:
    """Choose the highest exact version supported by both endpoints."""
    for version in client_offers + server_preference:
        parse_version(version)
    common = set(client_offers).intersection(server_preference)
    return max(common, key=parse_version, default=None)


def negotiate_capabilities(
    client_capabilities: list[str],
    server_capabilities: list[str],
) -> list[str]:
    offered = set(client_capabilities)
    return [capability for capability in server_capabilities if capability in offered]


def handshake_matches(
    request: dict[str, Any],
    response: dict[str, Any],
    server_versions: list[str],
    server_capabilities: list[str],
    expected_server_id: str,
) -> bool:
    selected = negotiate_versions(request["supported_versions"], server_versions)
    expected_capabilities = negotiate_capabilities(
        request["capabilities"],
        server_capabilities,
    )
    return (
        selected is not None
        and response["selected_version"] == selected
        and response["selected_version"] in request["supported_versions"]
        and response["selected_version"] in server_versions
        and response["capabilities"] == expected_capabilities
        and response["server_id"] == expected_server_id
    )


def byte_range_is_valid(start: str, end: str, length: str) -> bool:
    try:
        parsed_start = parse_uint64(start)
        parsed_end = parse_uint64(end)
        parsed_length = parse_uint64(length)
    except ContractError:
        return False
    return parsed_length > 0 and parsed_start <= parsed_end < parsed_length


def received_ranges_are_valid(
    ranges: list[dict[str, str]],
    length: str,
) -> bool:
    previous_end = -1
    for byte_range in ranges:
        if not byte_range_is_valid(
            byte_range["start"],
            byte_range["end"],
            length,
        ):
            return False
        start = parse_uint64(byte_range["start"])
        end = parse_uint64(byte_range["end"])
        if start <= previous_end:
            return False
        previous_end = end
    return True


def chunk_fits(offset: str, chunk_size: int, length: str) -> bool:
    if not isinstance(chunk_size, int) or isinstance(chunk_size, bool):
        return False
    if not 1 <= chunk_size <= 64 * 1024 * 1024:
        return False
    try:
        parsed_offset = parse_uint64(offset)
        parsed_length = parse_uint64(length)
    except ContractError:
        return False
    return parsed_offset + chunk_size <= parsed_length


def duplicate_chunk_outcome(previous_hash: str, retried_hash: str) -> str:
    if previous_hash == retried_hash:
        return "duplicate"
    return "idempotency_conflict"


def completion_matches(
    request: dict[str, Any],
    result: dict[str, Any],
    expected_length: str,
    expected_hash: str,
) -> bool:
    return (
        result["request_id"] == request["request_id"]
        and result["transfer_id"] == request["transfer_id"]
        and request["content_sha256"].lower() == expected_hash.lower()
        and result["verified"] is True
        and result["readable"] is True
        and result["verified_length"] == expected_length
        and result["verified_sha256"].lower() == expected_hash.lower()
    )


def replay_can_resume(result: dict[str, Any]) -> bool:
    """Require a gap-free, strictly ordered replay before live continuation."""
    if result.get("gap") is not False:
        return False
    events = result.get("events")
    if not isinstance(events, list):
        return False
    previous_sequence = -1
    for event in events:
        try:
            sequence = parse_uint64(event["sequence"])
        except (ContractError, KeyError, TypeError):
            return False
        if sequence <= previous_sequence:
            return False
        previous_sequence = sequence
    return not events or result.get("next_cursor") == events[-1].get("cursor")


def cursor_expired_requires_resync(error: dict[str, Any]) -> bool:
    details = error.get("error", {}).get("details", {})
    return (
        error.get("error", {}).get("code") == "cursor_expired"
        and details.get("action") == "full-resync"
        and isinstance(details.get("snapshot_token"), str)
        and bool(details["snapshot_token"])
    )


def _json_pointer(document: Any, fragment: str) -> Any:
    current = document
    if not fragment:
        return current
    if not fragment.startswith("/"):
        raise ContractError(f"unsupported JSON pointer: #{fragment}")
    for token in fragment[1:].split("/"):
        token = token.replace("~1", "/").replace("~0", "~")
        current = current[token]
    return current


class SchemaStore:
    """Resolve and validate the JSON Schema subset used by AssetLink."""

    def __init__(self, root: Path):
        self.root = root.resolve()
        self._documents: dict[str, dict[str, Any]] = {}

    def names(self) -> list[str]:
        return sorted(path.name for path in self.root.glob("*.schema.json"))

    def load(self, name: str) -> dict[str, Any]:
        if name not in self._documents:
            path = (self.root / name).resolve()
            if path.parent != self.root:
                raise ContractError(f"schema path escapes contract root: {name}")
            self._documents[name] = load_json(path)
        return self._documents[name]

    def resolve(
        self,
        reference: str,
        current_name: str,
    ) -> tuple[dict[str, Any], str]:
        document_name, separator, fragment = reference.partition("#")
        target_name = document_name or current_name
        target = self.load(target_name)
        if separator:
            target = _json_pointer(target, fragment)
        if not isinstance(target, dict):
            raise ContractError(f"$ref does not resolve to a schema: {reference}")
        return target, target_name

    def iter_references(
        self,
        value: Any,
        current_name: str,
    ) -> Iterator[tuple[str, str]]:
        if isinstance(value, dict):
            for key, child in value.items():
                if key == "$ref":
                    self.resolve(child, current_name)
                    yield current_name, child
                else:
                    yield from self.iter_references(child, current_name)
        elif isinstance(value, list):
            for child in value:
                yield from self.iter_references(child, current_name)

    def validate(self, name: str, value: Any) -> None:
        self._validate(self.load(name), value, name, "$")

    def is_valid(self, name: str, value: Any) -> bool:
        try:
            self.validate(name, value)
        except (ContractError, KeyError, TypeError, ValueError):
            return False
        return True

    def _validate(
        self,
        schema: dict[str, Any],
        value: Any,
        current_name: str,
        location: str,
    ) -> None:
        if "$ref" in schema:
            target, target_name = self.resolve(schema["$ref"], current_name)
            self._validate(target, value, target_name, location)
            return

        if "allOf" in schema:
            for child in schema["allOf"]:
                self._validate(child, value, current_name, location)
        if "anyOf" in schema:
            if not any(self._branch_valid(child, value, current_name, location)
                       for child in schema["anyOf"]):
                raise ContractError(f"{location} matches no anyOf branch")
        if "oneOf" in schema:
            matches = sum(
                self._branch_valid(child, value, current_name, location)
                for child in schema["oneOf"]
            )
            if matches != 1:
                raise ContractError(f"{location} matches {matches} oneOf branches")

        if "const" in schema and value != schema["const"]:
            raise ContractError(f"{location} does not match const")
        if "enum" in schema and value not in schema["enum"]:
            raise ContractError(f"{location} is not an allowed enum value")

        expected_type = schema.get("type")
        if expected_type == "object":
            self._validate_object(schema, value, current_name, location)
        elif expected_type == "array":
            self._validate_array(schema, value, current_name, location)
        elif expected_type == "string":
            self._validate_string(schema, value, location)
        elif expected_type == "integer":
            self._validate_integer(schema, value, location)
        elif expected_type == "boolean":
            if not isinstance(value, bool):
                raise ContractError(f"{location} must be a boolean")

    def _branch_valid(
        self,
        schema: dict[str, Any],
        value: Any,
        current_name: str,
        location: str,
    ) -> bool:
        try:
            self._validate(schema, value, current_name, location)
        except ContractError:
            return False
        return True

    def _validate_object(
        self,
        schema: dict[str, Any],
        value: Any,
        current_name: str,
        location: str,
    ) -> None:
        if not isinstance(value, dict):
            raise ContractError(f"{location} must be an object")
        for key in schema.get("required", []):
            if key not in value:
                raise ContractError(f"{location}.{key} is required")
        properties = schema.get("properties", {})
        for key, child in value.items():
            if key in properties:
                self._validate(
                    properties[key],
                    child,
                    current_name,
                    f"{location}.{key}",
                )
            elif schema.get("additionalProperties") is False:
                raise ContractError(f"{location}.{key} is not allowed")
            elif isinstance(schema.get("additionalProperties"), dict):
                self._validate(
                    schema["additionalProperties"],
                    child,
                    current_name,
                    f"{location}.{key}",
                )

    def _validate_array(
        self,
        schema: dict[str, Any],
        value: Any,
        current_name: str,
        location: str,
    ) -> None:
        if not isinstance(value, list):
            raise ContractError(f"{location} must be an array")
        if len(value) < schema.get("minItems", 0):
            raise ContractError(f"{location} has too few items")
        if len(value) > schema.get("maxItems", len(value)):
            raise ContractError(f"{location} has too many items")
        if schema.get("uniqueItems"):
            normalized = [json.dumps(item, sort_keys=True) for item in value]
            if len(normalized) != len(set(normalized)):
                raise ContractError(f"{location} must contain unique items")
        if "items" in schema:
            for index, child in enumerate(value):
                self._validate(
                    schema["items"],
                    child,
                    current_name,
                    f"{location}[{index}]",
                )

    def _validate_string(
        self,
        schema: dict[str, Any],
        value: Any,
        location: str,
    ) -> None:
        if not isinstance(value, str):
            raise ContractError(f"{location} must be a string")
        if len(value) < schema.get("minLength", 0):
            raise ContractError(f"{location} is too short")
        if len(value) > schema.get("maxLength", len(value)):
            raise ContractError(f"{location} is too long")
        pattern = schema.get("pattern")
        if pattern is not None and re.fullmatch(pattern, value) is None:
            raise ContractError(f"{location} does not match pattern")
        if schema.get("format") == "uint64-decimal":
            parse_uint64(value)

    def _validate_integer(
        self,
        schema: dict[str, Any],
        value: Any,
        location: str,
    ) -> None:
        if not isinstance(value, int) or isinstance(value, bool):
            raise ContractError(f"{location} must be an integer")
        if value < schema.get("minimum", value):
            raise ContractError(f"{location} is below minimum")
        if value > schema.get("maximum", value):
            raise ContractError(f"{location} exceeds maximum")
