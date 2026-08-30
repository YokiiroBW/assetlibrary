"""Bounded length-prefixed JSON protocol used only by the M0-008 fixture."""
from __future__ import annotations

import json
import struct
from typing import BinaryIO, Any


class ProtocolError(ValueError):
    pass


HEADER_BYTES = 4
DEFAULT_MAX_FRAME = 64 * 1024


def encode_frame(message: dict[str, Any], max_bytes: int = DEFAULT_MAX_FRAME) -> bytes:
    if not isinstance(message, dict):
        raise ProtocolError("RPC envelope must be an object")
    try:
        payload = json.dumps(message, ensure_ascii=False, separators=(",", ":")).encode("utf-8")
    except (TypeError, ValueError) as exc:
        raise ProtocolError("RPC envelope is not JSON encodable") from exc
    if len(payload) > max_bytes:
        raise ProtocolError(f"frame exceeds limit ({len(payload)} > {max_bytes})")
    return struct.pack(">I", len(payload)) + payload


def _read_exact(stream: BinaryIO, count: int) -> bytes:
    chunks: list[bytes] = []
    remaining = count
    while remaining:
        chunk = stream.read(remaining)
        if not chunk:
            raise EOFError("truncated RPC frame")
        chunks.append(chunk)
        remaining -= len(chunk)
    return b"".join(chunks)


def read_frame(stream: BinaryIO, max_bytes: int = DEFAULT_MAX_FRAME) -> dict[str, Any]:
    header = _read_exact(stream, HEADER_BYTES)
    (length,) = struct.unpack(">I", header)
    if length == 0 or length > max_bytes:
        raise ProtocolError(f"frame length {length} exceeds limit {max_bytes}")
    payload = _read_exact(stream, length)
    try:
        value = json.loads(payload.decode("utf-8"))
    except (UnicodeDecodeError, json.JSONDecodeError) as exc:
        raise ProtocolError("RPC payload is not valid UTF-8 JSON") from exc
    if not isinstance(value, dict):
        raise ProtocolError("RPC payload must be an object")
    return value


def canonical_size(message: dict[str, Any]) -> int:
    return len(json.dumps(message, ensure_ascii=False, separators=(",", ":")).encode("utf-8"))
