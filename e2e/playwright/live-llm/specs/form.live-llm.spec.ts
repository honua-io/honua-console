import { test, expect, classifyGeneration, recordSurface, GATE_REASON, LIVE_LLM_ENABLED } from '../support/live-llm';

// FORM generate-from-prompt against a REAL provider (honua-console#283).
//
// FORM generation is behind the studio generation feature gate (the shared
// WorkflowGeneration section). The lane's stack enables it; we still probe the
// providers endpoint and skip cleanly if a given server does not expose it.
// Tolerant: prove a coherent generated form package came back, not its exact shape.

const FORM_GENERATE = '/api/v1/admin/forms/packages/generate';
const FORM_PROVIDERS = '/api/v1/console/form-packages/generation/providers';
const PROMPT =
  'An inspection form with a required "Asset name" text field, a "Condition" choice field ' +
  '(good / fair / poor), and a "Notes" multi-line text area.';

test.beforeEach(() => {
  test.skip(!LIVE_LLM_ENABLED, GATE_REASON);
});

test.describe('Live-LLM · FORM from prompt', () => {
  test('server generates a coherent form package from the prompt (API)', async ({ server }) => {
    // The admin form-generation endpoint is the real capability. The console-facing
    // providers probe (FORM_PROVIDERS) can 404 on an image that generates fine, so
    // gating on it would wrongly skip — we gate on the generate call itself, which
    // classifyGeneration turns into a clean skip only when the server truly reports
    // the surface unsupported (no provider).
    const outcome = await server.generate(FORM_GENERATE, PROMPT);
    classifyGeneration('form', outcome);
    expect(outcome.data, 'a generated form response body').toBeTruthy();
    expect(
      outcome.data.package ?? outcome.data.form ?? outcome.data.schema,
      'a generated form artifact',
    ).toBeTruthy();
  });

  test('console /studio/form/ai from-prompt surface generates a form', async ({ page, server }) => {
    // The console form-AI surface decides availability from the console-facing
    // providers endpoint, which some server images do not expose even though the
    // admin generate endpoint works. If it is absent, the console honestly shows
    // "AI generation unavailable" — skip cleanly rather than fail on a UI gap.
    const providers = await server.getJson(FORM_PROVIDERS);
    if (providers.status === 404 || providers.status === 501) {
      recordSurface('form-ui', 'unsupported', `console providers http ${providers.status}`);
      test.skip(true, 'console form-AI providers endpoint absent on this server image — clean skip.');
    }

    await page.goto('/studio/form/ai');

    // The AI conversation must be AVAILABLE (provider present) — the refine input
    // is enabled and no "unavailable" banner shows.
    const refine = page.locator('.studio-ai-refine-input, textarea').first();
    await expect(refine).toBeEnabled({ timeout: 30_000 });
    await expect(page.getByText(/AI generation (is )?unavailable/i)).toHaveCount(0);

    const turnsBefore = await page.locator('.studio-ai-turn-honua').count();
    await refine.fill(PROMPT);
    await page.getByRole('button', { name: /Send/ }).click();

    // Tolerant success: generation round-tripped — a new Honua turn appears, and
    // (best effort) a rendered form preview with real controls.
    await expect
      .poll(async () => await page.locator('.studio-ai-turn-honua').count(), {
        timeout: 180_000,
        intervals: [2000, 4000, 8000],
      })
      .toBeGreaterThan(turnsBefore);
    recordSurface('form-ui', 'generated');
  });
});
