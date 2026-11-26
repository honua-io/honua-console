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
    const route = "/share/public/items/coastal-flood-service";
    assert.equal(await page.locator("[data-share-opendata-routes] code").textContent(),
      enabled ? `http://localhost${route}` : route);
    assert.equal(await page.locator(".console-panel .console-panel").count(), 0);
    if (enabled) {
      const downloads = page.locator("[data-share-opendata-downloads]");
      assert.match(await downloads.textContent(), /No download links are published/);
      assert.equal(await downloads.locator("a").count(), 0);
      assert.deepEqual(await page.locator("h2").allTextContents(), ["Connect from your software", "Download data", "Apps using this", "Details"]);
      assert.equal(await page.locator(".share-opendata-back").getAttribute("href"), "/share/public");
      assert.match(await page.locator(".public-item-meta").textContent(), /resilience-team/);
      assert.match(await page.locator(".public-item-kv").textContent(), /resilience-team/);
      assert.deepEqual(await page.locator(".public-item-tags li").allTextContents(), ["#flood", "#open-data", "#coastal"]);
      assert.equal(await page.locator("button[data-console-copy]").count(), 1);
      const intro = page.locator(".share-opendata-intro");
      assert.ok(await intro.evaluate(element => {
        const ruler = element.cloneNode(false);
        ruler.style.width = "72ch";
        ruler.style.maxWidth = "none";
        ruler.style.position = "absolute";
        element.after(ruler);
        const expected = ruler.getBoundingClientRect().width;
        ruler.remove();
        return Math.abs(parseFloat(getComputedStyle(element).maxWidth) - expected) < 0.1;
      }));
    } else {
      assert.deepEqual(await page.locator(".share-opendata-download-name").allTextContents(),
        ["GeoServices:FeatureService", "OGC:API:Features"]);
    }
  },
});
