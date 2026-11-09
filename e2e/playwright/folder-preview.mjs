import assert from "node:assert/strict";
import path from "node:path";
import { runV4PagePresentation } from "./console-presentation.mjs";

const previews = path.resolve(process.env.HONUA_CATALOG_PREVIEW_DIR ?? "artifacts/catalog-preview");
for (const variant of ["v4", "legacy"]) {
  await runV4PagePresentation({
    previews, pageNames: [`folder-${variant}`], title: "Folder presentation fixture",
    stylesheet: "css/folder-v4.css", heading: variant === "v4" ? "Transport" : "Folders",
    prepareMarkup: markup => markup,
    scope: "main", controls: "main :is(a[href], button, [tabindex='0']):not(:disabled):visible",
    inspect: async page => {
      assert.equal(await page.locator(".console-shell").getAttribute("data-console-v4"), variant === "v4" ? "true" : null);
      if (variant === "legacy") {
        assert.equal(await page.locator(".console-folder-services").count(), 0);
        assert.equal(await page.getByRole("link", { name: "Manage Folders in Services" }).getAttribute("href"), "/operate/services#folders");
        return;
      }
      assert.equal((await page.locator("[data-folder-count]").textContent()).trim(), "2 Services · 7 Layers");
      assert.equal(await page.getByRole("row").count(), 3);
      assert.equal(await page.getByRole("columnheader", { name: "Title", exact: true }).count(), 1);
      assert.equal(await page.getByRole("link", { name: "roads", exact: true }).getAttribute("href"), "/operate/services/roads/settings");
      assert.equal(await page.getByRole("heading", { name: "Administration", exact: true }).count(), 1);
      assert.equal(await page.locator(".console-folder-services.console-panel").count(), 0);
      assert.equal(await page.locator(".console-folder-services").evaluate(element => getComputedStyle(element).boxShadow), "none");
    },
  });
}
