import { describe, expect, it } from "vitest";
import { deriveFeatureId, findFeatureById, findFeatureIndexInSource } from "../../src/viewer/feature-id.js";
import type { PortalGeoJsonFeature } from "../../src/viewer/types.js";

const idless = (lon: number, name: string): PortalGeoJsonFeature => ({
  type: "Feature",
  properties: { name },
  geometry: { type: "Point", coordinates: [lon, 0] },
});

describe("deriveFeatureId", () => {
  it("prefers Feature.id over OBJECTID-style properties", () => {
    const feature: PortalGeoJsonFeature = {
      type: "Feature",
      id: "explicit",
      properties: { OBJECTID: 7 },
      geometry: null,
    };
    expect(deriveFeatureId("layer", feature, 0)).toBe("explicit");
  });

  it("falls back to OBJECTID/objectid/id/ID before the positional id", () => {
    expect(deriveFeatureId("layer", { type: "Feature", properties: { OBJECTID: 9 }, geometry: null }, 3)).toBe("9");
    expect(deriveFeatureId("layer", { type: "Feature", properties: { id: "abc" }, geometry: null }, 3)).toBe("abc");
  });

  it("uses layerId-index only when the feature has no usable id at all", () => {
    expect(deriveFeatureId("districts", { type: "Feature", properties: {}, geometry: null }, 4)).toBe("districts-4");
  });
});

describe("findFeatureIndexInSource", () => {
  it("recovers the source index for an id-less feature by content match", () => {
    const features = [idless(0, "alpha"), idless(1, "beta"), idless(2, "gamma")];
    // MapLibre returns a copied feature object — same content, different reference.
    const clicked = idless(2, "gamma");
    expect(findFeatureIndexInSource(features, clicked)).toBe(2);
  });

  it("ensures id-less map clicks share the table path's derived id", () => {
    const features = [idless(0, "alpha"), idless(1, "beta"), idless(2, "gamma")];
    const layerId = "id-less";
    const clicked = idless(2, "gamma");

    const tableIds = features.map((feature, index) => deriveFeatureId(layerId, feature, index));
    const mapIndex = findFeatureIndexInSource(features, clicked);
    const mapId = deriveFeatureId(layerId, clicked, mapIndex);

    expect(mapId).toBe(tableIds[2]);
    expect(new Set(tableIds).size).toBe(features.length);
  });

  it("matches by Feature.id when present, even if properties differ", () => {
    const features: PortalGeoJsonFeature[] = [
      { type: "Feature", id: "a", properties: { v: 1 }, geometry: null },
      { type: "Feature", id: "b", properties: { v: 2 }, geometry: null },
    ];
    const clicked: PortalGeoJsonFeature = {
      type: "Feature",
      id: "b",
      properties: { v: 99 },
      geometry: null,
    };
    expect(findFeatureIndexInSource(features, clicked)).toBe(1);
  });

  it("matches by OBJECTID when feature.id is missing", () => {
    const features: PortalGeoJsonFeature[] = [
      { type: "Feature", properties: { OBJECTID: 100 }, geometry: null },
      { type: "Feature", properties: { OBJECTID: 200 }, geometry: null },
    ];
    const clicked: PortalGeoJsonFeature = { type: "Feature", properties: { OBJECTID: 200 }, geometry: null };
    expect(findFeatureIndexInSource(features, clicked)).toBe(1);
  });

  it("falls back to 0 when nothing matches", () => {
    const features = [idless(0, "alpha")];
    expect(findFeatureIndexInSource(features, idless(99, "zulu"))).toBe(0);
  });
});

describe("findFeatureById", () => {
  it("rehydrates the same feature whether the id came from the table or the map", () => {
    const features = [idless(0, "alpha"), idless(1, "beta"), idless(2, "gamma")];
    const layerId = "id-less";
    const clicked = idless(1, "beta");
    const mapIndex = findFeatureIndexInSource(features, clicked);
    const mapId = deriveFeatureId(layerId, clicked, mapIndex);
    const found = findFeatureById(features, layerId, mapId);
    expect(found?.index).toBe(1);
    expect(found?.feature.properties?.["name"]).toBe("beta");
  });
});
