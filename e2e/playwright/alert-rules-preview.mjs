import assert from "node:assert/strict";
import path from "node:path";
import { runV4PagePresentation } from "./console-presentation.mjs";

const headings = { "alert-rules-create": "Create alert rule", "alert-rules-detail": "Speeding geofence" };

await runV4PagePresentation({
  previews: path.resolve(process.env.HONUA_CATALOG_PREVIEW_DIR ?? "artifacts/catalog-preview"),
  pageNames: ["alert-rules-list", "alert-rules-unbound", "alert-rules-create", "alert-rules-detail"],
  title: "Alert rules presentation fixture",
  stylesheet: "operate-alert-rules-v4.css",
  heading: name => headings[name] ?? "Alert rules",
  scope: ".console-alert-rules-v4",
  controls: ".console-alert-rules-v4 :is(a[href], input, select, button, textarea, summary, [tabindex='0']):not(:disabled):visible",
  async inspect(page, name) {
    if (name === "alert-rules-list") {
      assert.equal(await page.locator("tr[data-alert-rule]").count(), 1);
      assert.ok(await page.locator("#alert-rules-heading").isVisible());
    } else if (name === "alert-rules-unbound") {
      assert.equal(await page.locator("table").count(), 0);
      assert.ok((await page.locator(".console-alert-rules-v4").textContent()).includes("honua-server#1169"));
    } else if (name === "alert-rules-create") {
      assert.equal(await page.locator(".studio-field-grid label").count(), 10);
      assert.ok(await page.locator("[data-rule-create-submit]").isVisible());
    } else {
      assert.ok(await page.locator("#alert-rule-condition-heading").isVisible());
      assert.ok(await page.locator("[data-rule-enable]").isDisabled());
    }
  },
});
