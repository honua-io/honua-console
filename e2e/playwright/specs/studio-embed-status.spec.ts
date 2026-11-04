import { expect, test } from '@playwright/test';

async function openStudioEmbed(page: import('@playwright/test').Page) {
    await page.goto('/studio');
    await page.evaluate(async () => {
        if (!document.querySelector('[data-studio-embed]')) {
            const host = document.createElement('section');
            host.dataset.studioEmbed = '';
            host.innerHTML = '<p data-studio-status role="status"></p><div data-studio-mount></div>';
            document.body.append(host);
        }
        await import('/js/studio-embed.mjs');
    });
}

for (const status of [401, 403]) {
    test(`a ${status} workspace session asks the operator to sign in`, async ({ page }) => {
        await page.route('**/api/agent-workspace/session', route => route.fulfill({ status, body: '' }));

        await openStudioEmbed(page);

        await expect(page.locator('[data-studio-status]')).toHaveText('Sign in to Honua to open Studio.');
    });
}

for (const status of [429, 500, 503]) {
    test(`a ${status} workspace session reads as a load failure, not a sign-in prompt`, async ({ page }) => {
        await page.route('**/api/agent-workspace/session', route => route.fulfill({ status, body: '' }));

        await openStudioEmbed(page);

        await expect(page.locator('[data-studio-status]')).toContainText('Studio could not load.');
    });
}

test('a Studio load failure says what to do without naming internal paths', async ({ page }) => {
    await page.route('**/api/agent-workspace/session', route => route.abort('failed'));

    await openStudioEmbed(page);

    const status = page.locator('[data-studio-status]');
    await expect(status).toContainText('Studio could not load.');
    await expect(status).not.toContainText('/api/');
});
