import assert from "node:assert/strict";
import path from "node:path";
import { inspectNoticeSurface, runV4PagePresentation, statusEdges } from "./console-presentation.mjs";

await runV4PagePresentation({
  previews: path.resolve(process.env.HONUA_CATALOG_PREVIEW_DIR ?? "artifacts/studio-styles-preview"),
  pageNames: ["studio-styles-v4", "studio-styles-conflict-v4"],
  title: "Shared style editor presentation fixture",
  stylesheet: "studio-styles-v4.css",
  heading: "Styles",
  scope: ".studio-style-editor-v4",
  controls: ".studio-style-editor-v4 :is(a[href], input, select, button, textarea, summary):not(:disabled):visible",
  prepareMarkup: markup => markup,
  async inspect(page, name) {
    assert.ok(await page.locator("[data-shared-style-scope]").isVisible());
    assert.ok(await page.locator("[data-style-content]").isVisible());
    assert.ok(await page.locator("[data-style-save]").isVisible());
    assert.equal(await page.locator("[data-style-impact] h3").count(), 2);
    const panel = await page.locator(".console-panel").boundingBox();
    const editor = await page.locator("[data-style-content]").boundingBox();
    assert.ok(panel && editor && editor.x + editor.width <= panel.x + panel.width + 1, "style editor stays inside the Style panel");
    if (name === "studio-styles-conflict-v4") {
      // A conflict must read as a danger notice, not lose its edge inside the Style panel.
      await inspectNoticeSurface(page.locator("[data-style-result]"), statusEdges.danger);
    }
  },
});
