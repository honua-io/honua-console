import assert from "node:assert/strict";
import fs from "node:fs/promises";
import path from "node:path";
import AxeBuilder from "@axe-core/playwright";
import { chromium } from "@playwright/test";
import { startPresentationFixtureServer } from "./presentation-fixture-server.mjs";
import { checkKeyboardControls, inspectSharedPresentation, inspectCatalogCounts, catalogKeyboardTargets } from "./console-presentation.mjs";

import { inspectV4HomePresentation } from "./v4-home-presentation.mjs";
import { metadataPreviews, inspectMetadataPresentation } from "./metadata-presentation.mjs";

const previews = path.resolve("artifacts/catalog-preview");
const assets = path.resolve("artifacts/honua-console-web/wwwroot");
const v4Previews = ["home-v4-true", "home-v4-false"];
const sharedPreviews = ["navigation", "home", "service", "state-info", "state-warning", "state-danger", "state-unsupported", "service-success", "service-danger", "share-warning"];
const pages = new Map(await Promise.all(["list", "overview", "settings", "templates", "template-editor", "template-dirty", "template-confirmation", "listing", "listing-dirty", "categories", "categories-dirty", ...metadataPreviews, ...sharedPreviews, ...v4Previews].map(async name => [name, await fs.readFile(path.join(previews, `${name}.html`), "utf8")])));

const server = await startPresentationFixtureServer({
  pages,
  title: "Catalog presentation fixture",
  stylesheets: ["/app.css", "/_content/Honua.Console.Shell/honua-console.css"],
  assets,
  unwrappedPages: ["navigation", ...v4Previews],
});
let browser;
try {
  browser = await chromium.launch();
  const context = await browser.newContext();
  const page = await context.newPage();
  const samples = [...pages.keys()].flatMap(name => (v4Previews.includes(name) ? [390, 1280] : name.startsWith("service") ? [390, 600, 1440] : [390, 1440]).map(width => ({ name, width })));
  const failures = [];
  for (const sample of samples) failures.push(await capturePresentationCase(page, sample));
  assert.deepEqual(failures.filter(Boolean), []);
} finally {
  await browser?.close();
  await new Promise(resolve => server.close(resolve));
}
console.log("Dark presentation is unsupported by the current Console shell; no dark evidence was produced.");

async function capturePresentationCase(page, sample) {
  try {
    await inspectPresentation(page, sample);
    return null;
  } catch (error) {
    await page.screenshot({ path: path.join(previews, `${sample.name}-${sample.width}-failed.png`), fullPage: true });
    return `${sample.name}/${sample.width}: ${error.message}`;
  }
}

async function inspectPresentation(page, { name, width }) {
  await page.setViewportSize({ width, height: 844 });
  await page.emulateMedia({ colorScheme: "light", forcedColors: "none" });
  const stylesheet = page.waitForResponse(response => response.url().endsWith("/css/catalog-admin.css"));
  await page.goto(`http://127.0.0.1:${server.address().port}/${name}`);
  assert.equal((await stylesheet).status(), 200);
  assert.equal(await page.evaluate(() => getComputedStyle(document.documentElement).colorScheme), "light");
  assert.ok(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth), `${name} overflows at ${width}`);
  if (v4Previews.includes(name)) return inspectV4HomePresentation(page, { name, width }, previews);
  if (metadataPreviews.includes(name)) return inspectMetadataPresentation(page, { name, width }, previews);
  if (sharedPreviews.includes(name)) return inspectSharedPresentation(page, { name, width }, previews);
  await checkKeyboardControls(page, catalogKeyboardTargets);
  await inspectCatalogCounts(page, name);
  if (name === "settings") {
    const snippet = page.getByLabel("Embed snippet", { exact: true });
    await snippet.focus();
    const expected = await snippet.inputValue();
    await page.keyboard.press("Control+a");
    assert.equal(await snippet.evaluate(element => element.value.slice(element.selectionStart, element.selectionEnd)), expected);
    assert.ok(expected.includes('catalog="open"'));
    assert.equal(await page.locator(".catalog-settings script").count(), 0);
  }
  if (name.startsWith("template-")) {
    assert.equal(await page.locator(".catalog-template-editor :is(script, iframe, img, [srcdoc], [onerror])").count(), 0);
    if (name === "template-editor") assert.ok((await page.getByLabel("Template source", { exact: true }).inputValue()).includes("</textarea><script>"));
    if (name !== "template-editor") assert.ok(await page.getByRole("button", { name: "Discard local edits", exact: true }).isEnabled());
    if (name === "template-confirmation") {
      assert.ok(await page.getByRole("button", { name: "Keep editing", exact: true }).isVisible());
      assert.ok(await page.getByRole("button", { name: "Discard edits", exact: true }).isVisible());
    }
    assert.ok(await page.getByRole("heading", { name: "Preview", exact: true }).isVisible());
    assert.equal(await page.locator("button[data-template-insert]").count(), 6);
  }
  if (name === "listing") {
    assert.equal(await page.locator(".catalog-can-appear").textContent(), "Can appear in catalogs: Yes");
    assert.ok(await page.getByLabel("Title", { exact: true }).isVisible());
    assert.ok(await page.getByRole("button", { name: "Discard local edits", exact: true }).isDisabled());
    assert.ok(await page.getByRole("heading", { name: "Public card preview", exact: true }).isVisible());
  }
  if (name === "listing-dirty") {
    assert.ok(await page.getByRole("button", { name: "Discard local edits", exact: true }).isEnabled());
    assert.ok((await page.getByLabel("Description source (Markdown)", { exact: true }).inputValue()).includes("</textarea><script>"));
    assert.equal(await page.locator(".catalog-listing script, .catalog-listing img, .catalog-listing iframe").count(), 0);
  }
  if (name.startsWith("categories")) {
    assert.equal(await page.locator("[data-home-position]").count(), 4);
    assert.ok(await page.getByRole("button", { name: /Move Safety at position/ }).first().isVisible());
    assert.equal(await page.getByRole("button", { name: "Discard local edits", exact: true }).isEnabled(), name === "categories-dirty");
  }
  const accessibility = await new AxeBuilder({ page }).include(".catalog-admin").analyze();
  assert.deepEqual(accessibility.violations, []);
  await page.locator(".catalog-admin-table").evaluateAll(regions => regions.forEach(region => { region.scrollLeft = 0; }));
  await page.screenshot({ path: path.join(previews, `${name}-${width}-light.png`), fullPage: true });
  console.log(`${name}: ${width}px light, served styles, visible keyboard focus, control sizes, overflow and axe pass`);
}
