import {
  type HonuaTypedFeature,
  PROTOCOL_DEFAULT_CAPABILITIES,
  type SourceDescriptor,
  createDataset,
} from "@honua/sdk-js/honua";

import type { Session } from "../auth/types.js";
import type { ContentItem } from "../contracts/content-item.js";
import { createPortalViewerSdkClient } from "./sdk-client.js";
import type { PortalGeoJsonFeature, PortalGeoJsonGeometry, PortalViewerSdkSource } from "./types.js";

export type PortalViewerSdkFeatureLoader = (source: PortalViewerSdkSource) => Promise<readonly PortalGeoJsonFeature[]>;

export interface ParseGeoServicesFeatureSourceOptions {
  id?: string;
  sourceId?: string;
  limit?: number;
}

export interface CreatePortalViewerSdkFeatureLoaderOptions {
  session?: Session;
  fetchFn?: typeof fetch;
}

export function parseGeoServicesFeatureSource(
  item: ContentItem,
  options: ParseGeoServicesFeatureSourceOptions = {},
): PortalViewerSdkSource | null {
  const endpointUrl = item.endpoints.geoservices?.accessURL;
  if (!endpointUrl) return null;

  const parsed = parseFeatureServerEndpoint(endpointUrl);
  if (!parsed) return null;

  const targetLayerId = item.target.type === "layer" ? item.target.layerId : undefined;
  const layerId = targetLayerId ?? parsed.layerId ?? 0;

  return {
    id: options.id ?? `${item.id}:feature-service:${layerId}`,
    itemId: item.id,
    sourceId: options.sourceId ?? item.slug ?? item.id,
    baseUrl: parsed.baseUrl,
    endpointUrl,
    serviceId: parsed.serviceId,
    layerId,
    attribution: item.attribution ?? undefined,
    limit: options.limit,
  };
}

export function createPortalViewerSdkFeatureLoader(
  options: CreatePortalViewerSdkFeatureLoaderOptions = {},
): PortalViewerSdkFeatureLoader {
  return async (source) => {
    const client = createPortalViewerSdkClient({
      baseUrl: source.baseUrl,
      session: options.session,
      fetchFn: options.fetchFn,
    });
    const descriptor: SourceDescriptor = {
      id: source.id,
      protocol: "geoservices-feature-service",
      locator: {
        url: source.endpointUrl,
        serviceId: source.serviceId,
        layerId: source.layerId,
      },
      capabilities: PROTOCOL_DEFAULT_CAPABILITIES["geoservices-feature-service"],
      attribution: source.attribution,
    };
    const dataset = createDataset({
      id: `portal-viewer:${source.itemId}`,
      client,
      sources: [descriptor],
      skipCompatibilityCheck: true,
    });
    const sdkSource = dataset.source<Record<string, unknown>>(source.id);
    if (!sdkSource) throw new Error(`SDK source "${source.id}" was not registered`);

    const result = await sdkSource.query({
      where: "1=1",
      outFields: ["*"],
      returnGeometry: true,
      outSr: 4326,
      pagination: { limit: source.limit ?? 250 },
    });

    return result.features.map(toPortalGeoJsonFeature);
  };
}

function parseFeatureServerEndpoint(endpointUrl: string): {
  baseUrl: string;
  serviceId: string;
  layerId?: number;
} | null {
  let url: URL;
  try {
    url = new URL(endpointUrl);
  } catch {
    return null;
  }

  const segments = url.pathname.split("/").filter(Boolean);
  const restIndex = segments.findIndex((segment) => segment.toLowerCase() === "rest");
  if (restIndex < 0 || segments[restIndex + 1]?.toLowerCase() !== "services") return null;

  const featureServerIndex = segments.findIndex((segment, index) => {
    return index > restIndex + 1 && segment.toLowerCase() === "featureserver";
  });
  if (featureServerIndex < 0) return null;

  const serviceSegments = segments.slice(restIndex + 2, featureServerIndex);
  if (serviceSegments.length === 0) return null;

  const basePath = segments
    .slice(0, restIndex)
    .map((segment) => `/${segment}`)
    .join("");
  const layerSegment = segments[featureServerIndex + 1];
  const parsedLayerId = layerSegment ? Number.parseInt(layerSegment, 10) : undefined;

  return {
    baseUrl: `${url.origin}${basePath}`,
    serviceId: serviceSegments.map((segment) => decodeURIComponent(segment)).join("/"),
    layerId: Number.isInteger(parsedLayerId) ? parsedLayerId : undefined,
  };
}

