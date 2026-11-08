import assert from "node:assert/strict";
import path from "node:path";
import AxeBuilder from "@axe-core/playwright";

export async function inspectCatalogCounts(page, name) {
  if (name !== "overview") return;
  const sizes = await page.locator(".catalog-admin-counts dd").evaluateAll(elements =>
    elements.map(element => parseFloat(getComputedStyle(element).fontSize)));
  assert.equal(sizes.length, 5);
  assert.ok(sizes[0] > Math.max(...sizes.slice(1)), "Listed must be the only large visibility count");
}

export async function inspectSharedPresentation(page, { name, width }, previews) {
  await checkKeyboardControls(page, { scope: "main", minimumHeight: 24 });
  await inspectPanelsAndTables(page);
  if (name === "navigation") await inspectNavigation(page, width);
  if (name === "home") {
    const counts = await page.locator("[data-home-awaiting-count] strong, [data-home-total-count] strong, [data-summary-value]").evaluateAll(elements =>
      elements.map(element => parseFloat(getComputedStyle(element).fontSize)));
    assert.ok(counts.length >= 5);
    assert.ok(counts[0] > Math.max(...counts.slice(1)), "Awaiting approval must be the only large home count");
    const headings = page.locator(".console-home-administration > h2, .console-home-recent > h2");
    assert.equal(await headings.count(), 2);
    for (const heading of await headings.all()) {
      assert.equal(await heading.evaluate(element => getComputedStyle(element).backgroundColor), "rgba(0, 0, 0, 0)");
      assert.equal(await heading.evaluate(element => getComputedStyle(element).borderBottomWidth), "0px");
    }
  }
  if (name === "service") await inspectProtocolPills(page);
  if (name.startsWith("state-")) await inspectStateSurface(page, { name, width }, previews);
  const accessibility = await new AxeBuilder({ page }).include(name === "navigation" ? ".console-topbar" : "main").analyze();
  assert.deepEqual(accessibility.violations, []);
  await page.locator(".console-table-wrap").evaluateAll(regions => regions.forEach(region => { region.scrollLeft = 0; }));
  await page.screenshot({ path: path.join(previews, `${name}-${width}-light.png`), fullPage: true });
  console.log(`${name}: ${width}px light, published panels/tables/notices/protocols, keyboard, overflow and axe pass`);
}

async function inspectPanelsAndTables(page) {
  const panels = await page.locator(".console-panel:visible").evaluateAll(elements => elements.map(element => {
    const style = getComputedStyle(element);
    return { radius: style.borderRadius, shadow: style.boxShadow };
  }));
  for (const panel of panels) {
    assert.equal(panel.radius, "10px");
    assert.notEqual(panel.shadow, "none");
  }
  const headers = await page.locator(".console-table thead th:visible, .console-content-table thead th:visible").evaluateAll(elements =>
    elements.map(element => { const style = getComputedStyle(element); return [style.textTransform, style.backgroundColor]; }));
  for (const [casing, background] of headers) {
    assert.equal(casing, "none");
    assert.equal(background, "rgba(0, 0, 0, 0)");
  }
}

async function inspectNavigation(page, width) {
  const topbar = page.locator(".console-topbar");
  assert.equal(await topbar.evaluate(element => getComputedStyle(element).borderBottomWidth), "1px");
  assert.equal(await topbar.locator(".console-topbar-brand").evaluate(element => getComputedStyle(element).color), "rgb(23, 28, 26)");
  if (width < 1100) {
    assert.ok(await page.getByRole("button", { name: "Toggle navigation" }).isVisible());
    return;
  }
  await page.locator("[data-navigation-menu='Data'] > summary").press("Enter");
  const menu = page.locator("[data-navigation-menu='Data'] .console-navigation-items");
  assert.ok(await menu.isVisible());
  assert.notEqual(await menu.evaluate(element => getComputedStyle(element).boxShadow), "none");
  assert.equal(await menu.evaluate(element => getComputedStyle(element).backgroundColor), "rgb(255, 255, 255)");
}

