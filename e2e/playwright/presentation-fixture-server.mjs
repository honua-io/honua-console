import fs from "node:fs/promises";
import http from "node:http";
import path from "node:path";

export async function startPresentationFixtureServer({ pages, title, stylesheets, assets, assetPrefix = "/", unwrappedPages = [], scripts = new Map() }) {
  const root = path.resolve(assets);
  const server = http.createServer(async (request, response) => {
    const url = new URL(request.url, "http://localhost");
    const name = url.pathname.slice(1);
    const markup = pages.get(name);
    if (markup !== undefined) {
      const body = unwrappedPages.includes(name) ? markup : `<main class="console-main">${markup}</main>`;
      const pageScripts = scripts.get(name) ?? [];
      response.writeHead(200, {
        "Content-Type": "text/html; charset=utf-8",
        "Content-Security-Policy": `default-src 'none'; style-src 'self'; font-src 'self'; img-src 'self'; script-src ${pageScripts.length ? "'self'" : "'none'"}; frame-src 'none'; base-uri 'none'`,
      });
      response.end(`<!doctype html><html lang="en"><head><title>${title}</title><meta name="viewport" content="width=device-width">${stylesheets.map(href => `<link rel="stylesheet" href="${href}">`).join("")}</head><body>${body}${pageScripts.map(src => `<script src="${src}" defer></script>`).join("")}</body></html>`);
      return;
    }
    const file = path.resolve(root, url.pathname.slice(assetPrefix.length));
    const contentType = { ".css": "text/css", ".woff2": "font/woff2", ".woff": "font/woff", ".js": "text/javascript" }[path.extname(file)];
    if (!url.pathname.startsWith(assetPrefix) || !file.startsWith(`${root}${path.sep}`) || !contentType) {
      response.writeHead(404).end();
      return;
    }
    try {
      const content = await fs.readFile(file);
      response.writeHead(200, { "Content-Type": contentType });
      response.end(content);
    } catch { response.writeHead(404).end(); }
  });
  await new Promise((resolve, reject) => {
    server.once("error", reject);
    server.listen(0, "127.0.0.1", resolve);
  });
  return server;
}
