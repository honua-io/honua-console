import assert from "node:assert/strict";
import path from "node:path";
import { runV4PagePresentation } from "./console-presentation.mjs";

await runV4PagePresentation({
  previews: path.resolve(process.env.HONUA_CATALOG_PREVIEW_DIR ?? "artifacts/catalog-preview"),
  pageNames: ["data-layer-style", "data-layer-style-saved", "data-layer-style-unbound"],
  title: "Layer presentation fixture",
  stylesheet: "v4-shell.css",
  heading: "Layer presentation",
  scope: ".console-page",
  controls: ".console-page :is(a[href], button, input, select, textarea):not(:disabled):visible",
  inspect: async (page, name) => {
    assert.equal(await page.getByRole("link", { name: "Back to layer" }).getAttribute("href"), "/console/operate/layers/res-1");
    assert.equal(await page.locator("section.console-panel[aria-labelledby]").count(), 2);
    if (name.endsWith("-unbound")) {
      assert.equal(await page.locator("[data-slot-save], [data-slot-popup]").count(), 0);
      assert.ok(await page.getByRole("heading", { name: "Layer overrides are not available", level: 3 }).isVisible());
    } else {
      assert.equal(await page.locator("[data-slot-popup], [data-slot-drawing]").count(), 2);
      assert.equal(await page.getByRole("button", { name: "Save override" }).count(), 1);
      assert.equal(await page.locator("[data-style-slot].console-panel").count(), 0);
      if (name.endsWith("-saved")) assert.ok(await page.locator("[data-slot-saved]").isVisible());
    }
  },
});
