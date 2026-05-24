/**
 * Shared types for the portal map viewer slice (honua-portal#13).
 *
 * The portal viewer leans on `@honua/sdk-js` for the underlying style
 * specification. `PortalViewerItem` is the portal-side wrapper around a
 * Honua style spec with the metadata fields the catalog/item page surfaces
 * to end users (title, summary, owner, license, etc.). The catalog ticket
 * (#12) and the publish handoff (#11) are responsible for producing these
 * items; here the viewer only consumes them.
 */

import type { HonuaStyleSpecification } from "@honua/sdk-js";
import type { HonuaPopupConfig, HonuaPopupFieldInfo } from "@honua/sdk-js/webmap";

export interface PortalGeoJsonFeature {
  type: "Feature";
  id?: string | number;
  properties: Record<string, unknown> | null;
  geometry: PortalGeoJsonGeometry | null;
}

export type PortalGeoJsonGeometry =
  | { type: "Point"; coordinates: [number, number] }
  | { type: "MultiPoint"; coordinates: [number, number][] }
  | { type: "LineString"; coordinates: [number, number][] }
  | { type: "MultiLineString"; coordinates: [number, number][][] }
  | { type: "Polygon"; coordinates: [number, number][][] }
  | { type: "MultiPolygon"; coordinates: [number, number][][][] };

export interface PortalViewerItemMetadata {
  /** Stable identifier the catalog uses for this portal item. */
  id: string;
  title: string;
  summary?: string;
  description?: string;
  owner?: string;
  organization?: string;
  license?: string;
  attribution?: string;
  tags?: string[];
  /** ISO-8601 timestamp the item was last modified. */
  modified?: string;
  /** Source service URL when the layer is backed by a Honua service. */
  serviceUrl?: string;
  /** Detail page URL (typically rendered by the catalog item page). */
  itemUrl?: string;
  /** Coordinate system the published layer authored its data in. */
  coordinateSystem?: string;
}

export interface PortalViewerLayer {
  /** Unique identifier within the portal item. Used by URL state. */
  id: string;
  /** Human readable name surfaced in the layer list. */
  name: string;
  /** Optional one-line summary surfaced in the layer list. */
  summary?: string;
  /** MapLibre source name in the style. */
  sourceId: string;
  /**
   * Optional SDK-backed source descriptor. When present, the viewer loads
   * feature rows through @honua/sdk-js and hydrates the MapLibre source.
   */
  sdkSource?: PortalViewerSdkSource;
  /**
   * MapLibre layer ids that participate in this logical layer.
   *
   * Polygon services usually need a fill + outline pair; line and point
   * services typically have one. The viewer toggles them as a group.
   */
  renderLayerIds: string[];
  /** Whether this layer is on initially. */
  defaultVisible: boolean;
  /** Initial opacity in the [0, 1] range. */
  defaultOpacity: number;
  /** Inline legend rows the sidebar can render without a server round-trip. */
  legend: PortalLegendRow[];
  /**
   * Optional popup configuration. When provided, popups + the detail panel
   * use these field labels. When omitted, the viewer falls back to showing
   * every property in the feature.
   */
  popup?: HonuaPopupConfig;
  /**
   * Initial subset of feature properties to surface in the tabular detail
   * panel, in display order. When omitted, the viewer derives columns from
   * the first feature in the source.
   */
  detailFields?: PortalDetailField[];
  /** When true, treat the underlying source as queryable for the detail table. */
  inspectable: boolean;
  /** Optional MapLibre layer id used for click-to-inspect (defaults to first renderLayerId). */
  interactiveLayerId?: string;
}

export interface PortalViewerSdkSource {
  /** Stable source id passed into the SDK dataset contract. */
  id: string;
  /** Portal item this SDK source belongs to. */
  itemId: string;
  /** MapLibre GeoJSON source that receives queried SDK features. */
  sourceId: string;
  /** Browser-safe Honua/GeoServices base URL used to construct HonuaClient. */
  baseUrl: string;
  /** Fully qualified protocol endpoint exposed by the catalog item. */
  endpointUrl: string;
  /** GeoServices service identifier, e.g. "city/parcels". */
  serviceId: string;
  /** GeoServices FeatureServer layer id to query. */
  layerId: number;
  /** Optional user-facing attribution copied onto the style source. */
  attribution?: string;
  /** Optional max rows for the first viewer table/query page. */
  limit?: number;
}

export interface PortalLegendRow {
  label: string;
  color: string;
  shape: "fill" | "line" | "point";
}

export interface PortalDetailField {
  name: string;
  label?: string;
}

export interface PortalViewerItem {
  metadata: PortalViewerItemMetadata;
  /**
   * The Honua style specification the viewer renders. Native MapLibre
   * sources (geojson, raster, vector) are supported alongside Honua
   * service sources via the SDK's source factory.
   */
  style: HonuaStyleSpecification;
  /** Layer panel entries surfaced to the user. */
  layers: PortalViewerLayer[];
  /** Initial map view. URL state overrides these on hash hydration. */
  initialView: PortalViewerInitialView;
}

export interface PortalViewerInitialView {
  center: [number, number];
  zoom: number;
  /** Optional bounds the viewer can fit to instead of using center/zoom. */
  bounds?: [number, number, number, number];
}

export interface SelectedFeature {
  layerId: string;
  /**
   * Stable feature identifier when available. For GeoJSON sources without
   * an `id` field, the viewer composes one from the layer id + feature
   * index so URL state stays deterministic.
   */
  featureId: string;
}

export interface ViewerState {
  /** Map view center as [longitude, latitude]. */
  center: [number, number];
  /** Map zoom level. */
  zoom: number;
  /** Visible layer ids in render order (bottom → top). */
  visibleLayerIds: string[];
  /** Currently selected feature, if any. */
  selected?: SelectedFeature;
}

export type PortalLayerOrdering = readonly string[];

export interface ResolveDetailFieldsOptions {
  layer: PortalViewerLayer;
  sample?: PortalGeoJsonFeature;
}

export function resolveDetailFields(options: ResolveDetailFieldsOptions): PortalDetailField[] {
  const { layer, sample } = options;
  if (layer.detailFields && layer.detailFields.length > 0) return layer.detailFields;

  if (layer.popup?.fieldInfos && layer.popup.fieldInfos.length > 0) {
    return layer.popup.fieldInfos
      .filter((info: HonuaPopupFieldInfo) => info.visible !== false)
      .map((info: HonuaPopupFieldInfo) => ({ name: info.fieldName, label: info.label ?? info.fieldName }));
  }

  if (sample?.properties && typeof sample.properties === "object") {
    return Object.keys(sample.properties).map((name) => ({ name, label: name }));
  }

  return [];
}
