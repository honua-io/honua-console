import assert from "node:assert/strict";
import path from "node:path";
import { runV4PagePresentation } from "./console-presentation.mjs";

await runV4PagePresentation({
  previews: path.resolve(process.env.HONUA_APPROVALS_PREVIEW_DIR ?? "artifacts/approvals-preview"),
  pageNames: ["approvals-selected", "approvals-forbidden"],
  title: "Approvals presentation fixture",
  stylesheet: "approval-inbox-v4.css",
  heading: "Approvals",
  scope: ".approval-inbox-v4",
  controls: ".approval-inbox-page :is(a[href], input, select, button, textarea, summary, [tabindex='0']):not(:disabled):visible",
  async inspect(page, name) {
    assert.equal(await page.locator("tbody [data-proposal-id]").count(), 1);
    assert.ok(await page.locator("[data-live-state]").isVisible());
    if (name === "approvals-forbidden") {
      assert.ok((await page.locator("section.approval-inbox-page").textContent()).includes("requires the 'approve' permission"));
    } else {
      assert.ok(await page.locator("[data-proposal-approve]").isVisible());
    }
  },
});
