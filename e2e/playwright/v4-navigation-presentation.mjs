import assert from "node:assert/strict";
import path from "node:path";
import AxeBuilder from "@axe-core/playwright";

export async function inspectV4NavigationPresentation(page, { name, width }, previews) {
  const v4 = name.endsWith("true");
  if (v4) {
    for (const [selector, size] of [["h1", width === 390 ? "22px" : "28px"], ["main > h2", "22px"], ["#services-heading", "16px"]]) {
      assert.equal(await page.locator(selector).evaluate(element => getComputedStyle(element).fontSize), size);
    }
  }
  const toggle = page.getByRole("button", { name: "Toggle navigation" });
  const dataMenu = page.locator("[data-navigation-menu='Data']");
  const dataSummary = dataMenu.locator("summary");
  assert.equal(await page.locator(".console-topbar-brand > span").count(), v4 ? 0 : 1);
  assert.equal(await page.getByRole("link", { name: "Help", exact: true }).getAttribute("href"), "/support");
  assert.equal(await page.locator("[data-workspace-header-target]").getAttribute("href"), "/environments");
  if (width === 390) {
    assert.ok(await toggle.isVisible());
    await toggle.focus();
    await page.keyboard.press("Enter");
    assert.equal(await toggle.getAttribute("aria-expanded"), "true");
  } else {
    assert.ok(!(await toggle.isVisible()));
  }
  if (v4) {
    for (const control of await page.locator(".console-conversation-toggle, .console-help-link, .console-menu-toggle:visible").all()) {
      const bounds = await control.boundingBox();
      assert.equal(bounds.width, 34);
      assert.equal(bounds.height, 34);
    }
  }
  await dataSummary.focus();
  await page.keyboard.press("Enter");
  await page.waitForFunction(() => document.activeElement?.matches("[data-navigation-menu='Data'] a"));
  assert.equal(await dataMenu.getAttribute("open"), "");
  await page.keyboard.press("Escape");
  assert.equal(await dataMenu.getAttribute("open"), null);
  assert.ok(await dataSummary.evaluate(element => element === document.activeElement));
  if (width === 390) {
    await page.keyboard.press("Escape");
    assert.equal(await toggle.getAttribute("aria-expanded"), "false");
    assert.ok(await toggle.evaluate(element => element === document.activeElement));
  }
  assert.deepEqual((await new AxeBuilder({ page }).analyze()).violations, []);
  await page.screenshot({ path: path.join(previews, `${name}-${width}-light.png`), fullPage: true });
  console.log(`${name}: ${width}px light, routes, mobile toggle, menu keyboard, axe and overflow pass`);
}
