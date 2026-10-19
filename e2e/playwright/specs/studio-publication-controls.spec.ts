import { expect, test, type Page } from '@playwright/test';

// Actual browser controls over a controlled Studio API boundary. Live server
// authorization, publication and audience qualification use the installed lane.
async function mount(page: Page, clear = true, fail = false) {
    await page.goto('/');
    await page.evaluate(async ({ clear, fail }) => {
        const { mountStudioVersionControls } = await import('/js/studio-version-controls.mjs');
        const key = 'honua.workspace.studio-publication.publication-test';
        if (clear) sessionStorage.removeItem(key);
        const host = document.createElement('div'), status = document.createElement('p');
        const app = document.createElement('div') as any;
        status.dataset.publicationTestStatus = ''; status.setAttribute('role', 'status');
        host.append(app, status); document.body.append(host);
        const version = { itemId: 'item', versionId: 'version', contentHash: 'hash', versionNumber: 1, packageKey: 'parcels',
            envelope: { publicationIntent: { route: '/maps/parcels', visibility: 'organization' } },
            validation: { status: 'valid', diagnostics: [] } };
        const calls: any[] = [];
        app.workspace = { storage: sessionStorage, workspaceId: 'publication-test' };
        app.workspaceCookieBinding = { principalId: 'human', environmentId: 'local' };
        app.workspaceDraftIdentity = { draftId: 'draft' };
        app.listWorkspaceVersions = async () => [version];
        app.reviewWorkspacePublication = async () => structuredClone(version);
        app.publishWorkspaceVersion = async (review: any, input: any, command: string) => {
            calls.push({ versionId: review.versionId, input, command });
            if (fail) throw new Error('Response lost. The original submission is retained.');
            return { status: 'awaiting-approval', operationInstanceId: 'operation', proposalId: 'proposal' };
        };
        const controls = mountStudioVersionControls({ app, host, status });
        (window as any).publicationFixture = { app, calls, controls };
    }, { clear, fail });
}

test('reviewing a saved version does not publish; explicit submission shows pending approval', async ({ page }) => {
    await mount(page);
    await page.getByRole('button', { name: 'Saved versions', exact: true }).click();
    await page.getByRole('button', { name: 'Review publication of version 1', exact: true }).click();
    await expect(page.getByLabel('Publication route', { exact: true })).toHaveValue('/maps/parcels');
    await expect(page.getByLabel('Publication audience', { exact: true })).toHaveValue('organization');
    expect(await page.evaluate(() => (window as any).publicationFixture.calls)).toEqual([]);
    await page.getByRole('button', { name: 'Publish reviewed version', exact: true }).click();
    const panel = page.getByRole('region', { name: 'Map publication', exact: true });
    await expect(panel).toContainText('Awaiting separate human approval. Proposal proposal.');
    await expect(panel).not.toContainText('was published');
    await expect(page.getByRole('link', { name: 'Review approvals' })).toHaveAttribute('href', '/inbox');
    expect(await page.evaluate(() => (window as any).publicationFixture.calls.length)).toBe(1);
});

test('reload retains unknown submission and explicitly retries the original identity and audience', async ({ page }) => {
    await mount(page, true, true);
    await page.getByRole('button', { name: 'Saved versions', exact: true }).click();
    await page.getByRole('button', { name: 'Review publication of version 1', exact: true }).click();
    await page.getByRole('button', { name: 'Publish reviewed version', exact: true }).click();
    await expect(page.locator('[data-publication-test-status]')).toContainText('Response lost');
    const original = await page.evaluate(() => (window as any).publicationFixture.calls[0]);
    await mount(page, false);
    await expect(page.getByRole('region', { name: 'Map publication' })).toContainText('outcome is unknown');
    expect(await page.evaluate(() => (window as any).publicationFixture.calls)).toEqual([]);
    await page.getByRole('button', { name: 'Retry original submission', exact: true }).click();
    await expect(page.getByRole('region', { name: 'Map publication' })).toContainText('Awaiting separate human approval');
    expect(await page.evaluate(() => (window as any).publicationFixture.calls)).toEqual([original]);
});

test('a retained retry button cannot submit after the operator changes', async ({ page }) => {
    await mount(page);
    await page.getByRole('button', { name: 'Saved versions', exact: true }).click();
    await page.getByRole('button', { name: 'Review publication of version 1', exact: true }).click();
    await page.getByRole('button', { name: 'Publish reviewed version', exact: true }).click();
    await page.evaluate(() => {
        const app = (window as any).publicationFixture.app;
        app.workspaceCookieBinding = { principalId: 'other-human', environmentId: 'local' };
    });
    await page.getByRole('button', { name: 'Retry original submission', exact: true }).click();
    await expect(page.getByRole('region', { name: 'Map publication' })).toContainText('Restore the original map, account and environment');
    expect(await page.evaluate(() => (window as any).publicationFixture.calls.length)).toBe(1);
});
