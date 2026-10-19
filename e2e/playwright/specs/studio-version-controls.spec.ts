import { expect, test, type Page } from '@playwright/test';

// The actual published controls with a controlled Studio lifecycle boundary.
// This verifies browser orchestration, not persistence or live GIS publication.
async function mount(page: Page) {
    await page.goto('/');
    await page.evaluate(async () => {
        const { mountStudioVersionControls } = await import('/js/studio-version-controls.mjs');
        const host = document.createElement('div');
        const app = document.createElement('div') as any;
        const status = document.createElement('p');
        status.setAttribute('role', 'status');
        status.dataset.versionTestStatus = '';
        host.append(app, status); document.body.append(host);
        const calls: string[] = [];
        app.workspaceCookieBinding = { principalId: 'human', environmentId: 'local' };
        app.saveWorkspaceVersion = async () => { calls.push('save'); return { versionNumber: 1 }; };
        app.listWorkspaceVersions = async () => {
            calls.push('list'); return [{ versionId: 'saved-version', versionNumber: 1 }];
        };
        const select = (draftId: string) => {
            app.workspaceDraftIdentity = { draftId };
            app.dispatchEvent(new CustomEvent('honua-workspace-draft-change'));
        };
        app.reopenWorkspaceVersion = async (id: string) => { calls.push(`reopen:${id}`); select('reopened'); };
        const controls = mountStudioVersionControls({ app, host, status });
        (window as any).versionFixture = { app, host, status, calls, controls, select };
    });
}

test('version actions require a draft and explicitly save, list and reopen it', async ({ page }) => {
    await mount(page);
    const save = page.getByRole('button', { name: 'Save version', exact: true });
    const browse = page.getByRole('button', { name: 'Saved versions', exact: true });
    await expect(save).toBeDisabled(); await expect(browse).toBeDisabled();
    await page.evaluate(() => (window as any).versionFixture.select('original'));
    await save.click();
    await expect(page.locator('[data-version-test-status]')).toHaveText('Saved version 1.');
    await browse.click();
    await page.getByRole('button', { name: 'Reopen version 1 as draft' }).click();
    await expect(page.getByRole('list', { name: 'Versions of this map' })).toBeEmpty();
    expect(await page.evaluate(() => (window as any).versionFixture.calls)).toEqual([
        'save', 'list', 'reopen:saved-version',
    ]);
    expect(await page.evaluate(() => (window as any).versionFixture.app.workspaceDraftIdentity.draftId)).toBe('reopened');
});

test('switching maps discards a delayed version list', async ({ page }) => {
    await mount(page);
    await page.evaluate(() => {
        const fixture = (window as any).versionFixture;
        fixture.select('original');
        fixture.app.listWorkspaceVersions = () => new Promise(resolve => { fixture.finish = resolve; });
    });
    const browse = page.getByRole('button', { name: 'Saved versions', exact: true });
    await browse.click(); await expect(browse).toBeDisabled();
    await page.evaluate(() => {
        const fixture = (window as any).versionFixture;
        fixture.select('another-map');
        fixture.finish([{ versionId: 'old-version', versionNumber: 9 }]);
    });
    await expect(browse).toBeEnabled();
    await expect(page.getByRole('list', { name: 'Versions of this map' })).toBeEmpty();
});

test('a pending save prevents duplicate submissions and is never retried on failure', async ({ page }) => {
    await mount(page);
    await page.evaluate(() => {
        const fixture = (window as any).versionFixture;
        fixture.select('original');
        fixture.app.saveWorkspaceVersion = () => {
            fixture.calls.push('save');
            return new Promise((_resolve, reject) => { fixture.fail = reject; });
        };
    });
    const save = page.getByRole('button', { name: 'Save version', exact: true });
    await save.click(); await expect(save).toBeDisabled();
    await save.dispatchEvent('click');
    await page.evaluate(() => (window as any).versionFixture.fail(new Error('Connection lost. Check saved versions.')));
    await expect(save).toBeEnabled();
    await expect(page.locator('[data-version-test-status]')).toHaveText('Connection lost. Check saved versions.');
    expect(await page.evaluate(() => (window as any).versionFixture.calls)).toEqual(['save']);
});

test('a version button cannot mutate after the operator authority changes', async ({ page }) => {
    await mount(page);
    await page.evaluate(() => (window as any).versionFixture.select('original'));
    await page.getByRole('button', { name: 'Saved versions', exact: true }).click();
    await expect(page.getByRole('button', { name: 'Reopen version 1 as draft' })).toBeVisible();
    await page.evaluate(() => {
        const fixture = (window as any).versionFixture;
        fixture.app.workspaceCookieBinding = { principalId: 'another-human', environmentId: 'local' };
    });
    await page.getByRole('button', { name: 'Reopen version 1 as draft' }).click();
    expect(await page.evaluate(() => (window as any).versionFixture.calls)).toEqual(['list']);
});

test('disposing the controls ignores a pending save and detached button clicks', async ({ page }) => {
    await mount(page);
    await page.evaluate(() => {
        const fixture = (window as any).versionFixture;
        fixture.select('original');
        fixture.app.saveWorkspaceVersion = () => {
            fixture.calls.push('save');
            return new Promise(resolve => { fixture.finish = resolve; });
        };
    });
    const save = page.getByRole('button', { name: 'Save version', exact: true });
    await save.click(); await expect(save).toBeDisabled();
    await page.evaluate(async () => {
        const fixture = (window as any).versionFixture;
        const button = fixture.host.querySelector('button');
        fixture.controls.dispose();
        fixture.finish({ versionNumber: 7 });
        await Promise.resolve(); await Promise.resolve();
        button.dispatchEvent(new MouseEvent('click'));
    });
    await expect(page.locator('[data-studio-versions]')).toHaveCount(0);
    await expect(page.locator('[data-version-test-status]')).toBeEmpty();
    expect(await page.evaluate(() => (window as any).versionFixture.calls)).toEqual(['save']);
});
