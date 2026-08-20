import type { Page } from '@playwright/test';

// Is `service` actually published on this server?
//
// GeoServices answers "no such service" the Esri way: HTTP **200** carrying an error envelope
//   {"error":{"code":404,"message":"Not Found","details":["Service 'e2e_src_fs' not found."]}}
// so `response.ok()` is TRUE for a service that does not exist. Guards written as
//   test.skip(!res.ok(), '... not published ...')
// therefore never fired — the spec pressed on against a missing service and failed ~30s later
// waiting for a UI panel that could never render, reporting a timeout instead of the real reason.
//
// The inverse mistake is worse, and this suite is where it would hurt most: honua-release's S4 gate
// consumes these specs, so a probe that answers "not published" to ANY error would turn a 401 or a
// 500 into `test.skip(...)` and report a green release scenario over a broken server. A check that
// cannot fail is not a check. So classify three ways, and only the genuinely-missing case skips.

/** What a probe of `/rest/services/{service}/FeatureServer` means. */
export type ServiceProbe =
  | { state: 'published' }
  /** The server answered normally and the service is simply not there — skipping is legitimate. */
  | { state: 'missing' }
  /** Anything else: auth failure, server error, unparseable or unrecognised payload. Must FAIL. */
  | { state: 'error'; reason: string };

/**
 * Pure classifier for a FeatureServer probe — no I/O, so the distinction that matters
 * (missing vs. broken) is unit-testable. See live/specs/published-guard.live.spec.ts.
 */
export function classifyServiceProbe(status: number, body: unknown): ServiceProbe {
  // A transport-level 404 means the same thing as the Esri envelope's 404.
  if (status === 404) return { state: 'missing' };
  if (status < 200 || status >= 300) {
    return { state: 'error', reason: `HTTP ${status}` };
  }
  if (body === null || typeof body !== 'object') {
    return { state: 'error', reason: `HTTP ${status} with a non-object body (${typeof body})` };
  }

  const record = body as Record<string, unknown>;
  const envelope = record.error;
  if (envelope !== undefined && envelope !== null) {
    if (typeof envelope !== 'object') {
      return { state: 'error', reason: `unrecognised error envelope: ${JSON.stringify(envelope)}` };
    }
    const err = envelope as Record<string, unknown>;
    const code = Number(err.code);
    // ONLY a missing service is a skip. 401/403 (credential regression), 500 (server regression),
    // and anything unrecognised must surface as a failure rather than a quiet green.
    if (code === 404) return { state: 'missing' };
    const message = typeof err.message === 'string' ? err.message : JSON.stringify(err);
    return { state: 'error', reason: `server returned error code ${err.code ?? '<none>'}: ${message}` };
  }

  if (!Array.isArray(record.layers)) {
    return { state: 'error', reason: `HTTP ${status} with no error envelope and no layers[] array` };
  }
  return { state: 'published' };
}

/**
 * True when `service` is published, false when it is genuinely absent.
 *
 * THROWS on anything else — an auth failure or a server error must not be laundered into a skip.
 */
export async function isServicePublished(
  page: Page,
  serverUrl: string,
  service: string,
  headers: Record<string, string>,
): Promise<boolean> {
  const url = `${serverUrl}/rest/services/${service}/FeatureServer?f=json`;
  const res = await page.request.get(url, { headers });

  let body: unknown;
  try {
    body = await res.json();
  } catch {
    // Keep the raw text in the message — an HTML error page or a proxy banner is the usual cause.
    const text = await res.text().catch(() => '<unreadable>');
    throw new Error(`GET ${url} -> HTTP ${res.status()} with a non-JSON body: ${text.slice(0, 300)}`);
  }

  const probe = classifyServiceProbe(res.status(), body);
  if (probe.state === 'error') {
    throw new Error(`GET ${url} -> ${probe.reason}`);
  }
  return probe.state === 'published';
}
