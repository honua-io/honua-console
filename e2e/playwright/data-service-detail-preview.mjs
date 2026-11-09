import assert from "node:assert/strict";
import path from "node:path";
import { runV4PagePresentation } from "./console-presentation.mjs";

await runV4PagePresentation({
  previews: path.resolve(process.env.HONUA_CATALOG_PREVIEW_DIR ?? "artifacts/catalog-preview"),
  pageNames: ["data-service-detail", "data-service-detail-unbound", "data-service-detail-protocols", "data-service-detail-access", "data-service-detail-limits", "data-service-detail-success", "data-service-detail-danger"],
  title: "Service detail presentation fixture",
  allowScriptModules: true,
  stylesheet: "v4-data-primitives.css",
  heading: "Cadastre",
  scope: ".console-page",
  controls: ".console-page :is(a[href], button, input, select, summary):not(:disabled):not([tabindex='-1']):visible",
  inspect: async (page, name) => {
    assert.equal(await page.getByRole("tab").count(), 4);
    assert.equal(await page.locator("[role='tab'][aria-selected='true'][tabindex='0']").count(), 1);
    assert.equal(await page.locator("[role='tabpanel']:visible").count(), 1);
    const active = page.locator("[role='tab'][aria-selected='true']");
    const panelId = await active.getAttribute("aria-controls");
    assert.equal(await page.locator(`#${panelId}`).getAttribute("aria-labelledby"), await active.getAttribute("id"));
    assert.equal(await page.locator(".console-tabs").evaluate(element => getComputedStyle(element).display), "flex");
    // Exercise the production keyboard module against rendered tabs. Panel selection is covered by bUnit.
    await page.evaluate(async () => {
      const { attach } = await import("/_content/Honua.Console.Shell/console-tabs.js");
      attach(document.querySelector(".console-tabs"));
    });
    await active.focus();
    await page.keyboard.press("End");
    assert.equal(await page.evaluate(() => document.activeElement.id), "service-limits-tab");
    await page.keyboard.press("Home");
    assert.equal(await page.evaluate(() => document.activeElement.id), "service-layers-tab");
    await active.focus();
    if (name.endsWith("-access")) {
      assert.equal(await page.locator("[data-allowed-roles]").inputValue(), "surveyors");
      assert.equal(await page.locator("[data-save-access]:visible").count(), 1);
    } else if (name.endsWith("-protocols")) {
      assert.equal(await page.locator("[data-save-protocols]:visible").count(), 1);
    } else if (name.endsWith("-unbound")) {
      assert.equal(await page.locator("#service-protocols-panel .console-state-neutral:visible").count(), 1);
      assert.equal(await page.locator("[data-save-protocols]").count(), 0);
    } else if (name.endsWith("-limits")) {
      assert.equal(await page.locator("#service-limits-panel table:visible").count(), 1);
      assert.equal(await page.locator("[data-mapserver-unsupported]:visible").count(), 1);
    }
  },
});
