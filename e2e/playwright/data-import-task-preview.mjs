import assert from "node:assert/strict";
import path from "node:path";
import { runV4PagePresentation } from "./console-presentation.mjs";

const previews = path.resolve(process.env.HONUA_CATALOG_PREVIEW_DIR ?? "artifacts/data-import-task");
await runV4PagePresentation({
  previews, pageNames: ["data-import-task", "data-import-task-off"],
  title: "Dataset import task presentation fixture", stylesheet: "v4-shell.css",
  heading: name => name.endsWith("-off") ? "Import a file" : "Import dataset", scope: ".console-page",
  // The task controls stay disabled until dataset-import-panel.mjs binds the task, so only the off state has keyboard targets.
  controls: name => name === "data-import-task" ? null : ".console-page :is(a[href], input, select, textarea, button, summary, [tabindex='0']):not(:disabled):visible",
  async inspect(page, name) {
    assert.equal(await page.locator(".console-page .console-panel .console-panel").count(), 0);
    if (name === "data-import-task") {
      assert.equal(await page.locator("[data-dataset-import-panel] > .console-panel[aria-labelledby]").count(), 1);
      assert.equal(await page.locator("[data-import-status][role='status']").count(), 1);
    }
  },
});
