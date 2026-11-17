import assert from 'node:assert/strict';
import fs from 'node:fs/promises';
import path from 'node:path';

// An existing authorized session is required. The probe permits only reads.
// --source-css previews repository CSS; omit it for installed acceptance.
const { chromium } = await import(process.env.HONUA_PLAYWRIGHT_MODULE ?? 'playwright');
const origin = process.env.HONUA_CONSOLE_URL ?? 'https://localhost:26083/console/';
const output = process.env.HONUA_NAV_FOCUS_OUTPUT ?? 'artifacts/navigation-focus-reflow';
assert.ok(process.env.HONUA_BROWSER_STATE, 'HONUA_BROWSER_STATE must identify an authorized session');
const sourceCss = process.argv.includes('--source-css')
  ? await fs.readFile(new URL('../../src/Honua.Console.Shell/wwwroot/css/v4-navigation.css', import.meta.url), 'utf8') : null;
await fs.mkdir(output, { recursive: true });
const browser = await chromium.launch({ headless: true, ...(process.env.HONUA_CHROMIUM_PATH
  ? { executablePath: process.env.HONUA_CHROMIUM_PATH, args: ['--no-sandbox'] } : {}) });
const results = [];
const errors = [];
const blockedWrites = [];
try {
  for (const v4 of [false, true]) for (const width of [390, 1280, 1440]) {
    const context = await browser.newContext({ ignoreHTTPSErrors: true,
      storageState: process.env.HONUA_BROWSER_STATE, viewport: { width, height: 900 } });
    const page = await context.newPage();
    page.on('pageerror', error => errors.push(error.message));
    await page.route('**/console/config.json', async route => {
      const response = await route.fetch();
      await route.fulfill({ response, json: { ...await response.json(), v4Enabled: v4 } });
    });
    await page.route('**/api/**', async route => {
      if (!['GET', 'HEAD', 'OPTIONS'].includes(route.request().method())) {
        blockedWrites.push(new URL(route.request().url()).pathname);
        return route.abort();
      }
      await route.continue();
    });
    try {
      await page.goto(new URL('operate/observability', origin).href);
      await page.getByRole('heading', { name: v4 ? 'Activity' : 'Operate Observability', exact: true, level: 1 }).waitFor();
      if (sourceCss) await page.addStyleTag({ content: sourceCss });
      for (const root of [16, 32]) for (const spacing of [false, true]) {
        const style = await page.addStyleTag({ content: `html { font-size:${root}px !important; }
          ${spacing ? '* { line-height:1.5 !important; letter-spacing:.12em !important; word-spacing:.16em !important; }' : ''}` });
        const row = { v4, width, root, spacing, links: [], escapeRestored: true };
        results.push(row);
        const toggle = page.locator('[data-navigation-toggle]');
        const compact = await toggle.isVisible();
        if (compact) { await toggle.focus(); await page.keyboard.press('Enter'); }
        for (const group of await page.locator('.console-topnav details').all()) {
          const summary = group.locator(':scope > summary');
          await summary.focus();
          await page.keyboard.press('Enter');
          const links = group.locator('a');
          await links.first().waitFor({ state: 'visible' });
          await page.waitForFunction(element => element === document.activeElement, await links.first().elementHandle());
          for (let i = 0; i < await links.count(); i++) {
            if (i > 0) await page.keyboard.press('Tab');
            const box = await links.nth(i).evaluate(element => {
              const r = element.getBoundingClientRect();
              return { name: element.textContent.trim(), top:r.top, bottom:r.bottom, left:r.left, right:r.right,
                focused: element === document.activeElement,
                visible:r.width > 0 && r.height > 0 && r.top >= -1 && r.bottom <= innerHeight + 1
                  && r.left >= -1 && r.right <= innerWidth + 1 };
            });
            row.links.push(box);
            if (root === 32 && width === 1280 && /Releases/.test(box.name)) {
              await page.screenshot({ path:path.join(output, `${v4}-${width}-${root}-${spacing}-releases-focus.png`) });
            }
          }
          await page.keyboard.press('Escape');
          row.escapeRestored &&= await summary.evaluate(element => element === document.activeElement && !element.parentElement.open);
        }
        if (compact) {
          await page.keyboard.press('Escape');
          row.escapeRestored &&= await toggle.evaluate(element => element === document.activeElement && element.getAttribute('aria-expanded') === 'false');
        }
        row.pass = row.links.length > 0 && row.links.every(link => link.focused && link.visible) && row.escapeRestored;
        await page.screenshot({ path:path.join(output, `${v4}-${width}-${root}-${spacing}.png`) });
        await style.evaluate(element => element.remove());
      }
    } finally { await context.close(); }
  }
} finally {
  await browser.close();
  await fs.writeFile(path.join(output, 'result.json'), JSON.stringify({ sourceCssPreview:Boolean(sourceCss), results, errors, blockedWrites }, null, 2));
}
console.log(`${results.filter(row => row.pass).length}/${results.length} navigation focus cases passed`);
assert.deepEqual(errors, []);
assert.deepEqual(blockedWrites, []);
assert.equal(results.length, 24);
assert.ok(results.every(row => row.pass), 'Every menu link must receive keyboard focus fully inside the viewport and Escape must restore focus');
