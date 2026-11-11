import assert from "node:assert/strict";
import path from "node:path";
import { runV4PagePresentation } from "./console-presentation.mjs";

await runV4PagePresentation({
  previews: path.resolve(process.env.HONUA_CATALOG_PREVIEW_DIR || "artifacts/catalog-preview"),
  pageNames: ["share-public-v4", "share-public-legacy"],
  title: "Public item presentation",
  stylesheet: "v4-share-public.css",
  heading: "Coastal Flood Service",
  prepareMarkup: markup => markup,
  scope: "main",
  controls: name => name.endsWith("-v4") ? "main a[href]:visible" : null,
  inspect: async (page, name) => {
    const enabled = name.endsWith("-v4");
    assert.equal(await page.locator(".console-page-heading-v4").count(), enabled ? 1 : 0);
    assert.equal(await page.locator(".share-opendata-download-name").count(), 2);
    assert.equal(await page.locator("[data-share-opendata-routes] code").textContent(), "/share/public/items/coastal-flood-service");
    assert.match(await page.locator("[data-share-opendata-footer]").textContent(), /resilience-team/);
    assert.equal(await page.locator("button,input,select,textarea").count(), 0);
    assert.equal(await page.locator(".console-panel .console-panel").count(), 0);
    if (enabled) {
      assert.deepEqual(await page.locator("h2").allTextContents(), ["Download", "Routes"]);
      assert.equal(await page.locator(".share-opendata-back").getAttribute("href"), "/share/public");
      assert.equal(await page.locator(".console-page-heading-v4 p").textContent(), "Open data · Services");
      assert.deepEqual(await page.locator(".console-chip-row span").allTextContents(), ["#flood", "#open-data", "#coastal"]);
      const intro = page.locator(".share-opendata-intro");
      assert.ok(await intro.evaluate(element => {
        const ruler = element.cloneNode(false);
        ruler.style.width = "75ch";
        ruler.style.maxWidth = "none";
        ruler.style.position = "absolute";
        element.after(ruler);
        const expected = ruler.getBoundingClientRect().width;
        ruler.remove();
        return Math.abs(parseFloat(getComputedStyle(element).maxWidth) - expected) < 0.1;
      }));
    }
  },
});
