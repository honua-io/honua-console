import assert from "node:assert/strict";
import path from "node:path";
import { runV4PagePresentation } from "./console-presentation.mjs";

const previews = path.resolve(process.env.HONUA_CATALOG_PREVIEW_DIR ?? "artifacts/data-esri");
const pages = {
  dashboard: "Import source dashboard",
  "instant-app": "Import source Instant App",
  notebook: "Import source notebook",
  "web-map": "Import source Web Map",
  storymap: "Import StoryMap / Hub site",
};
const titles = new Map(Object.entries(pages).flatMap(([slug, title]) => [[`data-esri-${slug}`, title], [`data-esri-${slug}-empty`, title]]));
titles.set("data-esri-wizard", "Import source content");
await runV4PagePresentation({
  previews, pageNames: [...titles.keys()],
  title: "Source content import presentation fixture", stylesheet: "v4-shell.css",
  heading: name => titles.get(name), scope: ".console-page",
  controls: () => ".console-page :is(a[href], input:not([type='radio']), input[type='radio']:checked, select, textarea, button, summary, [tabindex='0']):not(:disabled):visible",
  async inspect(page, name) {
    assert.equal(await page.locator(".console-page .console-panel .console-panel").count(), 0);
    if (name === "data-esri-wizard" || name.endsWith("-empty")) return;
    assert.equal(await page.locator("[data-esri-mapping].console-panel h2").count(), 1);
    assert.equal(await page.locator("[data-esri-target-preview] > .console-panel h2").count(), 1);
  },
});
