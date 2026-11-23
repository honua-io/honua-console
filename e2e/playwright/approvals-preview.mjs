import assert from "node:assert/strict";
import path from "node:path";
import { runV4PagePresentation } from "./console-presentation.mjs";

await runV4PagePresentation({
  previews: path.resolve(process.env.HONUA_APPROVALS_PREVIEW_DIR ?? "artifacts/approvals-preview"),
  widths: [1440, 390],
  themes: ["light", "dark"],
  pageNames: ["approvals-selected", "approvals-forbidden"],
  title: "Approvals presentation fixture",
  stylesheet: "approval-inbox-v4.css",
  heading: "Approvals",
  scope: ".approval-inbox-v4",
  controls: ".approval-inbox-page :is(a[href], input, select, button, textarea, summary, [tabindex='0']):not(:disabled):visible",
  async inspect(page, name) {
    assert.equal(await page.locator("tbody [data-proposal-id]").count(), 1);
    assert.ok(await page.locator("[data-live-state]").isVisible());
    assert.equal(await page.locator("tbody td").first().evaluate(element => getComputedStyle(element).paddingTop), "12px");
    assert.equal(await page.locator("thead th").first().evaluate(element => getComputedStyle(element).textTransform), "none");
    if (name === "approvals-forbidden") {
      assert.ok((await page.locator("section.approval-inbox-page").textContent()).includes("requires the 'approve' permission"));
    } else {
      assert.ok(await page.locator("[data-proposal-approve]").isVisible());
    }
  },
});
