import assert from "node:assert/strict";
import { createHash, X509Certificate } from "node:crypto";
import { lstat, readFile } from "node:fs/promises";
import { connect } from "node:tls";

export const corpusCases = [
  { path: "landscape.jpg", size: [1800, 1200], result: "image" },
  { path: "landscape.png", size: [1800, 1200], result: "image" },
  { path: "landscape.webp", size: [1800, 1200], result: "image" },
  { path: "rotate-six.jpg", size: [1200, 1800], result: "image" },
  { path: "transparent.png", size: [900, 1200], result: "image", alpha: true },
  { path: "中文目录/重复内容.dat", size: [1800, 1200], result: "image" },
  { path: "active.svg", status: [415], codes: ["preview_unsupported"] },
  { path: "not-an-image.png", status: [415, 422], codes: ["preview_unsupported", "preview_invalid"] },
  { path: "truncated.jpg", status: [422], codes: ["preview_invalid"] },
  { path: "oversized-header.png", status: [422], codes: ["preview_limit_exceeded"] },
];

const uuid = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;
export function validateConnection(value, now = Date.now()) {
  assert.ok(value !== null && typeof value === "object", "connection must be an object");
  const origin = new URL(value.origin);
  assert.ok(
    origin.protocol === "https:" && origin.hostname === "localhost" && origin.port !== "",
    "only the shared localhost HTTPS fixture is allowed",
  );
  assert.ok(
    !origin.username && !origin.password && !origin.search && !origin.hash && origin.pathname === "/",
    "connection must be an exact origin",
  );
  assert.match(value.certificate_sha256 ?? "", /^[a-f0-9]{64}$/i, "an exact public leaf fingerprint is required");
  assert.match(value.library_id ?? "", uuid, "a stable fixture library UUID is required");
  assert.ok(
    Number.isInteger(value.sample_file_count) && value.sample_file_count === 148,
    "the shared 138+10 image fixture is required",
  );
  assert.ok(Date.parse(value.expires_at) - now >= 60_000, "fixture must have at least one minute remaining");
  for (const key of ["account_name", "password", "invisible_account_name", "invisible_account_password"])
    assert.ok(
      typeof value[key] === "string" && value[key].length > 0 && value[key].length <= 256,
      "required fixture credential field is absent or invalid",
    );
  return { ...value, origin: origin.origin };
}

export function validateCorpus(manifest) {
  assert.equal(
    manifest?.kind,
    "synthetic_preview_integration_inputs",
    "only the coordinator synthetic corpus is allowed",
  );
  assert.equal(manifest.source_count, corpusCases.length, "expected the ten-case corpus");
  assert.ok(
    Array.isArray(manifest.files) && manifest.files.length === corpusCases.length,
    "expected ten declared files",
  );
  assert.deepEqual(
    manifest.files.map((file) => file.path).sort(),
    corpusCases.map((file) => file.path).sort(),
    "corpus cases must match the reviewed generator",
  );
  for (const file of manifest.files) {
    assert.match(file.sha256 ?? "", /^[a-f0-9]{64}$/i, "source manifest digest is required");
    assert.ok(
      Number.isInteger(file.bytes) && file.bytes > 0 && file.bytes <= 33_554_432,
      "source size is out of bounds",
    );
  }
  return manifest;
}

export function discoverCorpusEntries(hits, libraryId) {
  assert.ok(Array.isArray(hits) && hits.length <= 100, "search discovery must stay within one bounded page");
  const ids = new Set();
  // Search can include physical directories. Validate every hit before narrowing to the corpus files.
  for (const hit of hits) {
    assert.ok(hit?.entry && hit?.library, "search hit associations are required");
    assert.equal(hit.library.library_id, libraryId, "search library must match the requested scope");
    assert.equal(hit.entry.library_id, libraryId, "search entry must belong to its associated library");
    assert.match(hit.entry.entry_id ?? "", uuid, "search entry needs a stable UUID");
    assert.ok(!ids.has(hit.entry.entry_id), "search entry IDs must be unique");
    ids.add(hit.entry.entry_id);
    assert.ok(
      ["file", "directory", "reparse_file", "reparse_directory"].includes(hit.entry.kind),
      "search kind is invalid",
    );
    assert.ok(
      typeof hit.entry.relative_path === "string" &&
        hit.entry.relative_path.length > 0 &&
        hit.entry.relative_path.length <= 4096,
      "search relative path is required",
    );
    // AssetLinkReadJson.Entry writes name on the wire; this is not a UI-derived fallback.
    assert.equal(
      hit.entry.name,
      hit.entry.relative_path.split("/").at(-1),
      "wire entry name must match its relative path",
    );
  }
  const files = hits.filter(({ entry }) => entry.kind === "file" && entry.relative_path.startsWith("图片样例/"));
  const entries = new Map();
  for (const { entry } of files) {
    assert.ok(!entries.has(entry.relative_path), "corpus file paths must be unique");
    entries.set(entry.relative_path, entry);
  }
  assert.deepEqual(
    [...entries.keys()].sort(),
    corpusCases.map((sample) => `图片样例/${sample.path}`).sort(),
    "the exact ten expected corpus files must be present once",
  );
  return entries;
}

