import assert from "node:assert/strict";
import path from "node:path";
import { runV4PagePresentation } from "./console-presentation.mjs";

await runV4PagePresentation({
  previews: path.resolve(process.env.HONUA_CATALOG_PREVIEW_DIR ?? "artifacts/catalog-preview"),
  pageNames: ["studio-styles-list-v4"],
  title: "Styles catalog presentation fixture",
  stylesheet: "v4-shell.css",
  heading: "Styles",
  scope: ".console-shell",
  controls: ".console-shell :is(a[href], input, button):not(:disabled):visible",
  prepareMarkup: markup => markup,
  async inspect(page) {
    assert.equal(await page.locator("[data-style-row]").count(), 25);
    assert.match(await page.locator("[data-style-catalog-summary]").innerText(), /^30 styles/);
    const first = page.locator('[data-style-row="style-01"]');
    assert.equal(await first.locator("a").getAttribute("href"), "/console/studio/styles/style-01");
    assert.equal(await first.locator("code").innerText(), "honua://styles/style-01");
    assert.match(await page.locator('[data-style-row="style-03"]').innerText(), /Flood zones/);
  },
});
