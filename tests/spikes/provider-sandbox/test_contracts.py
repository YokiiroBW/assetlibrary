from __future__ import annotations

import json
import unittest
from pathlib import Path

from manifest import ManifestError, negotiate_api, safe_mode_allows, validate_manifest
from protocol import ProtocolError, encode_frame, read_frame

ROOT = Path(__file__).resolve().parents[3]
CONTRACTS = ROOT / "contracts" / "providers"
FIXTURES = Path(__file__).with_name("fixtures")


def load(name: str):
    return json.loads((FIXTURES / name).read_text(encoding="utf-8"))


class ProviderContractTests(unittest.TestCase):
    def test_schemas_parse_and_local_refs_resolve(self) -> None:
        for path in CONTRACTS.glob("*.schema.json"):
            value = json.loads(path.read_text(encoding="utf-8"))
            self.assertEqual(value["$schema"], "https://json-schema.org/draft/2020-12/schema")
            self.assertTrue(value["$id"].startswith("https://assetlibrary.local/contracts/providers/"))
            text = path.read_text(encoding="utf-8")
            self.assertNotIn('"$ref": "other-file', text)
            self.assertTrue(text.endswith("\n"))

    def test_manifest_positive_unknown_fields_and_capability_are_retained(self) -> None:
        manifest = load("manifest-valid.json")
        self.assertIs(validate_manifest(manifest), manifest)
        self.assertIn("future.capability", manifest["capabilities"])
        self.assertTrue(manifest["future_optional"]["retained"])
        self.assertTrue(safe_mode_allows(manifest, safe_mode=True))

    def test_manifest_rejects_original_write_and_unapproved_network(self) -> None:
        with self.assertRaises(ManifestError):
            validate_manifest(load("manifest-invalid-write.json"))
        network = load("manifest-valid.json")
        network["permissions"] = {**network["permissions"], "network": True}
        with self.assertRaises(ManifestError):
            validate_manifest(network)

    def test_safe_mode_blocks_side_loaded_and_api_fails_closed(self) -> None:
        side_loaded = load("manifest-valid.json")
        side_loaded["trust_class"] = "side_loaded"
        self.assertFalse(safe_mode_allows(side_loaded, safe_mode=True))
        self.assertEqual(negotiate_api(["2.0"], ["1.0"]), None)
        self.assertEqual(negotiate_api(["1.0", "2.0"], ["1.0"]), "1.0")

    def test_every_rpc_envelope_has_correlation_and_unknown_optional_data(self) -> None:
        messages = load("messages.json")
        self.assertEqual({m["message_type"] for m in messages}, {"discover", "handshake", "request", "result", "error", "cancel", "health"})
        for message in messages:
            self.assertIsInstance(message["request_id"], str)
            framed = encode_frame({**message, "future_optional": {"preserve": True}})
            decoded = read_frame(__import__("io").BytesIO(framed))
            self.assertEqual(decoded["future_optional"], {"preserve": True})

    def test_framing_rejects_invalid_or_oversized_input_before_allocation(self) -> None:
        with self.assertRaises(ProtocolError):
            encode_frame({"message_type": "request", "payload": "x" * 100}, max_bytes=32)
        with self.assertRaises(ProtocolError):
            read_frame(__import__("io").BytesIO((1000).to_bytes(4, "big")), max_bytes=32)
        malformed = (4).to_bytes(4, "big") + b"oops"
        with self.assertRaises(ProtocolError):
            read_frame(__import__("io").BytesIO(malformed))


if __name__ == "__main__":
    unittest.main()
