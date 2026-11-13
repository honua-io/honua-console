import assert from "node:assert/strict";
import path from "node:path";
import { runV4PagePresentation } from "./console-presentation.mjs";

await runV4PagePresentation({
  previews: path.resolve(process.env.HONUA_CATALOG_PREVIEW_DIR ?? "artifacts/catalog-preview"),
  pageNames: ["quiet-sections-true", "quiet-sections-false"],
  title: "Shared quiet section presentation fixture",
  stylesheet: "v4-shell.css",
  heading: "Service overview",
  scope: "main",
  controls: "main :is(input, select, button, a[href], [tabindex='0']):visible",
  prepareMarkup: markup => markup,
  async inspect(page, name) {
    const enabled = name.endsWith("true");
    const panel = page.getByRole("region", { name: "Layers", exact: true });
    const style = await panel.evaluate(element => {
      const computed = getComputedStyle(element);
      return { padding: computed.padding, border: computed.borderTopWidth, shadow: computed.boxShadow };
    });
    assert.equal(style.padding, enabled ? "0px" : "20px");
    assert.equal(style.border, enabled ? "0px" : "1px");
    assert.equal(style.shadow === "none", enabled);
    const heading = await page.getByRole("heading", { name: "Layers", exact: true }).boundingBox();
    assert.equal(heading.width === 1, enabled, "hidden title remains in the accessibility tree");
    const state = page.locator(".console-state-unavailable");
    assert.equal(await state.getAttribute("aria-live"), "polite");
    assert.equal(await state.evaluate(element => getComputedStyle(element).borderInlineStartWidth), enabled ? "0px" : "3px");
    const filters = page.locator(".console-filter-bar");
    if (enabled && page.viewportSize().width === 390) {
      const boxes = await filters.locator(":scope > *").evaluateAll(elements => elements.map(element => element.getBoundingClientRect().toJSON()));
      assert.ok(boxes.every(box => Math.abs(box.x - boxes[0].x) < 1), "mobile filters stack in one column");
    }
    const table = page.getByRole("region", { name: "Service layers", exact: true });
    await table.focus();
    assert.ok(await table.evaluate(element => element === document.activeElement));
    if (page.viewportSize().width === 390) {
      assert.ok(await table.evaluate(element => element.scrollWidth > element.clientWidth));
      await page.keyboard.press("ArrowRight");
      await page.waitForFunction(() => document.querySelector(".console-table-wrap").scrollLeft > 0);
    }
  },
});
