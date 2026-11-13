import assert from "node:assert/strict";
import fs from "node:fs/promises";
import path from "node:path";
import { fileURLToPath } from "node:url";
import AxeBuilder from "@axe-core/playwright";
import { chromium } from "@playwright/test";
import { startPresentationFixtureServer } from "./presentation-fixture-server.mjs";

// Actual bUnit renders: behavior and authorization are asserted in CatalogBrowseV4Tests.
// This browser pass checks the shared v4 presentation at the two release widths.
const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "../..");
const previews = process.env.HONUA_CATALOG_PREVIEW_DIR || path.join(root, "artifacts/catalog-browse/previews");
const names = ["catalog-browse-v4", "catalog-item-overview-v4", "catalog-item-permissions-v4", "catalog-discovery-v4"];
const pages = new Map(await Promise.all(names.map(async name => [name, await fs.readFile(path.join(previews, `${name}.html`), "utf8")])));
const assets = path.join(root, "artifacts/catalog-browse/assets");
await fs.mkdir(path.join(assets, "_content"), { recursive: true });
await fs.cp(path.join(root, "src/Honua.Console.Shell/wwwroot"), path.join(assets, "_content/Honua.Console.Shell"), { recursive: true });
await fs.copyFile(path.join(root, "src/Honua.Console.Web/wwwroot/app.css"), path.join(assets, "app.css"));
const server = await startPresentationFixtureServer({ pages, assets, title: "Catalog v4 presentation", stylesheets: ["/app.css", "/_content/Honua.Console.Shell/honua-console.css"] });
let browser;
try {
  browser = await chromium.launch();
  const context = await browser.newContext();
  const page = await context.newPage();
  for (const name of names) {
    for (const width of [1280, 390]) {
      await page.setViewportSize({ width, height: 844 });
      await page.emulateMedia({ colorScheme: "light" });
      await page.goto(`http://127.0.0.1:${server.address().port}/${name}`);
      await page.locator(".console-page-heading-v4").waitFor();
      assert.equal(await page.locator("h1").count(), 1);
      assert.ok(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth), `${name} overflows at ${width}`);
      assert.equal(await page.locator(".console-panel:not(.console-panel-quiet)").count(), 0);
      if (name === "catalog-browse-v4") {
        const table = page.locator(".console-content-table");
        assert.equal(await table.evaluate(element => getComputedStyle(element).minWidth), "864px");
        const region = page.getByRole("region", { name: "Catalog content items", exact: true });
        if (width === 390) {
          assert.ok(await region.evaluate(element => element.scrollWidth > element.clientWidth), "Mobile results must scroll within their region");
        }
        await page.getByRole("textbox", { name: "Search", exact: true }).focus();
        await page.keyboard.type("roads");
        assert.equal(await page.getByRole("textbox", { name: "Search", exact: true }).inputValue(), "roads");
        await page.keyboard.press("Tab");
        assert.equal(await page.locator(":focus").getAttribute("name"), "type");
      } else if (name.startsWith("catalog-item")) {
        assert.equal(await page.locator("nav a[aria-current='page']").count(), 1);
        const tab = page.getByRole("link", { name: /^Overview/ });
        await tab.focus();
        assert.ok(await tab.evaluate(element => element === document.activeElement));
        if (name.includes("permissions")) {
          assert.ok(await page.getByText("Permissions are hidden", { exact: true }).isVisible());
          assert.deepEqual(await page.locator(".console-page-heading-actions a").allTextContents(), ["Open Detail", "Open Map"]);
        }
      } else {
        assert.equal(await page.locator("[data-catalog-endpoint]").count(), 5);
        assert.equal(await page.locator(".console-metric-tile").count(), 3);
      }
      for (const metric of await page.locator(".console-metric-tile").all()) {
        assert.deepEqual(await metric.evaluate(element => {
          const style = getComputedStyle(element);
          return { padding: style.padding, border: style.borderTopWidth, shadow: style.boxShadow };
        }), { padding: "0px", border: "0px", shadow: "none" });
      }
      assert.deepEqual((await new AxeBuilder({ page }).analyze()).violations, []);
      await page.screenshot({ path: path.join(previews, `${name}-${width}.png`), fullPage: true });
      console.log(`${name}: ${width}px, keyboard, overflow and axe pass`);
    }
  }
} finally {
  await browser?.close();
  await new Promise(resolve => server.close(resolve));
}
