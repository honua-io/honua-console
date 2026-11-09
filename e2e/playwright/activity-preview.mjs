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
  controls: "#events :is(a[href], input, select, button, summary, [tabindex='0']):not(:disabled):visible",
  inspect: async (page, name) => {
    assert.equal(await page.locator("[data-timeline-row]").count(), name === "activity-selected" ? 2 : 0);
    const width = page.viewportSize().width;
    assert.equal(await page.locator("h1").evaluate(element => getComputedStyle(element).fontSize), width === 390 ? "22px" : "28px");
    for (const heading of await page.locator("h2").all()) {
      const expected = await heading.evaluate(element => element.parentElement.classList.contains("console-panel-heading") ? "16px" : "22px");
      assert.equal(await heading.evaluate(element => getComputedStyle(element).fontSize), expected);
    }
  },
});
