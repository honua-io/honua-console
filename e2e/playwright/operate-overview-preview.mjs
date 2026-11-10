import assert from "node:assert/strict";
import path from "node:path";
import { runV4PagePresentation } from "./console-presentation.mjs";

await runV4PagePresentation({
  previews: path.resolve(process.env.HONUA_OPERATE_OVERVIEW_PREVIEW_DIR ?? "artifacts/operate-overview-preview"),
  pageNames: ["operate-overview-capabilities", "operate-overview-missing"],
  title: "Operate overview presentation fixture",
  stylesheet: "operate-overview-v4.css",
  heading: "Operate",
  scope: ".console-operate-overview-v4",
  controls: ".console-operate-overview-v4 a[href]:visible",
  async inspect(page, name) {
    await checkCardFocus(page);
    assert.match(await page.locator(".console-operate-overview-v4").textContent(), /Missing binding/);
    for (const route of ["connections", "resources", "services", "publishing", "settings", "releases"]) {
      assert.equal(await page.locator(`a[href='operate/${route}']`).count(), 1);
    }
    for (const route of ["temporal", "sync", "alerts/rules"]) {
      assert.equal(await page.locator(`a[href='operate/${route}']`).count(), name.endsWith("capabilities") ? 1 : 0);
    }
  },
});

async function checkCardFocus(page) {
  const cards = page.locator(".console-operate-overview-v4 .console-area-link");
  const shadows = await cards.evaluateAll(elements => elements.map(element => getComputedStyle(element).boxShadow));
  for (let index = 0; index < await cards.count(); index++) {
    const card = cards.nth(index);
    await card.focus();
    await page.keyboard.press("Shift+Tab");
    await page.keyboard.press("Tab");
    assert.ok(await card.evaluate(element => element.matches(":focus-visible")));
    assert.notEqual(await card.evaluate(element => getComputedStyle(element).boxShadow), shadows[index], "Keyboard focus must change the card's ordinary panel shadow");
  }
}
