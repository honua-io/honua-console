import assert from "node:assert/strict";
import path from "node:path";
import fs from "node:fs/promises";
import { chromium } from "@playwright/test";
import { startPresentationFixtureServer } from "./presentation-fixture-server.mjs";
import AxeBuilder from "@axe-core/playwright";

export const catalogKeyboardTargets = {
  controls: ".catalog-admin :is(input, select, button, textarea):not(:disabled):visible, .catalog-admin-tabs a:visible, .catalog-templates tbody a:visible",
  sizedControls: "input, select, button, textarea, .catalog-admin-tabs a, .catalog-templates tbody a",
  minimumHeight: 44
};
const sharedKeyboardTargets = {
  controls: "body :is(a[href], input, select, button, textarea, summary, [tabindex='0']):not(:disabled):visible",
  sizedControls: "input, select, button, textarea, summary, .console-button-link",
  minimumHeight: 24
};
const statusEdges = { info: "rgb(37, 99, 235)", warning: "rgb(180, 95, 6)", danger: "rgb(179, 38, 30)",
  neutral: "rgb(103, 115, 110)", success: "rgb(21, 128, 61)" };

export async function inspectCatalogCounts(page, name) {
  if (name !== "overview") return;
  const lead = page.locator('.catalog-admin-counts > [data-catalog-count="listed"]');
  assert.equal(await lead.count(), 1);
  assert.equal((await lead.locator("dt").textContent()).trim(), "Listed");
  const leadSize = await lead.locator("dd").evaluate(element => parseFloat(getComputedStyle(element).fontSize));
  const others = await page.locator('.catalog-admin-counts > div:not([data-catalog-count="listed"]) dd').evaluateAll(elements =>
    elements.map(element => parseFloat(getComputedStyle(element).fontSize)));
  assert.equal(others.length, 4);
  assert.ok(leadSize > Math.max(...others), "Listed must be the only large visibility count");
}

export async function inspectSharedPresentation(page, { name, width }, previews) {
  await checkKeyboardControls(page, sharedKeyboardTargets);
  await inspectPanelsAndTables(page);
  if (name === "navigation") await inspectNavigation(page, width);
  if (name === "home") await inspectHome(page);
  if (name.startsWith("service")) {
    await inspectProtocolPills(page);
    const variant = name === "service" ? "neutral" : name.slice("service-".length);
    await inspectNoticeSurface(page.locator("[data-service-config] > [role='status']"), statusEdges[variant]);
  }
  if (name === "share-warning") await inspectNoticeSurface(page.locator(".console-panel-warning"), statusEdges.warning);
  if (name.startsWith("state-")) await inspectStateSurface(page, { name, width }, previews);
  const accessibility = await new AxeBuilder({ page }).include(name === "navigation" ? ".console-topbar" : "main").analyze();
  assert.deepEqual(accessibility.violations, []);
  await page.locator(".console-table-wrap").evaluateAll(regions => regions.forEach(region => { region.scrollLeft = 0; }));
  await page.screenshot({ path: path.join(previews, `${name}-${width}-light.png`), fullPage: true });
  console.log(`${name}: ${width}px light, published panels/tables/notices/protocols, keyboard, overflow and axe pass`);
}

async function inspectHome(page) {
  const lead = page.locator("[data-home-awaiting-count] strong");
  assert.equal(await lead.count(), 1);
  const leadSize = await lead.evaluate(element => parseFloat(getComputedStyle(element).fontSize));
  const others = await page.locator("[data-home-total-count] strong, [data-summary-value]").evaluateAll(elements =>
    elements.map(element => parseFloat(getComputedStyle(element).fontSize)));
  assert.ok(others.length >= 4);
  assert.ok(leadSize > Math.max(...others), "Awaiting approval must be the only large home count");
  const headings = page.locator(".console-home-administration > h2, .console-home-recent > h2");
  assert.equal(await headings.count(), 2);
  for (const heading of await headings.all()) {
    assert.equal(await heading.evaluate(element => getComputedStyle(element).backgroundColor), "rgba(0, 0, 0, 0)");
    assert.equal(await heading.evaluate(element => getComputedStyle(element).borderBottomWidth), "0px");
  }
  await page.locator(".console-home-setup > summary").press("Enter");
  assert.ok(await page.locator(".console-home-setup[open]").isVisible());
  const cardHeadings = page.locator(".console-home-setup .console-start-option h2");
  assert.equal(await cardHeadings.count(), 2);
  for (const heading of await cardHeadings.all()) {
    assert.equal(await heading.evaluate(element => getComputedStyle(element).color), "rgb(23, 28, 26)");
  }
  await checkKeyboardControls(page, sharedKeyboardTargets);
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
  assert.equal(await pill.locator("strong").textContent(), "Exposed by honua-server");
  assert.ok(await pill.locator("small").evaluate(element => element.textContent.length > 100));
  assert.ok(await pill.evaluate(element => element.getBoundingClientRect().right <= innerWidth));
  const childrenFit = await pill.evaluate(element => {
    const bounds = element.getBoundingClientRect();
    return [...element.querySelectorAll("dt, dd, strong, small")].every(child => {
      const box = child.getBoundingClientRect();
      return box.left >= bounds.left && box.right <= bounds.right + .5;
    });
  });
  assert.ok(childrenFit, "Every protocol name, status and URL box must fit inside its pill");
}

