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
    for (const width of (name === "scale-v4-paged" ? [1280, 1050, 390] : [1280, 390])) {
      const context = await browser.newContext({ viewport: { width, height: 844 } });
      const page = await context.newPage();
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
      if (name.startsWith("scale-v4") && width > 1000) {
        assert.equal((await sidebar.boundingBox()).width, 272);
        if (hasRows) {
          assert.ok(dimensions.height < (name.endsWith("search") ? 60 : 36));
          assert.equal(dimensions.size, "14px");
        }
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
      if (name === "scale-v4-paged") {
        await page.keyboard.press("End");
        const last = page.getByRole("treeitem").last();
        assert.equal(await last.evaluate(element => element === document.activeElement), true);
        const visibility = await last.evaluate(element => {
          const pane = element.closest(".console-tree-branches");
          const row = element.getBoundingClientRect(), bounds = pane.getBoundingClientRect();
          return pane.scrollTop > 0 && row.top >= bounds.top && row.bottom <= bounds.bottom + 1;
        });
        assert.equal(visibility, true, "End keeps the focused row inside the scrolling branches");
        await page.keyboard.press("Home");
        if (width > 1000) {
          await page.locator("main").evaluate(element => element.style.minHeight = "1800px");
          await page.evaluate(() => window.scrollTo(0, 400));
          const footer = await page.locator(".console-tree-coverage").boundingBox();
          assert.ok(Math.abs(footer.y + footer.height - 844) <= 1, "Coverage stays at the viewport bottom after page scroll");
          assert.equal((await sidebar.boundingBox()).y, 48);
          await page.evaluate(() => window.scrollTo(0, 0));
        }
      }
      const accessibility = await new AxeBuilder({ page }).analyze();
      assert.deepEqual(accessibility.violations.map(violation => violation.id), []);
      assert.deepEqual(errors, []);
      await page.screenshot({ path: path.join(previews, `${name}-${width}.png`), fullPage: true });
      if (width === 390) {
        if (name.endsWith("search") || name.endsWith("empty")) {
          await page.locator("[data-tree-filter]").focus();
          await page.keyboard.press("Escape");
          assert.equal(await page.locator("[data-tree-filter]").inputValue(), "");
          assert.equal(await sidebar.isVisible(), true);
        }
        await page.keyboard.press("Escape");
        assert.equal(await sidebar.isVisible(), false);
        assert.equal(await toggle.evaluate(element => element === document.activeElement), true);
      }
      if (name === "scale-v4-paged" && width > 1000) {
        await sidebar.evaluate(element => element.remove());
        await page.locator("main").evaluate(element => {
          element.style.minHeight = "1800px";
          element.innerHTML = '<nav class="operate-section-nav" aria-label="Sections"><a href="#overview">Overview</a></nav><h1 id="overview">Deploy</h1>';
        });
        await page.evaluate(() => window.scrollTo(0, 400));
        assert.equal((await page.locator(".operate-section-nav").boundingBox()).y, 48,
          "Operate section navigation stays below the sticky topbar without a publication tree");
        for (const [selector, offset] of [["share-opendata-preview", 60], ["operate-release-side", 60], ["support-context-panel", 64], ["layer-preview-pane", 64]]) {
          assert.equal(await page.locator("main").evaluate((element, name) => {
            const panel = document.createElement("aside");
            panel.className = name;
            element.append(panel);
            const top = parseFloat(getComputedStyle(panel).top);
            panel.remove();
            return top;
          }, selector), offset);
        }
      }
      await context.close();
      console.log(`${name} ${width}: PASS`);
    }
  }
} finally {
  await browser?.close();
  await new Promise(resolve => server.close(resolve));
}
