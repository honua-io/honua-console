import { test, expect } from '@playwright/test';

test('shell exposes route-aware document titles', async ({ page }) => {
  await page.goto('/studio/map', { waitUntil: 'domcontentloaded' });
  await expect(page).toHaveTitle('Map Builder · Studio | Honua Console');

  await page.goto('/operate/connections', { waitUntil: 'domcontentloaded' });
  await expect(page).toHaveTitle('Connections · Operate | Honua Console');

  await page.goto('/embed/maps/example', { waitUntil: 'domcontentloaded' });
  await expect(page).toHaveTitle('Embedded Map | Honua Console');
});

test('mobile navigation stays out of the content flow and is keyboard operable', async ({ page }) => {
  await page.setViewportSize({ width: 390, height: 844 });
  await page.goto('/operate', { waitUntil: 'domcontentloaded' });
  await page.waitForTimeout(1500);

  const menuButton = page.getByRole('button', { name: 'Open navigation menu' });
  const panel = page.locator('#console-navigation-panel');
  const heading = page.getByRole('heading', { name: 'Operate', exact: true }).first();

  await expect(menuButton).toBeVisible();
  await expect(menuButton).toHaveAttribute('aria-expanded', 'false');
  await expect(panel).toBeHidden();
  await expect(heading).toBeVisible();

  const initialLayout = await page.evaluate(() => ({
    clientWidth: document.documentElement.clientWidth,
    scrollWidth: document.documentElement.scrollWidth,
  }));
  expect(initialLayout.scrollWidth).toBeLessThanOrEqual(initialLayout.clientWidth);

  const headingBox = await heading.boundingBox();
  expect(headingBox?.y).toBeLessThan(180);

  await menuButton.click();
  await expect(page.getByRole('button', { name: 'Close navigation menu' })).toHaveAttribute(
    'aria-expanded',
    'true',
  );
  await expect(panel).toBeVisible();

  await panel.getByRole('link').first().focus();
  await page.keyboard.press('Escape');
  const reopenedMenuButton = page.getByRole('button', { name: 'Open navigation menu' });
  await expect(reopenedMenuButton).toHaveAttribute(
    'aria-expanded',
    'false',
  );
  await expect(reopenedMenuButton).toBeFocused();
  await expect(panel).toBeHidden();
  await expect(page.locator('.console-collapse-toggle')).toHaveCount(0);
});

test('desktop navigation collapse uses a real keyboard-operable button', async ({ page }) => {
  await page.goto('/', { waitUntil: 'domcontentloaded' });
  await page.waitForTimeout(1500);

  const collapseButton = page.getByRole('button', { name: 'Collapse navigation' });
  await collapseButton.focus();
  await page.keyboard.press('Enter');

  await expect(page.locator('.console-shell')).toHaveClass(/is-collapsed/);
  await expect(page.getByRole('button', { name: 'Expand navigation' })).toBeFocused();
});

test('connection error dismiss is a native button', async ({ page }) => {
  await page.goto('/', { waitUntil: 'domcontentloaded' });

  const dismissControl = page.locator('[data-blazor-error-dismiss]');
  await expect(dismissControl).toHaveJSProperty('tagName', 'BUTTON');
  await page.locator('#blazor-error-ui').evaluate(element => {
    (element as HTMLElement).style.display = 'block';
  });
  const dismiss = page.getByRole('button', { name: 'Dismiss connection error' });
  await expect(dismiss).toBeVisible();
  await dismiss.click();
  await expect(page.locator('#blazor-error-ui')).toBeHidden();
});

test('confirmation dialog traps focus, closes on Escape, and restores focus', async ({ page }) => {
  await page.goto('/', { waitUntil: 'domcontentloaded' });
  // Wait for interactive hydration so the element captured for focus restoration is the
  // live circuit-owned node rather than a prerender node that is about to be replaced.
  await page.waitForTimeout(1500);

  const trigger = page.getByRole('link', { name: 'Honua Console' });
  await trigger.focus();

  await page.evaluate(() => {
    const backdrop = document.createElement('div');
    backdrop.setAttribute('data-console-confirm', '');
    backdrop.innerHTML = `
      <div role="alertdialog" aria-modal="true" data-console-confirm-dialog tabindex="-1">
        <button type="button" data-console-confirm-cancel>Cancel</button>
        <button type="button">Delete</button>
      </div>`;
    backdrop.querySelector('[data-console-confirm-cancel]')?.addEventListener('click', () => {
      backdrop.remove();
    });
    document.body.append(backdrop);
  });

  const cancel = page.getByRole('button', { name: 'Cancel' });
  const accept = page.getByRole('button', { name: 'Delete' });
  await expect(cancel).toBeFocused();

  await page.keyboard.press('Shift+Tab');
  await expect(accept).toBeFocused();
  await page.keyboard.press('Tab');
  await expect(cancel).toBeFocused();

  await page.keyboard.press('Escape');
  await expect(page.locator('[data-console-confirm]')).toHaveCount(0);
  await expect(trigger).toBeFocused();
});
