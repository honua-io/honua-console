import { test, expect } from '../admin-api';

// Live e2e for the STUDIO workflow goal: each Studio builder must work all the way to its FINAL OUTPUT —
// a real rendered result from real server data — not just a structural package. This drives the real
// "from prompt" flow against the live honua-server (local model generation) and asserts the rendered result:
//   - QUERY    -> a live Vega-Lite chart whose bars are the bound layer's REAL rows (the "analysis graph").
//   - MAP      -> a live MapLibre map bound to the published layer's real style/tiles (the "map").
//   - FORM     -> a live interactive form rendered from the generated schema (the form IS its result).
//   - ANALYSIS -> a live Vega-Lite distribution of the bound input layer's REAL rows. The 7b model can't draft
//                 an analysis (returns "unsupported"), so the console seeds an honest baseline bound to a real
//                 catalog layer — the rendered graph is the layer's actual features, never fabricated data.
//   - WORKFLOW -> the server-authored DAG (real node types) rendered as a node/edge graph. Driven via the
//                 server's `deterministic` generation provider (fixture replay, no model) with a fixture
//                 prompt, so the graph is genuine server output, not a console stand-in.
// Generation runs on the local CPU model (or the deterministic provider), so timeouts are generous. The
// verification target is the published service `e2e_src_fs` (layer 1); if it is absent the suite skips.

const ADMIN_KEY = process.env.HONUA_CONSOLE_E2E_ADMIN_KEY ?? 'honua-console-dev-key';
const ADMIN_HEADERS = { 'X-API-Key': ADMIN_KEY };
const QUERY_PROMPT = 'From the E2E Source layer, return the id and name fields for all features.';
const MAP_PROMPT = 'A map of the E2E Source layer.';
const ANALYSIS_PROMPT = 'Summarise the distribution of the E2E Source layer features.';
// Exact deterministic-provider fixture prompt (workflow-generation-contract-v1.json) — yields a real
// 3-node DAG (copy-features -> calculate-field -> area). Must match the fixture verbatim (trim+lowercase).
const WORKFLOW_PROMPT =
  'Nightly: copy new assessor parcels into a working layer, stamp a reviewed flag, then compute parcel area.';
const DETERMINISTIC_PROVIDER_LABEL = /Deterministic/i;

