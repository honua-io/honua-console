/**
 * Canonical service/API metadata registry.
 *
 * This module is the single source of truth for the closed `format` token set,
 * the well-known `conformsTo` URIs each token asserts, the human-readable
 * display labels for pills, and the Honua-curated documentation fallback paths
 * used when an item ships no `describedBy` URL.
 *
 * The JSON Schema enums on `$defs.ServiceLink.format` and
 * `$defs.ServiceLink.conformsTo` mirror these constants. A schema/TS parity
 * test in `src/contracts/__tests__/conforms-to.test.ts` and
 * `src/catalog/__tests__/schema-validation.test.ts` keeps the two in sync.
 *
 * Adding a new family is additive (patch bump in the schema `$id`): ship a new
 * `SERVICE_FORMATS` entry with paired `CONFORMS_TO_URIS`, `FORMAT_DISPLAY`,
 * and `FORMAT_FALLBACK_DOCS` rows, then mirror the change in the schema.
 */

export const SERVICE_FORMATS = [
  "Honua:Portal:v1",
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

export const CONFORMS_TO_URIS: Record<ServiceFormat, readonly string[]> = {
  "Honua:Portal:v1": ["https://schemas.honua.io/content-item/v1"],
  "Honua:API:v1": ["https://schemas.honua.io/honua-api/v1"],
  "OGC:API:Features": [
    "http://www.opengis.net/spec/ogcapi-features-1/1.0/conf/core",
    "http://www.opengis.net/spec/ogcapi-features-1/1.0/conf/oas30",
    "http://www.opengis.net/spec/ogcapi-features-1/1.0/conf/geojson",
  ],
  "OGC:API:Tiles": ["http://www.opengis.net/spec/ogcapi-tiles-1/1.0/conf/core"],
  "OGC:WMS:1.3.0": ["http://www.opengis.net/spec/wms/1.3.0", "http://www.opengis.net/def/serviceType/ogc/wms/1.3.0"],
  "OGC:WFS:2.0": ["http://www.opengis.net/spec/wfs/2.0", "http://www.opengis.net/def/serviceType/ogc/wfs/2.0"],
  "OGC:STAC:1.0": ["https://api.stacspec.org/v1.0.0/core"],
  "GeoServices:FeatureService": ["https://developers.arcgis.com/rest/services-reference/feature-service.htm"],
  "GeoServices:MapService": ["https://developers.arcgis.com/rest/services-reference/map-service.htm"],
  "GeoServices:ImageService": ["https://developers.arcgis.com/rest/services-reference/image-service.htm"],
  "GeoServices:VectorTileService": ["https://developers.arcgis.com/rest/services-reference/vector-tile-service.htm"],
  "GeoServices:TileService": ["https://developers.arcgis.com/rest/services-reference/tile-map-service.htm"],
};

export const FORMAT_DISPLAY: Record<ServiceFormat, string> = {
  "Honua:Portal:v1": "Honua Portal Item",
  "Honua:API:v1": "Honua API",
  "OGC:API:Features": "OGC API Features",
  "OGC:API:Tiles": "OGC API Tiles",
  "OGC:WMS:1.3.0": "OGC WMS 1.3.0",
  "OGC:WFS:2.0": "OGC WFS 2.0",
  "OGC:STAC:1.0": "STAC 1.0",
  "GeoServices:FeatureService": "GeoServices FeatureService",
  "GeoServices:MapService": "GeoServices MapService",
  "GeoServices:ImageService": "GeoServices ImageService",
  "GeoServices:VectorTileService": "GeoServices VectorTileService",
  "GeoServices:TileService": "GeoServices TileService",
};

/**
 * Honua-curated documentation route used when a {@link ServiceLink} ships no
 * `describedBy` URL. `null` is meaningful: it marks formats for which Honua
 * has not curated a fallback yet, exercising the empty-state branch on the
 * renderer side rather than producing a broken link.
 */
export const FORMAT_FALLBACK_DOCS: Record<ServiceFormat, string | null> = {
  "Honua:Portal:v1": "/docs/api/honua-portal",
  "Honua:API:v1": null,
  "OGC:API:Features": "/docs/api/ogc-features",
  "OGC:API:Tiles": "/docs/api/ogc-tiles",
  "OGC:WMS:1.3.0": "/docs/api/wms",
  "OGC:WFS:2.0": "/docs/api/wfs",
  "OGC:STAC:1.0": "/docs/api/stac",
  "GeoServices:FeatureService": "/docs/api/geoservices-feature",
  "GeoServices:MapService": "/docs/api/geoservices-map",
  "GeoServices:ImageService": "/docs/api/geoservices-image",
  "GeoServices:VectorTileService": "/docs/api/geoservices-vector-tile",
  "GeoServices:TileService": "/docs/api/geoservices-tile",
};

/**
 * Short labels for the well-known conformance URIs. Keys are URIs from
 * {@link CONFORMS_TO_URIS}; missing keys fall back to the URI itself.
 */
export const CONFORMS_TO_LABEL: Record<string, string> = {
  "https://schemas.honua.io/content-item/v1": "Honua Portal Item v1",
  "https://schemas.honua.io/honua-api/v1": "Honua API v1",
  "http://www.opengis.net/spec/ogcapi-features-1/1.0/conf/core": "Features Core",
  "http://www.opengis.net/spec/ogcapi-features-1/1.0/conf/oas30": "Features OAS30",
  "http://www.opengis.net/spec/ogcapi-features-1/1.0/conf/geojson": "Features GeoJSON",
  "http://www.opengis.net/spec/ogcapi-tiles-1/1.0/conf/core": "Tiles Core",
  "http://www.opengis.net/spec/wms/1.3.0": "WMS 1.3.0",
  "http://www.opengis.net/def/serviceType/ogc/wms/1.3.0": "WMS Service Type",
  "http://www.opengis.net/spec/wfs/2.0": "WFS 2.0",
  "http://www.opengis.net/def/serviceType/ogc/wfs/2.0": "WFS Service Type",
  "https://api.stacspec.org/v1.0.0/core": "STAC Core",
  "https://developers.arcgis.com/rest/services-reference/feature-service.htm": "FeatureService",
  "https://developers.arcgis.com/rest/services-reference/map-service.htm": "MapService",
  "https://developers.arcgis.com/rest/services-reference/image-service.htm": "ImageService",
  "https://developers.arcgis.com/rest/services-reference/vector-tile-service.htm": "VectorTileService",
  "https://developers.arcgis.com/rest/services-reference/tile-map-service.htm": "TileService",
};

/**
 * Flattened, de-duplicated list of every well-known conformance URI. The JSON
 * Schema enum on `ServiceLink.conformsTo` mirrors this list.
 */
export const ALL_CONFORMS_TO_URIS: readonly string[] = (() => {
  const seen = new Set<string>();
  for (const format of SERVICE_FORMATS) {
    for (const uri of CONFORMS_TO_URIS[format]) seen.add(uri);
  }
  return Object.freeze([...seen].sort());
})();

export function isServiceFormat(value: string): value is ServiceFormat {
  return (SERVICE_FORMATS as readonly string[]).includes(value);
}

export function conformanceLabel(uri: string): string {
  return CONFORMS_TO_LABEL[uri] ?? uri;
}
