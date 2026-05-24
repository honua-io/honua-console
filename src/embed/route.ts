/**
 * Embed route param parser. Defensive parsing — every query param has a
 * typed parse with a documented fallback rather than throwing — so an
 * embedder who hand-edits a snippet cannot wedge the iframe.
 *
 * The embed route URL shape:
 *
 *   /embed/maps/:id?chrome=minimal|none|full&legend=on|off
 *                  &zoom=on|off&extent=W,S,E,N
 *
 * Extents are WGS84 lon/lat (`west,south,east,north`). Out-of-range or
 * malformed extents fall back to the persisted default extent and never
 * throw. The smoke spec asserts this fallback explicitly.
 */

export type EmbedChromeProfile = "minimal" | "none" | "full";

export type ContentItemKind = "map" | "service" | "layer" | "scene" | "app" | "document";

export interface EmbedExtent {
  west: number;
  south: number;
  east: number;
  north: number;
}

export interface EmbedRouteParams {
  chrome: EmbedChromeProfile;
  legend: boolean;
  zoom: boolean;
  extent: EmbedExtent | null;
}

export const DEFAULT_EMBED_ROUTE_PARAMS: EmbedRouteParams = {
  chrome: "minimal",
  legend: true,
  zoom: true,
  extent: null,
};

const CHROME_VALUES = new Set<EmbedChromeProfile>(["minimal", "none", "full"]);

export function parseEmbedParams(search: string | URLSearchParams): EmbedRouteParams {
  const params = typeof search === "string" ? new URLSearchParams(search) : search;
  const chrome = parseChrome(params.get("chrome"));
  const legend = parseToggle(params.get("legend"), DEFAULT_EMBED_ROUTE_PARAMS.legend);
  const zoom = parseToggle(params.get("zoom"), DEFAULT_EMBED_ROUTE_PARAMS.zoom);
  const extent = parseExtent(params.get("extent"));
  return { chrome, legend, zoom, extent };
}

export function stringifyEmbedParams(params: EmbedRouteParams): string {
  const out = new URLSearchParams();
  out.set("chrome", params.chrome);
  out.set("legend", params.legend ? "on" : "off");
  out.set("zoom", params.zoom ? "on" : "off");
  if (params.extent) {
    out.set("extent", [params.extent.west, params.extent.south, params.extent.east, params.extent.north].join(","));
  }
  return out.toString();
}

function parseChrome(raw: string | null): EmbedChromeProfile {
  if (!raw) return DEFAULT_EMBED_ROUTE_PARAMS.chrome;
  return CHROME_VALUES.has(raw as EmbedChromeProfile) ? (raw as EmbedChromeProfile) : DEFAULT_EMBED_ROUTE_PARAMS.chrome;
}

function parseToggle(raw: string | null, fallback: boolean): boolean {
  if (raw == null) return fallback;
  const lower = raw.toLowerCase();
  if (lower === "on" || lower === "true" || lower === "1") return true;
  if (lower === "off" || lower === "false" || lower === "0") return false;
  return fallback;
}

/**
 * Parse `west,south,east,north` in WGS84 lon/lat. Returns `null` on:
 *   - missing / empty input
 *   - non-numeric components
 *   - wrong arity (≠ 4 components)
 *   - out-of-range lon (must be -180 ≤ lon ≤ 180)
 *   - out-of-range lat (must be -90 ≤ lat ≤ 90)
 *   - degenerate extent (`west ≥ east` or `south ≥ north`)
 */
export function parseExtent(raw: string | null): EmbedExtent | null {
  if (!raw) return null;
  const parts = raw.split(",");
  if (parts.length !== 4) return null;
  const [west, south, east, north] = parts.map((p) => Number(p.trim())) as [number, number, number, number];
  for (const v of [west, south, east, north]) {
    if (!Number.isFinite(v)) return null;
  }
  if (west < -180 || west > 180) return null;
  if (east < -180 || east > 180) return null;
  if (south < -90 || south > 90) return null;
  if (north < -90 || north > 90) return null;
  if (west >= east) return null;
  if (south >= north) return null;
  return { west, south, east, north };
}

/**
 * Parse the embed token from a URL fragment. Tokens are kept in the
 * fragment (`#embedToken=…`) so they don't leak to access logs. The
 * embed page reads from `window.location.hash`.
 *
 * `URLSearchParams.get` already decodes percent-encoding, so we return
 * its result as-is — a second `decodeURIComponent` would throw on
 * tokens containing literal `%` (which `buildEmbedUrl` encodes as
 * `%25`, then URLSearchParams decodes back to `%`). Defensive parsing:
 * malformed input must never wedge the iframe.
 */
export function parseEmbedTokenFragment(fragment: string | null): string | null {
  if (!fragment) return null;
  const trimmed = fragment.startsWith("#") ? fragment.slice(1) : fragment;
  if (!trimmed) return null;
  const params = new URLSearchParams(trimmed);
  const token = params.get("embedToken");
  if (!token) return null;
  return token;
}

/**
 * Resolve the effective extent. Caller passes the parsed query extent
 * and the persisted default extent from the saved map; this function
 * picks the one to use. Always returns a value, never throws.
 */
export function resolveEffectiveExtent(input: {
  query: EmbedExtent | null;
  persisted: EmbedExtent | null;
}): EmbedExtent | null {
  return input.query ?? input.persisted ?? null;
}
