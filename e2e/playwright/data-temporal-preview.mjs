import assert from "node:assert/strict";
import path from "node:path";
import { runV4PagePresentation } from "./console-presentation.mjs";

const previews = path.resolve(process.env.HONUA_CATALOG_PREVIEW_DIR ?? "artifacts/data-temporal");
const scope = "[data-temporal-viewer]";
await runV4PagePresentation({
  previews,
  pageNames: ["missing", "sources", "timeline", "diff", "forbidden", "unsupported", "rollback", "conflicts", "committed"].map(state => `temporal-${state}`),
  title: "Temporal presentation fixture",
  stylesheet: "v4-shell.css",
  heading: "Temporal",
  scope,
  controls: name => name === "temporal-missing" ? null : `${scope} :is(a[href], input, select, button, summary):not(:disabled):visible`,
  async inspect(page, name) {
    if (name === "temporal-missing") {
      assert.match(await page.locator(scope).textContent(), /not bound/);
      return;
    }
    assert.ok(await page.locator(`${scope} .console-panel[aria-labelledby]`).count() > 0);
    if (name === "temporal-timeline") assert.ok(await page.locator('[data-temporal-region="timeline"]').isVisible());
    if (name === "temporal-diff") assert.equal(await page.locator('[data-temporal-map]').count(), 2);
    if (name === "temporal-conflicts") assert.ok(await page.locator('[data-temporal-region="sync-conflicts"]').isVisible());
  },
});
