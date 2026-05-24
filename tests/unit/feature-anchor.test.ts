import { describe, expect, it } from "vitest";
import { computeFeatureAnchor } from "../../src/viewer/feature-anchor.js";
import type { PortalGeoJsonFeature } from "../../src/viewer/types.js";

describe("computeFeatureAnchor", () => {
  it("returns the point itself for Point geometries", () => {
    const feature: PortalGeoJsonFeature = {
      type: "Feature",
      properties: {},
      geometry: { type: "Point", coordinates: [-157.8, 21.3] },
    };
    expect(computeFeatureAnchor(feature)).toEqual([-157.8, 21.3]);
  });

  it("uses the bounds center for a closed polygon ring (no closing-vertex bias)", () => {
    // Square ring with the standard duplicate closing vertex.
    const ring: [number, number][] = [
      [0, 0],
      [10, 0],
      [10, 10],
      [0, 10],
      [0, 0],
    ];
    const feature: PortalGeoJsonFeature = {
      type: "Feature",
      properties: {},
      geometry: { type: "Polygon", coordinates: [ring] },
    };
    expect(computeFeatureAnchor(feature)).toEqual([5, 5]);
  });

  it("aggregates across all polygons in a MultiPolygon", () => {
    const feature: PortalGeoJsonFeature = {
      type: "Feature",
      properties: {},
      geometry: {
        type: "MultiPolygon",
        coordinates: [
          [
            [
              [0, 0],
              [2, 0],
              [2, 2],
              [0, 2],
              [0, 0],
            ],
          ],
          [
            [
              [10, 10],
              [12, 10],
              [12, 12],
              [10, 12],
              [10, 10],
            ],
          ],
        ],
      },
    };
    expect(computeFeatureAnchor(feature)).toEqual([6, 6]);
  });

  it("places the anchor on the geometry when the polygon crosses the antimeridian", () => {
    const feature: PortalGeoJsonFeature = {
      type: "Feature",
      properties: {},
      geometry: {
        type: "Polygon",
        coordinates: [
          [
            [170, 0],
            [-170, 0],
            [-170, 10],
            [170, 10],
            [170, 0],
          ],
        ],
      },
    };
    const anchor = computeFeatureAnchor(feature);
    expect(anchor).toBeDefined();
    if (!anchor) return;
    // The bounds-center anchor must land in the [170, 180]∪[-180, -170] band,
    // not on the opposite hemisphere near longitude 0.
    expect(Math.abs(anchor[0]) > 170).toBe(true);
    expect(anchor[1]).toBe(5);
  });

  it("returns undefined for null geometry", () => {
    const feature: PortalGeoJsonFeature = { type: "Feature", properties: {}, geometry: null };
    expect(computeFeatureAnchor(feature)).toBeUndefined();
  });

  it("uses bounds center for LineString features", () => {
    const feature: PortalGeoJsonFeature = {
      type: "Feature",
      properties: {},
      geometry: {
        type: "LineString",
        coordinates: [
          [0, 0],
          [4, 4],
          [8, 0],
        ],
      },
    };
    expect(computeFeatureAnchor(feature)).toEqual([4, 2]);
  });
});
