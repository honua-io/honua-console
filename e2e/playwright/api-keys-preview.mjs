import assert from "node:assert/strict";
import path from "node:path";
import { runV4PagePresentation } from "./console-presentation.mjs";

await runV4PagePresentation({
  previews: path.resolve(process.env.HONUA_CATALOG_PREVIEW_DIR ?? "artifacts/catalog-preview"),
  pageNames: ["api-keys-empty", "api-keys-created", "api-keys-list", "api-keys-denied"],
  title: "API keys presentation fixture",
  stylesheet: "v4-shell.css",
  heading: "API keys",
  scope: "main",
  controls: "main a, main button:not([disabled]), main input:not([disabled]), main [tabindex='0']",
  inspect: async (page, name) => {
    assert.equal(await page.getByRole("heading", { name: "API keys", exact: true }).count(), 1);
    assert.equal(await page.locator("#api-key-secret").count(), name === "api-keys-created" ? 1 : 0);
    if (name === "api-keys-created") assert.equal(await page.locator("#api-key-secret").inputValue(), "one-time-secret");
    if (name === "api-keys-denied") assert.equal(await page.locator("form").count(), 0);
    if (name === "api-keys-list") assert.equal(await page.getByRole("columnheader").count(), 6);
  },
});
