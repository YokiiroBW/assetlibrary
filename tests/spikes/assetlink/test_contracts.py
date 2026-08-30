from __future__ import annotations

import copy
import unittest
from pathlib import Path

from schema_support import (
    ContractError,
    MAX_UINT64,
    SchemaStore,
    byte_range_is_valid,
    chunk_fits,
    completion_matches,
    cursor_expired_requires_resync,
    duplicate_chunk_outcome,
    handshake_matches,
    load_json,
    negotiate_capabilities,
    negotiate_versions,
    parse_uint64,
    received_ranges_are_valid,
    replay_can_resume,
)

ROOT = Path(__file__).resolve().parents[3]
CONTRACTS = ROOT / "contracts" / "assetlink"
FIXTURES = ROOT / "tests" / "spikes" / "assetlink" / "fixtures"
STORE = SchemaStore(CONTRACTS)

VALID_FIXTURES = {
    "control-cancel.json": "control-cancel.schema.json",
    "control-error.json": "error.schema.json",
    "control-request.json": "control.schema.json",
    "control-result.json": "control-result.schema.json",
    "download-range.json": "download-range.schema.json",
    "download-result.json": "download-result.schema.json",
    "event.json": "event.schema.json",
    "handshake-response.json": "handshake-response.schema.json",
    "handshake-new-minor.json": "handshake.schema.json",
    "handshake-old-minor.json": "handshake.schema.json",
    "handshake.json": "handshake.schema.json",
    "replay-after-disconnect.json": "replay-request.schema.json",
    "replay-request.json": "replay-request.schema.json",
    "replay.json": "replay-result.schema.json",
    "retryable-error.json": "error.schema.json",
    "upload-cancel-result.json": "upload-cancel-result.schema.json",
    "upload-cancel.json": "upload-cancel.schema.json",
    "upload-chunk-result.json": "upload-chunk-result.schema.json",
    "upload-chunk.json": "upload-chunk.schema.json",
    "upload-create.json": "upload-create.schema.json",
    "upload-created.json": "upload-created.schema.json",
    "upload-status.json": "upload-status.schema.json",
    "valid-upload-complete-result.json": "upload-complete-result.schema.json",
    "valid-upload-complete.json": "upload-complete.schema.json",
}

INVALID_FIXTURES = {
    "invalid-canonical-offset.json": "upload-chunk.schema.json",
    "invalid-original-handshake-draft.json": "handshake.schema.json",
}


def fixture(name: str) -> dict[str, object]:
    value = load_json(FIXTURES / name)
    if not isinstance(value, dict):
        raise AssertionError(f"fixture must be an object: {name}")
    return value


class AssetLinkSchemaTests(unittest.TestCase):
    def test_schema_catalog_has_unique_ids_and_resolvable_refs(self) -> None:
        ids: list[str] = []
        reference_count = 0
        for name in STORE.names():
            path = CONTRACTS / name
            document = STORE.load(name)
            self.assertTrue(path.read_bytes().endswith(b"\n"), name)
            self.assertGreater(len(path.read_text(encoding="utf-8").splitlines()), 1)
            self.assertEqual(
                document["$schema"],
                "https://json-schema.org/draft/2020-12/schema",
            )
            self.assertIn("/assetlink/v1/", document["$id"])
            ids.append(document["$id"])
            reference_count += sum(
                1 for _ in STORE.iter_references(document, name)
            )
        self.assertEqual(len(ids), len(set(ids)))
        self.assertGreater(reference_count, 20)
        self.assertNotIn("replay.schema.json", STORE.names())

    def test_every_envelope_schema_has_a_valid_fixed_fixture(self) -> None:
        envelope_schemas = set(STORE.names()) - {"common.schema.json"}
        self.assertEqual(set(VALID_FIXTURES.values()), envelope_schemas)
        fixture_names = {path.name for path in FIXTURES.glob("*.json")}
        self.assertEqual(
            fixture_names,
            set(VALID_FIXTURES).union(INVALID_FIXTURES),
        )
        for fixture_name, schema_name in VALID_FIXTURES.items():
            with self.subTest(fixture=fixture_name, schema=schema_name):
                STORE.validate(schema_name, fixture(fixture_name))

    def test_invalid_fixed_fixtures_are_rejected(self) -> None:
        for fixture_name, schema_name in INVALID_FIXTURES.items():
            with self.subTest(fixture=fixture_name, schema=schema_name):
                self.assertFalse(
                    STORE.is_valid(schema_name, fixture(fixture_name)),
                )

    def test_required_fields_and_wire_types_are_enforced(self) -> None:
        request = fixture("upload-chunk.json")
        missing_request_id = copy.deepcopy(request)
        missing_request_id.pop("request_id")
        wrong_chunk_type = {**request, "chunk_size": "1024"}
        self.assertFalse(
            STORE.is_valid("upload-chunk.schema.json", missing_request_id),
        )
        self.assertFalse(
            STORE.is_valid("upload-chunk.schema.json", wrong_chunk_type),
        )

    def test_all_envelopes_accept_unknown_optional_fields(self) -> None:
        for fixture_name, schema_name in VALID_FIXTURES.items():
            value = {**fixture(fixture_name), "future_optional": {"v": 1}}
            with self.subTest(schema=schema_name):
                STORE.validate(schema_name, value)

    def test_future_string_values_are_preserved(self) -> None:
        handshake = fixture("handshake.json")
        handshake["client"] = {
            **handshake["client"],
            "kind": "future-client",
        }
        handshake["endpoint_role"] = "future-role"
        error = fixture("control-error.json")
        error["error"] = {**error["error"], "code": "future.code"}
        STORE.validate("handshake.schema.json", handshake)
        STORE.validate("error.schema.json", error)


