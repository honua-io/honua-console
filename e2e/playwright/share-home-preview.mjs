import assert from "node:assert/strict";
import path from "node:path";
import { runV4PagePresentation } from "./console-presentation.mjs";

await runV4PagePresentation({
  previews: path.resolve(process.env.HONUA_CATALOG_PREVIEW_DIR || "artifacts/catalog-preview"),
  pageNames: ["share-home-v4", "share-home-legacy"],
  title: "Share presentation",
  stylesheet: "v4-share.css",
  heading: "Share",
  prepareMarkup: markup => markup,
  scope: "main",
  controls: "main :is(a[href], [tabindex='0']):visible",
  inspect: async (page, name) => {
    const enabled = name.endsWith("-v4");
    assert.equal(await page.locator(".console-shell").getAttribute("data-console-v4"), String(enabled));
    assert.equal(await page.locator(".console-page-heading-v4").count(), enabled ? 1 : 0);
    assert.equal(await page.locator("[data-share-kpi]").count(), 5);
    assert.equal(await page.locator("[data-share-row]").count(), 2);
    assert.equal(await page.getByRole("link", { name: "Configure", exact: true }).first().getAttribute("href"), "/share/manage?itemId=svc-parcels");
    assert.equal(await page.getByRole("link", { name: "Tax Parcels (FY 2024)", exact: true }).getAttribute("href"), "/share/public/items/svc-parcels");
    if (enabled) {
      const lead = page.locator(".console-metric-value");
      assert.equal(await lead.count(), 1);
      assert.equal((await lead.textContent()).trim(), "2");
      const size = await lead.evaluate(element => parseFloat(getComputedStyle(element).fontSize));
      const secondary = await page.locator(".console-share-summary-details dd").evaluateAll(elements => elements.map(element => parseFloat(getComputedStyle(element).fontSize)));
      assert.equal(secondary.length, 4);
      assert.ok(secondary.every(value => value < size));
      assert.equal(await page.locator(".console-metric-tile").evaluate(element => getComputedStyle(element).borderTopWidth), "0px");
    }
  },
});
