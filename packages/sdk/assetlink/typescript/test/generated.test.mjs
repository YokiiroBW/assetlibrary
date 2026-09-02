import assert from "node:assert/strict";
import test from "node:test";

import {
  classifyAssetLinkMessage,
  encodeAssetLinkMessage,
  formatAssetLinkUint64,
  parseAssetLinkMessage,
  parseAssetLinkUint64,
} from "../dist/generated.js";

test("known and future wire values retain unknown fields", () => {
  const source = {
    message_type: "control.request",
    request_id: "request-1",
    operation: "future.operation",
    body: {},
    future_field: { answer: 42 },
  };

  const parsed = parseAssetLinkMessage(JSON.stringify(source));

  assert.equal(classifyAssetLinkMessage(parsed.message_type), "control.request");
  assert.deepEqual(JSON.parse(encodeAssetLinkMessage(parsed)), source);
});

test("unknown message types use the explicit unknown branch", () => {
  const parsed = parseAssetLinkMessage(
    JSON.stringify({ message_type: "future.message", future_enum: "new-value" }),
  );

  assert.equal(classifyAssetLinkMessage(parsed.message_type), "unknown");
  assert.equal(parsed.future_enum, "new-value");
});

test("uint64 helpers accept both boundaries", () => {
  for (const value of ["0", "1", "18446744073709551615"]) {
    assert.equal(formatAssetLinkUint64(parseAssetLinkUint64(value)), value);
  }
});

test("uint64 helpers reject non-canonical and overflow values", () => {
  for (const value of ["", "+1", "-1", "00", "01", "18446744073709551616", "100000000000000000000"]) {
    assert.throws(() => parseAssetLinkUint64(value), RangeError);
  }
  assert.throws(() => formatAssetLinkUint64(-1n), RangeError);
  assert.throws(() => formatAssetLinkUint64(18446744073709551616n), RangeError);
});

test("non-object documents and missing message_type fail closed", () => {
  assert.throws(() => parseAssetLinkMessage("[]"), TypeError);
  assert.throws(() => parseAssetLinkMessage("{}"), TypeError);
  assert.throws(() => parseAssetLinkMessage('{"message_type":42}'), TypeError);
});
