import { once } from "node:events";
import { createServer } from "node:http";

export async function startAssetLinkServer(page, handle) {
  const server = createServer(async (request, response) => {
    const chunks = [];
    for await (const chunk of request) chunks.push(chunk);
    response.setHeader("content-type", "application/json");
    response.setHeader("access-control-allow-origin", "http://127.0.0.1:4173");
    handle(JSON.parse(Buffer.concat(chunks).toString("utf8")), response);
  });
  server.listen(0, "127.0.0.1");
  await once(server, "listening");
  const address = server.address();
  if (address === null || typeof address === "string") throw new Error("The fixture has no TCP address.");
  await page.route("**/assetlink/v1/control", (route) =>
    route.continue({ url: `http://127.0.0.1:${address.port}/assetlink/v1/control` }),
  );

  return async () => {
    server.closeAllConnections();
    const closed = once(server, "close");
    server.close();
    await closed;
  };
}

export function sendResult(response, result) {
  response.statusCode = result.status ?? 200;
  response.end(JSON.stringify(result.body));
}
