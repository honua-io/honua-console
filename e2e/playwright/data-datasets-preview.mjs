import assert from "node:assert/strict";
import path from "node:path";
import { runV4PagePresentation } from "./console-presentation.mjs";

const previews = path.resolve(process.env.HONUA_CATALOG_PREVIEW_DIR ?? "artifacts/data-datasets");
for (const { names, heading, scope } of [
  { names: ["datasets", "datasets-focused"], heading: "Datasets", scope: "[data-operate-data]" },
  { names: ["dataset-detail", "dataset-missing"], heading: name => name.endsWith("missing") ? "Dataset Not Found" : "parcels", scope: "[data-resource-detail]" },
  { names: ["conflicts-pending", "conflicts-resolved", "conflicts-clean", "conflicts-posted", "conflicts-missing"], heading: "Conflict resolution", scope: "[data-version-conflicts]" },
]) {
  await runV4PagePresentation({
    previews, pageNames: names.map(name => `data-${name}`),
    title: "Dataset and conflict presentation fixture", stylesheet: "v4-shell.css", heading, scope,
    controls: name => name === "data-dataset-missing" ? null : `${scope} :is(a[href], input:not([type='radio']), input[type='radio']:checked, select, textarea, button, summary, [tabindex='0']):not(:disabled):visible`,
    async inspect(page, name) {
      assert.equal(await page.locator(`${scope} .console-panel .console-panel`).count(), 0);
      if (name === "data-dataset-detail") {
        assert.equal(await page.locator(`${scope} > .console-panel[aria-labelledby]`).count(), 2);
        assert.equal(await page.locator("nav a").first().getAttribute("href"), "/console/operate/resources/parcels#fields");
      }
      if (name.startsWith("data-datasets")) {
        assert.equal(await page.locator("[data-resource-tree]").count(), 1);
        assert.equal(await page.locator(`${scope} > .console-panel[aria-labelledby]`).count(), 1);
      }
      if (name === "data-conflicts-pending") {
        assert.ok(await page.locator("[data-post]").isDisabled());
        await checkResolutionRadios(page);
      }
      if (["data-conflicts-clean", "data-conflicts-resolved"].includes(name)) assert.ok(await page.locator("[data-post]").isEnabled());
      if (name === "data-conflicts-posted") assert.ok(await page.locator("[data-post-result]").isVisible());
    },
  });
}

async function checkResolutionRadios(page) {
  for (const group of await page.locator("[data-conflict-choice]").all()) {
    const radios = group.locator("input[type='radio']");
    const count = await radios.count();
    assert.equal(count, 3);
    await radios.first().focus();
    for (let index = 0; index < count; index++) {
      await page.keyboard.press("ArrowRight");
      const radio = radios.nth((index + 1) % count);
      assert.ok(await radio.evaluate(element => element === document.activeElement && element.checked),
        "Arrow keys must reach and select every resolution option");
      assert.ok(await radio.evaluate(element => {
        const style = getComputedStyle(element);
        const ring = (style.outlineStyle !== "none" && parseFloat(style.outlineWidth) > 0
          && style.outlineColor !== "rgba(0, 0, 0, 0)") || style.boxShadow !== "none";
        return element.getBoundingClientRect().height >= 24 && element.matches(":focus-visible") && ring;
      }), "Every resolution option must have visible keyboard focus and a 24px target");
    }
  }
}