test.describe('Studio · workflow result rendering (live)', () => {
  test.beforeEach(async ({ page, admin }) => {
    const res = await page.request.get(`${admin.serverUrl}/rest/services/e2e_src_fs/FeatureServer?f=json`, { headers: ADMIN_HEADERS });
    test.skip(!res.ok(), 'e2e_src_fs is not published on this server — run services-layers.live.spec first.');
  });

  test('QUERY from prompt renders a live chart of the bound layer\'s real rows', async ({ page }) => {
    test.setTimeout(180_000);
    await page.goto('/studio/query');
    await page.getByRole('button', { name: 'New from prompt' }).click();

    // Send the prompt; generation grounds in the real catalog and binds the real layer server-side.
    await page.locator('textarea').first().fill(QUERY_PROMPT);
    await page.getByRole('button', { name: /Send/ }).click();

    // The result section appears once the query is bound to a real service+layer (catalog fallback resolves
    // the serviceId the model omits). This is the gate for the live chart.
    await expect(page.getByText('Result · live')).toBeVisible({ timeout: 120_000 });

    // The FINAL OUTPUT: a live Vega-Lite chart over the layer's REAL rows. The chart lazily loads vega from a
    // CDN, fetches the rows through the console features proxy, and renders one bar per distinct value — so
    // real bars (Vega mark-rect paths) prove the workflow reached a rendered, data-bound result.
    const chart = page.locator('figure.chart-preview');
    await expect(chart).toBeVisible({ timeout: 15_000 });
    await expect(chart).toHaveAttribute('data-chart-bound', 'true', { timeout: 30_000 });
    await expect
      .poll(async () => await chart.locator('g.mark-rect path, path.mark-rect').count(), { timeout: 30_000, intervals: [500, 1000, 2000] })
      .toBeGreaterThan(0);
  });

  test('MAP from prompt binds the published layer\'s real style (live map preview)', async ({ page }) => {
    test.setTimeout(180_000);
    await page.goto('/studio/map');
    await page.getByRole('button', { name: 'New from prompt' }).click();
    await page.locator('textarea').first().fill(MAP_PROMPT);

    // Arm the listener BEFORE sending: when generation binds the real layer (catalog fallback resolves the
    // single source), the preview requests that layer's real MapLibre style through the console proxy.
    const styleRequest = page.waitForRequest((req) => /\/map-proxy\/styles\/\d+\.json(\?|$)/.test(req.url()), { timeout: 150_000 });
    await page.getByRole('button', { name: /Send/ }).click();

    // The FINAL OUTPUT: the map preview is bound to the published layer's REAL style/tiles (this proves the
    // workflow produced a map backed by real server data). The live MapLibre mount over it is best-effort
    // (CDN + vector-tile web worker), so the deterministic result-signal is the proxied real style request.
    const req = await styleRequest;
    expect(req.url(), 'map preview bound a real published-layer style URL').toMatch(/\/map-proxy\/styles\/\d+\.json/);
    const styleResponse = await req.response();
    expect(styleResponse?.status(), 'the bound style resolves through the proxy').toBe(200);
    await expect(page.locator('figure.map-preview').first()).toBeVisible();
  });

  test('FORM from prompt renders the form (real interactive controls) as its final output', async ({ page }) => {
    test.setTimeout(180_000);
    await page.goto('/studio/form/ai');
    // The from-prompt builder must be AVAILABLE (the server now exposes the form generation providers
    // endpoint; the console no longer shows the "AI generation unavailable" state).
    const refine = page.locator('textarea').first();
    await expect(refine).toBeEnabled({ timeout: 30_000 });
    await expect(page.getByText(/AI generation (is )?unavailable/i)).toHaveCount(0);

    const turnsBefore = await page.locator('.studio-ai-turn-honua').count();
    await refine.fill('An inspection form for the E2E Source layer: a required Asset name text field, a condition field with choices good/fair/poor, and a notes text area.');
    await page.getByRole('button', { name: /Send/ }).click();

    // Generation actually round-tripped through the server: a new Honua response turn appears (proving the
    // form providers endpoint + generate call worked, not just the starting scaffold).
    await expect
      .poll(async () => await page.locator('.studio-ai-turn-honua').count(), { timeout: 120_000, intervals: [1000, 2000, 5000] })
      .toBeGreaterThan(turnsBefore);

    // The FINAL OUTPUT: a live, interactive form rendered from the schema (the form IS its result).
    const form = page.locator('[aria-label="Form preview"] form').first();
    await expect(form).toBeVisible({ timeout: 15_000 });
    await expect
      .poll(async () => await form.locator('input, select, textarea').count(), { timeout: 15_000, intervals: [500, 1000, 2000] })
      .toBeGreaterThan(0);
  });

  test('ANALYSIS from prompt renders a result graph (baseline distribution of the bound layer\'s real rows)', async ({ page }) => {
    test.setTimeout(180_000);
    await page.goto('/studio/analysis');
    // Blazor Server: the button is in the DOM before its onclick handler is wired over the SignalR circuit,
    // so an early click can be lost and the from-prompt textarea never appears. Re-click until it shows.
    await expect(async () => {
      await page.getByRole('button', { name: 'New from prompt' }).click();
      await expect(page.locator('textarea').first()).toBeVisible({ timeout: 5_000 });
    }).toPass({ timeout: 60_000 });

    const turnsBefore = await page.locator('.studio-ai-turn-honua').count();
    await page.locator('textarea').first().fill(ANALYSIS_PROMPT);
    await page.getByRole('button', { name: /Send/ }).click();

    // Generation round-tripped through the server: a new Honua turn appears. The 7b model returns
    // "unsupported", so the console seeds a baseline bound to a real catalog layer (honest — labelled a
    // baseline in the turn, not an AI-authored plan).
    await expect
      .poll(async () => await page.locator('.studio-ai-turn-honua').count(), { timeout: 120_000, intervals: [1000, 2000, 5000] })
      .toBeGreaterThan(turnsBefore);

    // The FINAL OUTPUT: the input-data graph — a live Vega-Lite distribution of the bound input layer's
    // REAL rows (fetched through the console features proxy). Real bars prove a rendered, data-bound result.
    const chart = page.locator('[data-analysis-ai-input-chart] ~ figure.chart-preview, figure.chart-preview').first();
    await expect(chart).toBeVisible({ timeout: 30_000 });
    await expect(chart).toHaveAttribute('data-chart-bound', 'true', { timeout: 30_000 });
    await expect
      .poll(async () => await chart.locator('g.mark-rect path, path.mark-rect').count(), { timeout: 30_000, intervals: [500, 1000, 2000] })
      .toBeGreaterThan(0);
  });

  test('WORKFLOW from prompt renders the server-authored DAG (deterministic provider)', async ({ page }) => {
    test.setTimeout(120_000);
    await page.goto('/studio/workflows/new');

    // Choose the server's deterministic (fixture replay) provider so generation is model-free and returns a
    // real, validated graph. With ≥2 available providers the page renders a <select>; with exactly one it
    // renders a <span> (nothing to pick). Wait for whichever marker appears, then select deterministic by
    // its option VALUE and wait for the @bind to commit (Blazor Server round-trips onchange over the
    // circuit, so selectOption resolving does NOT guarantee _selectedProvider has updated) before sending —
    // otherwise Send can read a stale provider and run the wrong (model) provider.
    const providerSelect = page.locator('select[data-workflow-ai-provider]');
    const providerLabel = page.locator('span[data-workflow-ai-provider]');
    await expect(providerSelect.or(providerLabel).first()).toBeVisible({ timeout: 30_000 });
    if (await providerSelect.count()) {
      const detOption = providerSelect.locator('option', { hasText: DETERMINISTIC_PROVIDER_LABEL }).first();
      await expect(detOption).toHaveCount(1, { timeout: 10_000 });
      const detValue = (await detOption.getAttribute('value'))!;
      await providerSelect.selectOption(detValue);
      await expect(providerSelect).toHaveValue(detValue);
    } else {
      // Single-provider server: it must be the deterministic one for this model-free test to be meaningful.
      await expect(providerLabel).toContainText(DETERMINISTIC_PROVIDER_LABEL, { timeout: 10_000 });
    }

    await page.locator('textarea').first().fill(WORKFLOW_PROMPT);
    await page.getByRole('button', { name: /Send/ }).click();

    // The FINAL OUTPUT: the generated workflow's real nodes laid out as a node/edge graph. The deterministic
    // fixture yields a 3-step pipeline (copy-features -> calculate-field -> area); rendered nodes prove the
    // workflow reached a real, server-produced graph.
    const graph = page.locator('[data-workflow-graph]').first();
    await expect(graph).toBeVisible({ timeout: 30_000 });
    await expect
      .poll(async () => await graph.locator('[data-workflow-graph-node]').count(), { timeout: 60_000, intervals: [1000, 2000, 5000] })
      .toBeGreaterThan(0);
    await expect(graph.locator('[data-workflow-graph-empty]')).toHaveCount(0);
  });
});
