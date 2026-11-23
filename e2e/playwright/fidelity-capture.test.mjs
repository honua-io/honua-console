import assert from "node:assert/strict";
import fs from "node:fs/promises";
import os from "node:os";
import path from "node:path";
import { test } from "node:test";
import { chromium } from "@playwright/test";
import { captureFidelityThemes, presentationWidths } from "./fidelity-capture.mjs";
import { startPresentationFixtureServer } from "./presentation-fixture-server.mjs";

test("fidelity widths retain behavioral breakpoints and legacy coverage", () => {
  assert.deepEqual(presentationWidths("folder-v4"), [1440, 1280, 390]);
  assert.deepEqual(presentationWidths("scale-v4-expanded", [1280, 1050, 390]), [1440, 1280, 1050, 390]);
  assert.deepEqual(presentationWidths("folder-legacy"), [1280, 390]);
  for (const name of ["studio-styles-list-v4", "studio-styles-v4", "studio-styles-conflict-v4", "data-layers"]) {
    assert.deepEqual(presentationWidths(name), [1440, 1280, 390]);
  }
});

async function withFixture(inspect) {
  const previews = await fs.mkdtemp(path.join(os.tmpdir(), "console-fidelity-"));
  const server = await startPresentationFixtureServer({
    pages: new Map([["fixture", '<h1>Theme fixture</h1><p style="background:var(--surface);color:var(--text)">Visible content</p>']]),
    title: "Theme fixture", stylesheets: ["/css/tokens.css"],
    assets: path.resolve("src/Honua.Console.Shell/wwwroot"),
  });
  let browser;
  try {
    browser = await chromium.launch();
    const page = await browser.newPage({ viewport: { width: 390, height: 844 } });
    await page.goto(`http://127.0.0.1:${server.address().port}/fixture`);
    await inspect(page, previews);
  } finally {
    await browser?.close();
    await new Promise(resolve => server.close(resolve));
    await fs.rm(previews, { recursive: true, force: true });
  }
}

test("both themes render different images and original theme attributes are restored", async () => {
  await withFixture(async (page, previews) => {
    await page.evaluate(() => document.documentElement.setAttribute("data-theme", "light"));
    await captureFidelityThemes(page, { previews, name: "studio-styles-v4", width: 390 });
    const files = (await fs.readdir(previews)).sort();
    assert.deepEqual(files, ["studio-styles-v4-fidelity-390-dark.png", "studio-styles-v4-fidelity-390-light.png"]);
    const images = await Promise.all(files.map(file => fs.readFile(path.join(previews, file))));
    assert.notDeepEqual(images[0], images[1]);
    assert.equal(await page.locator("html").getAttribute("data-theme"), "light");
    assert.equal(await page.locator("html").getAttribute("data-console-theme-enabled"), null);
  });
});

test("a dark-only overflow fails and still restores the original theme", async () => {
  await withFixture(async (page, previews) => {
    await page.addStyleTag({ content: ':root[data-theme="dark"] body { min-width: 800px; }' });
    await assert.rejects(captureFidelityThemes(page, { previews, name: "folder-v4", width: 390 }), /horizontal page overflow/);
    assert.equal(await page.locator("html").getAttribute("data-theme"), null);
    assert.equal(await page.locator("html").getAttribute("data-console-theme-enabled"), null);
  });
});
