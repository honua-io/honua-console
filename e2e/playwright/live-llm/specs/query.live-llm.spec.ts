import { test, expect, classifyGeneration, recordSurface, GATE_REASON, LIVE_LLM_ENABLED } from '../support/live-llm';

// QUERY generate-from-prompt against a REAL provider (honua-console#283).
//
// The gap this closes: the deterministic live lane can only prove the console's
// baseline binding (QueryGenerationService returns "unsupported" with no provider,
// and the console seeds an honest baseline). Here a real provider is configured, so
// the service actually runs prompt -> LLM -> a structured saved query. Assertions
// are tolerant: we prove a coherent generated query came back, not its exact shape.

const QUERY_GENERATE = '/api/v1/analysis/content/queries/generate';
const PROMPT =
  'Return the id and name fields for all parcel features where the zoning value is R1.';

test.beforeEach(() => {
  test.skip(!LIVE_LLM_ENABLED, GATE_REASON);
});

test.describe('Live-LLM · QUERY from prompt', () => {
  test('server generates a coherent saved query from the prompt (API)', async ({ server }) => {
    const outcome = await server.generate(QUERY_GENERATE, PROMPT);
    // Returns only on a real `generated`; unsupported/error skip cleanly (recorded).
    classifyGeneration('query', outcome);
    // Tolerant coherence: a real, non-empty saved-query artifact was produced.
    expect(outcome.data, 'a generated query response body').toBeTruthy();
    expect(outcome.data.query ?? outcome.data.package, 'a generated query artifact').toBeTruthy();
  });

  test('console /studio/query from-prompt surface accepts the prompt and responds', async ({ page }) => {
    await page.goto('/studio/query');

    // Blazor Server wires onclick over the circuit after mount; retry until the
    // from-prompt textarea appears (a cold console must not flake the suite).
    await expect(async () => {
      await page.getByRole('button', { name: 'New from prompt' }).click();
      await expect(page.locator('textarea').first()).toBeVisible({ timeout: 5_000 });
    }).toPass({ timeout: 60_000 });

    // The console must see the provider — never the honest "unavailable" state.
    await expect(page.getByText(/AI generation (is )?unavailable/i)).toHaveCount(0);

    await page.locator('textarea').first().fill(PROMPT);
    await page.getByRole('button', { name: /Send/ }).click();

    // Tolerant success: the generated query surfaces as either the live result
    // section or a Honua response turn. Generous window — a real model is slow.
    const resultSignal = page
      .getByText('Result · live')
      .or(page.locator('.studio-ai-turn-honua'))
      .or(page.locator('figure.chart-preview'));
    await expect(resultSignal.first()).toBeVisible({ timeout: 180_000 });
    recordSurface('query-ui', 'generated');
  });
});
