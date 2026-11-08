import assert from "node:assert/strict";
import fs from "node:fs/promises";
import http from "node:http";
import path from "node:path";
import AxeBuilder from "@axe-core/playwright";
import { chromium } from "@playwright/test";

const previews = path.resolve("artifacts/catalog-preview");
const assets = path.resolve("artifacts/honua-console-web/wwwroot");
const pages = new Map(await Promise.all(["list", "overview"].map(async name => [name, await fs.readFile(path.join(previews, `${name}.html`), "utf8")])));

const server = http.createServer(async (request, response) => {
  const url = new URL(request.url, "http://localhost");
  const markup = pages.get(url.pathname.slice(1));
  if (markup) {
    response.writeHead(200, { "Content-Type": "text/html; charset=utf-8" });
    response.end(`<!doctype html><html lang="en"><head><title>Catalog presentation fixture</title><meta name="viewport" content="width=device-width"><link rel="stylesheet" href="/app.css"><link rel="stylesheet" href="/_content/Honua.Console.Shell/honua-console.css"></head><body><main>${markup}</main></body></html>`);
    return;
  }
  const file = path.resolve(assets, `.${url.pathname}`);
  const contentType = { ".css": "text/css", ".woff2": "font/woff2", ".woff": "font/woff" }[path.extname(file)];
  if (!file.startsWith(`${assets}${path.sep}`) || !contentType) {
    response.writeHead(404).end();
    return;
  }
  try {
    const content = await fs.readFile(file);
    response.writeHead(200, { "Content-Type": contentType });
    response.end(content);
  } catch { response.writeHead(404).end(); }
});

await new Promise(resolve => server.listen(0, "127.0.0.1", resolve));
let browser;
try {
  browser = await chromium.launch();
  const page = await browser.newPage();
  for (const name of pages.keys()) {
    for (const width of [390, 1440]) {
      for (const theme of ["light", "dark"]) {
        await inspectPresentation(page, { name, width, theme });
      }
    }
  }
} finally {
  await browser?.close();
  await new Promise(resolve => server.close(resolve));
}

async function inspectPresentation(page, { name, width, theme }) {
  await page.setViewportSize({ width, height: 844 });
  await page.emulateMedia({ colorScheme: theme });
  const stylesheet = page.waitForResponse(response => response.url().endsWith("/css/catalog-admin.css"));
  await page.goto(`http://127.0.0.1:${server.address().port}/${name}`);
  assert.equal((await stylesheet).status(), 200);
  await page.locator("html").evaluate((element, theme) => element.setAttribute("data-theme", theme), theme);
  assert.ok(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth), `${name} overflows at ${width}`);
  const control = page.locator(".catalog-admin input").first();
  await control.focus();
  assert.ok(await control.evaluate(element => element === document.activeElement));
  assert.ok(await control.evaluate(element => element.getBoundingClientRect().height >= 44));
  const accessibility = await new AxeBuilder({ page }).include(".catalog-admin").analyze();
  assert.deepEqual(accessibility.violations, []);
  await page.screenshot({ path: path.join(previews, `${name}-${width}-${theme}.png`), fullPage: true });
  console.log(`${name}: ${width}px ${theme}, served styles, focus, overflow and axe pass`);
}