async function inspectNoticeSurface(surface, edgeColor) {
  assert.equal(await surface.count(), 1);
  const normal = await surface.evaluate(element => {
    const style = getComputedStyle(element);
    return { edge: style.borderInlineStartWidth, background: style.backgroundColor, color: style.color, edgeColor: style.borderInlineStartColor };
  });
  assert.equal(normal.edgeColor, edgeColor);
  assert.equal(normal.edge, "3px");
  assert.equal(normal.color, "rgb(23, 28, 26)");
  assert.equal(normal.background, "rgb(255, 255, 255)");
}

async function inspectStateSurface(page, { name, width }, previews) {
  const state = page.locator(".console-state");
  const variant = name === "state-unsupported" ? "neutral" : name.slice("state-".length);
  await inspectNoticeSurface(state, statusEdges[variant]);
  const guidance = state.getByRole("link", { name: "Read connection guidance" });
  assert.equal(await guidance.evaluate(element => getComputedStyle(element).color), "rgb(17, 94, 89)");
  const action = state.getByRole("link", { name: "Review environment", exact: true });
  if (name === "state-unsupported") await inspectNestedNeutralState(state);
  await page.emulateMedia({ forcedColors: "active" });
  await action.focus();
  assert.ok(await state.evaluate(element => getComputedStyle(element).borderInlineStartStyle !== "none"));
  assert.ok(await action.evaluate(element => {
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

export async function checkKeyboardControls(page, { controls, sizedControls, minimumHeight }) {
  const targets = page.locator(controls);
  const total = await targets.count();
  assert.ok(total > 0);
  await targets.evaluateAll(elements => elements.forEach((element, index) => { element.dataset.previewControlIndex = String(index); }));
  const visited = new Set();
  for (let attempts = 0; attempts < total * 3 + 5 && visited.size < total; attempts++) {
    await page.keyboard.press("Tab");
    const focused = await page.evaluate(sizedControls => {
      const element = document.activeElement;
      if (!(element instanceof HTMLElement) || element.dataset.previewControlIndex === undefined) return null;
      const style = getComputedStyle(element);
      return { index: element.dataset.previewControlIndex, height: element.getBoundingClientRect().height,
        measureTarget: element.matches(sizedControls),
        focusVisible: element.matches(":focus-visible"),
        ring: (style.outlineStyle !== "none" && parseFloat(style.outlineWidth) > 0 && style.outlineColor !== "rgba(0, 0, 0, 0)") || style.boxShadow !== "none" };
    }, sizedControls);
    if (!focused) continue;
    if (focused.measureTarget) assert.ok(focused.height >= minimumHeight, `Control ${focused.index} is undersized`);
    assert.ok(focused.focusVisible && focused.ring, `Control ${focused.index} has no visible keyboard focus`);
    visited.add(focused.index);
  }
  assert.equal(visited.size, total, "Every enabled form, link and pager control must be keyboard reachable");
}

export async function runV4PagePresentation({ previews, pageNames, title, stylesheet, heading, scope, controls, inspect, allowScriptModules = false }) {
  const pages = new Map(await Promise.all(pageNames.map(async name =>
    [name, `<div class="console-shell" data-console-v4="true">${await fs.readFile(path.join(previews, `${name}.html`), "utf8")}</div>`])));
  const server = await startPresentationFixtureServer({
    pages, title, allowScriptModules,
    stylesheets: ["/_content/Honua.Console.Shell/honua-console.css"],
    assets: path.resolve("src/Honua.Console.Shell/wwwroot"),
    assetPrefix: "/_content/Honua.Console.Shell/",
  });
  let browser;
  try {
    browser = await chromium.launch();
    const context = await browser.newContext();
    const page = await context.newPage();
    for (const name of pages.keys()) {
      for (const width of [1280, 390]) {
        await inspectV4Presentation(page, {
          previews, name, width, origin: `http://127.0.0.1:${server.address().port}`,
          stylesheet, heading, scope, controls, inspect,
        });
      }
    }
  } finally {
    await browser?.close();
    await new Promise(resolve => server.close(resolve));
  }
}

async function inspectV4Presentation(page, { previews, name, width, origin, stylesheet, heading, scope, controls, inspect }) {
  await page.setViewportSize({ width, height: 844 });
  const response = page.waitForResponse(response => response.url().endsWith(`/${stylesheet}`));
  await page.goto(`${origin}/${name}`);
  assert.equal((await response).status(), 200);
  await page.evaluate(() => document.fonts.ready);
  assert.equal(await page.getByRole("heading", { level: 1 }).textContent(), typeof heading === "function" ? heading(name) : heading);
  assert.ok(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth), `${name}/${width} overflows`);
  await inspect(page, name);
  const pageControls = typeof controls === "function" ? controls(name) : controls;
  if (pageControls !== null) {
    await checkKeyboardControls(page, { controls: pageControls, sizedControls: "input, select, button, textarea, summary", minimumHeight: 24 });
  }
  const accessibility = await new AxeBuilder({ page }).include(scope).analyze();
  assert.deepEqual(accessibility.violations, []);
  await page.evaluate(() => {
    document.querySelectorAll("[role='region']").forEach(region => { region.scrollLeft = 0; });
    window.scrollTo(0, 0);
  });
  await page.screenshot({ path: path.join(previews, `${name}-${width}.png`), fullPage: true });
  console.log(`${name}/${width}: layout, evidence, keyboard focus and axe pass`);
}
