import assert from "node:assert/strict";
import path from "node:path";
import { runV4PagePresentation } from "./console-presentation.mjs";

await runV4PagePresentation({
  previews: path.resolve(process.env.HONUA_JOBS_PREVIEW_DIR ?? "artifacts/jobs-preview"),
  pageNames: ["jobs-list", "jobs-timeline"],
  title: "Geoprocessing presentation fixture",
  stylesheet: "operate-jobs-v4.css",
  heading: "Geoprocessing jobs",
  scope: ".console-jobs-v4",
  controls: ".console-jobs-v4 :is(a[href], input, select, button, textarea, summary, [tabindex='0']):not(:disabled):visible",
  async inspect(page, name) {
    if (name === "jobs-list") {
      assert.equal(await page.locator("tbody tr").count(), 2);
      assert.ok(await page.getByRole("link", { name: "gp-running", exact: true }).isVisible());
    } else {
      assert.ok(await page.locator("[data-job-timeline]").isVisible());
      assert.ok((await page.locator("[data-job-timeline]").textContent()).includes("older event"));
    }
  },
});
