import { expect, test } from '@playwright/test';
import { pendingRoleFixture } from './pending-role-fixture';

for (const v4Enabled of [false, true]) {
  test(`role creation keeps its draft stable while pending (v4=${v4Enabled})`, async ({ page }, testInfo) => {
    const { server, requests, releaseCreate } = await pendingRoleFixture(v4Enabled);
    try {
      const address = server.address();
      if (!address || typeof address === 'string') throw new Error('Fixture did not bind TCP');
      await page.goto(`http://127.0.0.1:${address.port}/console/operate/access?environment=first`);
      const toggle = page.locator('[data-rbac-custom-role]');
      await toggle.click();
      const name = page.locator('[data-rbac-new-role-name]');
      await name.fill('Synthetic pending role');
      await page.locator('[data-rbac-create-form] button[type=submit]').click();
      await expect.poll(() => requests.length).toBe(1);
      await expect(toggle).toBeDisabled();
      await expect(name).toBeDisabled();
      await expect(page.locator('.rbac-create-grants input[type=checkbox]')).toBeDisabled();
      await testInfo.attach(`pending-role-v4-${v4Enabled}`, {
        body: await page.screenshot({ fullPage: true }), contentType: 'image/png',
      });
      releaseCreate();
      await expect(name).toBeEnabled();
      await expect(name).toHaveValue('Synthetic pending role');
      await expect(page.locator('[data-rbac-create-form]')).toContainText('503');
      expect(requests).toEqual(['/api/v1/console/access/first/roles']);
    } finally {
      releaseCreate();
      await new Promise<void>((resolve, reject) => server.close(error => error ? reject(error) : resolve()));
    }
  });
}
