import { test, expect } from '../admin-api';

// Live e2e for the AI honesty guarantees introduced in feat/console-ai-honesty:
//
//   The omni-prompt confirm-chip cases were retired with the omni-prompt page itself: #432
//   replaced /operate/ai with the shared agent workspace, and the owner ruling on #450 retired
//   the confirm-before-route guarantee together with that page.
//
//   STUDIO-AI WORKING STATE + CANCEL — StudioAiConversation renders a "Honua is working…"
//      indicator and a Cancel button while the server is processing a turn. The server driver does
//      NOT stream partial output; the indicator covers the full latency window. The test
//      exercises the form-AI surface (/studio/form/ai) which is the simplest standalone
//      StudioAiConversation host.
//
// The "AI unavailable" path (no server binding / AI provider off) is covered by the
// missing-binding smoke (playwright.config.ts running the Console without HONUA_SERVER_BASE_URL);
// the live spec assumes a server is up and the form-AI providers endpoint is reachable.

const ADMIN_KEY = process.env.HONUA_CONSOLE_E2E_ADMIN_KEY ?? 'honua-console-dev-key';

test.describe('AI honesty · StudioAiConversation working state + cancel (live)', () => {
  test.beforeEach(async ({ page, admin }) => {
    // Verify the form-AI providers endpoint is reachable; skip if the server has no AI surface.
    const res = await page.request.get(
      `${admin.serverUrl}/api/v1/console/form-packages/generation/providers`,
      { headers: { 'X-API-Key': ADMIN_KEY } },
    );
    test.skip(
      res.status() === 404 || res.status() === 501,
      'This server has no form-AI generation surface — skip working-state + cancel tests.',
    );
  });

  test('working indicator appears while the server is processing and vanishes after the turn', async ({ page }) => {
    test.setTimeout(180_000);
    await page.goto('/studio/form/ai');

    // Wait for the AI conversation to initialise and AI to be available.
    const refine = page.locator('.studio-ai-refine-input');
    await expect(refine).toBeEnabled({ timeout: 30_000 });
    await expect(page.getByText(/AI generation (is )?unavailable/i)).toHaveCount(0);

    // Arm the listener BEFORE clicking Send so we race the brief working window.
    const workingAppeared = page.locator('[data-studio-ai-working]').waitFor({
      state: 'visible',
      timeout: 8_000,
    });

    await refine.fill('A simple inspection form with a name field and a condition field (good/fair/poor).');
    await page.getByRole('button', { name: /Send/ }).click();

    // The working indicator must appear while the server is processing.
    await workingAppeared;

    // The cancel button is visible and labelled while working.
    const cancelBtn = page.locator('[data-studio-ai-cancel]');
    await expect(cancelBtn).toBeVisible({ timeout: 5_000 });
    await expect(cancelBtn).toContainText(/Cancel/i);

    // After the server responds the indicator disappears and a Honua turn appears.
    await expect(page.locator('[data-studio-ai-working]')).not.toBeVisible({ timeout: 120_000 });
    const turnsBefore = 1; // the seed turn
    await expect
      .poll(async () => await page.locator('.studio-ai-turn-honua').count(), {
        timeout: 120_000,
        intervals: [1000, 2000, 5000],
      })
      .toBeGreaterThan(turnsBefore);
  });

  test('clicking Cancel while working stops the in-flight request', async ({ page }) => {
    test.setTimeout(180_000);
    await page.goto('/studio/form/ai');

    const refine = page.locator('.studio-ai-refine-input');
    await expect(refine).toBeEnabled({ timeout: 30_000 });
    await expect(page.getByText(/AI generation (is )?unavailable/i)).toHaveCount(0);

    const turnsBefore = await page.locator('.studio-ai-turn-honua').count();

    // Arm the Cancel listener before sending.
    const cancelVisible = page.locator('[data-studio-ai-cancel]').waitFor({
      state: 'visible',
      timeout: 8_000,
    });
    await refine.fill('Build me a complex form with dozens of fields for testing cancellation.');
    await page.getByRole('button', { name: /Send/ }).click();

    await cancelVisible;
    await page.locator('[data-studio-ai-cancel]').click();

    // After cancelling, the working indicator must disappear promptly.
    await expect(page.locator('[data-studio-ai-working]')).not.toBeVisible({ timeout: 10_000 });

    // A "cancelled" turn from HandleCancelAsync must appear, proving the cancel was handled.
    await expect
      .poll(async () => await page.locator('.studio-ai-turn-honua').count(), {
        timeout: 15_000,
        intervals: [500, 1000],
      })
      .toBeGreaterThan(turnsBefore);
    await expect(page.locator('.studio-ai-turn-honua').last()).toContainText(/cancel/i);

    // The textarea must still be ready for the next prompt.
    await expect(refine).toBeEnabled({ timeout: 5_000 });
  });
});
