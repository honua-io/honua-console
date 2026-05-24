/**
 * Portal-owned saved-map style overrides.
 *
 * Maputnik edits a complete MapLibre style document. Honua persists the
 * edited document as an inline portal override on the saved WebMapDoc while
 * preserving any admin/server-owned `styleRef.itemId` as lineage. Viewer,
 * share, and embed rendering all resolve through this module so they cannot
 * drift on which style wins.
 */

import type { HonuaStyleSpecification } from "@honua/sdk-js";

import type { OperationalLayer, SavedStyleOrigin, WebMapDoc } from "./types.js";

export const MAPUTNIK_VERSION = "v3.0.0" as const;
export const DEFAULT_MAPUTNIK_EDITOR_URL = `/maputnik/${MAPUTNIK_VERSION}/index.html`;

export type EditableStyleTargetKind = "saved-map" | "layer";

export interface EditableStyleTarget {
  id: string;
  kind: EditableStyleTargetKind;
  label: string;
  layerId?: string;
  origin: SavedStyleOrigin;
}

export interface ApplyPortalStyleOverrideInput {
  doc: WebMapDoc;
  targetId: string;
  style: Record<string, unknown>;
  sourceStyle: HonuaStyleSpecification;
}

const SAVED_MAP_TARGET_ID = "saved-map";
const LAYER_PRESENTATION_KEYS = ["paint", "layout", "filter", "minzoom", "maxzoom", "metadata"] as const;
const STYLE_PRESENTATION_KEYS = ["name", "metadata", "sprite", "glyphs", "transition"] as const;

export function listEditableStyleTargets(doc: WebMapDoc): EditableStyleTarget[] {
  const editableLayers = doc.operationalLayers.filter(isMapLibreCompatibleLayer);
  if (editableLayers.length === 0) return [];

  const targets: EditableStyleTarget[] = [
    {
      id: SAVED_MAP_TARGET_ID,
      kind: "saved-map",
      label: "Saved map style",
      origin: resolveDocStyleOrigin(doc),
    },
  ];

  for (const layer of editableLayers) {
    targets.push({
      id: `layer:${layer.id}`,
      kind: "layer",
      layerId: layer.id,
      label: layer.title,
      origin: resolveLayerStyleOrigin(layer),
    });
  }

  return targets;
}

export function applyPortalStyleOverride(input: ApplyPortalStyleOverrideInput): WebMapDoc {
  const doc = cloneJson(input.doc);
  const layer = resolveTargetLayer(doc, input.targetId);
  if (!layer) {
    throw new Error(`Unknown style target: ${input.targetId}`);
  }

  const normalized = normalizeMapLibreStyle(input.style, input.sourceStyle);
  clearExistingPortalOverrides(doc);
  layer.styleRef = {
    ...(layer.styleRef?.itemId ? { itemId: layer.styleRef.itemId } : {}),
    inline: normalized,
    origin: "portal-override",
  };
  return doc;
}

export function resolveSavedMapStyle(doc: WebMapDoc, fallbackStyle: HonuaStyleSpecification): HonuaStyleSpecification {
  const override = findPortalOverride(doc);
  if (!override) return cloneJson(fallbackStyle);
  return normalizeMapLibreStyle(override, fallbackStyle) as unknown as HonuaStyleSpecification;
}

export function resolveDocStyleOrigin(doc: WebMapDoc): SavedStyleOrigin {
  return findPortalOverride(doc) ? "portal-override" : "admin-layer-style";
}

export function resolveLayerStyleOrigin(layer: OperationalLayer): SavedStyleOrigin {
  return layer.styleRef?.origin ?? "admin-layer-style";
}

export function isMapLibreStyle(value: unknown): value is Record<string, unknown> {
  if (!value || typeof value !== "object" || Array.isArray(value)) return false;
  const style = value as { version?: unknown; sources?: unknown; layers?: unknown };
  return style.version === 8 && isRecord(style.sources) && Array.isArray(style.layers);
}

function resolveTargetLayer(doc: WebMapDoc, targetId: string): OperationalLayer | undefined {
  if (targetId === SAVED_MAP_TARGET_ID) return firstEditableLayer(doc);
  if (!targetId.startsWith("layer:")) return undefined;
  const layerId = targetId.slice("layer:".length);
  return doc.operationalLayers.find((layer) => layer.id === layerId && isMapLibreCompatibleLayer(layer));
}

