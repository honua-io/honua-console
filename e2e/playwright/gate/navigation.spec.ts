import { test, expect } from '@playwright/test';

test('installed Console navigation opens Services and Health with keyboard input', async ({ page }) => {
  const errors: string[] = [];
  page.on('pageerror', error => errors.push(error.message));
  const response = await page.goto('./');
  expect(response?.status()).toBe(200);
  // Headings also render for anonymous users. Check the server session without
  // returning claims, cookies or other operator details to the test reporter.
  const authenticated = await page.evaluate(async () => {
    const session = await fetch('/api/v1/admin/auth/session', {
      credentials: 'include', cache: 'no-store', redirect: 'error',
    });
    if (!session.ok) return false;
    try {
      return (await session.json()).isAuthenticated === true;
    } catch {
      return false;
    }
  });
  expect(authenticated, 'the installed server must accept the human operator session').toBe(true);
  await expect(page.locator('.console-shell')).toBeVisible();
  await expect(page.getByRole('heading', { level: 1 }).first()).toBeVisible();
  await page.waitForFunction(() => Array.from(
    document.querySelectorAll<HTMLLinkElement>('link[rel="stylesheet"]'),
  ).every(link => link.sheet !== null));

  // Use the deployed navigation, including its responsive disclosure. Do not
  // impose fixture-only capability menus or missing-binding copy on a live stack.
  for (const destination of [
    { group: 'Data', label: 'Services', heading: 'Services', path: 'operate/services' },
    { group: 'Operate', label: 'Health', heading: 'Operational health', path: 'operate/health' },
  ]) {
    const toggle = page.getByRole('button', { name: 'Toggle navigation', includeHidden: true });
    await expect(toggle).toBeAttached();
    if (await toggle.isVisible() && await toggle.getAttribute('aria-expanded') !== 'true') {
      await toggle.press('Enter');
      await expect(toggle).toHaveAttribute('aria-expanded', 'true');
    }
    const menu = page.locator(`[data-navigation-menu="${destination.group}"]`);
    await menu.locator(':scope > summary').press('Enter');
    const links = menu.getByRole('group', { name: destination.group, exact: true });
    await expect(links).toBeVisible();
    const target = links.getByRole('link', { name: destination.label, exact: true });
    await expect(target).toHaveAttribute('href', new RegExp(`/console/${destination.path}$`));
    await target.press('Enter');
    await expect(page).toHaveURL(new RegExp(`/console/${destination.path}$`));
    await expect(page.getByRole('heading', { level: 1, name: destination.heading, exact: true })).toBeVisible();
    await expect(page.getByRole('heading', { name: 'Page Not Found', exact: true })).toHaveCount(0);
    const width = page.viewportSize()!.width;
    expect(await page.evaluate(() => document.documentElement.scrollWidth)).toBeLessThanOrEqual(width);
  }
  expect(errors).toEqual([]);
});