async function inspectProtocolPills(page) {
  const pills = page.locator(".console-protocol-pill");
  assert.equal(await pills.count(), 1);
  const pill = pills.first();
  const label = await pill.locator("dt").boundingBox();
  const value = await pill.locator("dd").boundingBox();
  assert.ok(Math.abs(label.y - value.y) < 3, "Protocol name and reported values must share a line");
  assert.ok(await pill.locator("small").evaluate(element => element.textContent.length > 100));
  assert.ok(await pill.evaluate(element => element.getBoundingClientRect().right <= innerWidth));
}

async function inspectStateSurface(page, { name, width }, previews) {
  const state = page.locator(".console-state");
  const normal = await state.evaluate(element => {
    const style = getComputedStyle(element);
    return { edge: style.borderInlineStartWidth, background: style.backgroundColor, color: style.color, edgeColor: style.borderInlineStartColor };
  });
  const edges = { "state-info": "rgb(37, 99, 235)", "state-warning": "rgb(180, 95, 6)",
    "state-danger": "rgb(179, 38, 30)", "state-unsupported": "rgb(103, 115, 110)" };
  assert.equal(normal.edgeColor, edges[name]);
  assert.equal(normal.edge, "3px");
  assert.equal(normal.color, "rgb(23, 28, 26)");
  assert.equal(normal.background, "rgb(255, 255, 255)");
  if (name === "state-unsupported") await inspectNestedNeutralState(state);
  await page.emulateMedia({ forcedColors: "active" });
  await state.locator("a").focus();
  assert.ok(await state.evaluate(element => getComputedStyle(element).borderInlineStartStyle !== "none"));
  assert.ok(await state.locator("a").evaluate(element => {
    const style = getComputedStyle(element);
    return style.outlineStyle !== "none" && parseFloat(style.outlineWidth) > 0 && style.outlineColor !== "rgba(0, 0, 0, 0)";
  }));
  await page.screenshot({ path: path.join(previews, `${name}-${width}-forced-colors.png`), fullPage: true });
  await page.emulateMedia({ forcedColors: "none" });
}

async function inspectNestedNeutralState(state) {
  await state.evaluate(element => {
    const panel = document.createElement("section");
    panel.className = "console-panel console-state-warning";
    const content = document.createElement("div");
    element.before(panel);
    panel.append(content);
    content.append(element);
  });
  assert.equal(await state.evaluate(element => getComputedStyle(element).borderInlineStartColor),
    "rgb(103, 115, 110)", "An unsupported state must remain neutral inside a warning panel");
}

export async function checkKeyboardControls(page, { scope = ".catalog-admin", minimumHeight = 44 } = {}) {
  const selector = scope === ".catalog-admin"
    ? ".catalog-admin :is(input, select, button, textarea):not(:disabled):visible, .catalog-admin-tabs a:visible, .catalog-templates tbody a:visible"
    : `${scope} :is(a[href], input, select, button, textarea, summary, [tabindex='0']):not(:disabled):visible`;
  const controls = page.locator(selector);
  const total = await controls.count();
  assert.ok(total > 0);
  await controls.evaluateAll(elements => elements.forEach((element, index) => { element.dataset.previewControlIndex = String(index); }));
  const visited = new Set();
  for (let attempts = 0; attempts < total * 3 + 5 && visited.size < total; attempts++) {
    await page.keyboard.press("Tab");
    const focused = await page.evaluate(() => {
      const element = document.activeElement;
      if (!(element instanceof HTMLElement) || element.dataset.previewControlIndex === undefined) return null;
      const style = getComputedStyle(element);
      return { index: element.dataset.previewControlIndex, height: element.getBoundingClientRect().height,
        measureTarget: element.matches("input, select, button, textarea, summary, .console-button-link, .catalog-admin-tabs a, .catalog-templates tbody a"),
        focusVisible: element.matches(":focus-visible"),
        ring: (style.outlineStyle !== "none" && parseFloat(style.outlineWidth) > 0 && style.outlineColor !== "rgba(0, 0, 0, 0)") || style.boxShadow !== "none" };
    });
    if (!focused) continue;
    if (focused.measureTarget) assert.ok(focused.height >= minimumHeight, `Control ${focused.index} is undersized`);
    assert.ok(focused.focusVisible && focused.ring, `Control ${focused.index} has no visible keyboard focus`);
    visited.add(focused.index);
  }
  assert.equal(visited.size, total, "Every enabled form, link and pager control must be keyboard reachable");
}
