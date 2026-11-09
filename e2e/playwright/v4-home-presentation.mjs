import assert from "node:assert/strict";
import path from "node:path";
import AxeBuilder from "@axe-core/playwright";

export async function inspectV4HomePresentation(page, { name, width }, previews) {
  assert.equal(await page.locator("h1").count(), 1);
  assert.equal(await page.locator(".console-metric-tile").count(), name.endsWith("true") ? 1 : 0);
  if (name.endsWith("true")) {
    assert.equal(await page.locator(".console-metric-value").evaluate(element => getComputedStyle(element).fontSize), "32px");
    assert.equal(await page.locator(".console-page-heading-v4").evaluate(element => getComputedStyle(element).flexDirection), width === 390 ? "column" : "row");
  }
  await page.keyboard.press("Tab");
  assert.equal(await page.locator(":focus").getAttribute("href"), "/inbox");
  assert.deepEqual((await new AxeBuilder({ page }).analyze()).violations, []);
  await page.screenshot({ path: path.join(previews, `${name}-${width}-light.png`), fullPage: true });
  console.log(`${name}: ${width}px light, served styles, keyboard, overflow and axe pass`);
}
