import assert from "node:assert/strict";
import path from "node:path";
import { runV4PagePresentation } from "./console-presentation.mjs";

await runV4PagePresentation({
  previews: path.resolve(process.env.HONUA_EVENTS_PREVIEW_DIR ?? "artifacts/events-preview"),
  pageNames: ["events-run", "events-missing"],
  title: "Workflow event presentation fixture",
  stylesheet: "events-v4.css",
  heading: "Workflow events",
  scope: ".console-events-v4",
  controls: name => name === "events-run" ? null : ".console-events-v4 :is(a[href], button, summary):visible",
  inspect: async (page, name) => {
    if (name === "events-run") {
      assert.equal(await page.locator(".console-event-list li").count(), 3);
      assert.equal(await page.locator("a[href], button, summary, input, select, textarea").count(), 0);
      await page.getByText("succeeded", { exact: true }).waitFor();
    } else {
      assert.equal(await page.locator("[data-events-exit-jobs]").getAttribute("href"), "/operate/observability");
      assert.equal(await page.locator(".console-event-list").count(), 0);
    }
  },
});
