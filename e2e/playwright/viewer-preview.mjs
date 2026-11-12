import assert from "node:assert/strict";
import path from "node:path";
import { runV4PagePresentation } from "./console-presentation.mjs";

const headings = {
  "map-viewer-v4": "Storm Response Map",
  "map-draft-v4": "Coastal Flood Service draft map",
  "metadata-dataset-v4": "Dataset metadata",
  "conversation-entry-v4": "Continue your conversation",
};

await runV4PagePresentation({
  previews: path.resolve(process.env.HONUA_CATALOG_PREVIEW_DIR ?? "artifacts/viewer-preview"),
  pageNames: Object.keys(headings),
  title: "Map viewer, metadata and conversation presentation fixture",
  stylesheet: "viewer-v4.css",
  heading: name => headings[name],
  scope: ".console-viewer-v4",
  controls: ".console-viewer-v4 :is(a[href], input, select, button, textarea, summary):not(:disabled):visible",
  prepareMarkup: markup => markup,
  async inspect(page, name) {
    if (name.startsWith("map-")) {
      const surface = await page.locator(".console-map-surface").boundingBox();
      const layers = await page.locator(".console-map-panel.console-panel").boundingBox();
      assert.ok(surface && layers, `${name}: map surface and Layers panel render`);
      assert.ok(layers.x >= surface.x && layers.x + layers.width <= surface.x + surface.width + 1, `${name}: Layers panel stays inside the map surface`);
      assert.ok(await page.locator(".console-map-panel li").first().isVisible());
    }
    if (name === "map-viewer-v4") {
      assert.ok(await page.locator(".console-page-heading-actions a.console-button-link").first().isVisible());
    }
    if (name === "metadata-dataset-v4") {
      assert.ok(await page.locator(".console-page-heading-actions a[href='/catalogs']").isVisible());
    }
    if (name === "conversation-entry-v4") {
      assert.ok(await page.locator(".console-page-heading-actions [data-workspace-open]").isVisible());
    }
  },
});
