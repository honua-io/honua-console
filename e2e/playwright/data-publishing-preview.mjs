import assert from "node:assert/strict";
import path from "node:path";
import { runV4PagePresentation } from "./console-presentation.mjs";

await runV4PagePresentation({
  previews: path.resolve(process.env.HONUA_CATALOG_PREVIEW_DIR ?? "artifacts/catalog-preview"),
  pageNames: ["data-publishing-unbound", "data-publishing", "data-publishing-lookup", "data-quick-publish", "data-quick-publish-complete"],
  title: "Publishing fixture",
  stylesheet: "v4-shell.css",
  heading: name => name.startsWith("data-quick") ? "Publish a layer" : "Publishing",
  scope: ".console-page",
  controls: ".console-page :is(a[href], button, input, select, textarea, summary):not(:disabled):not(input[type=radio]:not(:checked)):visible",
  inspect: async (page, name) => {
    if (name === "data-publishing-unbound") {
      assert.equal(await page.locator("[data-connection-notice]").count(), 1);
      assert.equal(await page.locator("[data-publishing-review]").count(), 0);
    } else if (name.startsWith("data-quick")) {
      assert.equal(await page.locator("section.console-panel[aria-labelledby='quick-publish-source']").count(), 1);
      if (name.endsWith("-complete")) {
        assert.ok(await page.getByRole("heading", { name: "Layer published", level: 3 }).isVisible());
      }
    } else if (name === "data-publishing-lookup") {
      assert.ok(await page.getByRole("heading", { name: "Look up released content", exact: true }).isVisible());
    } else {
      assert.ok(await page.getByRole("heading", { name: "Publishing formats", exact: true }).isVisible());
      assert.ok(await page.getByRole("heading", { name: "Publishing review", exact: true }).isVisible());
    }
  },
});
