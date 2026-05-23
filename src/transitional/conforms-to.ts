/**
 * Transitional copy of `honua-portal/src/contracts/conforms-to.ts`. Retire
 * once `@honua/sdk-js` publishes the browser-safe catalog contract
 * (honua-sdk-js#225). Tracked by docs/studio/PORT.md.
 */

export const SERVICE_FORMATS = [
  "Honua:Portal:v1",
  "Honua:Console:v1",
  "Honua:API:v1",
  "OGC:API:Features",
  "OGC:API:Tiles",
  "OGC:WMS:1.3.0",
  "OGC:WFS:2.0",
  "OGC:STAC:1.0",
  "GeoServices:FeatureService",
  "GeoServices:MapService",
  "GeoServices:ImageService",
  "GeoServices:VectorTileService",
  "GeoServices:TileService",
] as const;

export type ServiceFormat = (typeof SERVICE_FORMATS)[number];

export function isServiceFormat(value: string): value is ServiceFormat {
  return (SERVICE_FORMATS as readonly string[]).includes(value);
}
