import assert from "node:assert/strict";
import path from "node:path";
import { runV4PagePresentation } from "./console-presentation.mjs";

await runV4PagePresentation({
  previews: path.resolve(process.env.HONUA_CATALOG_PREVIEW_DIR ?? "artifacts/catalog-preview"),
  pageNames: ["settings-v4", "settings-legacy"],
  title: "Settings presentation fixture",
  stylesheet: "v4-shell.css",
  heading: "Settings",
  scope: "main",
  controls: "main [tabindex='0']",
  wrapped: true,
  inspect: async (page, name) => {
    const width = page.viewportSize().width;
    assert.equal(await page.getByRole("columnheader").count(), 5);
    assert.equal(await page.locator("tbody td").last().textContent(), "/api/v1/admin/version");
    assert.equal(await page.locator("input, select, textarea, button").count(), 0);
    if (name === "settings-v4") {
      assert.equal(await page.locator("h1").evaluate(element => getComputedStyle(element).fontSize), width === 390 ? "22px" : "28px");
      assert.equal(await page.getByRole("heading", { name: "Server configuration" }).count(), 1);
    } else {
      assert.equal(await page.locator(".console-page-heading-v4").count(), 0);
    }
  },
});
