import { test, expect, classifyGeneration, recordSurface, GATE_REASON, LIVE_LLM_ENABLED } from '../support/live-llm';

// MAP generate-from-prompt against a REAL provider (honua-console#283).
// Tolerant: prove a coherent generated map package came back, not its exact shape.

const MAP_GENERATE = '/api/v1/studio/map-packages/generate';
const PROMPT = 'A simple map showing a single parcels layer over a light basemap.';

test.beforeEach(() => {
  test.skip(!LIVE_LLM_ENABLED, GATE_REASON);
});

test.describe('Live-LLM · MAP from prompt', () => {
  test('server generates a coherent map package from the prompt (API)', async ({ server }) => {
    const outcome = await server.generate(MAP_GENERATE, PROMPT);
    classifyGeneration('map', outcome);
    expect(outcome.data, 'a generated map response body').toBeTruthy();
    expect(outcome.data.package ?? outcome.data.map, 'a generated map artifact').toBeTruthy();
  });

  test('console /studio/map from-prompt surface accepts the prompt and responds', async ({ page }) => {
    await page.goto('/studio/map');

    await expect(async () => {
      await page.getByRole('button', { name: 'New from prompt' }).click();
      await expect(page.locator('textarea').first()).toBeVisible({ timeout: 5_000 });
    }).toPass({ timeout: 60_000 });

    await expect(page.getByText(/AI generation (is )?unavailable/i)).toHaveCount(0);

    await page.locator('textarea').first().fill(PROMPT);
    await page.getByRole('button', { name: /Send/ }).click();

    // Tolerant success: the generated map surfaces as a live preview figure or a
    // Honua response turn (a real published-layer style binding needs a seeded
    // catalog, which this lane deliberately does not require).
    const resultSignal = page
      .locator('figure.map-preview')
      .or(page.locator('.studio-ai-turn-honua'))
      .or(page.getByText('Result · live'));
    await expect(resultSignal.first()).toBeVisible({ timeout: 180_000 });
    recordSurface('map-ui', 'generated');
  });
});
