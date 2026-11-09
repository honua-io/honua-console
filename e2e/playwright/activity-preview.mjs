import assert from "node:assert/strict";
import path from "node:path";
import { runV4PagePresentation } from "./console-presentation.mjs";

await runV4PagePresentation({
  previews: path.resolve(process.env.HONUA_ACTIVITY_PREVIEW_DIR ?? "artifacts/activity-preview"),
  pageNames: ["activity-selected", "activity-empty"],
  title: "Activity presentation fixture",
  stylesheet: "operate-activity-v4.css",
  heading: "Activity",
  scope: ".console-activity-v4",
  controls: "#events :is(a[href], input, select, button, [tabindex='0']):not(:disabled):visible",
  async inspect(page, name) {
    assert.equal(await page.locator("[data-timeline-row]").count(), name === "activity-selected" ? 2 : 0);
  },
});
