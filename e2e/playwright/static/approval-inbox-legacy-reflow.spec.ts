import { expect, test, type Page } from '@playwright/test';
import { signInWithFixturePassword } from './fixture-sign-in';

const origin = 'http://127.0.0.1:5185';
const firstId = 'prop-long-unbroken-identifier-20261125-alpha-regional-parcel-serving-rebalance-0001';
const lastId = 'prop-long-unbroken-identifier-20261125-omega-tile-cache-invalidation-pipeline-9999';

const proposals = [
  {
    proposalId: firstId,
    kind: 'deploy',
    status: 'awaiting_approval',
    riskLevel: 'high',
    summary: 'UnbrokenSummaryIdentifierForFirstProposalRebalanceRegionalParcelServingPipeline20261125',
    requestedBy: 'honua-ops-agent-regional-cluster-node-01',
    createdAt: '2026-11-24T01:00:00Z',
    diff: ['update serving.regional_parcels_cache_policy_v20261125_long_unbroken_token_value'],
    dryRun: ['dry-run ok: 0 blocking findings'],
  },
  {
    proposalId: lastId,
    kind: 'config_change',
    status: 'awaiting_approval',
    riskLevel: 'medium',
    summary: 'UnbrokenSummaryIdentifierForLastProposalInvalidateVectorTileCachePartition20261125',
    requestedBy: 'honua-ops-agent-regional-cluster-node-02',
    createdAt: '2026-11-24T02:00:00Z',
    diff: ['invalidate tilecache.partition_20261125_long_unbroken_identifier_token'],
    dryRun: ['dry-run ok: 0 blocking findings'],
  },
];

async function mockLegacyShellConfig(page: Page) {
  await page.route(`${origin}/console/config.json`, async route => {
    const response = await route.fetch();
    await route.fulfill({ response, json: { ...await response.json(), v4Enabled: false } });
  });
}

async function mockV4ShellConfig(page: Page) {
  await page.route(`${origin}/console/config.json`, async route => {
    const response = await route.fetch();
    await route.fulfill({ response, json: { ...await response.json(), v4Enabled: true } });
  });
}

async function mockProposals(page: Page) {
  await page.route(`${origin}/api/v1/admin/proposals**`, async route => {
    const pathname = new URL(route.request().url()).pathname;
    const matched = proposals.find(item => pathname.endsWith(`/${item.proposalId}`));
    if (matched) {
      await route.fulfill({ json: matched });
      return;
    }
    await route.fulfill({ json: { proposals } });
  });
}

async function assertNoOverflow(page: Page, context: string) {
  const metrics = await page.evaluate(() => ({
    scrollWidth: document.documentElement.scrollWidth,
    innerWidth: window.innerWidth,
  }));
  expect(metrics.scrollWidth, `horizontal overflow (${context})`).toBeLessThanOrEqual(metrics.innerWidth);
}

async function checkSelectionAndClose(page: Page, proposalId: string, context: string) {
  const rowButton = page.locator(`button.approval-inbox-select[data-proposal-id="${proposalId}"]`);
  await rowButton.click();
  const panel = page.locator(`.proposal-approval-panel[data-proposal-id="${proposalId}"]`);
  await expect(panel).toBeVisible();
  await expect(panel.locator('#proposal-approval-reason')).toBeVisible();
  await assertNoOverflow(page, `${context} open ${proposalId}`);
  const closeButton = page.getByRole('button', { name: 'Close review', exact: true });
  await closeButton.click();
  await expect(panel).toHaveCount(0);
  await expect(rowButton).toBeFocused();
}

const shellModes: { name: string; configure: (page: Page) => Promise<void> }[] = [
  { name: 'legacy', configure: mockLegacyShellConfig },
  { name: 'v4', configure: mockV4ShellConfig },
];

for (const mode of shellModes) {
  for (const fontSize of [16, 32]) {
    test(`approvals inbox fits viewport in ${mode.name} mode at ${fontSize}px text`, async ({ page }, testInfo) => {
      const width = testInfo.project.name === 'mobile' ? 390 : 1280;
      await mode.configure(page);
      await mockProposals(page);
      await page.setViewportSize({ width, height: 900 });
      await signInWithFixturePassword(page, origin, '/inbox');
      await page.waitForURL(url => url.pathname === '/console/inbox');
      await expect(page.locator(`button.approval-inbox-select[data-proposal-id="${firstId}"]`)).toBeVisible();
      if (fontSize !== 16) {
        await page.addStyleTag({ content: `html { font-size: ${fontSize}px !important; }` });
      }
      const label = `${mode.name} ${width}px ${fontSize}px`;
      await assertNoOverflow(page, `${label} queue`);
      await checkSelectionAndClose(page, firstId, label);
      await checkSelectionAndClose(page, lastId, label);
    });
  }
}
