import { AssetLinkApiError } from "./assetLinkError";

export type ImageVariant = "thumbnail" | "preview";
export interface DerivedImage {
  blob: Blob;
  width: number;
  height: number;
}
export const imageLimits = {
  thumbnail: { edge: 512, bytes: 2_097_152, pixels: 262_144 },
  preview: { edge: 1600, bytes: 12_582_912, pixels: 2_560_000 },
} as const;

export function imageFailure(status: number, code = ""): AssetLinkApiError {
  const messages: Record<string, string> = {
    source_changed: "原文件已变化，请刷新文件信息后重试。",
    preview_unsupported: "此文件暂不支持图片预览，仍可查看文件信息。",
    preview_invalid: "图片已损坏或无法解码，仍可查看文件信息。",
    preview_limit_exceeded: "图片超出预览大小或像素限制，仍可查看文件信息。",
    preview_busy: "图片预览繁忙，请稍后重试。",
    preview_unavailable: "图片预览暂不可用，资源库可能离线或服务尚未启用。",
    preview_timeout: "图片读取超时，请重试。",
  };
  const access = status === 401 ? "登录已失效。" : "图片不可访问，仍可核对文件信息。";
  return new AssetLinkApiError(
    status,
    code || "preview_unavailable",
    [401, 403, 404].includes(status) ? access : (messages[code] ?? "暂时无法读取图片，请稍后重试。"),
  );
}

export async function readImageResponse(
  response: Response,
  signal: AbortSignal,
  variant: ImageVariant,
): Promise<DerivedImage> {
  if (response.status !== 200) {
    let code = "";
    try {
      const body: unknown = JSON.parse(new TextDecoder().decode(await readBytes(response, signal, 8192)));
      if (body !== null && typeof body === "object" && "code" in body && typeof body.code === "string")
        code = body.code;
    } catch {
      signal.throwIfAborted();
      // Error bodies are untrusted; status still wins over malformed or oversized content.
    }
    throw imageFailure(response.status, code);
  }
  const limits = imageLimits[variant];
  const length = response.headers.get("content-length") ?? "";
  if (response.headers.get("content-type")?.split(";")[0]?.trim().toLowerCase() !== "image/png")
    throw imageFailure(422, "preview_invalid");
  if (!/^\d+$/.test(length) || Number(length) < 45) throw imageFailure(422, "preview_invalid");
  if (Number(length) > limits.bytes) throw imageFailure(422, "preview_limit_exceeded");
  const bytes = await readBytes(response, signal, limits.bytes, Number(length));
  const { width, height } = inspectPng(bytes, variant);
  return { blob: new Blob([bytes], { type: "image/png" }), width, height };
}

async function readBytes(response: Response, signal: AbortSignal, maximum: number, expected?: number) {
  if (!response.body) throw imageFailure(422, "preview_invalid");
  const reader = response.body.getReader();
  const bytes = new Uint8Array(expected ?? maximum);
  let size = 0;
  try {
    while (true) {
      signal.throwIfAborted();
      const next = await reader.read();
      if (next.done) break;
      size += next.value.byteLength;
      if (size > maximum) throw imageFailure(422, "preview_limit_exceeded");
      if (size > bytes.byteLength) throw imageFailure(422, "preview_invalid");
      bytes.set(next.value, size - next.value.byteLength);
    }
    signal.throwIfAborted();
    if (expected !== undefined && size !== expected) throw imageFailure(422, "preview_invalid");
    return expected === undefined ? bytes.slice(0, size) : bytes;
  } finally {
    void reader.cancel().catch(() => undefined);
    reader.releaseLock();
  }
}

function inspectPng(bytes: Uint8Array, variant: ImageVariant) {
  const invalid = () => imageFailure(422, "preview_invalid");
  if (bytes.length < 45 || [137, 80, 78, 71, 13, 10, 26, 10].some((byte, i) => bytes[i] !== byte)) throw invalid();
  const view = new DataView(bytes.buffer, bytes.byteOffset, bytes.byteLength);
  const chunkName = (at: number) => String.fromCharCode(...bytes.subarray(at + 4, at + 8));
  if (view.getUint32(8) !== 13 || chunkName(8) !== "IHDR") throw invalid();
  const width = view.getUint32(16);
  const height = view.getUint32(20);
  const limits = imageLimits[variant];
  if (width === 0 || height === 0) throw invalid();
  if (width > limits.edge || height > limits.edge || width * height > limits.pixels)
    throw imageFailure(422, "preview_limit_exceeded");
  if (bytes[24] !== 8 || ![0, 2, 3, 4, 6].includes(bytes[25]!) || bytes[26] !== 0 || bytes[27] !== 0 || bytes[28]! > 1)
    throw invalid();
  let offset = 8;
  let hasData = false;
  while (offset + 12 <= bytes.length) {
    const length = view.getUint32(offset);
    const name = chunkName(offset);
    if (length > bytes.length - offset - 12 || name === "acTL" || (name === "IHDR" && offset !== 8)) throw invalid();
    if (name === "IDAT") hasData = true;
    offset += length + 12;
    if (name === "IEND") {
      if (length !== 0 || offset !== bytes.length || !hasData) throw invalid();
      return { width, height };
    }
  }
  throw invalid();
}
