import { deflateSync } from "node:zlib";
import { once } from "node:events";
import { createServer } from "node:http";
import {
  browsePage,
  entryDetail,
  libraryDetail,
  libraryPage,
  mockAssetLink,
  mockSession,
} from "./assetlink-fixtures.mjs";

export const imagePattern = "**/assetlink/v1/libraries/*/entries/*/image?variant=*";

// Synthetic RGBA data; no personal images or source-file metadata enter these fixtures.
export function png(width = 320, height = 200, tint = 0) {
  const header = Buffer.alloc(13);
  header.writeUInt32BE(width, 0);
  header.writeUInt32BE(height, 4);
  header[8] = 8;
  header[9] = 6;
  const pixels = Buffer.alloc((width * 4 + 1) * height);
  for (let y = 0; y < height; y++) {
    for (let x = 0; x < width; x++) {
      const at = y * (width * 4 + 1) + 1 + x * 4;
      pixels[at] = (35 + tint + Math.floor((x / width) * 130)) % 256;
      pixels[at + 1] = 85 + Math.floor((y / height) * 110);
      pixels[at + 2] = 140;
      pixels[at + 3] = x < width / 8 ? 100 : 255;
    }
  }
  return Buffer.concat([
    Buffer.from([137, 80, 78, 71, 13, 10, 26, 10]),
    chunk("IHDR", header),
    chunk("IDAT", deflateSync(pixels)),
    chunk("IEND", Buffer.alloc(0)),
  ]);
}

function chunk(type, data) {
  const content = Buffer.concat([Buffer.from(type), data]);
  let crc = 0xffffffff;
  for (const byte of content) {
    crc ^= byte;
    for (let i = 0; i < 8; i++) crc = (crc >>> 1) ^ (crc & 1 ? 0xedb88320 : 0);
  }
  const result = Buffer.alloc(data.length + 12);
  result.writeUInt32BE(data.length, 0);
  content.copy(result, 4);
  result.writeUInt32BE((crc ^ 0xffffffff) >>> 0, result.length - 4);
  return result;
}

export async function imageWorkspace(page, items) {
  await mockSession(page);
  await mockAssetLink(page, (request) => {
    if (request.operation === "libraries.list") return libraryPage(request);
    if (request.operation === "libraries.get") return libraryDetail(request);
    if (request.operation === "entries.get")
      return entryDetail(
        request,
        items.find((item) => item.entry_id === request.body.entry_id),
      );
    return browsePage(request, items);
  });
  await page.addInitScript(() => {
    const create = URL.createObjectURL.bind(URL);
    const revoke = URL.revokeObjectURL.bind(URL);
    window.liveImageUrls = new Set();
    URL.createObjectURL = (blob) => {
      const url = create(blob);
      window.liveImageUrls.add(url);
      return url;
    };
    URL.revokeObjectURL = (url) => {
      window.liveImageUrls.delete(url);
      revoke(url);
    };
  });
}

export async function servePng(route, body = png()) {
  await route.fulfill({
    status: 200,
    contentType: "image/png",
    headers: { "content-length": String(body.length), "cache-control": "private, no-store" },
    body,
  });
}

export async function startImageServer(page, handle) {
  const server = createServer((request, response) => {
    response.setHeader("access-control-allow-origin", "http://127.0.0.1:4173");
    handle(request, response);
  });
  server.listen(0, "127.0.0.1");
  await once(server, "listening");
  const { port } = server.address();
  await page.route(imagePattern, (route) =>
    route.continue({
      url: `http://127.0.0.1:${port}${new URL(route.request().url()).pathname}${new URL(route.request().url()).search}`,
    }),
  );
  return async () => {
    server.closeAllConnections();
    const closed = once(server, "close");
    server.close();
    await closed;
  };
}
