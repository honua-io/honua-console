import assert from "node:assert/strict";
import path from "node:path";
import { runV4PagePresentation } from "./console-presentation.mjs";

await runV4PagePresentation({
  previews: path.resolve(process.env.HONUA_CATALOG_PREVIEW_DIR ?? "artifacts/catalog-preview"),
  pageNames: ["data-layer-detail", "data-layer-detail-unbound", "data-layer-detail-alias-saved", "data-layer-detail-domain-saved"],
  title: "Layer detail presentation fixture",
  stylesheet: "v4-data-primitives.css",
  heading: name => name.endsWith("-unbound") ? "Parcels" : "parcels",
  scope: ".console-page",
  controls: ".console-page :is(a[href], button, input, select, textarea, summary):not(:disabled):not([tabindex='-1']):visible",
  inspect: async (page, name) => {
    assert.equal(await page.locator("[data-layer-preview] figure[data-map-mode='layer']").count(), 1);
    assert.equal(await page.locator(".console-page-heading-v4").count(), 1);
    const panels = await page.locator(".console-panel:visible").evaluateAll(elements => elements.map(element => ({
      title: element.querySelector("h2")?.textContent,
      radius: getComputedStyle(element).borderRadius,
    })));
    assert.equal(panels[0].title, "Layer preview");
    assert.equal(panels[0].radius, "10px");
    assert.equal(await page.locator("[data-layer-style-link]").getAttribute("href"), name.endsWith("-unbound")
      ? "/operate/layers/conn-1-layer-1/style" : "/operate/layers/conn-1-layer-7/style");
    if (name.endsWith("-unbound")) {
      assert.equal(await page.locator("[data-fields-panel] [role='status'].console-state-neutral").count(), 1);
      assert.equal(await page.locator("[data-field-save]").count(), 0);
    } else {
      assert.equal(await page.locator("[data-field-row]").count(), 1);
      assert.equal(await page.locator("[data-domain-save]").count(), 1);
    }
  },
});
