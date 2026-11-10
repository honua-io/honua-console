import assert from "node:assert/strict";
import path from "node:path";
import { runV4PagePresentation } from "./console-presentation.mjs";

await runV4PagePresentation({
  previews: path.resolve(process.env.HONUA_CATALOG_PREVIEW_DIR ?? "artifacts/catalog-preview"),
  pageNames: ["data-connections", "data-connection-detail", "data-connection-new", "data-connection-new-unsupported", "data-connection-detail-tested", "data-connection-new-tested", "data-connection-new-error"],
  title: "Connections fixture",
  stylesheet: "v4-shell.css",
  heading: (name) => name === "data-connections" ? "Data connections" : name.startsWith("data-connection-detail") ? "Production database" : "Add connection",
  scope: ".console-page",
  controls: ".console-page :is(a[href], button, input, select, textarea):not(:disabled):visible",
  inspect: async (page, name) => {
    if (name.endsWith("-tested")) assert.ok(await page.locator(".console-panel[role=status]").isVisible());
    if (name.endsWith("-error")) assert.ok(await page.locator("[role=alert]").isVisible());
    if (name === "data-connections") {
      assert.equal(await page.getByRole("link", { name: "Production database" }).getAttribute("href"), "/console/operate/connections/connection%20a");
    } else if (name.startsWith("data-connection-detail")) {
      assert.ok(await page.getByRole("button", { name: "Test connection" }).isVisible());
      assert.ok(await page.getByRole("heading", { name: "Overview", exact: true }).isVisible());
    } else {
      assert.equal(await page.locator("select").first().isDisabled(), name.endsWith("-unsupported"));
      assert.equal(await page.locator("select").first().locator("option").count(), name.endsWith("-unsupported") ? 0 : 1);
    }
  },
});
