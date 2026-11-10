import { createServer } from "node:http";
import { readFile, realpath } from "node:fs/promises";
import { extname, resolve, sep } from "node:path";
import { fileURLToPath } from "node:url";

const contentTypes = {
  ".html": "text/html; charset=utf-8", ".js": "text/javascript", ".mjs": "text/javascript",
  ".json": "application/json", ".css": "text/css", ".wasm": "application/wasm",
  ".woff2": "font/woff2", ".png": "image/png", ".svg": "image/svg+xml", ".ico": "image/x-icon",
};

export async function startStaticBundleServer(directory, port = 0, { configuration, handleApi } = {}) {
  const root = await realpath(directory);
  const server = createServer(async (request, response) => {
    response.setHeader("Cache-Control", "no-store");
    const url = new URL(request.url, "http://localhost");
    if (handleApi && url.pathname.startsWith("/api/")) {
      await handleApi(request, response);
      return;
    }
    if (!["GET", "HEAD"].includes(request.method)) {
      response.writeHead(405).end();
      return;
    }
    if (configuration && url.pathname === "/console/config.json") {
      response.writeHead(200, { "Content-Type": "application/json" });
      response.end(request.method === "HEAD" ? undefined : JSON.stringify(configuration));
      return;
    }
    try {
      const pathname = decodeURIComponent(new URL(request.url, "http://localhost").pathname);
      if (!pathname.startsWith("/console/")) {
        response.writeHead(404).end();
        return;
      }
      const relative = pathname.slice("/console/".length);
      const target = resolve(root, relative || "index.html");
      if (!target.startsWith(`${root}${sep}`)) {
        response.writeHead(404).end();
        return;
      }
      let file;
      try {
        file = await realpath(target);
      } catch {
        if (extname(relative) || !request.headers.accept?.includes("text/html")) {
          response.writeHead(404).end();
          return;
        }
        file = await realpath(resolve(root, "index.html"));
      }
      if (!file.startsWith(`${root}${sep}`)) {
        response.writeHead(404).end();
        return;
      }
      const body = await readFile(file);
      response.writeHead(200, {
        "Content-Type": contentTypes[extname(file)] ?? "application/octet-stream",
        "Content-Length": body.length,
      });
      response.end(request.method === "HEAD" ? undefined : body);
    } catch {
      response.writeHead(404).end();
    }
  });
  await new Promise((accept, reject) => {
    server.once("error", reject);
    server.listen(port, "127.0.0.1", accept);
  });
  return server;
}

if (process.argv[1] && resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  await startStaticBundleServer(process.env.HONUA_CONSOLE_STATIC_ROOT ?? "artifacts/static-publish/wwwroot/console",
    Number(process.env.HONUA_CONSOLE_STATIC_PORT ?? "5184"));
}
