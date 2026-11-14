import assert from "node:assert/strict";
import path from "node:path";
import { runV4PagePresentation } from "./console-presentation.mjs";

const previews = path.resolve(process.env.HONUA_CATALOG_PREVIEW_DIR ?? "artifacts/data-flow");
const scope = "[data-data-publish-flow]";
await runV4PagePresentation({
  previews, pageNames: ["data-flow-add", "data-flow-publish", "data-flow-golive"],
  title: "Add data flow presentation fixture", stylesheet: "v4-shell.css", heading: "Add data", scope,
  controls: () => `${scope} :is(a[href], input:not([type='radio']), input[type='radio']:checked, select, textarea, button, summary, [tabindex='0']):not(:disabled):visible`,
  async inspect(page, name) {
    assert.equal(await page.locator(`${scope} .console-panel .console-panel`).count(), 0);
    assert.equal(await page.locator(`${scope} [data-step-body] > .console-panel[aria-labelledby]`).count(), 1);
    assert.equal(await page.locator(`${scope} [data-step-body] .console-kicker`).count(), 0);
    if (name === "data-flow-golive") assert.equal(await page.locator("[data-protocol-result]").count(), 2);
  },
});
