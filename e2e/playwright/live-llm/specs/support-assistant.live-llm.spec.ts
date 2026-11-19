import { test, expect, type Page } from '@playwright/test';
import { GATE_REASON, LIVE_LLM_ENABLED, recordSurface } from '../support/live-llm';

// The /support GIS assistant against a REAL provider.
//
// It is the Console surface that still runs live inference: the self-service panel
// sends the conversation to the OpenAI-compatible endpoint in HONUA_LLM_BASE_URL.
// Model output is non-deterministic, so the answer check is tolerant (a substantive
// reply appeared). A missing panel or an error reply is a real failure: the
// orchestrator only enables this spec once a provider resolved.

const SURFACE = 'support-assistant';
const QUESTION =
  'My WFS GetFeature request returns features in EPSG:4326 but my map is in EPSG:3857. How do I get them reprojected?';
const MIN_ANSWER_CHARS = 40;

test.beforeEach(() => {
  test.skip(!LIVE_LLM_ENABLED, GATE_REASON);
});

async function askAssistant(page: Page, question: string) {
  // Development hosts sign in a dev operator at /auth/login and return to the page.
  await page.goto('/auth/login?returnTo=%2Fsupport');
  const assistant = page.getByRole('region', { name: 'GIS assistant' });
  await expect(assistant, 'assistant panel shown (HONUA_LLM_* configured)').toBeVisible();
  const input = assistant.getByRole('textbox', { name: 'Ask the GIS assistant' });
  const ask = assistant.getByRole('button', { name: 'Ask', exact: true });
  // Ask enables only once the interactive circuit has the input; a fill before
  // hydration is dropped, so fill again until it lands.
  await expect(async () => {
    await input.fill(question);
    await expect(ask).toBeEnabled({ timeout: 2_000 });
  }).toPass({ timeout: 60_000 });
  await ask.click();
  return assistant;
}

test.describe('Live-LLM · support GIS assistant', () => {
  test('answers a GIS question through the configured provider', async ({ page }) => {
    const assistant = await askAssistant(page, QUESTION);
    const reply = assistant.locator('.support-assistant-turn-assistant .support-assistant-body');
    const failure = assistant.getByRole('alert');
    await expect(reply.or(failure).first()).toBeVisible({ timeout: 180_000 });

    if (await failure.isVisible()) {
      const detail = (await failure.innerText()).trim();
      recordSurface(SURFACE, 'error', detail.slice(0, 200));
      throw new Error(`support assistant returned an error: ${detail}`);
    }

    const answer = (await reply.first().innerText()).trim();
    recordSurface(SURFACE, 'answered', `${answer.length} chars`);
    expect(answer.length, 'a substantive answer').toBeGreaterThanOrEqual(MIN_ANSWER_CHARS);
  });
});
