import { expect, test } from '@playwright/test';

test('saved maps can be listed and a draft opened without crossing authority', async ({ page }) => {
    await page.goto('/');
    await page.evaluate(async () => {
        const { mountStudioSavedMaps } = await import('/js/studio-saved-maps.mjs');
        const host = document.createElement('div');
        const app = document.createElement('div') as any;
        const status = document.createElement('p');
        host.append(app, status); document.body.append(host);
        app.auth = {};
        app.workspaceCookieBinding = { principalId: 'customer', environmentId: 'install' };
        app.prepareWorkspaceTurn = async () => {};
        app.attachWorkspaceDraft = async (id: string) => { app.workspaceDraftIdentity = { draftId: id }; };
        const controls = mountStudioSavedMaps({ app, host, status });
        (window as any).savedMapFixture = { app, host, status, controls };
    });
    await page.getByRole('button', { name: 'Open saved maps' }).click();
    await page.evaluate(() => {
        const browser = document.querySelector('honua-studio-content-browser')!;
        browser.dispatchEvent(new CustomEvent('honua-studio-open-item', { detail: { draftId: 'draft-1', family: 'map', packageKey: 'city' } }));
    });
    await expect.poll(() => page.evaluate(() => (window as any).savedMapFixture.app.workspaceDraftIdentity?.draftId)).toBe('draft-1');
    await page.getByRole('button', { name: 'Open saved maps' }).click();
    await page.evaluate(() => {
        const fixture = (window as any).savedMapFixture;
        fixture.app.workspaceCookieBinding = { principalId: 'other', environmentId: 'install' };
        document.querySelector('honua-studio-content-browser')!.dispatchEvent(new CustomEvent('honua-studio-open-item', { detail: { draftId: 'other-draft', family: 'map', packageKey: 'city' } }));
    });
    expect(await page.evaluate(() => (window as any).savedMapFixture.app.workspaceDraftIdentity.draftId)).toBe('draft-1');
});

test('a saved version can be reopened and stale asynchronous attachment is refused', async ({ page }) => {
    await page.goto('/');
    await page.evaluate(async () => {
        const { mountStudioSavedMaps } = await import('/js/studio-saved-maps.mjs');
        const host = document.createElement('div'); const app = document.createElement('div') as any;
        const status = document.createElement('p'); host.append(app, status); document.body.append(host);
        app.auth = {}; app.workspaceCookieBinding = { principalId: 'customer', environmentId: 'install' };
        app.prepareWorkspaceTurn = async () => {};
        app.attachWorkspaceDraft = async (id: string) => { app.workspaceDraftIdentity = { draftId: id }; };
        mountStudioSavedMaps({ app, host, status }); (window as any).savedMapFixture = { app };
    });
    await page.getByRole('button', { name: 'Open saved maps' }).click();
    await page.evaluate(() => document.querySelector('honua-studio-content-browser')!.dispatchEvent(
        new CustomEvent('honua-studio-open-item', { detail: { itemId: 'item-1', family: 'map', packageKey: 'city' } })));
    await expect(page.locator('honua-studio-lifecycle-panel')).toHaveCount(1);
    await page.evaluate(() => document.querySelector('honua-studio-lifecycle-panel')!.dispatchEvent(
        new CustomEvent('honua-studio-lifecycle-activity', { detail: { kind: 'version-reopened', draftId: 'new-draft' } })));
    await expect.poll(() => page.evaluate(() => (window as any).savedMapFixture.app.workspaceDraftIdentity?.draftId)).toBe('new-draft');
    await page.getByRole('button', { name: 'Open saved maps' }).click();
    await page.evaluate(() => {
        const app = (window as any).savedMapFixture.app;
        app.prepareWorkspaceTurn = () => new Promise(resolve => { (window as any).finishPrepare = resolve; });
        document.querySelector('honua-studio-content-browser')!.dispatchEvent(new CustomEvent('honua-studio-open-item', {
            detail: { draftId: 'stale-draft', family: 'map', packageKey: 'city' },
        }));
        app.workspaceCookieBinding = { principalId: 'other', environmentId: 'install' };
        (window as any).finishPrepare();
    });
    await expect.poll(() => page.evaluate(() => (window as any).savedMapFixture.app.workspaceDraftIdentity.draftId)).toBe('new-draft');
});
