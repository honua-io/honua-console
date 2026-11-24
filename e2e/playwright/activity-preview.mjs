import assert from "node:assert/strict";
import path from "node:path";
import { expect } from "@playwright/test";
import { runV4PagePresentation } from "./console-presentation.mjs";

const splitWords = page => page.locator(".console-activity-v4").evaluate(scope =>
  [...scope.querySelectorAll(".operate-table :is(th, .console-status, td, td > a)")]
    .filter(element => {
      const text = element.textContent.trim();
      if (!/^[A-Za-z][A-Za-z.]*$/.test(text)) return false;
      const range = document.createRange();
      const walker = document.createTreeWalker(element, NodeFilter.SHOW_TEXT);
      const tops = new Set();
      for (let node = walker.nextNode(); node; node = walker.nextNode()) {
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

const assertHeadingSizes = async (page, width) => {
  assert.equal(await page.locator("h1").evaluate(element => getComputedStyle(element).fontSize), width === 390 ? "22px" : "28px");
  for (const heading of await page.locator("h2").all()) {
    const expected = await heading.evaluate(element => element.parentElement.classList.contains("console-panel-heading") ? "16px" : "22px");
    assert.equal(await heading.evaluate(element => getComputedStyle(element).fontSize), expected);
  }
};

const assertLargeTextFits = async page => {
  const { filterRight, viewportWidth, scrollWidth } = await page.locator(".console-activity-feed-filter").evaluate(filter => {
    const root = document.documentElement;
    const previousSize = root.style.fontSize;
    try {
      root.style.fontSize = "32px";
      return { filterRight: filter.getBoundingClientRect().right, viewportWidth: innerWidth, scrollWidth: root.scrollWidth };
    } finally {
      root.style.fontSize = previousSize;
    }
  });
  assert.ok(filterRight <= viewportWidth && scrollWidth <= viewportWidth + 1, "Activity fits at 200% text size on a phone");
};

const assertChangesKeyboardScroll = async (page, width) => {
  const filter = page.getByRole("combobox", { name: "Actor", exact: true });
  const region = page.getByRole("region", { name: "Changes, newest first" });
  const agent = region.locator("[data-feed-actor-kind='agent']");
  const reference = region.getByRole("link").first();
  assert.equal(await agent.locator(".console-activity-feed-actor").textContent(),
    "Regional Planning and Geospatial Operations Assistant");
  assert.equal(await agent.locator(".console-activity-feed-channel").textContent(),
    "Agent · on behalf of regional-planning-and-geospatial-operations@example.org");

  for (const fontSize of ["16px", "32px"]) {
    await page.locator("html").evaluate((root, size) => { root.style.fontSize = size; }, fontSize);
    try {
      await filter.focus();
      await page.keyboard.press("Tab");
      assert.ok(await region.evaluate(element => element === document.activeElement));
      const overflows = await region.evaluate(element => element.scrollWidth > element.clientWidth);
      if (width === 390) assert.ok(overflows, "The phone fixture exercises horizontal keyboard scrolling");
      if (overflows) {
        await region.evaluate(element => {
          delete element.dataset.keyboardScrollEnded;
          element.addEventListener("scrollend", () => { element.dataset.keyboardScrollEnded = "true"; }, { once: true });
        });
        await page.keyboard.press("ArrowRight");
        await page.waitForFunction(() => {
          const element = document.activeElement;
          return element.dataset.keyboardScrollEnded === "true" && element.scrollLeft > 0;
        }, undefined, { timeout: 3000 });
      }
      await page.keyboard.press("Tab");
      assert.ok(await reference.evaluate(element => element === document.activeElement),
        `The first change reference receives focus at ${width}px and ${fontSize}`);
      await expect.poll(() => reference.evaluate(element => {
        const tableRegion = element.closest("[role='region']");
        const linkBounds = element.getBoundingClientRect();
        const regionBounds = tableRegion.getBoundingClientRect();
        return Math.ceil(Math.max(regionBounds.left - linkBounds.left, linkBounds.right - regionBounds.right, 0));
      }), { message: `The focused reference fits in the Changes region at ${width}px and ${fontSize}` }).toBeLessThanOrEqual(1);
      assert.ok(await page.locator("html").evaluate(root => root.scrollWidth <= innerWidth + 1),
        `The Changes table does not cause page overflow at ${fontSize}`);
    } finally {
      await page.locator("html").evaluate(root => { root.style.fontSize = ""; });
      await region.evaluate(element => { element.scrollLeft = 0; });
    }
  }
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
    if (name === "activity-selected") {
      assert.equal(await page.locator("[data-feed-row]").count(), 3);
      assert.ok(await page.locator("[data-activity-feed]").evaluate(feed =>
        feed.getBoundingClientRect().top < document.querySelector("#events").getBoundingClientRect().top));
    }
    const width = page.viewportSize().width;
    await assertHeadingSizes(page, width);
    if (width === 390) await assertLargeTextFits(page);
    if (name === "activity-selected") {
      const clipped = await page.locator(".operate-timeline").evaluate(region => {
        const bounds = region.getBoundingClientRect();
        return [...region.querySelectorAll("[data-timeline-row] td")]
          .filter(cell => cell.getBoundingClientRect().right > bounds.right + 0.5)
          .map(cell => cell.dataset.label ?? cell.cellIndex);
      });
      assert.deepEqual(clipped, [], `Activity event columns are hidden at ${width}px`);
      if (width !== 390) await assertWholeWords(page, width);
      if (width === 1280 || width === 390) await assertChangesKeyboardScroll(page, width);
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
