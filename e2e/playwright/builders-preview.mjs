import assert from "node:assert/strict";
import path from "node:path";
import { runV4PagePresentation } from "./console-presentation.mjs";

const headings = {
  "form-builder-unbound": "Form builder",
  "form-builder-ready": "Form builder",
  "report-builder-unbound": "Report builder",
  "report-builder-new": "Report builder",
  "workflow-editor-seeded": "Workflow editor",
};
const controls = ".console-shell :is(a[href], input, select, button, textarea, summary, [tabindex='0']):not(:disabled):visible";
await runV4PagePresentation({
  previews: path.resolve(process.env.HONUA_BUILDERS_PREVIEW_DIR ?? "artifacts/builders-preview"),
  pageNames: Object.keys(headings),
  title: "Studio builders presentation fixture",
  stylesheet: "studio-builders-v4.css",
  heading: name => headings[name],
  scope: ".console-shell",
  controls: name => name === "form-builder-unbound" ? null : controls,
  inspect: async (page, name) => {
    if (name === "form-builder-unbound") assert.equal(await page.locator(controls).count(), 0);
    if (name.endsWith("unbound")) {
      assert.equal(await page.locator("[data-form-builder], [data-report-builder]").count(), 0);
    } else if (name === "form-builder-ready") {
      assert.ok(await page.getByRole("button", { name: "Publish", exact: true }).isEnabled());
    } else if (name === "report-builder-new") {
      assert.equal(await page.locator("[data-report-outline], [data-report-page], [data-report-inspector]").count(), 3);
      assert.equal(await page.getByText("Give the report a title.", { exact: true }).count(), 3);
    } else {
      assert.ok(await page.getByRole("textbox", { name: "Workflow name", exact: true }).isVisible());
      assert.ok(await page.getByRole("button", { name: "Save draft", exact: true }).isVisible());
    }
  },
});
