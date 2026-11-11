import assert from "node:assert/strict";
import path from "node:path";
import { runV4PagePresentation } from "./console-presentation.mjs";

const headings = {
  "environments-v4": "Environments",
  "environment-new-v4": "Add environment",
};

await runV4PagePresentation({
  previews: path.resolve(process.env.HONUA_CATALOG_PREVIEW_DIR ?? "artifacts/environments-preview"),
  pageNames: Object.keys(headings),
  title: "Environments presentation fixture",
  stylesheet: "environments-v4.css",
  heading: name => headings[name],
  scope: ".console-environments-v4",
  controls: ".console-environments-v4 :is(a[href], input, select, button, textarea, summary):not(:disabled):visible",
  prepareMarkup: markup => markup,
  async inspect(page, name) {
    if (name === "environments-v4") {
      assert.deepEqual(await page.locator("article h2").allTextContents(), ["Development", "Production"]);
      assert.ok(await page.locator(".console-page-heading-actions").getByRole("link", { name: "Add environment" }).isVisible());
      assert.ok(await page.getByRole("alert").isVisible());
    } else if (name === "environment-new-v4") {
      assert.equal(await page.locator("input, select").count(), 5);
      assert.ok(await page.locator("[data-environment-validation='blocking']").isVisible());
    }
  },
});
