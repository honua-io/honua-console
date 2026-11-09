import assert from "node:assert/strict";
import fs from "node:fs/promises";
import path from "node:path";
import { chromium } from "@playwright/test";
import AxeBuilder from "@axe-core/playwright";
import { startPresentationFixtureServer } from "./presentation-fixture-server.mjs";
import { checkKeyboardControls } from "./console-presentation.mjs";

const previews = path.resolve(process.env.HONUA_CATALOG_PREVIEW_DIR ?? "artifacts/catalog-preview");
const pages = new Map(await Promise.all(["settings-v4", "settings-legacy"].map(async name =>
  [name, await fs.readFile(path.join(previews, `${name}.html`), "utf8")])));
const server = await startPresentationFixtureServer({
  pages, title: "Settings presentation fixture",
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
      const stylesheet = page.waitForResponse(response => response.url().endsWith("/css/v4-shell.css"));
      await page.goto(`http://127.0.0.1:${server.address().port}/${name}`);
      assert.equal((await stylesheet).status(), 200);
      await page.evaluate(() => document.fonts.ready);
      assert.equal(await page.getByRole("heading", { level: 1 }).textContent(), "Settings");
      assert.equal(await page.getByRole("columnheader").count(), 5);
      assert.equal(await page.locator("tbody td").last().textContent(), "/api/v1/admin/version");
      assert.equal(await page.locator("input, select, textarea, button").count(), 0);
      if (name === "settings-v4") {
        assert.equal(await page.locator("h1").evaluate(element => getComputedStyle(element).fontSize), width === 390 ? "22px" : "28px");
        assert.equal(await page.getByRole("heading", { name: "Server configuration" }).count(), 1);
      } else {
        assert.equal(await page.locator(".console-page-heading-v4").count(), 0);
      }
      assert.ok(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth), `${name}/${width} overflows`);
      await checkKeyboardControls(page, { controls: "main [tabindex='0']", sizedControls: "input, select, button", minimumHeight: 24 });
      assert.deepEqual((await new AxeBuilder({ page }).include("main").analyze()).violations, []);
      await page.screenshot({ path: path.join(previews, `${name}-${width}.png`), fullPage: true });
      console.log(`${name}/${width}: read-only values, layout, keyboard and axe pass`);
    }
  }
} finally {
  await browser?.close();
  await new Promise(resolve => server.close(resolve));
}
