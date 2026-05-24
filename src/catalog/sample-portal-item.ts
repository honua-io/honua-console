/**
 * Sample portal item used until the catalog (#12) and publish handoff
 * (#11) are wired end-to-end. Two layers backed by inline GeoJSON sources
 * exercise the polygon and point render paths plus popup configuration:
 *
 *   - "districts" — policy boundary polygons with a labelled popup
 *   - "field-stations" — point markers with attribute table affordances
 *
 * The fixture is shaped exactly like a real portal item the catalog will
 * eventually emit: metadata + a HonuaStyleSpecification. The viewer code
 * has no special path for this fixture.
 */

import type { PortalGeoJsonFeature, PortalViewerItem } from "../viewer/types.js";

const DISTRICTS: PortalGeoJsonFeature[] = [
  {
    type: "Feature",
    id: "district-east",
    properties: {
      NAME: "East District",
      OBJECTID: 1,
      population: 184_220,
      land_area_km2: 38.7,
      stewards: "Honua Field Office East",
      established: "2024-09-01",
    },
    geometry: {
      type: "Polygon",
      coordinates: [
        [
          [-157.83, 21.305],
          [-157.78, 21.305],
          [-157.78, 21.34],
          [-157.83, 21.34],
          [-157.83, 21.305],
        ],
      ],
    },
  },
  {
    type: "Feature",
    id: "district-west",
    properties: {
      NAME: "West District",
      OBJECTID: 2,
      population: 92_410,
      land_area_km2: 26.1,
      stewards: "Honua Field Office West",
      established: "2024-09-01",
    },
    geometry: {
      type: "Polygon",
      coordinates: [
        [
          [-157.89, 21.27],
          [-157.84, 21.27],
          [-157.84, 21.305],
          [-157.89, 21.305],
          [-157.89, 21.27],
        ],
      ],
    },
  },
];

const FIELD_STATIONS: PortalGeoJsonFeature[] = [
  {
    type: "Feature",
    id: "station-makiki",
    properties: {
      NAME: "Makiki Watershed Station",
      OBJECTID: 100,
      district: "East District",
      sensor_count: 12,
      uptime_pct: 99.4,
      last_visit: "2026-04-18",
    },
    geometry: { type: "Point", coordinates: [-157.825, 21.32] },
  },
  {
    type: "Feature",
    id: "station-pearl",
    properties: {
      NAME: "Pearl Harbor Tide Station",
      OBJECTID: 101,
      district: "West District",
      sensor_count: 8,
      uptime_pct: 97.1,
      last_visit: "2026-04-22",
    },
    geometry: { type: "Point", coordinates: [-157.872, 21.288] },
  },
  {
    type: "Feature",
    id: "station-kahala",
    properties: {
      NAME: "Kahala Coastal Station",
      OBJECTID: 102,
      district: "East District",
      sensor_count: 5,
      uptime_pct: 100.0,
      last_visit: "2026-04-29",
    },
    geometry: { type: "Point", coordinates: [-157.79, 21.273] },
  },
];

export const SAMPLE_PORTAL_ITEM_ID = "sample-published-layer";

