import assert from "node:assert/strict";
import path from "node:path";
import { checkKeyboardControls, runV4PagePresentation } from "./console-presentation.mjs";
const names = ["roles", "roles-create", "roles-unbound", "users", "users-invite", "users-unbound"];
await runV4PagePresentation({
  previews: path.resolve(process.env.HONUA_CATALOG_PREVIEW_DIR || "artifacts/catalog-preview"),
  pageNames: names.flatMap(name => [`access-${name}-v4`, `access-${name}-legacy`]),
  title: "Access rollout", stylesheet: "v4-access.css", prepareMarkup: markup => markup,
  heading: name => name.includes("roles") ? name.endsWith("v4") ? "Roles and permissions" : "Access · roles & permissions" : name.endsWith("legacy") && !name.includes("unbound") ? "Users · Public Works" : name.endsWith("v4") ? "Users" : "Users ",
  scope: "main", controls: null,
  inspect: async (page, name) => {
    const enabled = name.endsWith("v4");
    assert.equal(await page.locator(".console-page-heading-v4").count(), enabled ? 1 : 0);
    if (enabled) {
      assert.equal(await page.locator("h1").evaluate(element => getComputedStyle(element).fontSize), page.viewportSize().width < 640 ? "22px" : "28px");
      assert.equal(await page.locator(".console-metric-tile").count(), name.includes("unbound") ? 0 : 1);
      await checkKeyboardControls(page, { controls: "main :is(a[href],button,input,select,textarea):not(:disabled):not(input[type=radio]:not(:checked)):visible", sizedControls: "button,input,select,textarea", minimumHeight:24 });
    }
    if (name.includes("roles-create")) assert.equal(await page.locator("[data-rbac-create-form]").count(), 1);
    if (name.includes("users-invite")) {
      assert.equal(await page.locator("[data-rbac-invite-drawer]").count(), 1);
      const radios = page.locator("input[name=rbac-content-scope]");
      await radios.first().focus();
      await page.keyboard.press("ArrowRight");
      assert.equal(await radios.nth(1).isChecked(), true, "Radio group supports arrow-key navigation");
      await page.keyboard.press("ArrowLeft");
      assert.equal(await radios.first().isChecked(), true);
    }
  },
});
