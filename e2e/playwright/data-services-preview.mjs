import assert from "node:assert/strict";
import path from "node:path";
import { runV4PagePresentation } from "./console-presentation.mjs";

await runV4PagePresentation({
  previews: path.resolve(process.env.HONUA_CATALOG_PREVIEW_DIR ?? "artifacts/catalog-preview"),
  pageNames: ["data-services", "data-services-unbound"],
  title: "Services presentation fixture",
  stylesheet: "v4-data-primitives.css",
  heading: "Services",
  scope: ".console-page",
  controls: ".console-page :is(a[href], button, input, select, summary):not(:disabled):visible",
  inspect: async (page, name) => {
    assert.equal(await page.locator(".console-panel[aria-labelledby='services-list-heading']").count(), 1);
    assert.equal(await page.locator(".console-page > .console-page-heading .console-kicker").count(), 0);
    if (name === "data-services") {
      const link = page.getByRole("link", { name: "Public roads", exact: true });
      assert.equal(await link.getAttribute("href"), "/operate/services/roads%2Fpublic/settings");
      assert.equal(await page.locator(".console-name-identifier-id").textContent(), "roads/public");
      assert.deepEqual(await page.locator(".console-protocol-list li").allTextContents(), ["WMS", "WFS"]);
      assert.equal(await page.locator(".console-protocol-list").evaluate(element => getComputedStyle(element).display), "flex");
      assert.equal(await page.locator(".console-protocol-list").evaluate(element => getComputedStyle(element).listStyleType), "none");
      assert.equal(await page.locator(".console-protocol-list li").first().evaluate(element => getComputedStyle(element).borderTopWidth), "1px");
      assert.equal(await page.locator(".console-name-identifier-id").evaluate(element => getComputedStyle(element).color), "rgb(75, 85, 81)");
      assert.equal(await page.locator("tbody td").nth(2).textContent(), "0");
      assert.equal(await page.locator("[data-connection-notice]").count(), 0);
    } else {
      assert.equal(await page.locator("table, .console-state-empty").count(), 0);
      await page.getByRole("link", { name: "Connect environment", exact: true }).waitFor();
      const disclosure = page.locator("[data-connection-notice] summary");
      assert.equal(await disclosure.evaluate(element => getComputedStyle(element).display), "list-item");
      await disclosure.focus();
      await page.keyboard.press("Enter");
      assert.equal(await disclosure.evaluate(element => element.parentElement.open), true);
    }
    const surface = page.locator(".console-panel").first();
    assert.equal(await surface.evaluate(element => getComputedStyle(element).borderRadius), "10px");
    assert.notEqual(await surface.evaluate(element => getComputedStyle(element).boxShadow), "none");
  },
});
