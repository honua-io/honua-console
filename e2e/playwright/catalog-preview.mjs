import assert from "node:assert/strict";
import fs from "node:fs/promises";
import http from "node:http";
import path from "node:path";
import AxeBuilder from "@axe-core/playwright";
import { chromium } from "@playwright/test";

const previews = path.resolve("artifacts/catalog-preview");
const assets = path.resolve("artifacts/honua-console-web/wwwroot");
const pages = new Map(await Promise.all(["list", "overview", "settings"].map(async name => [name, await fs.readFile(path.join(previews, `${name}.html`), "utf8")])));

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
  const context = await browser.newContext();
  const page = await context.newPage();
  for (const name of pages.keys()) {
    for (const width of [390, 1440]) {
      await inspectPresentation(page, { name, width });
    }
  }
} finally {
  await browser?.close();
  await new Promise(resolve => server.close(resolve));
}
console.log("Dark presentation is unsupported by the current Console shell; no dark evidence was produced.");

async function inspectPresentation(page, { name, width }) {
  await page.setViewportSize({ width, height: 844 });
  await page.emulateMedia({ colorScheme: "light" });
  const stylesheet = page.waitForResponse(response => response.url().endsWith("/css/catalog-admin.css"));
  await page.goto(`http://127.0.0.1:${server.address().port}/${name}`);
  assert.equal((await stylesheet).status(), 200);
  assert.equal(await page.evaluate(() => getComputedStyle(document.documentElement).colorScheme), "light");
  assert.ok(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth), `${name} overflows at ${width}`);
  await checkKeyboardControls(page);
  const accessibility = await new AxeBuilder({ page }).include(".catalog-admin").analyze();
  assert.deepEqual(accessibility.violations, []);
  await page.screenshot({ path: path.join(previews, `${name}-${width}-light.png`), fullPage: true });
  console.log(`${name}: ${width}px light, served styles, visible keyboard focus, control sizes, overflow and axe pass`);
}

async function checkKeyboardControls(page) {
  const controls = page.locator(".catalog-admin :is(input, select, button, textarea):not(:disabled), .catalog-admin-tabs a");
  const total = await controls.count();
  assert.ok(total > 0);
  await controls.evaluateAll(elements => elements.forEach((element, index) => { element.dataset.previewControlIndex = String(index); }));
  const visited = new Set();
  for (let attempts = 0; attempts < total * 3 + 5 && visited.size < total; attempts++) {
    await page.keyboard.press("Tab");
    const focused = await page.evaluate(() => {
      const element = document.activeElement;
      if (!(element instanceof HTMLElement) || element.dataset.previewControlIndex === undefined) return null;
      const style = getComputedStyle(element);
      return { index: element.dataset.previewControlIndex, height: element.getBoundingClientRect().height,
        focusVisible: element.matches(":focus-visible"),
        ring: (style.outlineStyle !== "none" && parseFloat(style.outlineWidth) > 0 && style.outlineColor !== "rgba(0, 0, 0, 0)") || style.boxShadow !== "none" };
    });
    if (!focused) continue;
    assert.ok(focused.height >= 44, `Control ${focused.index} is undersized`);
    assert.ok(focused.focusVisible && focused.ring, `Control ${focused.index} has no visible keyboard focus`);
    visited.add(focused.index);
  }
  assert.equal(visited.size, total, "Every enabled form and pager control must be keyboard reachable");
}