class AssetLinkSemanticTests(unittest.TestCase):
    def test_uint64_decimal_boundaries(self) -> None:
        for value in ("0", "100000000000", str(MAX_UINT64)):
            with self.subTest(value=value):
                self.assertEqual(parse_uint64(value), int(value))
        for value in ("-1", "01", str(MAX_UINT64 + 1), "123456789012345678901"):
            with self.subTest(value=value):
                with self.assertRaises(ContractError):
                    parse_uint64(value)

    def test_version_negotiation_uses_exact_highest_intersection(self) -> None:
        self.assertEqual(
            negotiate_versions(["1.0", "1.1", "2.0"], ["1.0", "2.0"]),
            "2.0",
        )
        self.assertIsNone(negotiate_versions(["1.1"], ["1.2"]))
        self.assertIsNone(negotiate_versions(["1.0"], ["2.0"]))

    def test_capability_intersection_preserves_server_order(self) -> None:
        self.assertEqual(
            negotiate_capabilities(
                ["events.replay", "transfer.range"],
                ["server.future", "transfer.range", "events.replay"],
            ),
            ["transfer.range", "events.replay"],
        )

    def test_fixed_minor_and_unknown_capability_scenarios(self) -> None:
        old_client = fixture("handshake-old-minor.json")
        new_client = fixture("handshake-new-minor.json")
        server_versions = ["1.0", "1.1"]
        self.assertEqual(
            negotiate_versions(old_client["supported_versions"], server_versions),
            "1.0",
        )
        self.assertEqual(
            negotiate_versions(new_client["supported_versions"], server_versions),
            "1.1",
        )
        self.assertEqual(
            negotiate_capabilities(
                new_client["capabilities"],
                ["events.replay", "transfer.range"],
            ),
            ["events.replay"],
        )

    def test_fixed_disconnect_and_retry_scenarios(self) -> None:
        replay = fixture("replay-after-disconnect.json")
        retry = fixture("retryable-error.json")
        STORE.validate("replay-request.schema.json", replay)
        STORE.validate("error.schema.json", retry)
        self.assertEqual(replay["after_cursor"], "opaque:last-committed/cursor")
        self.assertTrue(retry["error"]["retryable"])
        self.assertEqual(retry["error"]["retry_after_ms"], 1500)

    def test_handshake_checks_selection_capabilities_and_failover_id(self) -> None:
        request = fixture("handshake.json")
        response = fixture("handshake-response.json")
        self.assertTrue(
            handshake_matches(
                request,
                response,
                ["1.0", "1.1"],
                ["events.replay"],
                "s",
            ),
        )
        self.assertFalse(
            handshake_matches(
                request,
                response,
                ["1.0", "1.1"],
                ["events.replay"],
                "different-server",
            ),
        )
        self.assertFalse(
            handshake_matches(
                request,
                {**response, "selected_version": "2.0"},
                ["1.0", "1.1"],
                ["events.replay"],
                "s",
            ),
        )

    def test_cursor_expiry_requires_explicit_snapshot_resync(self) -> None:
        error = fixture("control-error.json")
        self.assertTrue(cursor_expired_requires_resync(error))
        missing_token = copy.deepcopy(error)
        missing_token["error"]["details"].pop("snapshot_token")
        self.assertFalse(cursor_expired_requires_resync(missing_token))

    def test_replay_result_validates_nested_event_refs(self) -> None:
        replay = fixture("replay.json")
        replay["events"] = [fixture("event.json")]
        replay["next_cursor"] = replay["events"][0]["cursor"]
        STORE.validate("replay-result.schema.json", replay)
        self.assertTrue(replay_can_resume(replay))
        replay["events"][0].pop("cursor")
        self.assertFalse(STORE.is_valid("replay-result.schema.json", replay))

    def test_replay_rejects_gaps_and_non_monotonic_sequences(self) -> None:
        first = fixture("event.json")
        second = {
            **first,
            "event_id": "event-2",
            "cursor": "cursor-2",
            "sequence": str(parse_uint64(first["sequence"]) + 1),
        }
        replay = {
            **fixture("replay.json"),
            "events": [first, second],
            "next_cursor": second["cursor"],
        }
        self.assertTrue(replay_can_resume(replay))
        self.assertFalse(replay_can_resume({**replay, "gap": True}))
        self.assertFalse(
            replay_can_resume(
                {
                    **replay,
                    "events": [first, {**second, "sequence": first["sequence"]}],
                },
            ),
        )

    def test_chunk_bounds_and_size_limits(self) -> None:
        self.assertTrue(chunk_fits("0", 64 * 1024 * 1024, "100000000000"))
        self.assertTrue(chunk_fits("99999999000", 1000, "100000000000"))
        self.assertFalse(chunk_fits("99999999001", 1000, "100000000000"))
        self.assertFalse(chunk_fits("0", 0, "100000000000"))
        self.assertFalse(chunk_fits("0", 64 * 1024 * 1024 + 1, "100000000000"))

    def test_duplicate_chunk_hash_is_idempotent_but_mismatch_conflicts(self) -> None:
        digest = "a" * 64
        self.assertEqual(duplicate_chunk_outcome(digest, digest), "duplicate")
        self.assertEqual(
            duplicate_chunk_outcome(digest, "b" * 64),
            "idempotency_conflict",
        )

    def test_received_ranges_are_sorted_non_overlapping_and_bounded(self) -> None:
        status = fixture("upload-status.json")
        self.assertTrue(
            received_ranges_are_valid(
                status["received_ranges"],
                status["length"],
            ),
        )
        self.assertFalse(
            received_ranges_are_valid(
                [{"start": "10", "end": "19"}, {"start": "15", "end": "20"}],
                "100",
            ),
        )
        self.assertFalse(
            received_ranges_are_valid(
                [{"start": "90", "end": "100"}],
                "100",
            ),
        )

    def test_completion_requires_reread_length_hash_and_readability(self) -> None:
        request = fixture("valid-upload-complete.json")
        result = fixture("valid-upload-complete-result.json")
        expected_hash = "a" * 64
        self.assertTrue(
            completion_matches(
                request,
                result,
                "100000000000",
                expected_hash,
            ),
        )
        self.assertFalse(
            completion_matches(
                request,
                {**result, "verified_length": "99999999999"},
                "100000000000",
                expected_hash,
            ),
        )
        self.assertFalse(
            completion_matches(
                request,
                {**result, "verified_sha256": "b" * 64},
                "100000000000",
                expected_hash,
            ),
        )

    def test_download_range_and_result_are_bounded_and_verified(self) -> None:
        request = fixture("download-range.json")
        result = fixture("download-result.json")
        self.assertTrue(
            byte_range_is_valid(
                request["start"],
                request["end"],
                result["length"],
            ),
        )
        self.assertFalse(byte_range_is_valid("9", "10", "10"))
        self.assertFalse(byte_range_is_valid("10", "9", "20"))
        STORE.validate("download-result.schema.json", result)
        self.assertFalse(
            STORE.is_valid(
                "download-result.schema.json",
                {**result, "verified": False},
            ),
        )

    def test_original_handshake_draft_is_rejected(self) -> None:
        original_draft = fixture("invalid-original-handshake-draft.json")
        self.assertFalse(
            STORE.is_valid("handshake.schema.json", original_draft),
        )


if __name__ == "__main__":
    unittest.main()
