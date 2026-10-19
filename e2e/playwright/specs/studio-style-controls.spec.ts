import { expect, test, type Page } from '@playwright/test';

// Browser orchestration over the actual published Console controls. The Studio
// lifecycle is controlled here; persistence and Maputnik require the live lane.
async function mount(page: Page) {
    await page.goto('/');
    await page.evaluate(async () => {
        const { mountStudioStyleControls } = await import('/js/studio-style-controls.mjs');
        const host = document.createElement('div');
        const app = document.createElement('div') as any;
        app.attachShadow({ mode: 'open' });
        const status = document.createElement('p');
        status.dataset.styleTestStatus = '';
        host.append(app, status); document.body.append(host);
        const calls: string[] = [];
        app.workspaceCookieBinding = { principalId: 'human', environmentId: 'local' };
        app.openStyleEditor = async () => {
            calls.push('open');
            const panel = document.createElement('section');
            panel.setAttribute('aria-label', 'Map style editor');
            panel.innerHTML = '<select aria-label="Map layer for styling"><option>Parcels</option></select>';
            app.shadowRoot.append(panel);
        };
        app.closeStyleEditor = async () => {
            calls.push('close');
            if (app.dirty) throw new Error('Save or discard visual style edits before leaving.');
            app.shadowRoot.replaceChildren();
        };
        const select = (draftId: string) => {
            app.workspaceDraftIdentity = { draftId };
            app.dispatchEvent(new CustomEvent('honua-workspace-draft-change'));
        };
        const controls = mountStudioStyleControls({ app, host, status });
        (window as any).styleFixture = { app, host, status, calls, controls, select, mountStudioStyleControls };
    });
}

test('style editing requires a draft and opens and closes the existing Studio lifecycle', async ({ page }) => {
    await mount(page);
    const edit = page.getByRole('button', { name: 'Edit map style', exact: true });
    await expect(edit).toBeDisabled();
    await page.evaluate(() => (window as any).styleFixture.select('map'));
    await edit.click();
    await expect(page.getByRole('combobox', { name: 'Map layer for styling' })).toBeFocused();
    const close = page.getByRole('button', { name: 'Return to map', exact: true });
    await expect(close).toHaveAttribute('aria-pressed', 'true');
    await close.click();
    await expect(page.getByRole('region', { name: 'Map style editor' })).toHaveCount(0);
    await expect(edit).toHaveAttribute('aria-pressed', 'false');
    expect(await page.evaluate(() => (window as any).styleFixture.calls)).toEqual(['open', 'close']);
});

test('returning to the map preserves unsaved visual edits and reports Studio refusal', async ({ page }) => {
    await mount(page);
    await page.evaluate(() => (window as any).styleFixture.select('map'));
    await page.getByRole('button', { name: 'Edit map style', exact: true }).click();
    await page.evaluate(() => { (window as any).styleFixture.app.dirty = true; });
    await page.getByRole('button', { name: 'Return to map', exact: true }).click();
    await expect(page.locator('[data-style-test-status]')).toHaveText('Save or discard visual style edits before leaving.');
    await expect(page.getByRole('region', { name: 'Map style editor' })).toBeVisible();
    await expect(page.getByRole('button', { name: 'Return to map', exact: true })).toBeEnabled();
});

test('pending opens cannot be duplicated or write status after an authority change', async ({ page }) => {
    await mount(page);
    await page.evaluate(() => {
        const fixture = (window as any).styleFixture;
        fixture.select('map');
        fixture.app.openStyleEditor = () => {
            fixture.calls.push('open');
            return new Promise((_resolve, reject) => { fixture.fail = reject; });
        };
    });
    const edit = page.getByRole('button', { name: 'Edit map style', exact: true });
    await edit.click(); await expect(edit).toBeDisabled();
    await edit.dispatchEvent('click');
    await page.evaluate(() => {
        const fixture = (window as any).styleFixture;
        fixture.app.workspaceCookieBinding = { principalId: 'different-human', environmentId: 'local' };
        fixture.status.textContent = 'Current operator';
        fixture.fail(new Error('Old operator failure'));
    });
    await expect(edit).toBeEnabled();
    await expect(page.locator('[data-style-test-status]')).toHaveText('Current operator');
    expect(await page.evaluate(() => (window as any).styleFixture.calls)).toEqual(['open']);
});

test('remounted controls reflect the retained editor and disposed buttons cannot act', async ({ page }) => {
    await mount(page);
    await page.evaluate(() => (window as any).styleFixture.select('map'));
    await page.getByRole('button', { name: 'Edit map style', exact: true }).click();
    await page.evaluate(() => {
        const fixture = (window as any).styleFixture;
        const detached = fixture.host.querySelector('button');
        fixture.controls.dispose();
        detached.dispatchEvent(new MouseEvent('click'));
        fixture.controls = fixture.mountStudioStyleControls(fixture);
    });
    await expect(page.getByRole('button', { name: 'Return to map', exact: true })).toBeEnabled();
    expect(await page.evaluate(() => (window as any).styleFixture.calls)).toEqual(['open']);
});
