import assert from "node:assert/strict";
import path from "node:path";
import { runV4PagePresentation } from "./console-presentation.mjs";

const previews = path.resolve(process.env.HONUA_CATALOG_PREVIEW_DIR ?? "artifacts/data-runtime");
const areas = [
  { prefix: "scenes", heading: "Scenes", scope: "[data-scenes-page]", states: ["list", "detail", "missing", "ingest-success", "ingest-error"] },
  { prefix: "sensors", heading: "SensorThings", scope: "[data-sensorthings-page]", states: ["list", "detail", "missing"] },
  { prefix: "versions", heading: "Versions", scope: "[data-version-manager]", states: ["list", "conflicts", "alter", "missing"] },
];

const requestedAreas = process.env.HONUA_DATA_PREVIEW_AREAS?.split(",");
for (const { prefix, heading, scope, states } of areas.filter(area => !requestedAreas || requestedAreas.includes(area.prefix))) {
  await runV4PagePresentation({
    previews,
    pageNames: states.map(state => `${prefix}-${state}`),
    title: `${heading} presentation fixture`,
    stylesheet: "v4-shell.css",
    heading,
    scope,
    controls: name => name === "sensors-missing" ? null : `${scope} :is(a[href], input, select, button, summary, [tabindex='0']):not(:disabled):visible`,
    async inspect(page, name) {
      assert.ok(await page.locator(`${scope} .console-panel[aria-labelledby]`).count() > 0);
      for (const link of await page.locator(`${scope} a[href]`).all()) {
        assert.match(await link.getAttribute("href"), /^\/(console\/)?operate\//);
      }
      if (name.startsWith("scenes-ingest-")) assert.ok(await page.locator(`[data-pointcloud-${name.slice(7)}][role="status"]`).isVisible());
      if (name === "scenes-detail") assert.ok(await page.locator("[data-scene-viewer]").isVisible());
      if (name === "sensors-detail") assert.ok(await page.locator("[data-timeseries-line]").isVisible());
      if (name === "versions-conflicts") assert.ok(await page.locator("[data-open-conflicts]").isVisible());
      if (name.endsWith("missing")) assert.match(await page.locator(scope).textContent(), /unavailable|not bound/);
    },
  });
}
