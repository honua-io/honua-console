import assert from "node:assert/strict";
import path from "node:path";
import { runV4PagePresentation } from "./console-presentation.mjs";

await runV4PagePresentation({
  previews: path.resolve(process.env.HONUA_RELEASE_WITNESS_PREVIEW_DIR ?? "artifacts/release-witness-preview"),
  pageNames: ["release-witness-verified", "release-witness-denied"],
  title: "Release witness presentation fixture",
  stylesheet: "release-witness-v4.css",
  heading: "Publication evidence",
  scope: ".console-release-witness-v4",
  controls: ".console-release-witness-v4 :is(a[href], input, select, button, summary, [tabindex='0']):not(:disabled):visible",
  inspect: async (page, name) => {
    assert.equal(await page.locator("[data-release-witness]").count(), name === "release-witness-verified" ? 1 : 0);
    if (name === "release-witness-verified") {
      assert.equal(await page.locator("[data-release-public-link]").getAttribute("href"), "https://example.test/maps/publication-one");
    } else {
      await page.getByText("Evidence access is denied.", { exact: true }).waitFor();
      const diagnostics = page.locator("details > summary");
      await diagnostics.focus();
      await diagnostics.press("Enter");
      await page.getByText("request-denied-one", { exact: true }).waitFor();
      await diagnostics.press("Enter");
    }
  },
});
