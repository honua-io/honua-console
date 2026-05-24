/**
 * Stable URL helpers for saved web maps.
 *
 * Canonical path is `/maps/{id}`. Optional view-state query params
 * (`center`, `zoom`, `t`) are *ephemeral overlay only* — `parseMapUrl`
 * surfaces them, but `mapUrl` strips them on emit unless the caller asks
 * for them, and the saved-map item is never mutated by their presence.
 */

export interface ViewState {
  /** [lng, lat] */
  center?: [number, number];
  zoom?: number;
  /** ISO-8601 timestamp for time-aware overlays. */
  t?: string;
}

export interface ParsedMapUrl {
  id: string;
  viewState?: ViewState;
}

const ID_PATTERN = /^\/maps\/([^/?#]+)(?:[?#].*)?$/;

/**
 * Build a saved-map URL.
 *
 * - Canonical: `/maps/{id}`.
 * - Optional view state goes in the query string and is treated as ephemeral.
 */
export function mapUrl(id: string, viewState?: ViewState): string {
  if (!id) throw new Error("mapUrl: id is required");
  const base = `/maps/${encodeURIComponent(id)}`;
  if (!viewState) return base;
  const params = serializeViewState(viewState);
  if (!params) return base;
  return `${base}?${params}`;
}

/**
 * Parse a saved-map URL or path.
 *
 * Accepts either an absolute URL or a path. Returns `null` when the URL
 * does not match the saved-map route (the caller decides what to do; the
 * router should already have matched the route).
 */
export function parseMapUrl(input: string): ParsedMapUrl | null {
  let pathname: string;
  let search: string;
  try {
    if (input.startsWith("/")) {
      pathname = input.split("?")[0]?.split("#")[0] ?? "";
      const qIndex = input.indexOf("?");
      search = qIndex === -1 ? "" : (input.slice(qIndex + 1).split("#")[0] ?? "");
    } else {
      const url = new URL(input);
      pathname = url.pathname;
      search = url.search.slice(1);
    }
  } catch {
    return null;
  }

  const match = ID_PATTERN.exec(pathname);
  if (!match || !match[1]) return null;
  const id = decodeURIComponent(match[1]);
  const viewState = parseViewState(search);
  return viewState ? { id, viewState } : { id };
}

function serializeViewState(state: ViewState): string {
  const parts: string[] = [];
  if (state.center) {
    const [lng, lat] = state.center;
    if (Number.isFinite(lng) && Number.isFinite(lat)) {
      parts.push(`center=${formatNumber(lng)},${formatNumber(lat)}`);
    }
  }
  if (state.zoom !== undefined && Number.isFinite(state.zoom)) {
    parts.push(`zoom=${formatNumber(state.zoom)}`);
  }
  if (state.t) {
    parts.push(`t=${encodeURIComponent(state.t)}`);
  }
  return parts.join("&");
}

function parseViewState(search: string): ViewState | undefined {
  if (!search) return undefined;
  const params = new URLSearchParams(search);
  const view: ViewState = {};
  const center = params.get("center");
  if (center) {
    const [lngStr, latStr] = center.split(",", 2);
    const lng = Number(lngStr);
    const lat = Number(latStr);
    if (Number.isFinite(lng) && Number.isFinite(lat)) {
      view.center = [lng, lat];
    }
  }
  const zoomStr = params.get("zoom");
  if (zoomStr !== null) {
    const zoom = Number(zoomStr);
    if (Number.isFinite(zoom)) view.zoom = zoom;
  }
  const t = params.get("t");
  if (t) view.t = t;
  return Object.keys(view).length > 0 ? view : undefined;
}

function formatNumber(n: number): string {
  return Number.isInteger(n) ? n.toString() : n.toFixed(6).replace(/0+$/, "").replace(/\.$/, "");
}
