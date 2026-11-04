import type { APIRequestContext } from '@playwright/test';

// GeoServices answers a refused read the Esri way: HTTP 200 with an error envelope (code 499, token required).
export const GEOSERVICES_TOKEN_REQUIRED = 499;

export type GeoServicesQueryOutcome = { readonly features: number } | { readonly errorCode: number };

export async function anonymousGeoServicesQuery(request: APIRequestContext, url: string): Promise<GeoServicesQueryOutcome> {
  const response = await request.get(url);
  if (!response.ok()) return { errorCode: response.status() };
  const body = await response.json();
  return Array.isArray(body.features) ? { features: body.features.length } : { errorCode: body.error?.code ?? response.status() };
}
