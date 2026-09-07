export const visibleLibrary = {
  library_id: "11111111-1111-4111-8111-111111111111",
  display_name: "设计素材",
  availability: "online",
  access_level: "read_only",
};

export const hiddenLibraryName = "财务私库";

export function entry(index, overrides = {}) {
  const suffix = String(index).padStart(4, "0");
  return {
    entry_id: `22222222-2222-4222-8222-${suffix.padStart(12, "0")}`,
    library_id: visibleLibrary.library_id,
    relative_path: `asset-${suffix}.png`,
    name: `asset-${suffix}.png`,
    kind: "file",
    content_length: String(index + 100),
    last_write_time_utc: "2026-09-04T00:00:00.0000000Z",
    ...overrides,
  };
}

export async function mockAssetLink(page, handler) {
  await page.route("**/assetlink/v1/control", async (route) => {
    const request = route.request().postDataJSON();
    const response = await handler(request);
    await route.fulfill({
      status: response.status ?? 200,
      contentType: "application/json",
      body: JSON.stringify(response.body),
    });
  });
}

export function result(request, body) {
  return {
    body: {
      message_type: "control.result",
      request_id: request.request_id,
      ok: true,
      body,
    },
  };
}

export function failure(request, status, code, message) {
  return {
    status,
    body: {
      message_type: "error",
      request_id: request.request_id,
      error: { code, message },
    },
  };
}

export function libraryPage(request, libraries = [visibleLibrary], nextCursor = null) {
  return result(request, { items: libraries, next_cursor: nextCursor });
}

export function browsePage(request, items = [], nextCursor = null, library = visibleLibrary) {
  return result(request, {
    library,
    parent_relative_path: request.body.parent_relative_path,
    items,
    next_cursor: nextCursor,
  });
}

export function searchPage(request, hits = [], nextCursor = null) {
  return result(request, { items: hits, next_cursor: nextCursor });
}
