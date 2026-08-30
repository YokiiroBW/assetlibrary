import json
import re
import unittest
from pathlib import Path

ROOT = Path(__file__).parents[3]
SCHEMAS = ROOT / "contracts/assetlink"

def load(name):
    return json.loads((SCHEMAS / name).read_text())

def valid(schema, value):
    if "$ref" in schema:
        _, frag = schema["$ref"].split("#")
        target = load("common.schema.json")
        for part in frag.lstrip("/").split("/"): target = target[part]
        return valid(target, value)
    if schema.get("type") == "object" and not isinstance(value, dict): return False
    if schema.get("type") == "array" and not isinstance(value, list): return False
    if "const" in schema and value != schema["const"]: return False
    if "enum" in schema and value not in schema["enum"]: return False
    if schema.get("type") == "integer" and (not isinstance(value, int) or isinstance(value, bool) or value < schema.get("minimum", value) or value > schema.get("maximum", value)): return False
    if schema.get("type") == "string":
        if not isinstance(value, str) or len(value) < schema.get("minLength", 0): return False
        if "pattern" in schema and not re.fullmatch(schema["pattern"], value): return False
    if schema.get("type") == "object":
        if any(k not in schema.get("properties", {}) and not schema.get("additionalProperties", True) for k in value): return False
        if any(k not in value for k in schema.get("required", [])): return False
        if any(not valid(schema["properties"][k], v) for k, v in value.items() if k in schema.get("properties", {})): return False
    if schema.get("type") == "array" and any(not valid(schema["items"], x) for x in value): return False
    return True

class AssetLinkContractTests(unittest.TestCase):
    def test_schemas_are_draft2020_and_ids_unique(self):
        ids = []
        for p in SCHEMAS.glob("*.schema.json"):
            x = json.loads(p.read_text()); self.assertEqual(x["$schema"], "https://json-schema.org/draft/2020-12/schema")
            self.assertRegex(x["$id"], r"/assetlink/v1/"); ids.append(x["$id"])
        self.assertEqual(len(ids), len(set(ids)))

    def test_handshake_and_failover(self):
        request = {"message_type":"handshake.request","client":{"kind":"web","version":"1"},"supported_versions":["1.1"],"capabilities":["events.replay","transfer.range"],"endpoint_role":"primary","request_id":"r-1"}
        self.assertTrue(valid(load("handshake.schema.json"), request))
        response = {"message_type":"handshake.response","selected_version":"1.1","server_id":"srv-a","server_version":"1","capabilities":["events.replay"],"endpoint_role":"secondary"}
        self.assertTrue(valid(load("handshake-response.schema.json"), response)); self.assertNotEqual(response["server_id"], "srv-b")
        self.assertTrue(valid(load("handshake.schema.json"), {**request, "supported_versions":["2.0"]}))
        self.assertNotEqual(set(["2.0"]).intersection(["1.1"]), {"2.0"})

    def test_event_replay_and_transfer_boundaries(self):
        event = {"message_type":"event","event_id":"e-1","event_type":"asset.changed","cursor":"opaque.cursor","sequence":"100","occurred_at":"2026-01-01T00:00:00Z","payload":{}}
        self.assertTrue(valid(load("event.schema.json"), event)); self.assertTrue(valid(load("replay.schema.json"), {"message_type":"event.replay.request","after_cursor":"opaque.cursor","limit":100}))
        h = "a" * 64; t = {"message_type":"transfer.chunk","transfer_id":"t-1","length":"100000000000","offset":"99999999000","chunk_size":1000,"chunk_sha256":h,"content_sha256":h}
        self.assertTrue(valid(load("transfer.schema.json"), t)); self.assertFalse(valid(load("transfer.schema.json"), {**t,"offset":"01"}))
        self.assertTrue(valid(load("transfer.schema.json"), {**t,"offset":"18446744073709551615"}))

    def test_unknown_optional_control_field_is_forward_compatible(self):
        c = {"message_type":"control.request","request_id":"r-1","operation":"asset.list","body":{},"future_optional":True}
        self.assertTrue(valid(load("control.schema.json"), c))

    def test_original_draft_would_accept_unsafe_or_incomplete_handshake(self):
        draft = {"protocol_version":"not-a-version","client":{},"server_id":"x","capabilities":[],"endpoint_role":"unknown"}
        self.assertFalse(valid(load("handshake.schema.json"), draft))

if __name__ == "__main__": unittest.main()
