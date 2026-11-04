import type { APIRequestContext } from '@playwright/test';

// GeoServices answers a refused or missing resource the Esri way: HTTP 200 with an error envelope
// (499 token required, 404 not found), so the HTTP status alone never shows the refusal.
export const GEOSERVICES_TOKEN_REQUIRED = 499;
export const GEOSERVICES_NOT_FOUND = 404;

export type GeoServicesQueryOutcome = { readonly features: number } | { readonly errorCode: number };

type GeoServicesResponse = { readonly body: any } | { readonly errorCode: number };

async function readGeoServices(request: APIRequestContext, url: string, headers: Record<string, string>): Promise<GeoServicesResponse> {
  const response = await request.get(url, { headers });
  if (!response.ok()) return { errorCode: response.status() };
  const body = await response.json();
  return body.error ? { errorCode: body.error.code ?? response.status() } : { body };
}

export async function anonymousGeoServicesQuery(request: APIRequestContext, url: string): Promise<GeoServicesQueryOutcome> {
  const read = await readGeoServices(request, url, {});
  if ('errorCode' in read) return read;
  return Array.isArray(read.body.features) ? { features: read.body.features.length } : { errorCode: 0 };
}

// The envelope's error code, or null when the resource is served.
export async function geoServicesErrorCode(request: APIRequestContext, url: string, headers: Record<string, string>): Promise<number | null> {
  const read = await readGeoServices(request, url, headers);
  return 'errorCode' in read ? read.errorCode : null;
}
