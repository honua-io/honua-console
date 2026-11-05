import {test, expect, type Page} from '@playwright/test';
import {readFileSync} from 'node:fs';
import {readConversationMarkup} from '../../../scripts/lib/conversation-markup.mjs';

async function openRecentFixture(page: Page) {
  const recent = readFileSync(new URL('../../../src/Honua.Console.Shell/Components/ConsoleHomeRecentVisits.razor', import.meta.url), 'utf8');
  await page.route('**/recent-fixture', route => route.fulfill({contentType: 'text/html', body: `
    <html lang="en"><head><title>Recent visits fixture</title></head><body>
    <span hidden data-console-location="/operate/data"></span>
    <main id="console-main"><h1>Data &amp; Layers</h1>${recent}</main>${readConversationMarkup()}</body></html>`}));
  await page.goto('/recent-fixture');
  await page.evaluate(async () => {
    const {mountWorkspacePreferences} = await import('/js/operator-preferences.mjs');
    const {mountRecentVisits} = await import('/js/operator-recent-visits.mjs');
    const lifetime = new AbortController();
    const state = (window as any).recentFixture = {reads: 0, pending: false, lifetime};
    state.workspace = {session: {authenticated: true, principalId: 'operator-one', environmentId: 'environment-one',
      releaseId: 'release-one', sourcePathBase: '', expiresAt: new Date(Date.now() + 60000).toISOString()},
      follow: true, authorityKey: () => JSON.stringify([state.workspace.session.principalId, state.workspace.session.environmentId]),
      api: async () => {state.reads++; if (state.pending) return new Promise(resolve => {state.resolve = resolve;}); return state.workspace.session;}};
    state.sync = mountWorkspacePreferences(state.workspace, document.querySelector('[data-agent-workspace]'),
      {signal: lifetime.signal, onPartitionChanged: mountRecentVisits(lifetime.signal)});
    state.sync();
  });
}

async function visitHome(page: Page) {
  await page.evaluate(() => {
    document.querySelector('[data-console-location]')!.setAttribute('data-console-location', '/');
    document.querySelector('h1')!.textContent = 'Welcome';
    window.dispatchEvent(new CustomEvent('honua-console-navigation'));
  });
}

for (const width of [1440, 390]) test(`recent visits survive Home rendering at ${width}px without extra session polling`, async ({page}) => {
  await page.setViewportSize({width, height: 900});
  await openRecentFixture(page);
  await visitHome(page);
  await expect(page.locator('[data-recent-items] a')).toHaveText('Data & Layers');
  await expect(page.locator('[data-recent-items] a')).toHaveAttribute('href', '/operate/data');
  await page.locator('[data-home-recent]').evaluate(host => host.replaceWith(host.cloneNode(true)));
  await expect(page.locator('[data-recent-items] a')).toHaveText('Data & Layers');
  expect(await page.evaluate(() => (window as any).recentFixture.reads)).toBe(1);
});

test('operator and environment changes hide prior recent visits while preserving the right namespace', async ({page}) => {
  await openRecentFixture(page); await visitHome(page);
  await expect(page.locator('[data-recent-items] a')).toHaveCount(1);
  for (const change of [{principalId: 'operator-two'}, {principalId: 'operator-one', environmentId: 'environment-two'}]) {
    await page.evaluate(change => {
      const state = (window as any).recentFixture; Object.assign(state.workspace.session, change); state.sync();
    }, change);
    await expect(page.locator('[data-recent-items]')).toBeHidden();
    await expect(page.locator('[data-recent-status]')).toBeVisible();
  }
  await page.evaluate(() => {
    const state = (window as any).recentFixture;
    Object.assign(state.workspace.session, {principalId: 'operator-one', environmentId: 'environment-one'}); state.sync();
  });
  await expect(page.locator('[data-recent-items] a')).toHaveText('Data & Layers');
});

test('pending validation hides links and a late prior-authority response cannot restore them', async ({page}) => {
  await openRecentFixture(page); await visitHome(page);
  await page.evaluate(() => {
    const state = (window as any).recentFixture; state.oldSession = {...state.workspace.session}; state.pending = true;
    window.dispatchEvent(new CustomEvent('honua-console-authority'));
  });
  await expect(page.locator('[data-recent-items]')).toBeHidden();
  await page.evaluate(() => {
    const state = (window as any).recentFixture;
    state.workspace.session = {...state.workspace.session, principalId: 'operator-two'}; state.sync();
    state.resolve(state.oldSession);
  });
  await expect(page.locator('[data-recent-items]')).toBeHidden();
  await expect(page.locator('[data-recent-items] a')).toHaveCount(0);
});

test('recent links disappear when the validated lease expires', async ({page}) => {
  await openRecentFixture(page); await visitHome(page);
  await page.clock.install();
  await page.evaluate(() => {
    const state = (window as any).recentFixture;
    state.workspace.session = {...state.workspace.session, expiresAt: new Date(Date.now() + 60000).toISOString()}; state.sync();
  });
  await expect(page.locator('[data-recent-items]')).toBeVisible();
  await page.clock.fastForward(60001);
  await expect(page.locator('[data-recent-items]')).toBeHidden();
});
