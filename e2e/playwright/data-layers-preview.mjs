import assert from "node:assert/strict";
import path from "node:path";
import { runV4PagePresentation } from "./console-presentation.mjs";

await runV4PagePresentation({
  previews: path.resolve(process.env.HONUA_CATALOG_PREVIEW_DIR ?? "artifacts/catalog-preview"),
  pageNames: ["data-layers"],
  title: "Layers presentation fixture",
  stylesheet: "v4-shell.css",
  heading: "Layers",
  scope: ".console-page",
  controls: ".console-page a[href]:visible",
  inspect: async page => {
    const row = page.locator("[data-layer-id='42'][data-service-id='cadastre']");
    assert.equal(await row.locator(".console-name-identifier").evaluate(element => getComputedStyle(element).display), "grid");
    assert.equal(await row.locator("a[href='/console/operate/layers/connection-1-layer-42']").count(), 2);
    assert.equal(await row.locator("a[href='/console/operate/services/cadastre/settings']").count(), 1);
  },
});
