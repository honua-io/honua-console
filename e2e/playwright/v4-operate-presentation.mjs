import assert from "node:assert/strict";
import path from "node:path";
import AxeBuilder from "@axe-core/playwright";

export async function inspectOperateSharedPresentation(page, { name, width }, previews) {
  assert.equal(await page.locator("h1").count(), 1);
  assert.equal(await page.locator("[data-timeline-row]").count(), 1);
  assert.deepEqual(await page.locator("th").allTextContents(), name.endsWith("true")
    ? ["Time", "Action", "Severity", "What", "Reference"]
    : ["Time", "Kind", "Severity", "Message", "Correlation"]);
  assert.ok(await page.getByRole("link", { name: "Published candidate map", exact: true }).isVisible());
  assert.ok(await page.locator("[data-proposal-approve]").isDisabled());
  if (name.endsWith("true")) {
    assert.equal(await page.locator(".proposal-approval-panel").evaluate(element => getComputedStyle(element).padding), "20px");
    for (const variant of ["is-data-import", "is-deliverable"]) {
      const shadow = await page.locator(".proposal-approval-panel").evaluate((element, className) => {
        element.classList.add(className);
        const value = getComputedStyle(element).boxShadow;
        element.classList.remove(className);
        return value;
      }, variant);
      assert.ok(shadow.includes("inset"), `${variant} retains its type accent`);
    }
  }
  await page.keyboard.press("Tab");
  assert.equal(await page.locator(":focus").getAttribute("aria-label"), "Operational events");
  await page.keyboard.press("Tab");
  assert.equal(await page.locator(":focus").getAttribute("href"), "/operate/events/event-1");
  assert.deepEqual((await new AxeBuilder({ page }).analyze()).violations, []);
  await page.screenshot({ path: path.join(previews, `${name}-${width}-light.png`), fullPage: true });
  console.log(`${name}: ${width}px light, event identity, approval evidence, keyboard, overflow and axe pass`);
}
