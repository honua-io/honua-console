import assert from "node:assert/strict";
import path from "node:path";
import { runV4PagePresentation } from "./console-presentation.mjs";

const previews = path.resolve(process.env.HONUA_CATALOG_PREVIEW_DIR ?? "artifacts/data-releases");
await runV4PagePresentation({
  previews,
  pageNames: ["list", "unavailable", "missing", "ready", "blocked", "no-scripts", "coordinated"].map(state => `releases-${state}`),
  title: "Releases presentation fixture",
  stylesheet: "v4-shell.css",
  heading: "Releases",
  scope: "[data-releases-page]",
  controls: () => "[data-releases-page] :is(a[href], button):not(:disabled):visible",
  async inspect(page, name) {
    for (const link of await page.locator("[data-releases-page] a[href]").all()) {
      assert.match(await link.getAttribute("href"), /^(#|https:\/\/|\/(console\/)?operate\/)/);
    }
    if (["ready", "blocked", "no-scripts", "coordinated"].some(state => name === `releases-${state}`)) {
      assert.ok(await page.locator("#semantic-diff .console-panel[aria-labelledby]").isVisible());
      assert.ok(await page.locator("#rollback .console-panel[aria-labelledby]").isVisible());
      for (const link of await page.locator(".operate-release-tabs a").all()) {
        assert.equal(await page.locator(await link.getAttribute("href")).count(), 1);
      }
    }
  },
});