function toPortalGeoJsonFeature(
  feature: HonuaTypedFeature<Record<string, unknown>>,
  index: number,
): PortalGeoJsonFeature {
  const properties = isRecord(feature.attributes) ? feature.attributes : {};
  const id = deriveFeatureId(properties, index);
  return {
    type: "Feature",
    ...(id === undefined ? {} : { id }),
    properties,
    geometry: toGeoJsonGeometry(feature.geometry),
  };
}

function deriveFeatureId(properties: Record<string, unknown>, index: number): string | number | undefined {
  for (const key of ["OBJECTID", "ObjectID", "objectid", "id", "ID"]) {
    const value = properties[key];
    if (typeof value === "string" || typeof value === "number") return value;
  }
  return index;
}

function toGeoJsonGeometry(value: unknown): PortalGeoJsonGeometry | null {
  if (!isRecord(value)) return null;

  const geoJson = toNativeGeoJsonGeometry(value);
  if (geoJson) return geoJson;

  const x = value["x"];
  const y = value["y"];
  if (typeof x === "number" && typeof y === "number") {
    return { type: "Point", coordinates: [x, y] };
  }

  const rings = toLines(value["rings"]);
  if (rings.length > 0) return { type: "Polygon", coordinates: rings };

  const paths = toLines(value["paths"]);
  if (paths.length === 1) return { type: "LineString", coordinates: paths[0] };
  if (paths.length > 1) return { type: "MultiLineString", coordinates: paths };

  const points = toCoordinates(value["points"]);
  if (points.length > 0) return { type: "MultiPoint", coordinates: points };

  const envelope = toEnvelopePolygon(value);
  if (envelope) return envelope;

  return null;
}

function toNativeGeoJsonGeometry(value: Record<string, unknown>): PortalGeoJsonGeometry | null {
  const type = value["type"];
  const coordinates = value["coordinates"];
  if (typeof type !== "string" || !Array.isArray(coordinates)) return null;
  if (
    type === "Point" ||
    type === "MultiPoint" ||
    type === "LineString" ||
    type === "MultiLineString" ||
    type === "Polygon" ||
    type === "MultiPolygon"
  ) {
    return { type, coordinates } as PortalGeoJsonGeometry;
  }
  return null;
}

function toEnvelopePolygon(value: Record<string, unknown>): PortalGeoJsonGeometry | null {
  const xmin = value["xmin"];
  const ymin = value["ymin"];
  const xmax = value["xmax"];
  const ymax = value["ymax"];
  if (typeof xmin !== "number" || typeof ymin !== "number" || typeof xmax !== "number" || typeof ymax !== "number") {
    return null;
  }

  return {
    type: "Polygon",
    coordinates: [
      [
        [xmin, ymin],
        [xmax, ymin],
        [xmax, ymax],
        [xmin, ymax],
        [xmin, ymin],
      ],
    ],
  };
}

function toLines(value: unknown): [number, number][][] {
  if (!Array.isArray(value)) return [];
  return value.map(toCoordinates).filter((coordinates) => coordinates.length > 0);
}

function toCoordinates(value: unknown): [number, number][] {
  if (!Array.isArray(value)) return [];
  return value.map(toCoordinate).filter((coordinate): coordinate is [number, number] => coordinate !== null);
}

function toCoordinate(value: unknown): [number, number] | null {
  if (!Array.isArray(value) || value.length < 2) return null;
  const [x, y] = value;
  if (typeof x !== "number" || typeof y !== "number") return null;
  return [x, y];
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return !!value && typeof value === "object" && !Array.isArray(value);
}
