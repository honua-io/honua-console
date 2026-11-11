import assert from "node:assert/strict";
import fs from "node:fs/promises";
import path from "node:path";
import AxeBuilder from "@axe-core/playwright";
import { chromium } from "@playwright/test";
import { startPresentationFixtureServer } from "./presentation-fixture-server.mjs";

const previews = path.resolve(process.env.HONUA_CATALOG_PREVIEW_DIR ?? "artifacts/catalog-preview");
const markup = await fs.readFile(path.join(previews, "navigation-v4-true.html"), "utf8");
const legacy = await fs.readFile(path.join(previews, "navigation-v4-false.html"), "utf8");
const pages = new Map([["user-a", markup], ["user-b", markup], ["anonymous", markup], ["legacy", legacy]]);
const supportPages = ["support-form-v4", "support-ticket-v4"];
for (const name of supportPages) {
  const support = await fs.readFile(path.join(previews, `${name}.html`), "utf8");
  pages.set(name, `<main class="console-main">${support}</main>`);
}
const server = await startPresentationFixtureServer({
  pages, title: "Console theme presentation fixture", unwrappedPages: [...pages.keys()],
  stylesheets: ["/src/Honua.Console.Shell/wwwroot/honua-console.css"],
  headScripts: ["/src/Honua.Console.Web/wwwroot/js/console-theme.js"],
  assets: path.resolve("."),
});
let browser;
try {
  browser = await chromium.launch();
  const context = await browser.newContext();
  const page = await context.newPage();
  const origin = `http://127.0.0.1:${server.address().port}`;
  const configure = (target, name) => target.evaluate(name => window.honuaConsoleTheme.configure(name !== "legacy", name === "anonymous" ? null : `honua-theme:${name}`), name);
  const navigate = async (target, name) => {
    await target.goto(`${origin}/${name}`);
    await configure(target, name);
  };
  const theme = () => page.locator("html").getAttribute("data-theme");
  for (const width of [1280, 390]) {
    await page.setViewportSize({ width, height: 844 });
    await navigate(page, "user-a");
    await page.evaluate(() => { localStorage.clear(); });
    await page.reload();
    await configure(page, "user-a");
    assert.equal(await theme(), "light");
    for (const mode of ["light", "dark"]) {
      if (mode === "dark") {
        await page.getByRole("button", { name: "Switch to dark mode" }).focus();
        await page.keyboard.press("Enter");
      }
      assert.equal(await theme(), mode);
      assert.equal(await page.locator("[data-console-theme-toggle]").getAttribute("aria-pressed"), String(mode === "dark"));
      assert.equal(await page.locator("html").evaluate(element => getComputedStyle(element).colorScheme), mode);
      assert.equal(await page.locator("#services-heading").evaluate(element => getComputedStyle(element).color), mode === "dark" ? "rgb(230, 239, 238)" : "rgb(23, 28, 26)");
      assert.ok(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth), `${mode}/${width}: overflow`);
      const toggleBounds = await page.locator("[data-console-theme-toggle]").boundingBox();
      assert.ok(toggleBounds.width >= 24 && toggleBounds.height >= 24);
      assert.deepEqual((await new AxeBuilder({ page }).analyze()).violations, []);
      await page.screenshot({ path: path.join(previews, `theme-${mode}-${width}.png`), fullPage: true });
    }
    await page.reload();
    await configure(page, "user-a");
    assert.equal(await theme(), "dark", "saved preference survives reload");
    assert.equal(await page.getByRole("button", { name: "Switch to light mode" }).count(), 1);
    await navigate(page, "user-b");
    assert.equal(await theme(), "light", "another user does not inherit the choice");
    await navigate(page, "user-a");
    assert.equal(await theme(), "dark");
    await page.evaluate(() => {
      const current = document.querySelector('[data-console-theme-toggle]');
      const replacement = current.cloneNode(true);
      replacement.setAttribute("aria-pressed", "false");
      replacement.setAttribute("aria-label", "Switch to dark mode");
      current.replaceWith(replacement);
    });
    await page.getByRole("button", { name: "Switch to light mode" }).focus();
    await page.keyboard.press("Space");
    assert.equal(await theme(), "light", "replacement navigation remains operable");
    await navigate(page, "anonymous");
    await page.getByRole("button", { name: "Switch to dark mode" }).click();
    assert.equal(await theme(), "dark");
    await page.evaluate(() => history.pushState({}, "", "/anonymous-next"));
    await configure(page, "anonymous");
    assert.equal(await theme(), "dark", "anonymous choice survives in-app navigation");
    await navigate(page, "anonymous");
    assert.equal(await theme(), "light", "anonymous preferences remain in memory");
    await navigate(page, "legacy");
    await page.evaluate(() => localStorage.setItem("honua-theme:legacy", "dark"));
    await page.reload();
    await configure(page, "legacy");
    assert.equal(await theme(), "light");
    assert.equal(await page.locator("[data-console-theme-toggle]").count(), 0);
    for (const name of supportPages) {
      await navigate(page, name);
      for (const mode of ["light", "dark"]) {
        await page.evaluate(({ name, mode }) => localStorage.setItem(`honua-theme:${name}`, mode), { name, mode });
        await page.reload();
        await configure(page, name);
        assert.equal(await theme(), mode);
        assert.equal(await page.locator("html").evaluate(element => getComputedStyle(element).colorScheme), mode);
        assert.equal(await page.getByRole("heading", { level: 1 }).isVisible(), true);
        assert.ok(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth), `${name}/${mode}/${width}: overflow`);
        assert.deepEqual((await new AxeBuilder({ page }).include("main").analyze()).violations, []);
        await page.screenshot({ path: path.join(previews, `theme-${name}-${mode}-${width}.png`), fullPage: true });
      }
    }
    console.log(`${width}px: both modes, keyboard, reload, user isolation, replacement, flag, axe and overflow pass`);
  }
  const blocked = await browser.newContext();
  await blocked.addInitScript(() => Object.defineProperty(window, "localStorage", { get() { throw new Error("Storage unavailable"); } }));
  const blockedPage = await blocked.newPage();
  await navigate(blockedPage, "user-a");
  await blockedPage.getByRole("button", { name: "Switch to dark mode" }).click();
  assert.equal(await blockedPage.locator("html").getAttribute("data-theme"), "dark");
  await blockedPage.evaluate(() => history.pushState({}, "", "/user-a-next"));
  await configure(blockedPage, "user-a");
  assert.equal(await blockedPage.locator("html").getAttribute("data-theme"), "dark", "blocked-storage choice survives in-app navigation");
  await blocked.close();
  console.log("Blocked storage: in-memory toggle passes");
} finally {
  await browser?.close();
  await new Promise(resolve => server.close(resolve));
}
