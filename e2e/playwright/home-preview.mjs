import assert from "node:assert/strict";
import fs from "node:fs/promises";
import path from "node:path";
import AxeBuilder from "@axe-core/playwright";
import { chromium } from "@playwright/test";
import { startPresentationFixtureServer } from "./presentation-fixture-server.mjs";
import { checkKeyboardControls } from "./console-presentation.mjs";

const previews = path.resolve(process.env.HONUA_CATALOG_PREVIEW_DIR ?? "artifacts/catalog-preview");
const pages = new Map(await Promise.all(["home-inventory-v4", "home-inventory-legacy"].map(async name => {
  const markup = await fs.readFile(path.join(previews, `${name}.html`), "utf8");
  return [name, `<div class="console-shell" data-console-v4="${name.endsWith("v4")}">${markup}</div>`];
})));
const server = await startPresentationFixtureServer({
  pages, title: "Home presentation fixture",
  stylesheets: ["/_content/Honua.Console.Shell/honua-console.css"],
  assets: path.resolve("src/Honua.Console.Shell/wwwroot"),
  assetPrefix: "/_content/Honua.Console.Shell/",
});
let browser;
try {
  browser = await chromium.launch();
  const context = await browser.newContext();
  const page = await context.newPage();
  for (const name of pages.keys()) {
    for (const width of [1280, 390]) {
      await page.setViewportSize({ width, height: 844 });
      await page.goto(`http://127.0.0.1:${server.address().port}/${name}`);
      await page.evaluate(() => document.fonts.ready);
      assert.equal(await page.getByRole("heading", { level: 1 }).textContent(), "Welcome");
      assert.ok(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth), `${name}/${width} overflows`);
      const inventory = page.locator(".console-home-inventory");
      if (name.endsWith("v4")) {
        assert.equal((await inventory.locator(".console-metric-value").textContent()).trim(), "500");
        assert.equal((await inventory.locator("[data-home-layer-count]").textContent()).trim(), "5,000");
        assert.equal((await inventory.locator("[data-home-folder-count]").textContent()).trim(), "1");
        const leadSize = await inventory.locator(".console-metric-value").evaluate(element => parseFloat(getComputedStyle(element).fontSize));
        const secondary = await inventory.locator(".console-home-inventory-details dd").evaluateAll(elements => elements.map(element => parseFloat(getComputedStyle(element).fontSize)));
        assert.ok(secondary.every(size => size < leadSize), "Services must be the single large inventory count");
      } else assert.equal(await inventory.count(), 0);
      await checkKeyboardControls(page, {
        controls: "main :is(a[href], input, select, button, summary, [tabindex='0']):not(:disabled):visible",
        sizedControls: "input, select, button, summary", minimumHeight: 24,
      });
      const accessibility = await new AxeBuilder({ page }).include("main").analyze();
      assert.deepEqual(accessibility.violations, []);
      await page.evaluate(() => window.scrollTo(0, 0));
      await page.screenshot({ path: path.join(previews, `${name}-${width}.png`), fullPage: true });
      console.log(`${name}/${width}: server counts, hierarchy, keyboard, overflow and axe pass`);
    }
  }
} finally {
  await browser?.close();
  await new Promise(resolve => server.close(resolve));
}