export function buildSamplePortalItem(): PortalViewerItem {
  return {
    metadata: {
      id: SAMPLE_PORTAL_ITEM_ID,
      title: "Honolulu watersheds (sample)",
      summary: "Sample published portal item used for the viewer MVP slice.",
      description:
        "Two layers — district policy boundaries and field stations — published as a portal item to exercise the layer list, popups, feature inspection, and the URL state contract.",
      owner: "Honua Demo Org",
      organization: "Honua",
      license: "CC-BY-4.0",
      attribution: "Honua sample data",
      tags: ["sample", "watershed", "field-stations"],
      modified: "2026-05-01T00:00:00Z",
      coordinateSystem: "EPSG:4326",
      itemUrl: "/items/sample-published-layer",
    },
    style: {
      version: 8,
      sources: {
        "demotiles-basemap": {
          type: "raster",
          tiles: ["https://demotiles.maplibre.org/tiles/{z}/{x}/{y}.png"],
          tileSize: 256,
          attribution: "© MapLibre demo tiles",
        },
        "districts-source": {
          type: "geojson",
          data: { type: "FeatureCollection", features: DISTRICTS },
          attribution: "Honua sample data",
        },
        "field-stations-source": {
          type: "geojson",
          data: { type: "FeatureCollection", features: FIELD_STATIONS },
          attribution: "Honua sample data",
        },
      },
      layers: [
        {
          id: "demotiles-basemap-layer",
          type: "raster",
          source: "demotiles-basemap",
        },
        {
          id: "districts-fill",
          type: "fill",
          source: "districts-source",
          paint: {
            "fill-color": "#4ec9b0",
            "fill-opacity": 0.32,
          },
        },
        {
          id: "districts-outline",
          type: "line",
          source: "districts-source",
          paint: {
            "line-color": "#22806f",
            "line-width": 2,
          },
        },
        {
          id: "field-stations-circles",
          type: "circle",
          source: "field-stations-source",
          paint: {
            "circle-radius": 7,
            "circle-color": "#f3b562",
            "circle-stroke-color": "#1c2a36",
            "circle-stroke-width": 1.5,
          },
        },
      ],
    },
    layers: [
      {
        id: "districts",
        name: "Districts",
        summary: "Policy boundaries",
        sourceId: "districts-source",
        renderLayerIds: ["districts-fill", "districts-outline"],
        interactiveLayerId: "districts-fill",
        defaultVisible: true,
        defaultOpacity: 0.32,
        inspectable: true,
        legend: [{ label: "District", color: "#4ec9b0", shape: "fill" }],
        popup: {
          title: "{NAME}",
          fieldInfos: [
            { fieldName: "NAME", label: "District", visible: true },
            { fieldName: "population", label: "Population", visible: true },
            { fieldName: "land_area_km2", label: "Land area (km²)", visible: true },
            { fieldName: "stewards", label: "Steward", visible: true },
            { fieldName: "established", label: "Established", visible: true },
            { fieldName: "OBJECTID", label: "OBJECTID", visible: false },
          ],
          mediaInfos: [],
        },
        detailFields: [
          { name: "NAME", label: "District" },
          { name: "population", label: "Population" },
          { name: "land_area_km2", label: "Area (km²)" },
          { name: "stewards", label: "Steward" },
          { name: "established", label: "Established" },
        ],
      },
      {
        id: "field-stations",
        name: "Field stations",
        summary: "Sensor station network",
        sourceId: "field-stations-source",
        renderLayerIds: ["field-stations-circles"],
        interactiveLayerId: "field-stations-circles",
        defaultVisible: true,
        defaultOpacity: 1,
        inspectable: true,
        legend: [{ label: "Station", color: "#f3b562", shape: "point" }],
        popup: {
          title: "{NAME}",
          fieldInfos: [
            { fieldName: "NAME", label: "Station", visible: true },
            { fieldName: "district", label: "District", visible: true },
            { fieldName: "sensor_count", label: "Sensors", visible: true },
            { fieldName: "uptime_pct", label: "Uptime (%)", visible: true },
            { fieldName: "last_visit", label: "Last visit", visible: true },
            { fieldName: "OBJECTID", label: "OBJECTID", visible: false },
          ],
          mediaInfos: [],
        },
        detailFields: [
          { name: "NAME", label: "Station" },
          { name: "district", label: "District" },
          { name: "sensor_count", label: "Sensors" },
          { name: "uptime_pct", label: "Uptime (%)" },
          { name: "last_visit", label: "Last visit" },
        ],
      },
    ],
    initialView: {
      center: [-157.84, 21.3],
      zoom: 11.5,
      bounds: [-157.91, 21.26, -157.77, 21.35],
    },
  };
}

export function getSampleSourceFeatures(item: PortalViewerItem, sourceId: string): PortalGeoJsonFeature[] {
  const source = item.style.sources[sourceId];
  if (!source || typeof source !== "object" || (source as { type?: string }).type !== "geojson") return [];
  const data = (source as { data?: unknown }).data;
  if (!data || typeof data !== "object") return [];
  const features = (data as { features?: unknown }).features;
  if (!Array.isArray(features)) return [];
  return features as PortalGeoJsonFeature[];
}
