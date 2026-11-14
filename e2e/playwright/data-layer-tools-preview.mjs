import assert from "node:assert/strict";
import path from "node:path";
import { runV4PagePresentation } from "./console-presentation.mjs";

const previews = path.resolve(process.env.HONUA_CATALOG_PREVIEW_DIR ?? "artifacts/data-layer-tools");
for (const { names, heading, scope } of [
  { names: ["editing"], heading: "Parcels · editing", scope: "[data-layer-metadata-page]" },
  { names: ["filter", "filter-saved"], heading: "Parcels · permanent filter", scope: "[data-filter-page]" },
  { names: ["relationships-empty", "relationships-draft"], heading: "Parcels · relationships", scope: "[data-relationships-page]" },
]) {
  await runV4PagePresentation({
    previews, pageNames: names.map(name => `data-layer-${name}`),
    title: "Layer tools presentation fixture", stylesheet: "v4-shell.css", heading, scope,
    controls: `${scope} :is(a[href], input, select, textarea, button, [tabindex='0']):not(:disabled):visible`,
    async inspect(page, name) {
      assert.equal(await page.locator(`${scope} > .console-panel[aria-labelledby]`).count(), 1);
      assert.match(await page.locator("[data-layer-detail-link]").getAttribute("href"), /operate\/layers\/conn-1-layer-1$/);
      assert.equal(await page.locator(`${scope} .console-panel .console-panel`).count(), 0);
      if (name.endsWith("filter-saved")) assert.ok(await page.locator("[data-filter-result][role='status']").isVisible());
      if (name.endsWith("relationships-draft")) assert.equal(await page.locator("[data-relationship-row]").count(), 1);
    },
  });
}
