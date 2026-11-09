import assert from "node:assert/strict";
import path from "node:path";
import { runV4PagePresentation } from "./console-presentation.mjs";

await runV4PagePresentation({
  previews: path.resolve(process.env.HONUA_CATALOG_PREVIEW_DIR || "artifacts/catalog-preview"),
  pageNames: ["link", "embed", "exports", "opendata", "audit"].flatMap(tab =>
    ["v4", "legacy"].map(flag => `share-manage-${tab}-${flag}`)),
  title: "Share configuration presentation",
  stylesheet: "v4-share-manage.css",
  heading: "Storm Layer",
  prepareMarkup: markup => markup,
  scope: "main",
  controls: "main :is(a[href], button, input:not([type='hidden']), select, textarea):not(:disabled):visible",
  inspect: async (page, name) => {
    const enabled = name.endsWith("-v4");
    assert.equal(await page.locator(".console-shell").getAttribute("data-console-v4"), enabled ? "true" : null);
    assert.equal(await page.locator(".console-page-heading-v4").count(), enabled ? 1 : 0);
    assert.equal(await page.locator("[data-share-tab]").count(), 5);
    assert.equal(await page.locator("[data-share-sidebar]").count(), 1);
    assert.equal(await page.getByRole("button", { name: "Open share access", exact: true }).count(), 1);
    if (enabled) {
      const input = page.locator("input[placeholder='item-...']");
      assert.equal(await input.evaluate(element => getComputedStyle(element).borderRadius), "6px");
      assert.equal(await input.evaluate(element => getComputedStyle(element).paddingLeft), "12px");
      const sidebar = page.locator("[data-share-sidebar] .console-panel-emphasis");
      assert.equal(await sidebar.evaluate(element => getComputedStyle(element).boxShadow), "none");
    }
  },
});
