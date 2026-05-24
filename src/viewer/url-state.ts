/**
 * URL hash state for the portal map viewer (honua-portal#13 AC3:
 * "Viewer state can be copied as a URL and restored").
 *
 * The hash format is intentionally human readable so a copied link can be
 * shared in chat or pasted into a browser without an opaque blob:
 *
 *   #item=<id>&center=<lon>,<lat>&zoom=<z>&layers=<id1,id2>&selected=<layer.id>
 *
 * Layer ids are comma-joined in render order (bottom → top). When a key
 * is omitted the viewer keeps its current value, which lets older shared
 * URLs continue to work as the schema grows.
 */

import type { SelectedFeature, ViewerState } from "./types.js";

const COORD_PRECISION = 5;
const ZOOM_PRECISION = 2;

export interface ViewerStateUrl {
  itemId?: string;
  center?: [number, number];
  zoom?: number;
  visibleLayerIds?: string[];
  selected?: SelectedFeature;
}

export interface EncodeOptions {
  itemId?: string;
}

export function encodeViewerStateToHash(state: ViewerState, options?: EncodeOptions): string {
  const params = new URLSearchParams();
  if (options?.itemId) params.set("item", options.itemId);
  params.set("center", `${roundTo(state.center[0], COORD_PRECISION)},${roundTo(state.center[1], COORD_PRECISION)}`);
  params.set("zoom", roundTo(state.zoom, ZOOM_PRECISION).toString());
  params.set("layers", state.visibleLayerIds.join(","));
  if (state.selected) {
    params.set("selected", `${state.selected.layerId}.${state.selected.featureId}`);
  }
  return `#${params.toString()}`;
}

export function decodeViewerStateFromHash(hash: string): ViewerStateUrl {
  const trimmed = hash.startsWith("#") ? hash.slice(1) : hash;
  if (trimmed.length === 0) return {};

  const params = new URLSearchParams(trimmed);
  const result: ViewerStateUrl = {};

  const item = params.get("item");
  if (item) result.itemId = item;

  const center = params.get("center");
  if (center) {
    const parsedCenter = parseCoordinatePair(center);
    if (parsedCenter) result.center = parsedCenter;
  }

  const zoom = params.get("zoom");
  if (zoom) {
    const parsedZoom = Number.parseFloat(zoom);
    if (Number.isFinite(parsedZoom) && parsedZoom >= 0 && parsedZoom <= 24) {
      result.zoom = parsedZoom;
    }
  }

  const layers = params.get("layers");
  if (layers !== null) {
    result.visibleLayerIds = layers
      .split(",")
      .map((value) => value.trim())
      .filter((value) => value.length > 0);
  }

  const selected = params.get("selected");
  if (selected) {
    const parsedSelected = parseSelectedFeature(selected);
    if (parsedSelected) result.selected = parsedSelected;
  }

  return result;
}

export function applyHashToState(base: ViewerState, hash: string, knownLayerIds?: string[]): ViewerState {
  const overrides = decodeViewerStateFromHash(hash);
  return mergeViewerState(base, overrides, knownLayerIds);
}

export function mergeViewerState(base: ViewerState, overrides: ViewerStateUrl, knownLayerIds?: string[]): ViewerState {
  const next: ViewerState = {
    center: overrides.center ?? base.center,
    zoom: overrides.zoom ?? base.zoom,
    visibleLayerIds: filterKnownLayers(overrides.visibleLayerIds, base.visibleLayerIds, knownLayerIds),
    selected: overrides.selected ?? base.selected,
  };

  if (next.selected && knownLayerIds && !knownLayerIds.includes(next.selected.layerId)) {
    next.selected = undefined;
  }
  return next;
}

function filterKnownLayers(override: string[] | undefined, fallback: string[], knownLayerIds?: string[]): string[] {
  if (!override) return fallback;
  if (!knownLayerIds) return [...override];
  return override.filter((id) => knownLayerIds.includes(id));
}

function parseCoordinatePair(value: string): [number, number] | undefined {
  const parts = value.split(",");
  if (parts.length !== 2) return undefined;
  const lon = Number.parseFloat(parts[0]);
  const lat = Number.parseFloat(parts[1]);
  if (!Number.isFinite(lon) || !Number.isFinite(lat)) return undefined;
  if (lon < -180 || lon > 180 || lat < -90 || lat > 90) return undefined;
  return [lon, lat];
}

function parseSelectedFeature(value: string): SelectedFeature | undefined {
  const dotIndex = value.indexOf(".");
  if (dotIndex <= 0 || dotIndex === value.length - 1) return undefined;
  return {
    layerId: value.slice(0, dotIndex),
    featureId: value.slice(dotIndex + 1),
  };
}

function roundTo(value: number, precision: number): number {
  const factor = 10 ** precision;
  return Math.round(value * factor) / factor;
}
