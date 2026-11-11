import assert from "node:assert/strict";
import path from "node:path";
import { checkKeyboardControls, runV4PagePresentation } from "./console-presentation.mjs";

const previews = path.resolve(process.env.HONUA_CATALOG_PREVIEW_DIR || "artifacts/catalog-preview");
await runV4PagePresentation({
  previews,
  pageNames: ["support-form-v4", "support-form-legacy", "support-ticket-v4", "support-ticket-legacy"],
  title: "Support rollout",
  stylesheet: "v4-support.css",
  prepareMarkup: markup => markup,
  heading: name => name.includes("ticket") ? "Ticket ticket-9" : "Contact support",
  scope: "main",
  controls: null,
  inspect: async (page, name) => {
    const enabled = name.endsWith("-v4");
    assert.equal(await page.locator(".console-page-heading-v4").count(), enabled ? 1 : 0);
    if (name.includes("form")) {
      assert.equal(await page.getByRole("combobox").count(), 2);
      assert.equal(await page.getByRole("button", { name: "Submit ticket" }).isDisabled(), true);
      assert.equal(await page.getByRole("complementary", { name: "Auto-attached context" }).count(), 1);
      await checkKeyboardControls(page, {
        controls: "main :is(input,select,textarea):not(:disabled)",
        sizedControls: "input:not([type=checkbox]),select,textarea", minimumHeight: 24,
      });
      if (enabled) {
        assert.equal(await page.locator(".support-context-row dt").first().evaluate(element => getComputedStyle(element).fontWeight), "400");
        assert.equal(await page.locator("textarea").evaluate(element => getComputedStyle(element).paddingTop), "12px");
        assert.equal(await page.locator(".studio-field-grid label > span").first().evaluate(element => getComputedStyle(element).fontWeight), "500");
      }
    } else {
      assert.ok((await page.locator("main").textContent()).includes("diagnose errors"));
      assert.deepEqual(await page.locator(".support-phase-badge").allTextContents(), enabled ? ["Resolved", "Read-only"] : ["resolved", "read-only"]);
      const links = "main a[href]:not([aria-disabled=true])";
      if (await page.locator(links).count()) {
        await checkKeyboardControls(page, { controls: links, sizedControls: "button", minimumHeight: 24 });
      }
    }
  },
});
