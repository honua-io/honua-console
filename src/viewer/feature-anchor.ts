/**
 * Display anchor for popup placement and the table-row flyTo. Falls
 * back to the bounding-box center across all rings/parts so the anchor
 * is sensible for multipolygons and concave polygons, skips closing
 * duplicate vertices when collecting coordinates, and unwraps longitudes
 * when the geometry crosses the antimeridian.
 */

import type { PortalGeoJsonFeature, PortalGeoJsonGeometry } from "./types.js";

export function computeFeatureAnchor(feature: PortalGeoJsonFeature): [number, number] | undefined {
  const geometry = feature.geometry;
  if (!geometry) return undefined;
  if (geometry.type === "Point") return [geometry.coordinates[0], geometry.coordinates[1]];

  const coordinates = collectCoordinates(geometry);
  if (coordinates.length === 0) return undefined;
  return boundsCenter(coordinates);
}

function collectCoordinates(geometry: PortalGeoJsonGeometry): [number, number][] {
  switch (geometry.type) {
    case "Point":
      return [[geometry.coordinates[0], geometry.coordinates[1]]];
    case "MultiPoint":
      return geometry.coordinates.map(([x, y]) => [x, y]);
    case "LineString":
      return geometry.coordinates.map(([x, y]) => [x, y]);
    case "MultiLineString":
      return geometry.coordinates.flatMap((line) => line.map(([x, y]) => [x, y] as [number, number]));
    case "Polygon":
      return collectPolygonRings(geometry.coordinates);
    case "MultiPolygon":
      return geometry.coordinates.flatMap(collectPolygonRings);
    default:
      return [];
  }
}

function collectPolygonRings(rings: ReadonlyArray<ReadonlyArray<[number, number]>>): [number, number][] {
  const out: [number, number][] = [];
  for (const ring of rings) {
    if (ring.length === 0) continue;
    const last = ring.length - 1;
    const closes = ring.length > 1 && ring[0][0] === ring[last][0] && ring[0][1] === ring[last][1];
    const limit = closes ? last : ring.length;
    for (let i = 0; i < limit; i++) out.push([ring[i][0], ring[i][1]]);
  }
  return out;
}

function boundsCenter(coordinates: ReadonlyArray<[number, number]>): [number, number] {
  let minLat = Number.POSITIVE_INFINITY;
  let maxLat = Number.NEGATIVE_INFINITY;
  let minLon = Number.POSITIVE_INFINITY;
  let maxLon = Number.NEGATIVE_INFINITY;
  for (const [lon, lat] of coordinates) {
    if (lon < minLon) minLon = lon;
    if (lon > maxLon) maxLon = lon;
    if (lat < minLat) minLat = lat;
    if (lat > maxLat) maxLat = lat;
  }

  const lat = (minLat + maxLat) / 2;
  if (maxLon - minLon > 180) {
    let shiftedMin = Number.POSITIVE_INFINITY;
    let shiftedMax = Number.NEGATIVE_INFINITY;
    for (const [lon] of coordinates) {
      const shifted = lon < 0 ? lon + 360 : lon;
      if (shifted < shiftedMin) shiftedMin = shifted;
      if (shifted > shiftedMax) shiftedMax = shifted;
    }
    let centerLon = (shiftedMin + shiftedMax) / 2;
    if (centerLon > 180) centerLon -= 360;
    return [centerLon, lat];
  }
  return [(minLon + maxLon) / 2, lat];
}
