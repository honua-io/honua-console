import { expect, test, type Page } from '@playwright/test';

// Actual browser controls over a controlled Studio API boundary. Live server
// authorization, publication and audience qualification use the installed lane.
async function mount(page: Page, clear = true, fail = false) {
    await page.goto('/');
    await page.evaluate(async ({ clear, fail }) => {
        const { mountStudioVersionControls } = await import('/js/studio-version-controls.mjs');
        const key = 'honua.workspace.studio-publication.publication-test';
        if (clear) for (const stored of Object.keys(sessionStorage)) {
            if (stored === key || stored.startsWith(key + '.') || stored.startsWith('honua.studio.publication.')) sessionStorage.removeItem(stored);
        }
        const host = document.createElement('div'), status = document.createElement('p');
        const app = document.createElement('div') as any;
        status.dataset.publicationTestStatus = ''; status.setAttribute('role', 'status');
        host.append(app, status); document.body.append(host);
        const version = { itemId: 'item', versionId: 'version', contentHash: 'hash', versionNumber: 1, packageKey: 'parcels',
            envelope: { publicationIntent: { route: '/maps/parcels', visibility: 'organization' } },
            validation: { status: 'valid', diagnostics: [] } };
        const calls: any[] = [];
        const reads: any[] = [];
        app.workspace = { storage: sessionStorage, workspaceId: 'publication-test' };
        app.workspaceCookieBinding = { principalId: 'human', environmentId: 'local' };
        app.workspaceDraftIdentity = { draftId: 'draft' };
        app.listWorkspaceVersions = async () => [version];
        app.reviewWorkspacePublication = async () => structuredClone(version);
        app.readWorkspacePublication = async (versionId: string, requestId: string) => {
            reads.push({ versionId, requestId });
            if (app.publicationReceipt) return app.publicationReceipt;
            throw Object.assign(new Error('Publication is not created yet.'), { status: 404 });
        };
        app.publishWorkspaceVersion = async (review: any, input: any, command: string) => {
            calls.push({ versionId: review.versionId, input, command });
            if (fail) throw new Error('Response lost. The original submission is retained.');
            return { status: 'awaiting-approval', operationInstanceId: 'operation', proposalId: 'proposal',
                requestId: 'request', itemId: 'item', versionId: 'version' };
        };
        const controls = mountStudioVersionControls({ app, host, status });
        (window as any).publicationFixture = { app, calls, reads, controls };
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
    await expect(page.getByRole('link', { name: 'Review approvals' })).toHaveAttribute('href', '/inbox?proposalId=proposal');
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

test('a retained refresh button cannot read or submit after the operator changes', async ({ page }) => {
    await mount(page);
    await page.getByRole('button', { name: 'Saved versions', exact: true }).click();
    await page.getByRole('button', { name: 'Review publication of version 1', exact: true }).click();
    await page.getByRole('button', { name: 'Publish reviewed version', exact: true }).click();
    await page.evaluate(() => {
        const app = (window as any).publicationFixture.app;
        app.workspaceCookieBinding = { principalId: 'other-human', environmentId: 'local' };
    });
    await page.getByRole('button', { name: 'Refresh publication status', exact: true }).click();
    await expect(page.locator('[data-publication-test-status]')).toContainText('Restore the original map, account and environment');
    await expect(page.getByRole('region', { name: 'Map publication' })).toBeHidden();
    expect(await page.evaluate(() => (window as any).publicationFixture.calls.length)).toBe(1);
    expect(await page.evaluate(() => (window as any).publicationFixture.reads.length)).toBe(0);
});

test('a pending publication does not block another operator and returns when its original operator reconnects', async ({ page }) => {
    await mount(page);
    const review = async () => {
        await page.getByRole('button', { name: 'Saved versions', exact: true }).click();
        await page.getByRole('button', { name: 'Review publication of version 1', exact: true }).click();
        await page.getByRole('button', { name: 'Publish reviewed version', exact: true }).click();
        await expect(page.getByRole('region', { name: 'Map publication' })).toContainText('Awaiting separate human approval');
    };
    await review();
    const original = await page.evaluate(() => (window as any).publicationFixture.calls[0]);
    await page.evaluate(() => {
        const app = (window as any).publicationFixture.app;
        app.workspaceCookieBinding = { principalId: 'other-human', environmentId: 'local' };
        app.dispatchEvent(new CustomEvent('honua-workspace-draft-change'));
    });
    await expect(page.getByRole('region', { name: 'Map publication' })).toBeHidden();
    await review();
    const calls = await page.evaluate(() => (window as any).publicationFixture.calls);
    expect(calls).toHaveLength(2);
    expect(calls[1].command).not.toBe(original.command);
    await page.evaluate(() => {
        const app = (window as any).publicationFixture.app;
        app.workspaceCookieBinding = { principalId: 'human', environmentId: 'local' };
        app.dispatchEvent(new CustomEvent('honua-workspace-draft-change'));
    });
    await expect(page.getByRole('region', { name: 'Map publication' })).toContainText('Awaiting separate human approval');
    expect(await page.evaluate(() => (window as any).publicationFixture.calls)).toEqual(calls);
});

test('reload refreshes a completed publication without another submission', async ({ page }) => {
    await mount(page);
    await page.getByRole('button', { name: 'Saved versions', exact: true }).click();
    await page.getByRole('button', { name: 'Review publication of version 1', exact: true }).click();
    await page.getByRole('button', { name: 'Publish reviewed version', exact: true }).click();
    await expect(page.getByRole('region', { name: 'Map publication' })).toContainText('Awaiting separate human approval');
    await mount(page, false);
    await page.evaluate(() => {
        (window as any).publicationFixture.app.publicationReceipt = {
            status: 'accepted', itemId: 'item', versionId: 'version', requestId: 'request',
        };
    });
    await page.getByRole('button', { name: 'Refresh publication status', exact: true }).click();
    await expect(page.getByRole('region', { name: 'Map publication' })).toContainText('The reviewed version was published.');
    expect(await page.evaluate(() => (window as any).publicationFixture.calls)).toEqual([]);
    expect(await page.evaluate(() => (window as any).publicationFixture.reads)).toEqual([{ versionId: 'version', requestId: 'request' }]);
    await expect(page.getByRole('button', { name: 'Acknowledge publication outcome', exact: true })).toBeVisible();
});

test('changing conversations on the same map retains the pending command and prevents a second publication', async ({ page }) => {
    await mount(page);
    await page.getByRole('button', { name: 'Saved versions', exact: true }).click();
    await page.getByRole('button', { name: 'Review publication of version 1', exact: true }).click();
    await page.getByRole('button', { name: 'Publish reviewed version', exact: true }).click();
    const calls = await page.evaluate(() => (window as any).publicationFixture.calls);
    await page.evaluate(() => {
        const app = (window as any).publicationFixture.app;
        app.workspace.workspaceId = 'another-conversation';
        app.dispatchEvent(new CustomEvent('honua-workspace-draft-change'));
    });
    await expect(page.getByRole('region', { name: 'Map publication' })).toContainText('Awaiting separate human approval');
    await page.getByRole('button', { name: 'Saved versions', exact: true }).click();
    await page.getByRole('button', { name: 'Review publication of version 1', exact: true }).click();
    await expect(page.locator('[data-publication-test-status]')).toContainText('Resolve the original publication');
    expect(await page.evaluate(() => (window as any).publicationFixture.calls)).toEqual(calls);
});

test('clearing map selection hides publication controls and restores the retained outcome on return', async ({ page }) => {
    await mount(page);
    await page.getByRole('button', { name: 'Saved versions', exact: true }).click();
    await page.getByRole('button', { name: 'Review publication of version 1', exact: true }).click();
    await page.getByRole('button', { name: 'Publish reviewed version', exact: true }).click();
    await page.evaluate(() => {
        const app = (window as any).publicationFixture.app;
        app.workspaceDraftIdentity = undefined;
        app.dispatchEvent(new CustomEvent('honua-workspace-draft-change'));
    });
    await expect(page.getByRole('region', { name: 'Map publication' })).toBeHidden();
    await page.evaluate(() => {
        const app = (window as any).publicationFixture.app;
        app.workspaceDraftIdentity = { draftId: 'draft' };
        app.dispatchEvent(new CustomEvent('honua-workspace-draft-change'));
    });
    await expect(page.getByRole('region', { name: 'Map publication' })).toContainText('Awaiting separate human approval');
    expect(await page.evaluate(() => (window as any).publicationFixture.calls)).toHaveLength(1);
});

test('read-only approval rejection is shown without claiming publication or sending another mutation', async ({ page }) => {
    let reads = 0;
    await page.route('**/api/processes/admin/proposals/proposal', async route => {
        expect(route.request().method()).toBe('GET');
        expect(route.request().headers()['x-honua-environment']).toBe('local');
        expect(route.request().headers()['x-honua-session-partition']).toBe('human');
        reads++;
        await route.fulfill({ json: { proposalId: 'proposal', status: 'Rejected' } });
    });
    await mount(page);
    await page.getByRole('button', { name: 'Saved versions', exact: true }).click();
    await page.getByRole('button', { name: 'Review publication of version 1', exact: true }).click();
    await page.getByRole('button', { name: 'Publish reviewed version', exact: true }).click();
    await page.getByRole('button', { name: 'Refresh publication status', exact: true }).click();
    const panel = page.getByRole('region', { name: 'Map publication' });
    await expect(panel).toContainText('A human approver rejected the publication.');
    await expect(panel).not.toContainText('was published');
    expect(reads).toBe(1);
    expect(await page.evaluate(() => (window as any).publicationFixture.calls.length)).toBe(1);
});
