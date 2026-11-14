import assert from "node:assert/strict";
import path from "node:path";
import { runV4PagePresentation } from "./console-presentation.mjs";

await runV4PagePresentation({
  previews: path.resolve(process.env.HONUA_CATALOG_PREVIEW_DIR ?? "artifacts/copilot-preview"),
  pageNames: ["copilot-findings", "copilot-proposed"],
  title: "Copilot findings presentation fixture",
  stylesheet: "operate-copilot-v4.css",
  heading: "Copilot findings",
  scope: ".console-copilot-v4",
  controls: ".console-copilot-v4 :is(a[href], input, select, button, textarea, summary):not(:disabled):visible",
  prepareMarkup: markup => markup,
  async inspect(page, name) {
    assert.ok(await page.locator(".console-page-heading-actions [data-refresh-findings]").isVisible());
    if (name === "copilot-findings") {
      assert.ok(await page.locator("[data-autonomy-kill-switch]").isVisible());
      assert.ok(await page.locator("[data-autonomy-audit]").isVisible());
    } else {
      assert.ok(await page.locator("a[href*='inbox?proposalId=prop-42']").first().isVisible());
      assert.ok(await page.locator("[data-investigate-finding='gp-queue-idle-def456']").isVisible());
    }
  },
});
