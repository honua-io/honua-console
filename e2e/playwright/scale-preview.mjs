import assert from "node:assert/strict";
import fs from "node:fs/promises";
import path from "node:path";
import AxeBuilder from "@axe-core/playwright";
import { chromium } from "@playwright/test";
import { startPresentationFixtureServer } from "./presentation-fixture-server.mjs";

const previews = path.resolve(process.env.HONUA_CATALOG_PREVIEW_DIR ?? "artifacts/catalog-preview");
const states = ["initial", "paged", "expanded", "search", "empty"];
const names = ["v4", "legacy"].flatMap(mode => states.map(state => `scale-${mode}-${state}`));
const pages = new Map(await Promise.all(names.map(async name => [name, await fs.readFile(path.join(previews, `${name}.html`), "utf8")])));
const scripts = ["/src/Honua.Console.Web/wwwroot/js/modal-surface.js", "/src/Honua.Console.Web/wwwroot/js/publication-tree.js"];
const server = await startPresentationFixtureServer({
  pages, title: "Service navigation at scale",
  stylesheets: ["/src/Honua.Console.Shell/wwwroot/honua-console.css"],
  assets: path.resolve("."), unwrappedPages: names,
  scripts: new Map(names.map(name => [name, scripts])),
});
let browser;
try {
  browser = await chromium.launch();
  for (const name of names) {
    for (const width of [1280, 390]) {
      const page = await browser.newPage({ viewport: { width, height: 844 } });
      const errors = [];
      page.on("pageerror", error => errors.push(error.message));
      const stylesheet = page.waitForResponse(response => response.url().endsWith("/css/publication-tree.css"));
      await page.goto(`http://127.0.0.1:${server.address().port}/${name}`);
      assert.equal((await stylesheet).status(), 200);
      await page.evaluate(() => document.fonts.ready);
      const sidebar = page.locator(".console-publication-sidebar");
      const toggle = page.locator("[data-tree-toggle]");
      if (width === 390) {
        assert.equal(await sidebar.isVisible(), false);
        if (name.endsWith("initial")) await page.screenshot({ path: path.join(previews, `${name}-${width}-closed.png`), fullPage: true });
        await toggle.click();
        await page.waitForFunction(() => document.querySelector(".console-publication-sidebar").hasAttribute("data-tree-open"));
        assert.equal(await sidebar.getAttribute("aria-modal"), "true");
        assert.equal(await page.locator("[data-tree-filter]").evaluate(element => element === document.activeElement), true);
      }
      assert.equal(await sidebar.isVisible(), true);
      assert.equal(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth), true);
      const row = page.locator(".console-tree-row").first();
      const hasRows = await row.count() > 0;
      const dimensions = hasRows ? await row.evaluate(element => ({ height: element.getBoundingClientRect().height, size: getComputedStyle(element).fontSize })) : null;
      if (hasRows) assert.ok(dimensions.height >= (width === 390 ? 44 : 28));
      if (name.startsWith("scale-v4") && width === 1280) {
        assert.equal((await sidebar.boundingBox()).width, 248);
        assert.ok(dimensions.height < 36);
        assert.equal(dimensions.size, "13px");
        assert.equal((await page.locator(".console-topbar").boundingBox()).height, 48);
        const footer = await page.locator(".console-tree-coverage").boundingBox();
        assert.ok(footer.y + footer.height <= 845, "Coverage remains visible while the tree scrolls");
        if (name.endsWith("paged") || name.endsWith("expanded")) {
          assert.equal(await page.locator(".console-tree-branches").evaluate(element => element.scrollHeight > element.clientHeight), true);
        }
      }
      if (name === "scale-v4-search" || name === "scale-v4-empty") {
        assert.deepEqual(await page.locator("[data-publication-search] h3").allTextContents(),
          ["Folders (0)", `Services (${name.endsWith("search") ? 1 : 0})`, "Layers (0)", "Styles (0)"]);
        assert.equal(await page.locator(".console-search-scope").textContent(), "Scope: Services · service names");
        if (name.endsWith("search")) {
          assert.deepEqual(await page.locator("mark").allTextContents(), ["service-499", "service-499"]);
          await page.locator("[data-tree-link]").evaluate(element => element.addEventListener("click", event => {
            event.preventDefault();
            element.dataset.activated = "true";
          }));
          await page.getByRole("treeitem").focus();
          await page.keyboard.press("Enter");
          assert.equal(await page.locator("[data-tree-link]").getAttribute("data-activated"), "true");
        }
      }
      const first = page.getByRole("treeitem").first();
      if (hasRows) await first.focus();
      if (await page.getByRole("treeitem").count() > 1) {
        await page.keyboard.press("ArrowDown");
        assert.equal(await page.getByRole("treeitem").nth(1).evaluate(element => element === document.activeElement), true);
        await page.keyboard.press("ArrowUp");
        assert.equal(await first.evaluate(element => element === document.activeElement), true);
      }
      const accessibility = await new AxeBuilder({ page }).analyze();
      assert.deepEqual(accessibility.violations.map(violation => violation.id), []);
      assert.deepEqual(errors, []);
      await page.screenshot({ path: path.join(previews, `${name}-${width}.png`), fullPage: true });
      if (width === 390) {
        if (name === "scale-v4-search" || name === "scale-v4-empty") {
          await page.locator("[data-tree-filter]").focus();
          await page.keyboard.press("Escape");
          assert.equal(await page.locator("[data-tree-filter]").inputValue(), "");
          assert.equal(await sidebar.isVisible(), true);
        }
        await page.keyboard.press("Escape");
        assert.equal(await sidebar.isVisible(), false);
        assert.equal(await toggle.evaluate(element => element === document.activeElement), true);
      }
      await page.close();
      console.log(`${name} ${width}: PASS`);
    }
  }
} finally {
  await browser?.close();
  await new Promise(resolve => server.close(resolve));
}
