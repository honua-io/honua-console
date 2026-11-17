import { expect, test, type Page } from '@playwright/test';
import { signInWithFixturePassword } from './fixture-sign-in';

const origin = 'http://127.0.0.1:5185';
// An identity provider subject is a common display name when the token carries no name claim.
const longIdentity = 'a42804c8-9091-70c5-4a25-4c0f297a29ba';

type Shell = 'v4' | 'legacy';

async function signIn(page: Page, returnTo: string, shell: Shell) {
  await page.route('**/console/config.json', async route => {
    const response = await route.fetch();
    await route.fulfill({ response, json: { ...await response.json(), v4Enabled: shell === 'v4' } });
  });
  await page.route('**/api/v1/admin/auth/session', async route => {
    const response = await route.fetch();
    if (response.status() !== 200) return route.fulfill({ response });
    const session = await response.json();
    session.claims = session.claims.map((claim: { type: string; value: string }) =>
      claim.type.endsWith('/claims/name') ? { ...claim, value: longIdentity } : claim);
    return route.fulfill({ response, json: session });
  });
  await signInWithFixturePassword(page, origin, returnTo);
  await page.waitForURL(url => url.pathname === `/console${returnTo}`);
}

test('a long identity stays on one header row and wrapped controls stay right-aligned', async ({ page }, testInfo) => {
  await page.setViewportSize({ width: 1440, height: 900 });
  await signIn(page, '/operate/connections', 'v4');
  const identity = page.locator('.console-account-identity');
  await expect(identity).toHaveText(longIdentity);
  for (const width of [1377, 1440, 1536]) {
    await page.setViewportSize({ width, height: 900 });
    const header = await page.evaluate(() => {
      const bar = document.querySelector<HTMLElement>('.console-topbar')!.getBoundingClientRect();
      const identityBox = document.querySelector('.console-account-identity')!.getBoundingClientRect();
      const help = document.querySelector('.console-help-link')!.getBoundingClientRect();
      return { height: bar.height, barRight: bar.right, identityWidth: identityBox.width, helpRight: help.right, rootFont: parseFloat(getComputedStyle(document.documentElement).fontSize) };
    });
    expect(header.identityWidth, `identity width at ${width}`).toBeLessThanOrEqual(12 * header.rootFont + 1);
    expect(header.height, `header rows at ${width}`).toBeLessThanOrEqual(49);
    expect(header.barRight - header.helpRight, `help stays at the trailing edge at ${width}`).toBeLessThan(40);
  }
  // When the bar still wraps (wide navigation), the wrapped controls hold the trailing edge.
  await page.setViewportSize({ width: 1440, height: 900 });
  await page.evaluate(() => { document.querySelector<HTMLElement>('.console-topnav')!.style.minWidth = '1100px'; });
  const wrapped = await page.evaluate(() => {
    const bar = document.querySelector<HTMLElement>('.console-topbar')!.getBoundingClientRect();
    const help = document.querySelector('.console-help-link')!.getBoundingClientRect();
    return { height: bar.height, barRight: bar.right, helpRight: help.right };
  });
  expect(wrapped.height, 'forced second row').toBeGreaterThan(49);
  expect(wrapped.barRight - wrapped.helpRight, 'help on a wrapped row').toBeLessThan(40);
  await testInfo.attach('header-long-identity', { body: await page.screenshot(), contentType: 'image/png' });
  // The truncated name stays available on hover.
  await expect(identity).toHaveAttribute('title', longIdentity);
});

test('sticky panes follow the measured top bar height', async ({ page }) => {
  await page.setViewportSize({ width: 1440, height: 900 });
  await signIn(page, '/operate/connections', 'v4');
  const published = () => page.evaluate(() => getComputedStyle(document.documentElement).getPropertyValue('--topbar-h').trim());
  await expect.poll(published).toBe('48px');
  await page.evaluate(() => { document.querySelector<HTMLElement>('.console-topbar')!.style.paddingBlock = '20px'; });
  const height = await page.evaluate(() => Math.ceil(document.querySelector('.console-topbar')!.getBoundingClientRect().height));
  expect(height).toBeGreaterThan(48);
  await expect.poll(published).toBe(`${height}px`);
});
