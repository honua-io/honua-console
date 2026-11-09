import assert from "node:assert/strict";
import fs from "node:fs/promises";
import path from "node:path";
import AxeBuilder from "@axe-core/playwright";
import { chromium } from "@playwright/test";
import { startPresentationFixtureServer } from "./presentation-fixture-server.mjs";
import { checkKeyboardControls } from "./console-presentation.mjs";

const previews = path.resolve(process.env.HONUA_STUDIO_ENTRY_PREVIEW_DIR ?? "artifacts/studio-preview");
const names = ["studio-unavailable", "studio-connected", "studio-retired", "processes-connected", "processes-disabled"];
const pages = new Map(await Promise.all(names.map(async name =>
  [name, await fs.readFile(path.join(previews, `${name}.html`), "utf8")])));
const server = await startPresentationFixtureServer({
  pages,
  title: "Studio entry presentation fixture",
  stylesheets: ["/_content/Honua.Console.Shell/honua-console.css"],
  assets: path.resolve("src/Honua.Console.Shell/wwwroot"),
  assetPrefix: "/_content/Honua.Console.Shell/",
});
let browser;
try {
  browser = await chromium.launch();
  const context = await browser.newContext();
  const page = await context.newPage();
  for (const name of names) {
    for (const width of [1280, 390]) {
      await page.setViewportSize({ width, height: 844 });
      const stylesheet = page.waitForResponse(response => response.url().endsWith("/v4-shell.css"));
      await page.goto(`http://127.0.0.1:${server.address().port}/${name}`);
      assert.equal((await stylesheet).status(), 200);
      await page.evaluate(() => document.fonts.ready);
      assert.equal(await page.getByRole("heading", { level: 1 }).count(), 1);
      assert.ok(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth), `${name}/${width} overflows`);
      const controls = ".console-shell :is(a[href], input, select, button, [tabindex='0']):not(:disabled):visible";
      if (await page.locator(controls).count()) {
        await checkKeyboardControls(page, { controls, sizedControls: "input, select, button", minimumHeight: 24 });
      }
      const accessibility = await new AxeBuilder({ page }).include(".console-shell").analyze();
      assert.deepEqual(accessibility.violations, []);
      await page.screenshot({ path: path.join(previews, `${name}-${width}.png`), fullPage: true });
      console.log(`${name}/${width}: shared heading, layout, keyboard focus and axe pass`);
    }
  }
} finally {
  await browser?.close();
  await new Promise(resolve => server.close(resolve));
}