export async function readBoundedJson(filename, maximum) {
  const info = await lstat(filename);
  assert.ok(
    info.isFile() && !info.isSymbolicLink() && info.size > 0 && info.size <= maximum,
    "input must be a bounded regular JSON file",
  );
  const bytes = await readFile(filename);
  assert.ok(bytes.length <= maximum, "JSON input grew beyond its bound");
  return JSON.parse(bytes.toString("utf8").replace(/^\uFEFF/, ""));
}

// No HTTP request or credential is sent until this no-data TLS probe validates the exact fixture leaf.
export function verifyFixtureTls(connection) {
  const origin = new URL(connection.origin);
  return new Promise((resolve, reject) => {
    const socket = connect({
      host: "localhost",
      port: Number(origin.port),
      servername: "localhost",
      rejectUnauthorized: false,
    });
    const timer = setTimeout(() => {
      socket.destroy();
      reject(new Error("fixture_tls_timeout"));
    }, 5000);
    socket.once("error", () => {
      clearTimeout(timer);
      reject(new Error("fixture_tls_failed"));
    });
    socket.once("secureConnect", () => {
      try {
        const raw = socket.getPeerCertificate().raw;
        assert.ok(raw, "fixture certificate missing");
        assert.equal(
          createHash("sha256").update(raw).digest("hex"),
          connection.certificate_sha256.toLowerCase(),
          "fixture leaf fingerprint changed",
        );
        const certificate = new X509Certificate(raw);
        assert.equal(certificate.checkHost("localhost"), "localhost", "fixture certificate hostname mismatch");
        assert.ok(
          Date.parse(certificate.validFrom) <= Date.now() && Date.parse(certificate.validTo) > Date.now(),
          "fixture certificate is outside its validity interval",
        );
        resolve(
          createHash("sha256")
            .update(certificate.publicKey.export({ type: "spki", format: "der" }))
            .digest("base64"),
        );
      } catch {
        reject(new Error("fixture_tls_identity_rejected"));
      } finally {
        clearTimeout(timer);
        socket.destroy();
      }
    });
  });
}

export function inspectDerivedPng(bytes, variant, originalSize) {
  const edge = variant === "thumbnail" ? 512 : 1600;
  const maximum = variant === "thumbnail" ? 2_097_152 : 12_582_912;
  assert.ok(bytes.length <= maximum && bytes.length >= 45, "derived body size is invalid");
  assert.equal(bytes.subarray(0, 8).toString("hex"), "89504e470d0a1a0a", "response is not a PNG");
  assert.equal(bytes.toString("ascii", 12, 16), "IHDR", "PNG header missing");
  const width = bytes.readUInt32BE(16),
    height = bytes.readUInt32BE(20);
  assert.ok(width > 0 && height > 0 && width <= edge && height <= edge, "derived dimensions exceed profile");
  const scale = Math.min(1, edge / Math.max(...originalSize));
  assert.ok(
    Math.abs(width - originalSize[0] * scale) <= 1 && Math.abs(height - originalSize[1] * scale) <= 1,
    "source orientation/aspect/profile does not match",
  );
  assert.equal(bytes[24], 8, "expected eight-bit output");
  let offset = 8;
  let ended = false;
  while (offset + 12 <= bytes.length) {
    const length = bytes.readUInt32BE(offset);
    const type = bytes.toString("ascii", offset + 4, offset + 8);
    assert.ok(length <= bytes.length - offset - 12, "PNG chunk is truncated");
    assert.ok(!["acTL", "eXIf", "tEXt", "iTXt", "zTXt"].includes(type), "output retains animation or source metadata");
    offset += length + 12;
    if (type === "IEND") {
      ended = true;
      break;
    }
  }
  assert.ok(ended && offset === bytes.length, "PNG ending is invalid");
  return { width, height, bytes: bytes.length, sha256: createHash("sha256").update(bytes).digest("hex") };
}
