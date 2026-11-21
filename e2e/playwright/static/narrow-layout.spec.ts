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

test('a long identity is truncated and wrapped header controls stay right-aligned', async ({ page }, testInfo) => {
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
      const overflow = document.documentElement.scrollWidth - document.documentElement.clientWidth;
      return { overflow, barRight: bar.right, identityWidth: identityBox.width, helpRight: help.right, rootFont: parseFloat(getComputedStyle(document.documentElement).fontSize) };
    });
    expect(header.identityWidth, `identity width at ${width}`).toBeLessThanOrEqual(12 * header.rootFont + 1);
    expect(header.overflow, `no sideways scroll at ${width}`).toBeLessThanOrEqual(0);
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
  const bar = () => page.evaluate(() => ({
    published: getComputedStyle(document.documentElement).getPropertyValue('--topbar-h').trim(),
    measured: `${Math.ceil(document.querySelector('.console-topbar')!.getBoundingClientRect().height)}px`,
  }));
  // The Operate approval count renders (and re-renders) after async reads and can wrap the bar at
  // 1440px, so compare the published value with the bar measured in the same poll, not a fixed 48px.
  await expect(page.locator('.console-approval-count')).toBeVisible();
  await expect.poll(async () => { const { published, measured } = await bar(); return published === measured; }).toBe(true);
  const before = parseInt((await bar()).measured);
  await page.evaluate(() => { document.querySelector<HTMLElement>('.console-topbar')!.style.paddingBlock = '20px'; });
  await expect.poll(async () => {
    const { published, measured } = await bar();
    return published === measured && parseInt(measured) > before;
  }).toBe(true);
});

for (const shell of ['legacy', 'v4'] as const) {
  for (const width of [1280, 390]) {
    test(`report inspector field rows keep usable controls at ${width}px in the ${shell} shell`, async ({ page }, testInfo) => {
      await page.setViewportSize({ width, height: 900 });
      await signIn(page, '/studio/report', shell);
      await page.getByRole('button', { name: 'New report', exact: true }).click();
      await page.getByRole('button', { name: '+ Panel', exact: true }).click();
      const inspector = page.locator('.studio-report-inspector-body');
      await expect(inspector.locator('.console-field-state')).toHaveCount(3);
      // A new panel has no binding alias, so its row carries an error list (the pill-escape case).
      await expect(inspector.locator('.console-field-state__errors')).toHaveCount(1);
      await inspector.locator('.console-field-state').last().scrollIntoViewIfNeeded();
      const rows = await inspector.evaluate(body => {
        const card = body.getBoundingClientRect();
        return [...body.querySelectorAll('.console-field-state')].map(row => {
          const control = row.querySelector('.console-field-state__value .console-input')!.getBoundingClientRect();
          const pill = row.querySelector('.console-field-state__pill')!.getBoundingClientRect();
          return {
            label: row.querySelector('.console-field-state__label-text')!.textContent,
            controlWidth: control.width,
            rowWidth: row.getBoundingClientRect().width,
            pillInside: pill.left >= card.left - 1 && pill.right <= card.right + 1,
          };
        });
      });
      for (const row of rows) {
        expect(row.controlWidth, `${row.label} control width`).toBeGreaterThanOrEqual(Math.min(160, row.rowWidth - 40));
        expect(row.pillInside, `${row.label} state pill inside the inspector`).toBe(true);
      }
      await testInfo.attach(`report-inspector-${width}`, { body: await inspector.screenshot(), contentType: 'image/png' });
    });
  }
}
