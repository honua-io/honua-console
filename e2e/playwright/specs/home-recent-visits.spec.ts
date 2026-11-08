import {test, expect, type Page} from '@playwright/test';
import {readFileSync} from 'node:fs';

const firstScope = 'a'.repeat(64);
const secondScope = 'b'.repeat(64);
const thirdScope = 'c'.repeat(64);

async function openRecentFixture(page: Page) {
  const recent = readFileSync(new URL('../../../src/Honua.Console.Shell/Components/ConsoleHomeRecentVisits.razor', import.meta.url), 'utf8');
  await page.route('**/recent-fixture', route => route.fulfill({contentType: 'text/html', body: `
    <html lang="en"><head><title>Recent visits fixture</title></head><body>
    <span hidden data-console-location="/operate/data" data-console-recent-label="Datasets"
      data-console-recent-scope="${firstScope}" data-console-recent-until="${Date.now() + 60000}"></span>
    <main id="console-main"><h1>Datasets</h1>${recent}</main></body></html>`}));
  await page.goto('/recent-fixture');
  await page.evaluate(async () => {
    const {mountRecentVisits} = await import('/js/operator-recent-visits.mjs');
    const lifetime = new AbortController();
    (window as any).recentFixture = {lifetime}; mountRecentVisits(lifetime.signal);
  });
}

async function visitHome(page: Page) {
  await page.evaluate(() => {
    document.querySelector('[data-console-location]')!.setAttribute('data-console-location', '/');
    document.querySelector('h1')!.textContent = 'Welcome';
  });
}

for (const width of [1440, 390]) test(`recent visits work without starting a conversation at ${width}px`, async ({page}) => {
  let workspaceRequests = 0;
  page.on('request', request => {if (request.url().includes('/api/agent-workspace/')) workspaceRequests++;});
  await page.setViewportSize({width, height: 900}); await openRecentFixture(page); await visitHome(page);
  await expect(page.locator('[data-recent-items] a')).toHaveText('Datasets');
  await expect(page.locator('[data-recent-items] a')).toHaveAttribute('href', '/operate/data');
  await page.locator('[data-home-recent]').evaluate(host => host.replaceWith(host.cloneNode(true)));
  await expect(page.locator('[data-recent-items] a')).toHaveText('Datasets');
  await expect(page.locator('[data-agent-workspace]')).toHaveCount(0);
  expect(workspaceRequests).toBe(0);
});

test('operator and environment scope changes isolate recent visits and restore only their own namespace', async ({page}) => {
  await openRecentFixture(page); await visitHome(page);
  await expect(page.locator('[data-recent-items] a')).toHaveCount(1);
  for (const scope of [secondScope, thirdScope]) {
    await page.locator('[data-console-location]').evaluate((marker, scope) => marker.setAttribute('data-console-recent-scope', scope), scope);
    await expect(page.locator('[data-recent-items]')).toBeHidden();
    await expect(page.locator('[data-recent-status]')).toBeVisible();
  }
  await page.locator('[data-console-location]').evaluate((marker, scope) => marker.setAttribute('data-console-recent-scope', scope), firstScope);
  await expect(page.locator('[data-recent-items] a')).toHaveText('Datasets');
});

test('pending scope validation hides history and an expiry without a validated scope cannot reveal it', async ({page}) => {
  await openRecentFixture(page); await visitHome(page);
  await page.locator('[data-console-location]').evaluate(marker => marker.removeAttribute('data-console-recent-scope'));
  await expect(page.locator('[data-recent-items]')).toBeHidden();
  await page.locator('[data-console-location]').evaluate(marker => marker.setAttribute('data-console-recent-until', String(Date.now() + 60000)));
  await expect(page.locator('[data-recent-items]')).toBeHidden();
  await expect(page.locator('[data-recent-items] a')).toHaveCount(0);
});

test('recent links disappear when the validated host scope snapshot expires', async ({page}) => {
  await openRecentFixture(page); await visitHome(page); await page.clock.install();
  await page.locator('[data-console-location]').evaluate(marker => marker.setAttribute('data-console-recent-until', String(Date.now() + 60000)));
  await expect(page.locator('[data-recent-items]')).toBeVisible();
  await page.clock.fastForward(60001);
  await expect(page.locator('[data-recent-items]')).toBeHidden();
});
