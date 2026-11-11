import assert from "node:assert/strict";
import path from "node:path";
import { runV4PagePresentation } from "./console-presentation.mjs";

const titles = { environments: "Environments", "environment-new": "Add environment", "environment-detail": "Production", signin: "Sign In" };
await runV4PagePresentation({
  previews: path.resolve(process.env.HONUA_CATALOG_PREVIEW_DIR ?? "artifacts/catalog-preview"),
  pageNames: Object.keys(titles).flatMap(name => [`system-${name}-v4`, `system-${name}-legacy`]),
  title: "Environment and sign-in presentation fixture",
  stylesheet: "v4-shell.css",
  heading: name => name === "system-signin-v4" ? "Sign in" : titles[name.replace(/^system-/, "").replace(/-(v4|legacy)$/, "")],
  scope: "main",
  controls: "main :is(a[href], input, select, button, summary):not(:disabled):visible",
  prepareMarkup: markup => markup,
  inspect: async (page, name) => {
    assert.equal(await page.locator(".console-page-heading-v4").count(), name.endsWith("-v4") ? 1 : 0);
    if (name.includes("environment-new")) {
      assert.ok(await page.getByRole("button", { name: "Connect and continue" }).isDisabled());
      assert.ok(await page.getByRole("alert").isVisible());
    }
    if (name.includes("signin")) assert.ok(await page.getByRole("button", { name: "Continue", exact: true }).isVisible());
  },
});
