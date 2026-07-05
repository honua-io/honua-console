import { test, expect } from '../admin-api';

// Live e2e for the AI honesty guarantees introduced in feat/console-ai-honesty:
//
//   1. OMNI-PROMPT CONFIRM CHIP — the omni-prompt page (/studio/ai, /operate/ai) NEVER silently
//      routes a prompt to the Studio or DevOps lane. Every verdict — including high-confidence
//      keyword matches — stops at a confirmable suggestion chip the user accepts with one click.
//      "Best guess · confirm or change?" for a high-confidence verdict; "Which lane?" for an
//      ambiguous one.
//
//   2. STUDIO-AI WORKING STATE + CANCEL — StudioAiConversation renders a "Honua is working…"
//      indicator and a Cancel button while the server is processing a turn. The server driver does
//      NOT stream partial output; the indicator covers the full latency window. The test
//      exercises the form-AI surface (/studio/form/ai) which is the simplest standalone
//      StudioAiConversation host.
//
// The "AI unavailable" path (no server binding / AI provider off) is covered by the
// missing-binding smoke (playwright.config.ts running the Console without HONUA_SERVER_BASE_URL);
// the live spec assumes a server is up and the form-AI providers endpoint is reachable.

const ADMIN_KEY = process.env.HONUA_CONSOLE_E2E_ADMIN_KEY ?? 'honua-console-dev-key';

// Blazor Server wires button onclick handlers over the SignalR circuit AFTER the button is in the
// DOM. The submit button is disabled until _prompt is non-empty (server-side state); calling
// fill() before the circuit is established loses the oninput event, so the button stays disabled
// and a subsequent click() is swallowed by the browser. This helper retries fill + toBeEnabled
// until the circuit processes the input, then the caller can safely click.
async function fillAndAwaitCircuit(
  page: import('@playwright/test').Page,
  text: string,
): Promise<void> {
  await expect(async () => {
    await page.locator('[data-omni-prompt-input]').fill(text);
    await expect(page.locator('[data-omni-prompt-submit]')).toBeEnabled({ timeout: 3_000 });
  }).toPass({ timeout: 30_000 });
}

test.describe('AI honesty · omni-prompt confirm chip (live)', () => {
  // The classifier is keyword-based and server-independent, so the confirm chip is deterministic:
  // no server state is required. The tests rely only on the Console being up.

  test('a Studio-keyword prompt shows "Best guess" chip, not the Studio lane surface', async ({ page }) => {
    test.setTimeout(60_000);
    await page.goto('/studio/ai');

    await fillAndAwaitCircuit(page, 'publish Maui parcels as a feature service');
    await page.locator('[data-omni-prompt-submit]').click();

    // The confirm chip MUST appear — no silent route.
    const chip = page.locator('[data-omni-prompt-confirm]');
    await expect(chip).toBeVisible({ timeout: 10_000 });
    // High-confidence copy says "Best guess".
    await expect(chip).toContainText(/Best guess/i);
    // Studio is the primary suggested button.
    await expect(chip.locator('[data-omni-confirm-studio]')).toBeVisible();
    // The DevOps override is also available.
    await expect(chip.locator('[data-omni-confirm-devops]')).toBeVisible();
    // The Studio lane content (DataToPublishFlow) has NOT rendered — no auto-route.
    await expect(page.locator('[data-data-publish-flow]')).toHaveCount(0);
  });

  test('a DevOps-keyword prompt shows "Best guess" chip, not the DevOps lane surface', async ({ page }) => {
    test.setTimeout(60_000);
    await page.goto('/operate/ai');

    await fillAndAwaitCircuit(page, 'roll back staging to the last good revision');
    await page.locator('[data-omni-prompt-submit]').click();

    const chip = page.locator('[data-omni-prompt-confirm]');
    await expect(chip).toBeVisible({ timeout: 10_000 });
    await expect(chip).toContainText(/Best guess/i);
    // DevOps is the primary suggested button for a DevOps-matching prompt.
    await expect(chip.locator('[data-omni-confirm-devops]')).toBeVisible();
    // The DevOps surface has NOT rendered.
    await expect(page.locator('[data-omni-devops-surface]')).toHaveCount(0);
  });

  test('an ambiguous prompt shows "Which lane?" chip with equal choices', async ({ page }) => {
    test.setTimeout(60_000);
    await page.goto('/studio/ai');

    await fillAndAwaitCircuit(page, 'help me with this');
    await page.locator('[data-omni-prompt-submit]').click();

    const chip = page.locator('[data-omni-prompt-confirm]');
    await expect(chip).toBeVisible({ timeout: 10_000 });
    await expect(chip).toContainText(/Which lane/i);
    await expect(chip.locator('[data-omni-confirm-studio]')).toBeVisible();
    await expect(chip.locator('[data-omni-confirm-devops]')).toBeVisible();
    // Neither lane surface has rendered.
    await expect(page.locator('[data-data-publish-flow]')).toHaveCount(0);
    await expect(page.locator('[data-omni-devops-surface]')).toHaveCount(0);
  });

  test('confirming the suggested lane routes to that lane surface', async ({ page }) => {
    test.setTimeout(60_000);
    await page.goto('/studio/ai');

    await fillAndAwaitCircuit(page, 'publish parcels as a feature service');
    await page.locator('[data-omni-prompt-submit]').click();

    await expect(page.locator('[data-omni-prompt-confirm]')).toBeVisible({ timeout: 10_000 });
    // One click to confirm the suggested lane.
    await page.locator('[data-omni-confirm-studio]').click();

    // After confirmation the routed status line appears and the lane surface renders.
    await expect(page.locator('[data-omni-prompt-routed]')).toBeVisible({ timeout: 10_000 });
    await expect(page.locator('[data-data-publish-flow]')).toBeVisible({ timeout: 10_000 });
  });
});

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
