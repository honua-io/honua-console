import assert from 'node:assert/strict';
import fs from 'node:fs/promises';
import path from 'node:path';

// Runs against an installed Console without signing in or changing server state.
// --source-css is a diagnostic preview; omit it for installed acceptance.
const { chromium } = await import(process.env.HONUA_PLAYWRIGHT_MODULE ?? 'playwright');
const origin = process.env.HONUA_CONSOLE_URL ?? 'https://localhost:26083/console/';
const output = process.env.HONUA_SKIP_LINK_OUTPUT ?? 'artifacts/skip-link-reflow';
const sourceCss = process.argv.includes('--source-css')
  ? await fs.readFile(new URL('../../src/Honua.Console.Shell/wwwroot/css/shell.css', import.meta.url), 'utf8')
  : null;
await fs.mkdir(output, { recursive: true });
const browser = await chromium.launch({ headless: true, ...(process.env.HONUA_CHROMIUM_PATH
  ? { executablePath: process.env.HONUA_CHROMIUM_PATH, args: ['--no-sandbox'] } : {}) });
const results = [];
try {
  const viewports = [false, true].flatMap(v4 => [320, 390, 1280].map(width => ({ v4, width })));
  for (const viewport of viewports) await checkViewport(viewport);
} finally {
  await browser.close();
  await fs.writeFile(path.join(output, 'result.json'), JSON.stringify({ sourceCssPreview: Boolean(sourceCss), results }, null, 2));
}
console.log(`${results.filter(row => row.pass).length}/${results.length} skip-link cases passed`);
assert.equal(results.length, 24);
assert.ok(results.every(row => row.pass), 'unfocused link stays offscreen; focused link is visible and reaches main');

async function checkViewport({ v4, width }) {
  const context = await browser.newContext({ ignoreHTTPSErrors: true, viewport: { width, height: 900 } });
  const page = await context.newPage();
  const errors = [];
  page.on('pageerror', error => errors.push(error.message));
  await page.route('**/console/config.json', async route => {
    const response = await route.fetch();
    await route.fulfill({ response, json: { ...await response.json(), v4Enabled: v4 } });
  });
  await page.route('**/api/**', async route => {
    assert.equal(route.request().method(), 'GET', 'anonymous check must never write');
    await route.continue();
  });
  try {
    await page.goto(new URL('auth/signin', origin).href);
    const password = page.locator('input[type="password"]');
    await password.waitFor();
    if (sourceCss) await page.addStyleTag({ content: sourceCss });
    for (const mode of ['normal', 'spacing', 'enlarged', 'combined']) {
      await checkMode({ page, password, v4, width, mode });
    }
    assert.deepEqual(errors, []);
  } finally { await context.close(); }
}

async function checkMode({ page, password, v4, width, mode }) {
  const style = await page.addStyleTag({ content: `
    html { font-size: ${['enlarged', 'combined'].includes(mode) ? 32 : 16}px !important; }
    ${['spacing', 'combined'].includes(mode) ? '* { line-height:1.5 !important; letter-spacing:.12em !important; word-spacing:.16em !important; }' : ''}
  ` });
  await password.focus();
  const link = page.locator('.console-skip-link');
  const hidden = await link.evaluate(element => element.getBoundingClientRect().bottom <= 0);
  await link.focus();
  await page.evaluate(() => new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve))));
  const visibleFocus = element => {
    const r = element.getBoundingClientRect();
    return document.activeElement === element
      && document.elementFromPoint(r.left + r.width / 2, r.top + r.height / 2) === element
      && r.top >= 0 && r.bottom <= innerHeight && r.left >= 0 && r.right <= innerWidth;
  };
  const focused = await link.evaluate(visibleFocus);
  await page.screenshot({ path: path.join(output, `${v4}-${width}-${mode}.png`) });
  assert.equal(await link.evaluate(visibleFocus), true, 'skip link remains visibly focused after capture');
  await page.keyboard.press('Enter');
  const targetsMain = await page.evaluate(() => document.activeElement?.id === 'console-main');
  const hiddenAfterActivation = await link.evaluate(element => element.getBoundingClientRect().bottom <= 0);
  results.push({ v4, width, mode, hidden, focused, targetsMain, hiddenAfterActivation,
    pass: hidden && focused && targetsMain && hiddenAfterActivation });
  await style.evaluate(element => element.remove());
}
