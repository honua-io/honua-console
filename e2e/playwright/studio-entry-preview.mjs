import assert from "node:assert/strict";
import path from "node:path";
import { runV4PagePresentation } from "./console-presentation.mjs";

const headings = {
  "studio-unavailable": "Studio is unavailable on this deployment",
  "studio-connected": "Studio",
  "studio-retired": "This view has moved to Studio",
  "processes-connected": "Processes",
  "processes-disabled": "Realtime Studio is not enabled",
};
const controls = ".console-shell :is(a[href], input, select, button, textarea, summary, [tabindex='0']):not(:disabled):visible";
const noControlPages = new Set(["processes-connected", "processes-disabled"]);
await runV4PagePresentation({
  previews: path.resolve(process.env.HONUA_STUDIO_ENTRY_PREVIEW_DIR ?? "artifacts/studio-preview"),
  pageNames: Object.keys(headings),
  title: "Studio entry presentation fixture",
  stylesheet: "v4-shell.css",
  heading: name => headings[name],
  scope: ".console-shell",
  controls: name => noControlPages.has(name) ? null : controls,
  async inspect(page, name) {
    if (noControlPages.has(name)) {
      assert.equal(await page.locator(controls).count(), 0, `${name} has no interactive controls`);
      return;
    }
    const links = {
      "studio-unavailable": ["/environments", "/setup/local"],
      "studio-connected": ["/studio/processes"],
      "studio-retired": ["/studio", "/studio/processes"],
    };
    for (const href of links[name]) assert.equal(await page.locator(`a[href="${href}"]`).count(), 1);
  },
});
