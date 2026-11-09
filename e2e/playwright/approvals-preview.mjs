import assert from "node:assert/strict";
import fs from "node:fs/promises";
import path from "node:path";
import AxeBuilder from "@axe-core/playwright";
import { chromium } from "@playwright/test";
import { startPresentationFixtureServer } from "./presentation-fixture-server.mjs";
import { checkKeyboardControls } from "./console-presentation.mjs";

const previews = path.resolve("artifacts/approvals-preview");
const shellAssets = path.resolve("src/Honua.Console.Shell/wwwroot");
const pages = new Map(await Promise.all(["approvals-selected", "approvals-forbidden"].map(async name =>
  [name, await fs.readFile(path.join(previews, `${name}.html`), "utf8")])));

const server = await startPresentationFixtureServer({
  pages,
  title: "Approvals presentation fixture",
  stylesheets: ["/_content/Honua.Console.Shell/honua-console.css"],
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
      const stylesheet = page.waitForResponse(response => response.url().endsWith("/approval-inbox-v4.css"));
      await page.goto(`http://127.0.0.1:${server.address().port}/${name}`);
      assert.equal((await stylesheet).status(), 200);
      await page.evaluate(() => document.fonts.ready);
      assert.equal(await page.getByRole("heading", { level: 1 }).textContent(), "Approvals");
      assert.ok(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth), `${name}/${width} overflows`);
      assert.equal(await page.locator("tbody [data-proposal-id]").count(), 1);
      assert.ok(await page.locator("[data-live-state]").isVisible());
      if (name === "approvals-forbidden") {
        assert.ok((await page.locator("section.approval-inbox-page").textContent()).includes("requires the 'approve' permission"));
      } else {
        assert.ok(await page.locator("[data-proposal-approve]").isVisible());
      }
      await checkKeyboardControls(page, {
        controls: ".approval-inbox-page :is(a[href], input, select, button, [tabindex='0']):not(:disabled):visible",
        sizedControls: "input, select, button",
        minimumHeight: 24,
      });
      const accessibility = await new AxeBuilder({ page }).include(".approval-inbox-v4").analyze();
      assert.deepEqual(accessibility.violations, []);
      await page.evaluate(() => window.scrollTo(0, 0));
      await page.screenshot({ path: path.join(previews, `${name}-${width}.png`), fullPage: true });
      console.log(`${name}/${width}: layout, proposal evidence, keyboard focus and axe pass`);
    }
  }
} finally {
  await browser?.close();
  await new Promise(resolve => server.close(resolve));
}
