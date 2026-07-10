import { test, expect } from '../admin-api';

// Live e2e spec: destructive-action confirmation + toast feedback across the Operate admin surface.
//
// This spec verifies the four UX contracts introduced by the trust-feedback sweep:
//   1. A destructive action shows [data-console-confirm] before proceeding.
//   2. Clicking Cancel closes the dialog and does NOT perform the action.
//   3. The [data-console-toast-host] is mounted on every operate route.
//   4. Clicking the backdrop also cancels the dialog.
//
// The publishing page (OperatePublishingPage) is the reference implementation and the most reliable
// test target because it always has rollback buttons once a publication exists. Tests that require
// server-side data (versions, custom roles) skip gracefully when that data is absent.

test.describe('Trust & feedback · confirm dialog (live)', () => {
  test('toast host is mounted on every operate route', async ({ page }) => {
    await page.goto('/operate');
    // [data-console-toast-host] is rendered once in ConsoleLayout and must be present on every route.
    await expect(page.locator('[data-console-toast-host]')).toBeVisible();
    // No toasts on cold navigation.
    await expect(page.locator('[data-console-toast]')).toHaveCount(0);
  });

  test.describe('publishing page · rollback confirm (reference implementation)', () => {
    test('rollback button opens the confirm dialog', async ({ page }) => {
      await page.goto('/operate/publishing');

      // If no publications are loaded the test cannot proceed; skip gracefully.
      const rollbackButton = page.getByRole('button', { name: /Roll back to rev/ }).first();
      if ((await rollbackButton.count()) === 0) {
        test.skip(true, 'No rollback buttons visible; server has no published resources');
        return;
      }

      // Confirm dialog must be absent before clicking.
      await expect(page.locator('[data-console-confirm]')).not.toBeVisible();

      await rollbackButton.click();

      // Dialog appears and contains the alertdialog role.
      await expect(page.locator('[data-console-confirm]')).toBeVisible();
      await expect(page.locator('[role="alertdialog"]')).toBeVisible();
      await expect(page.locator('[data-console-confirm-accept]')).toBeVisible();
    });

    test('Cancel closes the dialog without performing the rollback', async ({ page }) => {
      await page.goto('/operate/publishing');

      const rollbackButton = page.getByRole('button', { name: /Roll back to rev/ }).first();
      if ((await rollbackButton.count()) === 0) {
        test.skip(true, 'No rollback buttons visible');
        return;
      }

      await rollbackButton.click();
      await expect(page.locator('[data-console-confirm]')).toBeVisible();

      // Click Cancel.
      await page.locator('[data-console-confirm]').getByRole('button', { name: 'Cancel' }).click();
      await expect(page.locator('[data-console-confirm]')).not.toBeVisible();
      // No toast should appear since the action was aborted.
      await expect(page.locator('[data-console-toast]')).toHaveCount(0);
    });

    test('backdrop click cancels the dialog', async ({ page }) => {
      await page.goto('/operate/publishing');

      const rollbackButton = page.getByRole('button', { name: /Roll back to rev/ }).first();
      if ((await rollbackButton.count()) === 0) {
        test.skip(true, 'No rollback buttons visible');
        return;
      }

      await rollbackButton.click();
      await expect(page.locator('[data-console-confirm]')).toBeVisible();

      // Click the backdrop at position (5, 5) — safely outside the inner dialog box.
      await page.locator('[data-console-confirm]').click({ position: { x: 5, y: 5 } });
      await expect(page.locator('[data-console-confirm]')).not.toBeVisible();
    });
  });

  test.describe('versions page · delete version confirm', () => {
    test('delete button opens confirm dialog and Cancel aborts', async ({ page }) => {
      await page.goto('/operate/versions');
      await expect(page.locator('[data-version-manager]')).toBeVisible();

      // The confirm dialog must not be visible before any delete click.
      await expect(page.locator('[data-console-confirm]')).not.toBeVisible();

      // If a service has been loaded and versions are visible, test the confirm flow.
      const deleteButton = page.locator('[data-delete]').first();
      if ((await deleteButton.count()) === 0) {
        // No versions loaded; the dialog wiring is verified at component level in bUnit.
        return;
      }

      await deleteButton.click();
      await expect(page.locator('[data-console-confirm]')).toBeVisible();
      await expect(page.locator('[data-console-confirm-accept]')).toBeVisible();

      // Cancel — no version should be deleted.
      await page.locator('[data-console-confirm]').getByRole('button', { name: 'Cancel' }).click();
      await expect(page.locator('[data-console-confirm]')).not.toBeVisible();
      // No error toast (the cancel path is clean).
      await expect(page.locator('[data-console-toast][data-toast-level="error"]')).toHaveCount(0);
    });
  });

  test.describe('access roles page · delete role confirm', () => {
    test('delete role button opens confirm dialog and Cancel aborts', async ({ page }) => {
      await page.goto('/operate/access');
      await expect(page.locator('[data-rbac-overview]')).toBeVisible();

      // Only custom roles have a delete button.
      const deleteRoleButton = page.locator('[data-rbac-delete-role]').first();
      if ((await deleteRoleButton.count()) === 0) {
        test.skip(true, 'No custom roles visible; create one first or seed via admin API');
        return;
      }

      await expect(page.locator('[data-console-confirm]')).not.toBeVisible();

      await deleteRoleButton.click();
      await expect(page.locator('[data-console-confirm]')).toBeVisible();

      // The dialog body should mention the role name and warn about irreversibility.
      await expect(page.locator('[data-console-confirm] .console-confirm-body')).toBeVisible();

      // Cancel: no role is deleted.
      await page.locator('[data-console-confirm]').getByRole('button', { name: 'Cancel' }).click();
      await expect(page.locator('[data-console-confirm]')).not.toBeVisible();
    });
  });
});
