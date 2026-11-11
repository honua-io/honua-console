import assert from "node:assert/strict";
import path from "node:path";
import { runV4PagePresentation } from "./console-presentation.mjs";

await runV4PagePresentation({
  previews: path.resolve(process.env.HONUA_CATALOG_PREVIEW_DIR ?? "artifacts/catalog-preview"),
  pageNames: ["data-import-file", "data-import-file-complete", "data-import-service"],
  title: "Import fixture",
  stylesheet: "v4-shell.css",
  heading: (name) => name === "data-import-service" ? "Import from a service" : "Import a file",
  scope: ".console-page",
  controls: ".console-page :is(a[href], button, input, select, textarea):not(:disabled):not(input[type=radio]:not(:checked)):visible",
  inspect: async (page, name) => {
    assert.equal(await page.locator("section.console-panel[aria-labelledby]").count(), 1);
    if (name === "data-import-file") {
      assert.ok(await page.locator("[data-import-coordinate-system]").isVisible());
      assert.equal(await page.locator("input[type=radio]").count(), 2);
      const radios = page.locator("input[type=radio]");
      await radios.first().focus();
      await page.keyboard.press("ArrowRight");
      assert.ok(await radios.last().evaluate(element => element === document.activeElement && element.checked));
      assert.ok(await radios.last().evaluate(element => {
        const style = getComputedStyle(element);
        return element.getBoundingClientRect().height >= 24 && element.matches(":focus-visible") &&
          ((style.outlineStyle !== "none" && parseFloat(style.outlineWidth) > 0) || style.boxShadow !== "none");
      }));
      await page.keyboard.press("ArrowLeft");
      assert.ok(await radios.first().evaluate(element => element === document.activeElement && element.checked));
    } else if (name.endsWith("-complete")) {
      assert.equal(await page.locator("[data-import-stored-system]").textContent(), "EPSG:2193");
      assert.ok(await page.getByRole("heading", { name: "File imported", level: 3 }).isVisible());
    } else {
      assert.equal(await page.locator("[data-layer-checkbox]").count(), 2);
      assert.equal(await page.locator("[data-layer-checkbox]:checked").count(), 2);
    }
  },
});
