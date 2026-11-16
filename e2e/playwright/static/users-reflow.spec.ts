import { readFile } from 'node:fs/promises';
import { expect, test, type Page, type TestInfo } from '@playwright/test';
import { populatedMembersFixture } from './populated-members-fixture';

async function checkInvitationControls(page: Page, mode: string) {
  const drawer = page.locator('[data-rbac-invite-drawer]');
  expect(await drawer.evaluate(element => {
    const bounds = element.getBoundingClientRect();
    return [...element.querySelectorAll('fieldset, input, select, button')].filter(control => {
      const box = control.getBoundingClientRect();
      return box.left < bounds.left - 1 || box.right > bounds.right + 1;
    }).map(control => control.outerHTML);
  }), `${mode}: invitation controls fit the drawer`).toEqual([]);
  expect(await drawer.locator('.rbac-invite-head, fieldset').evaluateAll(elements =>
    elements.filter(element => element.scrollWidth > element.clientWidth + 1).map(element => element.className)
  ), `${mode}: invitation text is not clipped`).toEqual([]);
}

async function checkUsersReflow(page: Page, context: { v4Enabled: boolean; testInfo: TestInfo }) {
  const region = page.getByRole('region', { name: 'Users', exact: true });
  for (const mode of ['normal', 'spacing', 'enlarged', 'combined']) {
    const style = await page.addStyleTag({ content: `
      html { font-size: ${mode === 'enlarged' || mode === 'combined' ? 32 : 16}px !important; }
      ${mode === 'spacing' || mode === 'combined' ? '* { line-height: 1.5 !important; letter-spacing: .12em !important; word-spacing: .16em !important; } p { margin-bottom: 2em !important; }' : ''}
    ` });
    await region.evaluate(element => { element.scrollLeft = 0; });
    await page.evaluate(() => window.scrollTo(0, 0));
    await expect.poll(() => page.evaluate(() => document.documentElement.scrollWidth))
      .toBeLessThanOrEqual(page.viewportSize()!.width);
    await checkInvitationControls(page, mode);
    await region.focus();
    await expect(region).toBeFocused();
    if (await region.evaluate(element => element.scrollWidth > element.clientWidth)) {
      await page.keyboard.press('ArrowRight');
      await expect.poll(() => region.evaluate(element => element.scrollLeft), 'keyboard scroll exposes user columns')
        .toBeGreaterThan(0);
      await expect.poll(() => page.evaluate(() => document.documentElement.scrollWidth))
        .toBeLessThanOrEqual(page.viewportSize()!.width);
    }
    await context.testInfo.attach(`users-reflow-${mode}-v4-${context.v4Enabled}`, {
      body: await page.screenshot({ fullPage: true }), contentType: 'image/png',
    });
    await style.evaluate(element => element.remove());
  }
}

for (const v4Enabled of [false, true]) {
  test(`Populated users remain inside the page with enlarged text (v4=${v4Enabled})`, async ({ page }, testInfo) => {
    const fixture = await populatedMembersFixture(v4Enabled);
    const errors: string[] = [];
    page.on('pageerror', error => errors.push(error.message));
    try {
      const address = fixture.server.address();
      if (!address || typeof address === 'string') throw new Error('Fixture did not bind TCP');
      await page.route('**/honua-console.css', async route => route.fulfill({
        contentType: 'text/css',
        body: await readFile(new URL('../../../src/Honua.Console.Shell/wwwroot/honua-console.css', import.meta.url), 'utf8'),
      }));
      await page.goto(`http://127.0.0.1:${address.port}/console/operate/access/members?workspace=first`);
      const region = page.getByRole('region', { name: 'Users', exact: true });
      await expect(region.locator('tbody')).toContainText('Synthetic operator');
      await page.evaluate(() => document.fonts.ready);
      await page.locator('[data-rbac-invite-open]').click();
      await expect(page.locator('[data-rbac-invite-drawer]')).toBeVisible();
      await expect(page.getByRole('region', { name: 'Pending invitations', exact: true })).toContainText('synthetic-invitation');
      await checkUsersReflow(page, { v4Enabled, testInfo });
      expect(fixture.requests, 'roster preview does not mutate users').toEqual([]);
      expect(errors).toEqual([]);
    } finally {
      await new Promise<void>((resolve, reject) => fixture.server.close(error => error ? reject(error) : resolve()));
    }
  });
}