function firstEditableLayer(doc: WebMapDoc): OperationalLayer | undefined {
  return doc.operationalLayers.find(isMapLibreCompatibleLayer);
}

function clearExistingPortalOverrides(doc: WebMapDoc): void {
  for (const layer of doc.operationalLayers) {
    const styleRef = layer.styleRef;
    if (styleRef?.origin !== "portal-override") continue;
    layer.styleRef = styleRef.itemId ? { itemId: styleRef.itemId, origin: "admin-layer-style" } : null;
  }
}

function findPortalOverride(doc: WebMapDoc): Record<string, unknown> | null {
  for (const layer of doc.operationalLayers) {
    if (layer.styleRef?.origin === "portal-override" && isMapLibreStyle(layer.styleRef.inline)) {
      return layer.styleRef.inline;
    }
  }
  for (const layer of doc.operationalLayers) {
    if (isMapLibreStyle(layer.styleRef?.inline)) return layer.styleRef.inline;
  }
  return null;
}

function normalizeMapLibreStyle(
  candidate: Record<string, unknown>,
  sourceStyle: HonuaStyleSpecification,
): Record<string, unknown> {
  if (!isMapLibreStyle(candidate)) {
    throw new Error("Edited style must be a MapLibre style document with version 8, sources, and layers.");
  }
  const normalized = cloneJson(sourceStyle) as unknown as Record<string, unknown>;
  for (const key of STYLE_PRESENTATION_KEYS) {
    copyOptionalKey(normalized, candidate, key);
  }
  normalized.sources = cloneJson(sourceStyle.sources);
  normalized.layers = normalizeStyleLayers(candidate["layers"] as unknown[], sourceStyle.layers);
  return normalized;
}

function normalizeStyleLayers(
  candidateLayers: unknown[],
  sourceLayers: HonuaStyleSpecification["layers"],
): Record<string, unknown>[] {
  const candidateById = indexLayersById(candidateLayers, "edited");
  const sourceById = indexLayersById(sourceLayers, "source");

  if (candidateById.size !== sourceById.size) {
    throw new Error("Edited style must preserve the original render layer set.");
  }

  const normalizedLayers: Record<string, unknown>[] = [];
  for (const sourceLayer of sourceLayers) {
    const editedLayer = candidateById.get(sourceLayer.id);
    if (!editedLayer) {
      throw new Error(`Edited style must preserve render layer: ${sourceLayer.id}`);
    }
    const normalizedLayer = cloneJson(sourceLayer) as unknown as Record<string, unknown>;
    for (const key of LAYER_PRESENTATION_KEYS) {
      copyOptionalKey(normalizedLayer, editedLayer, key);
    }
    normalizedLayers.push(normalizedLayer);
  }

  for (const editedLayerId of candidateById.keys()) {
    if (!sourceById.has(editedLayerId)) {
      throw new Error(`Edited style cannot add render layer: ${editedLayerId}`);
    }
  }

  return normalizedLayers;
}

function indexLayersById(layers: unknown[], label: string): Map<string, Record<string, unknown>> {
  const indexed = new Map<string, Record<string, unknown>>();
  for (const layer of layers) {
    if (!isRecord(layer) || typeof layer["id"] !== "string") {
      throw new Error(`Edited style contains an invalid ${label} render layer.`);
    }
    const layerId = layer["id"];
    if (indexed.has(layerId)) {
      throw new Error(`Edited style contains a duplicate ${label} render layer: ${layerId}`);
    }
    indexed.set(layerId, layer);
  }
  return indexed;
}

function copyOptionalKey(target: Record<string, unknown>, source: Record<string, unknown>, key: string): void {
  if (Object.prototype.hasOwnProperty.call(source, key)) {
    target[key] = cloneJson(source[key]);
  } else {
    delete target[key];
  }
}

function isMapLibreCompatibleLayer(layer: OperationalLayer): boolean {
  return (
    layer.layerType === "honua-feature" ||
    layer.layerType === "honua-vector-tile" ||
    layer.layerType === "honua-raster-tile" ||
    layer.layerType === "geojson" ||
    layer.layerType === "ogc-tiles"
  );
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return !!value && typeof value === "object" && !Array.isArray(value);
}

function cloneJson<T>(value: T): T {
  if (typeof structuredClone === "function") return structuredClone(value);
  return JSON.parse(JSON.stringify(value)) as T;
}
