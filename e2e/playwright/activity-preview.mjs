import assert from "node:assert/strict";
import path from "node:path";
import { runV4PagePresentation } from "./console-presentation.mjs";

await runV4PagePresentation({
  previews: path.resolve(process.env.HONUA_ACTIVITY_PREVIEW_DIR ?? "artifacts/activity-preview"),
  widths: [1440, 390],
  themes: ["light", "dark"],
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
    if (name === "activity-selected") {
      const clipped = await page.locator(".operate-timeline").evaluate(region => {
        const bounds = region.getBoundingClientRect();
        return [...region.querySelectorAll("[data-timeline-row] td")]
          .filter(cell => cell.getBoundingClientRect().right > bounds.right + 0.5)
          .map(cell => cell.dataset.label ?? cell.cellIndex);
      });
      assert.deepEqual(clipped, [], `Activity event columns are hidden at ${width}px`);
      if (width === 390) {
        assert.ok(await page.locator(".operate-timeline").evaluate(region => region.scrollWidth <= region.clientWidth + 0.5),
          "Activity event history scrolls sideways at phone width");
      }
      assert.equal(await page.locator(".operate-event-table th").first().evaluate(element => getComputedStyle(element).textTransform), "none");
      const timeline = page.locator(".operate-table-panel .operate-timeline");
      assert.equal(await timeline.evaluate(element => getComputedStyle(element).borderTopWidth), "0px");
      assert.equal(await page.locator(".operate-event-table th").first().evaluate(element => {
        const probe = document.createElement("span");
        probe.style.color = "var(--c-text-3)";
        element.append(probe);
        const matches = getComputedStyle(element).color === getComputedStyle(probe).color;
        probe.remove();
        return matches;
      }), true, "Activity keeps its quiet table headers");
    }
  },
});
