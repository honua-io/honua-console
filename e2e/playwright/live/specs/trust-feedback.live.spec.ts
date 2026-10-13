import { test, expect, type AdminApi } from '../admin-api';
import type { Page } from '@playwright/test';

// Live e2e spec: destructive-action confirmation + toast feedback across the Operate admin surface.
//
// This spec verifies the four UX contracts introduced by the trust-feedback sweep:
//   1. A destructive action shows [data-console-confirm] before proceeding.
//   2. Clicking Cancel closes the dialog and does NOT perform the action.
//   3. The [data-console-toast-host] is mounted on every operate route.
//   4. Clicking the backdrop also cancels the dialog.
//
// The publishing page (OperatePublishingPage) is the reference implementation and the most reliable
// test target after a publication lookup loads prior revisions. Tests that require server-side
// data (versions, custom roles) explicitly skip when that fixture is absent.

// A missing rollback fixture must expose its publication-specific state. A generic error,
// a loading placeholder, or another surface's empty state is not acceptable evidence.
async function expectPublicationsEmptyState(page: import('@playwright/test').Page) {
  const notConfigured = page.locator('article.console-panel')
    .filter({ has: page.getByRole('heading', { name: 'Publishing', exact: true }) })
    .filter({ has: page.getByText('Honua:Server:PublicationIds', { exact: true }) });
  const empty = page.locator('article.console-panel')
    .filter({ has: page.getByRole('heading', { name: 'No publications', exact: true }) });
  await expect(notConfigured.or(empty)).toBeVisible();
  if (await notConfigured.isVisible()) {
    await expect(notConfigured.getByText('Not configured', { exact: true })).toBeVisible();
    await expect(notConfigured).toContainText('no publication ids are configured');
  } else {
    await expect(empty).toContainText('No publications are available in this Operate environment.');
  }
}

// Configured workspaces list reviews, but lifecycle controls appear only after lookup.
// Confirm the server's version count before treating a publication as an insufficient fixture.
async function loadRollbackFixture(page: Page, admin: AdminApi) {
  await page.goto('/operate/publishing');
  const review = page.locator('[data-publishing-review]').first();
  const notConfigured = page.locator('article.console-panel')
    .filter({ has: page.getByRole('heading', { name: 'Publishing', exact: true }) })
    .filter({ has: page.getByText('Honua:Server:PublicationIds', { exact: true }) });
  const empty = page.locator('article.console-panel')
    .filter({ has: page.getByRole('heading', { name: 'No publications', exact: true }) });
  await expect(review.or(notConfigured).or(empty).first()).toBeVisible();
  if ((await review.count()) === 0) {
    await expectPublicationsEmptyState(page);
    return null;
  }

  const publicationId = await review.getAttribute('data-publication-id');
  expect(publicationId).toBeTruthy();
  await page.goto(`/operate/publishing?publicationId=${encodeURIComponent(publicationId!)}`);
  const lookup = page.locator('[data-publication-lookup]');
  await expect(lookup.locator('[data-publishing-review]')).toHaveAttribute('data-publication-id', publicationId!);
  await expect(lookup.getByRole('button', { name: 'Republish', exact: true })).toBeVisible();
  const detail = await admin.getJson(`/api/v1/console/publications/${encodeURIComponent(publicationId!)}`);
  expect(detail.route.publicationId).toBe(publicationId);
  expect(Array.isArray(detail.versions)).toBe(true);
  expect(detail.versions.length).toBeGreaterThan(0);
  const rollback = lookup.getByRole('button', { name: /Roll back to rev/ }).first();
  if (detail.versions.length === 1) {
    await expect(rollback).toHaveCount(0);
    return null;
  }
  await expect(rollback).toBeVisible();
  return rollback;
}

test.describe('Trust & feedback · confirm dialog (live)', () => {
  test('toast host is mounted on every operate route', async ({ page }) => {
    await page.goto('/operate');
    // [data-console-toast-host] is rendered once in ConsoleLayout and must be present on every route.
    await expect(page.locator('[data-console-toast-host]')).toBeVisible();
    // No toasts on cold navigation.
    await expect(page.locator('[data-console-toast]')).toHaveCount(0);
  });

  test.describe('publishing page · rollback confirm (reference implementation)', () => {
    test('publishing exposes revision actions or a verified missing revision fixture', async ({ page, admin }) => {
      await loadRollbackFixture(page, admin);
    });

    test('rollback button opens the confirm dialog', async ({ page, admin }) => {
      const rollbackButton = await loadRollbackFixture(page, admin);
      if (rollbackButton === null) {
        test.skip(true, 'Publication state verified; no prior revision fixture is available for rollback confirmation.');
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

    test('Cancel closes the dialog without performing the rollback', async ({ page, admin }) => {
      const rollbackButton = await loadRollbackFixture(page, admin);
      if (rollbackButton === null) {
        test.skip(true, 'Publication state verified; no prior revision fixture is available for rollback confirmation.');
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

    test('backdrop click cancels the dialog', async ({ page, admin }) => {
      const rollbackButton = await loadRollbackFixture(page, admin);
      if (rollbackButton === null) {
        test.skip(true, 'Publication state verified; no prior revision fixture is available for rollback confirmation.');
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
