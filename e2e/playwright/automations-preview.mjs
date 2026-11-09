import assert from "node:assert/strict";
import path from "node:path";
import { runV4PagePresentation } from "./console-presentation.mjs";

await runV4PagePresentation({
  previews: path.resolve(process.env.HONUA_AUTOMATIONS_PREVIEW_DIR ?? "artifacts/automations-preview"),
  pageNames: ["automations-seeded", "automations-unbound", "automation-editor-seeded", "automation-editor-unbound", "automation-versions-seeded", "automation-versions-unbound"],
  title: "Automations presentation fixture",
  stylesheet: "studio-automations-v4.css",
  heading: name => name.startsWith("automation-editor") ? "Automation editor" : name.startsWith("automation-versions") ? "Automation versions" : "Automations",
  scope: ".console-automations-v4",
  controls: ".console-automations-v4 :is(a[href], input, select, button, textarea, summary, [tabindex='0']):not(:disabled):visible",
  inspect: async (page, name) => {
      if (name === "automations-seeded") {
        assert.ok(await page.getByRole("link", { name: "Permit intake automation", exact: true }).isVisible());
        assert.ok(await page.locator("tbody tr").count() > 0);
      } else if (name === "automations-unbound") {
        assert.ok(await page.getByRole("heading", { name: "Automation authoring is not bound", exact: true }).isVisible());
        assert.equal(await page.locator("tbody tr").count(), 0);
      }
      if (name.startsWith("automations-")) {
        assert.equal(await page.getByRole("link", { name: "New automation", exact: true }).getAttribute("href"), "/studio/automations/new");
      } else if (name === "automation-editor-seeded") {
        assert.ok(await page.getByRole("textbox", { name: "Automation name", exact: true }).isVisible());
        assert.ok(await page.getByRole("button", { name: "Save version", exact: true }).isVisible());
      } else if (name === "automation-versions-seeded") {
        assert.ok(await page.locator(".automation-version-current").isVisible());
        assert.ok(await page.getByRole("button", { name: "View body", exact: true }).first().isVisible());
      } else {
        assert.ok(await page.getByRole("link", { name: "Back to automations", exact: true }).isVisible());
      }
  },
});
