import assert from "node:assert/strict";
import path from "node:path";
import { runV4PagePresentation } from "./console-presentation.mjs";

await runV4PagePresentation({
  previews: path.resolve(process.env.HONUA_CATALOG_PREVIEW_DIR ?? "artifacts/catalog-preview"),
  pageNames: ["data-connections", "data-connection-detail", "data-connection-new", "data-connection-new-unsupported"],
  title: "Connections fixture",
  stylesheet: "v4-data-primitives.css",
  heading: (name) => name === "data-connections" ? "Data connections" : name === "data-connection-detail" ? "Production database" : "Add connection",
  scope: ".console-page",
  controls: ".console-page :is(a[href], button, input, select, textarea):not(:disabled):visible",
  inspect: async (page, name) => {
    if (name === "data-connections") {
      assert.equal(await page.getByRole("link", { name: "Production database" }).getAttribute("href"), "/operate/connections/connection%20a");
    } else if (name === "data-connection-detail") {
      assert.ok(await page.getByRole("button", { name: "Test connection" }).isVisible());
      assert.ok(await page.getByRole("heading", { name: "Overview", exact: true }).isVisible());
    } else {
      assert.equal(await page.locator("select").first().isDisabled(), name.endsWith("-unsupported"));
      assert.equal(await page.locator("select").first().locator("option").count(), name.endsWith("-unsupported") ? 0 : 1);
    }
  },
});
