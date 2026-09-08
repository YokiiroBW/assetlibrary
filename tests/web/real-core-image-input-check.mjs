import assert from "node:assert/strict";
import test from "node:test";
import {
  corpusCases,
  discoverCorpusEntries,
  inspectDerivedPng,
  validateConnection,
  validateCorpus,
} from "./real-core-image-support.mjs";
import { png } from "./image-fixtures.mjs";

const connection = {
  origin: "https://localhost:55555",
  certificate_sha256: "a".repeat(64),
  library_id: "11111111-1111-4111-8111-111111111111",
  sample_file_count: 148,
  expires_at: "2036-09-08T00:00:00Z",
  account_name: "synthetic-admin",
  password: "synthetic-not-a-real-secret",
  invisible_account_name: "synthetic-reader",
  invisible_account_password: "synthetic-not-a-real-secret",
};

test("real acceptance accepts only the explicit localhost image rendezvous", () => {
  assert.equal(validateConnection(connection).origin, connection.origin);
  for (const origin of [
    "http://localhost:55555",
    "https://127.0.0.1:55555",
    "https://example.com:55555",
    "https://localhost:55555/private",
    "https://localhost:55555?token=synthetic",
    "https://user@localhost:55555",
  ])
    assert.throws(() => validateConnection({ ...connection, origin }));
});
test("real acceptance rejects expired, unpinned and wrong-corpus connections", () => {
  for (const changes of [
    { expires_at: "2000-01-01T00:00:00Z" },
    { certificate_sha256: "" },
    { library_id: "path" },
    { sample_file_count: 138 },
    { invisible_account_password: "" },
  ])
    assert.throws(() => validateConnection({ ...connection, ...changes }));
});
test("real acceptance requires the reviewed ten-case manifest", () => {
  const manifest = {
    kind: "synthetic_preview_integration_inputs",
    source_count: 10,
    files: corpusCases.map((item) => ({ path: item.path, bytes: 100, sha256: "b".repeat(64) })),
  };
  assert.equal(validateCorpus(manifest), manifest);
  assert.throws(() => validateCorpus({ ...manifest, kind: "personal_photos" }));
  assert.throws(() => validateCorpus({ ...manifest, files: manifest.files.slice(1) }));
  assert.throws(() =>
    validateCorpus({
      ...manifest,
      files: manifest.files.map((item, index) => (index ? item : { ...item, path: "../outside.png" })),
    }),
  );
});
test("real PNG acceptance checks profile, aspect, orientation and complete bytes", () => {
  const actual = inspectDerivedPng(png(512, 341), "thumbnail", [1800, 1200]);
  assert.equal(actual.width, 512);
  assert.equal(inspectDerivedPng(png(1067, 1600), "preview", [1200, 1800]).height, 1600);
  assert.throws(() => inspectDerivedPng(png(1600, 1067), "preview", [1200, 1800]));
  assert.throws(() => inspectDerivedPng(png(513, 342), "thumbnail", [1800, 1200]));
  assert.throws(() => inspectDerivedPng(png().subarray(0, 40), "preview", [320, 200]));
});

function searchHit(path, index, kind = "file") {
  return {
    library: { library_id: connection.library_id },
    entry: {
      library_id: connection.library_id,
      entry_id: `aaaaaaaa-aaaa-4aaa-8aaa-${String(index).padStart(12, "0")}`,
      relative_path: path,
      kind,
      name: path.split("/").at(-1),
    },
  };
}
function corpusHits() {
  return corpusCases.map((sample, index) => searchHit(`图片样例/${sample.path}`, index));
}
test("discovery accepts physical directory hits while selecting the exact ten files and wire names", () => {
  const hits = [
    ...corpusHits(),
    searchHit("图片样例", 10, "directory"),
    searchHit("图片样例/中文目录", 11, "directory"),
    searchHit("图片样例备份/other.png", 12),
  ];
  const files = discoverCorpusEntries(hits, connection.library_id);
  assert.equal(files.size, 10);
  assert.equal(files.get("图片样例/中文目录/重复内容.dat").name, "重复内容.dat");
  assert.equal(files.get("图片样例/landscape.jpg"), hits[0].entry);
});
test("discovery validates every association before filtering directory or unrelated hits", () => {
  for (const mismatch of ["library", "entry"]) {
    const extra = searchHit("图片样例/中文目录", 10, "directory");
    extra[mismatch].library_id = "bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb";
    assert.throws(() => discoverCorpusEntries([...corpusHits(), extra], connection.library_id));
  }
  const invalid = searchHit("图片样例备份/other.png", 10);
  invalid.entry.entry_id = "not-a-uuid";
  assert.throws(() => discoverCorpusEntries([...corpusHits(), invalid], connection.library_id));
});
test("discovery rejects missing, duplicate, non-file corpus entries and absent or false wire names", () => {
  assert.throws(() => discoverCorpusEntries(corpusHits().slice(1), connection.library_id));
  assert.throws(() =>
    discoverCorpusEntries([...corpusHits(), searchHit("图片样例/landscape.jpg", 10)], connection.library_id),
  );
  const duplicateId = corpusHits();
  duplicateId[1].entry.entry_id = duplicateId[0].entry.entry_id;
  assert.throws(() => discoverCorpusEntries(duplicateId, connection.library_id));
  for (const change of [
    { kind: "reparse_file" },
    { relative_path: "图片样例备份/landscape.jpg" },
    { name: undefined },
    { name: "wrong.png" },
  ]) {
    const hits = corpusHits();
    Object.assign(hits[0].entry, change);
    assert.throws(() => discoverCorpusEntries(hits, connection.library_id));
  }
});
