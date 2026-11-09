import assert from "node:assert/strict";
import fs from "node:fs/promises";
import path from "node:path";
import AxeBuilder from "@axe-core/playwright";
import { chromium } from "@playwright/test";
import { startPresentationFixtureServer } from "./presentation-fixture-server.mjs";
import { checkKeyboardControls } from "./console-presentation.mjs";

const previews = path.resolve("artifacts/activity-preview");
const shellAssets = path.resolve("src/Honua.Console.Shell/wwwroot");
const pages = new Map(await Promise.all(["activity-selected", "activity-empty"].map(async name =>
  [name, await fs.readFile(path.join(previews, `${name}.html`), "utf8")])));

const server = await startPresentationFixtureServer({
  pages,
  title: "Activity presentation fixture",
  stylesheets: ["/_content/Honua.Console.Shell/honua-console.css", "/_content/Honua.Console.Shell/css/operate-activity-v4.css"],
  assets: shellAssets,
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
      const stylesheet = page.waitForResponse(response => response.url().endsWith("/operate-activity-v4.css"));
      await page.goto(`http://127.0.0.1:${server.address().port}/${name}`);
      assert.equal((await stylesheet).status(), 200);
      await page.evaluate(() => document.fonts.ready);
      assert.equal(await page.getByRole("heading", { level: 1 }).textContent(), "Activity");
      assert.ok(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth), `${name}/${width} overflows`);
      assert.equal(await page.locator("[data-timeline-row]").count(), name === "activity-selected" ? 2 : 0);
      await checkKeyboardControls(page, {
        controls: "#events :is(a[href], input, select, button, [tabindex='0']):not(:disabled):visible",
        sizedControls: "input, select, button",
        minimumHeight: 24,
      });
      const accessibility = await new AxeBuilder({ page }).include(".console-activity-v4").analyze();
      assert.deepEqual(accessibility.violations, []);
      await page.evaluate(() => {
        document.querySelectorAll("[role='region']").forEach(region => { region.scrollLeft = 0; });
        window.scrollTo(0, 0);
      });
      await page.screenshot({ path: path.join(previews, `${name}-${width}.png`), fullPage: true });
      console.log(`${name}/${width}: layout, event evidence, keyboard focus and axe pass`);
    }
  }
} finally {
  await browser?.close();
  await new Promise(resolve => server.close(resolve));
}
