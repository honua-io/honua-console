import assert from "node:assert/strict";
import path from "node:path";
import { runV4PagePresentation } from "./console-presentation.mjs";

const headings = { "health-snapshot": "Operational health", "metrics-live": "Server metrics", "deploy-overview": "Deploy" };
const sections = { "health-snapshot": 8, "metrics-live": 6, "deploy-overview": 5 };

await runV4PagePresentation({
  previews: path.resolve(process.env.HONUA_CATALOG_PREVIEW_DIR ?? "artifacts/monitoring-preview"),
  pageNames: Object.keys(headings),
  title: "Operate monitoring presentation fixture",
  stylesheet: "operate-monitoring-v4.css",
  heading: name => headings[name],
  scope: ".console-monitoring-v4",
  controls: ".console-monitoring-v4 :is(a[href], input, select, button, textarea, summary, [tabindex='0']):not(:disabled):visible",
  async inspect(page, name) {
    assert.equal(await page.locator(".operate-section-nav a").count(), sections[name]);
    assert.equal(await page.locator(".console-monitoring-v4 h1").count(), 1);
    if (name === "health-snapshot") {
      assert.ok(await page.locator("[data-health-drilldown='serving-latency']").isVisible());
    } else if (name === "metrics-live") {
      assert.ok(await page.locator("#connection-pool .operate-metric-bar").first().isVisible());
    } else {
      assert.ok(await page.locator("#deploy-approvals").isVisible());
    }
  },
});
