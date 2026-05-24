/**
 * Feature identifier helpers. Honua/Esri feature services return stable
 * `OBJECTID`s; GeoJSON sources may or may not. The portal viewer needs a
 * deterministic id so URL state can round-trip a selection across reloads,
 * so when a feature has neither a `Feature.id` nor an `OBJECTID`-style
 * property the viewer falls back to the feature's positional index in
 * its source. Map clicks, the sidebar detail, and the table all resolve
 * the position via `findFeatureIndexInSource` so id-less features still
 * share one stable id across selection paths.
 */

import type { PortalGeoJsonFeature } from "./types.js";

export function deriveFeatureId(layerId: string, feature: PortalGeoJsonFeature, fallbackIndex: number): string {
  if (feature.id !== undefined && feature.id !== null) return String(feature.id);

  const properties = feature.properties ?? {};
  const candidate = properties["OBJECTID"] ?? properties["objectid"] ?? properties["id"] ?? properties["ID"];
  if (candidate !== undefined && candidate !== null) return String(candidate);

  return `${layerId}-${fallbackIndex}`;
}

export function findFeatureById(
  features: ReadonlyArray<PortalGeoJsonFeature>,
  layerId: string,
  featureId: string,
): { feature: PortalGeoJsonFeature; index: number } | undefined {
  for (let i = 0; i < features.length; i++) {
    const candidate = features[i];
    if (deriveFeatureId(layerId, candidate, i) === featureId) {
      return { feature: candidate, index: i };
    }
  }
  return undefined;
}

/**
 * Locate a feature's index in its source. Map-click handlers receive
 * features from MapLibre that may not carry a positional index, so this
 * helper recovers the source index by id, then by `OBJECTID`-style
 * property id, then by a content signature of properties + geometry.
 */
export function findFeatureIndexInSource(
  features: ReadonlyArray<PortalGeoJsonFeature>,
  feature: PortalGeoJsonFeature,
): number {
  if (feature.id !== undefined && feature.id !== null) {
    const target = String(feature.id);
    for (let i = 0; i < features.length; i++) {
      const candidate = features[i];
      if (candidate.id !== undefined && candidate.id !== null && String(candidate.id) === target) return i;
    }
  }

  const props = feature.properties ?? {};
  const propertyId = props["OBJECTID"] ?? props["objectid"] ?? props["id"] ?? props["ID"];
  if (propertyId !== undefined && propertyId !== null) {
    const target = String(propertyId);
    for (let i = 0; i < features.length; i++) {
      const fp = features[i].properties ?? {};
      const candidate = fp["OBJECTID"] ?? fp["objectid"] ?? fp["id"] ?? fp["ID"];
      if (candidate !== undefined && candidate !== null && String(candidate) === target) return i;
    }
  }

  const targetSig = featureContentSignature(feature);
  if (targetSig) {
    for (let i = 0; i < features.length; i++) {
      if (featureContentSignature(features[i]) === targetSig) return i;
    }
  }
  return 0;
}

function featureContentSignature(feature: PortalGeoJsonFeature): string {
  try {
    return JSON.stringify({ properties: feature.properties ?? {}, geometry: feature.geometry ?? null });
  } catch {
    return "";
  }
}
