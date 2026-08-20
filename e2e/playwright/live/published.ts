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
// Inspect the payload, not the status: a real FeatureServer answer carries a `layers` array and no
// `error`. Then a spec whose fixture genuinely is not there skips with an accurate message, and a
// spec that fails is failing for a real reason.
export async function isServicePublished(
  page: Page,
  serverUrl: string,
  service: string,
  headers: Record<string, string>,
): Promise<boolean> {
  const res = await page.request.get(`${serverUrl}/rest/services/${service}/FeatureServer?f=json`, { headers });
  if (!res.ok()) return false;
  try {
    const body = await res.json();
    return !body?.error && Array.isArray(body?.layers);
  } catch {
    return false;
  }
}
