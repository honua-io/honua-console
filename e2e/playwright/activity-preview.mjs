import assert from "node:assert/strict";
import path from "node:path";
import { runV4PagePresentation } from "./console-presentation.mjs";

const splitWords = page => page.locator(".console-activity-v4").evaluate(scope =>
  [...scope.querySelectorAll(".operate-table :is(th, .console-status, td, td > a)")]
    .filter(element => {
      const text = element.textContent.trim();
      if (!/^[A-Za-z][A-Za-z.]*$/.test(text)) return false;
      const walker = document.createTreeWalker(element, NodeFilter.SHOW_TEXT);
      const tops = new Set();
      for (let node = walker.nextNode(); node; node = walker.nextNode()) {
        const range = document.createRange();
        range.selectNodeContents(node);
        for (const rect of range.getClientRects()) tops.add(Math.round(rect.top));
      }
      return tops.size > 1;
    })
    .map(element => element.textContent.trim()));

const assertWholeWords = async (page, width) => {
  for (const tableWidth of [1280, 1024, 800, 700]) {
    await page.setViewportSize({ width: tableWidth, height: 844 });
    assert.deepEqual(await splitWords(page), [], `Activity tables split words mid-word at ${tableWidth}px`);
  }
  await page.setViewportSize({ width, height: 844 });
};

const assertFeedLeads = async page => {
  assert.equal(await page.locator("[data-feed-row]").count(), 3);
  const feed = await page.locator("[data-activity-feed]").boundingBox();
  const events = await page.locator("#events").boundingBox();
  assert.ok(feed.y < events.y, "Activity leads with the change list");
};

await runV4PagePresentation({
  previews: path.resolve(process.env.HONUA_ACTIVITY_PREVIEW_DIR ?? "artifacts/activity-preview"),
  widths: [1440, 1280, 390],
  themes: ["light", "dark"],
  pageNames: ["activity-selected", "activity-empty"],
  title: "Activity presentation fixture",
  stylesheet: "operate-activity-v4.css",
  heading: "Activity",
  scope: ".console-activity-v4",
  controls: "#events :is(a[href], input, select, button, summary, [tabindex='0']):not(:disabled):visible",
  inspect: async (page, name) => {
    assert.equal(await page.locator("[data-timeline-row]").count(), name === "activity-selected" ? 2 : 0);
    if (name === "activity-selected") await assertFeedLeads(page);
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
      if (width !== 390) await assertWholeWords(page, width);
      if (width === 390) {
        assert.ok(await page.locator(".operate-timeline").evaluate(region => region.scrollWidth <= region.clientWidth + 0.5),
          "Activity event history scrolls sideways at phone width");
      }
      const label = page.locator(width === 390 ? ".operate-event-table td[data-label]" : ".operate-event-table th").first();
      const pseudo = width === 390 ? "::before" : null;
      assert.equal(await label.evaluate((element, pseudo) => getComputedStyle(element, pseudo).textTransform, pseudo), "none");
      const timeline = page.locator(".operate-table-panel .operate-timeline");
      assert.equal(await timeline.evaluate(element => getComputedStyle(element).borderTopWidth), "0px");
      assert.equal(await label.evaluate((element, pseudo) => {
        const probe = document.createElement("span");
        probe.style.color = "var(--c-text-3)";
        element.append(probe);
        const matches = getComputedStyle(element, pseudo).color === getComputedStyle(probe).color;
        probe.remove();
        return matches;
      }, pseudo), true, "Activity keeps its quiet table labels");
    }
  },
});
