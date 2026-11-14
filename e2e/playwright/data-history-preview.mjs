import assert from "node:assert/strict";
import path from "node:path";
import { runV4PagePresentation } from "./console-presentation.mjs";

const previews = path.resolve(process.env.HONUA_CATALOG_PREVIEW_DIR ?? "artifacts/data-history");
const scope = "[data-sync-review]";
await runV4PagePresentation({
  previews,
  pageNames: ["list", "queue", "conflicts", "resolved", "missing"].map(state => `sync-${state}`),
  title: "Sync presentation fixture",
  stylesheet: "v4-shell.css",
  heading: "Sync",
  scope,
  controls: name => name === "sync-missing" ? null : `${scope} :is(a[href], input, select, button):not(:disabled):visible`,
  async inspect(page, name) {
    if (name === "sync-missing") {
      assert.match(await page.locator(scope).textContent(), /not bound/);
      return;
    }
    assert.ok(await page.locator(`${scope} .console-panel[aria-labelledby]`).count() > 0);
    if (name === "sync-queue") assert.ok(await page.locator('[data-sync-region="replica-queue"]').isVisible());
    if (name === "sync-conflicts") assert.ok(await page.locator('[data-sync-conflict]').isVisible());
    if (name === "sync-resolved") {
      assert.match(await page.locator('[data-sync-region="committed"] a').getAttribute("href"), /^\/(console\/)?operate\/jobs\/job-3$/);
    }
  },
});
