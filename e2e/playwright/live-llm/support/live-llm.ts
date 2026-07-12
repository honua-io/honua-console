import { test as base, expect, request, type APIRequestContext } from '@playwright/test';
import { appendFileSync, mkdirSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';

// Shared harness for the live-LLM smoke lane (honua-console#283).
//
// Two jobs:
//   1. A `server` fixture that talks to the honua-server admin API directly
//      (X-API-Key), so each spec can prove prompt -> LLM -> result at the API
//      layer (the deterministic-enough core signal) independently of the flaky
//      Blazor UI it also drives.
//   2. Gating + honest-skip helpers: skip when no credential/provider is
//      configured, and skip-not-fail when the server reports the surface
//      `unsupported` (no provider) — the two states the issue asks us to detect
//      cleanly rather than failing on.

export const SERVER_URL =
  process.env.HONUA_CONSOLE_E2E_LIVE_LLM_SERVER_URL ?? 'http://127.0.0.1:5681';
export const ADMIN_KEY = process.env.HONUA_CONSOLE_E2E_ADMIN_KEY ?? 'honua-console-dev-key';

// The credential gate: run-live-llm.mjs / the nightly workflow set this to '1'
// only when a real provider was resolved. Absent => the lane skips (never fails).
export const LIVE_LLM_ENABLED = process.env.HONUA_LIVE_LLM_ENABLED === '1';
export const PROVIDER_MODE = process.env.HONUA_LIVE_LLM_MODE ?? 'unknown';

export const GATE_REASON =
  'live-LLM lane requires a configured AI provider. Set HONUA_LIVE_LLM_ENABLED=1 with ' +
  'the HONUA_LIVE_LLM_* credentials (Bedrock via LiteLLM, or a direct OpenAI-compatible key), ' +
  'or configure the nightly repo secret. Skipping cleanly.';

// Surface-outcome ledger consumed by the CI job summary. Specs run serially in a
// single worker, so plain appends are safe.
const RESULTS_DIR = fileURLToPath(new URL('../results/', import.meta.url));
const SURFACES_FILE = join(RESULTS_DIR, 'surfaces.jsonl');

export type SurfaceStatus = 'generated' | 'unsupported' | 'error' | 'skipped';

export function recordSurface(surface: string, status: SurfaceStatus, detail?: string): void {
  try {
    mkdirSync(dirname(SURFACES_FILE), { recursive: true });
    appendFileSync(
      SURFACES_FILE,
      JSON.stringify({ surface, status, mode: PROVIDER_MODE, detail: detail ?? '', at: new Date().toISOString() }) + '\n',
    );
  } catch {
    // A recording failure must never fail the lane.
  }
}

export interface GenerationOutcome {
  httpOk: boolean;
  httpStatus: number;
  status: string; // server-reported generation status: generated | unsupported | clarify | error | ...
  /** The generation payload, unwrapped from a `{ success, data }` envelope when present. */
  data: any;
  /** The raw response body (envelope included). */
  raw: any;
}

// Some generation endpoints return the bare result object; others wrap it in a
// `{ success, data, message }` envelope. Normalise so specs read one shape.
function unwrap(body: any): any {
  if (body && typeof body === 'object' && 'data' in body && (body as any).success !== undefined) {
    return (body as any).data;
  }
  return body;
}

export interface ServerApi {
  readonly serverUrl: string;
  /** POST a generate-from-prompt request; returns the parsed generation outcome. */
  generate(path: string, prompt: string): Promise<GenerationOutcome>;
  /** GET arbitrary server JSON (provider probes, catalog, etc.). Non-throwing. */
  getJson(path: string): Promise<{ ok: boolean; status: number; body: any }>;
}

export const test = base.extend<{ server: ServerApi }>({
  server: async ({ playwright }, use) => {
    const ctx: APIRequestContext = await request.newContext({
      baseURL: SERVER_URL,
      extraHTTPHeaders: { 'X-API-Key': ADMIN_KEY },
    });

    const api: ServerApi = {
      serverUrl: SERVER_URL,
      async generate(path, prompt) {
        const res = await ctx.post(path, { data: { prompt }, timeout: 180_000 });
        let raw: any = null;
        try {
          raw = await res.json();
        } catch {
          raw = await res.text().catch(() => null);
        }
        const data = unwrap(raw);
        return {
          httpOk: res.ok(),
          httpStatus: res.status(),
          status: (data && typeof data === 'object' && typeof data.status === 'string') ? data.status : 'unknown',
          data,
          raw,
        };
      },
      async getJson(path) {
        const res = await ctx.get(path);
        let raw: any = null;
        try {
          raw = await res.json();
        } catch {
          raw = null;
        }
        return { ok: res.ok(), status: res.status(), body: unwrap(raw) };
      },
    };

    await use(api);
    await ctx.dispose();
  },
});

export { expect };

/**
 * Interpret a generation outcome for a TOLERANT smoke assertion, recording the
 * surface result for the job summary along the way. This lane is NON-BLOCKING, so
 * only a genuine `generated` result asserts hard; every other state is recorded
 * and skipped (never a red build) so the summary reports the truth per surface:
 *
 * - `generated` => returns 'generated'; the caller asserts a coherent artifact.
 * - `unsupported` / 404 / 501 => the server has no provider for this surface;
 *   recorded `unsupported` and skipped cleanly (the honest-detection requirement).
 * - anything else (`error`, `clarify`, unknown) => recorded `error` with detail
 *   and skipped with an annotation — non-deterministic model output must not turn
 *   the nightly signal red, but it stays visible in the surface ledger.
 */
export function classifyGeneration(surface: string, outcome: GenerationOutcome): 'generated' | 'skip' {
  if (outcome.httpStatus === 404 || outcome.httpStatus === 501 || outcome.status === 'unsupported') {
    recordSurface(surface, 'unsupported', `http ${outcome.httpStatus}, status ${outcome.status}`);
    test.skip(true, `${surface}: server reports generation unsupported (no provider) — clean skip.`);
    return 'skip';
  }
  if (outcome.status === 'generated') {
    recordSurface(surface, 'generated');
    return 'generated';
  }
  const detail = summariseDetail(outcome);
  recordSurface(surface, 'error', detail);
  test.info().annotations.push({ type: 'generation-error', description: `${surface}: ${detail}` });
  test.skip(true, `${surface}: generation did not return a coherent result (status=${outcome.status}) — recorded, non-blocking skip.`);
  return 'skip';
}

function summariseDetail(outcome: GenerationOutcome): string {
  const rationale = outcome.data && typeof outcome.data === 'object' ? outcome.data.rationale : undefined;
  return `http ${outcome.httpStatus}, status ${outcome.status}${rationale ? `: ${String(rationale).slice(0, 200)}` : ''}`;
}
