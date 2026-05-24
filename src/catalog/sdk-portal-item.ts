import type { ContentItem } from "../contracts/content-item.js";
import { parseGeoServicesFeatureSource } from "../viewer/sdk-feature-loader.js";
import type { PortalViewerItem } from "../viewer/types.js";
import { buildSamplePortalItem } from "./sample-portal-item.js";

type StyleLayer = PortalViewerItem["style"]["layers"][number];
type RenderableFeatureGeometry = "point" | "multipoint" | "polyline" | "polygon";

const PARCEL_DETAIL_FIELDS = [
  { name: "PARCEL_ID", label: "Parcel ID" },
  { name: "LAND_USE", label: "Land use" },
  { name: "OWNER_TYPE", label: "Owner type" },
  { name: "ASSESSED_VALUE", label: "Assessed value" },
] as const;

export function buildSdkBackedPortalItem(item: ContentItem): PortalViewerItem | null {
  const geometryType = item.target.type === "layer" ? item.target.geometryType : "polygon";
  if (!isRenderableFeatureGeometry(geometryType)) return null;

  const layerId = slugify(item.slug ?? item.id);
  const sourceId = `${layerId}-sdk-source`;
  const sdkSource = parseGeoServicesFeatureSource(item, {
    sourceId,
    id: `${item.id}:geoservices-feature`,
    limit: 250,
  });
  if (!sdkSource) return null;

  const sample = buildSamplePortalItem();
  const bounds: [number, number, number, number] | undefined = item.extent
    ? [item.extent.bbox[0], item.extent.bbox[1], item.extent.bbox[2], item.extent.bbox[3]]
    : undefined;
  const center: [number, number] = bounds
    ? [(bounds[0] + bounds[2]) / 2, (bounds[1] + bounds[3]) / 2]
    : sample.initialView.center;
  const renderLayers = buildRenderLayers({
    geometryType,
    layerId,
    sourceId,
  });

  return {
    metadata: {
      id: item.id,
      title: item.title,
      summary: item.summary,
      description: item.description,
      owner: item.owner.name,
      organization: item.owner.kind === "org" ? item.owner.name : undefined,
      license: item.license.name,
      attribution: item.attribution ?? undefined,
      tags: [...item.tags],
      modified: item.timestamps.modified,
      serviceUrl: serviceUrlFor(item),
      itemUrl: `/catalog/${encodeURIComponent(item.slug ?? item.id)}`,
      coordinateSystem: item.nativeCrs ?? item.extent?.crs ?? sample.metadata.coordinateSystem,
    },
    style: {
      version: 8,
      sources: {
        "demotiles-basemap": sample.style.sources["demotiles-basemap"],
        [sourceId]: {
          type: "geojson",
          data: { type: "FeatureCollection", features: [] },
          attribution: item.attribution ?? undefined,
        },
      },
      layers: [
        {
          id: "demotiles-basemap-layer",
          type: "raster",
          source: "demotiles-basemap",
        },
        ...renderLayers,
      ],
    },
    layers: [
      {
        id: layerId,
        name: item.title,
        summary: item.summary,
        sourceId,
        sdkSource,
        renderLayerIds: renderLayers.map((layer) => layer.id),
        interactiveLayerId: renderLayers[0]?.id,
        defaultVisible: true,
        defaultOpacity: geometryType === "polygon" ? 0.4 : 1,
        inspectable: true,
        legend: [
          {
            label: item.title,
            color: geometryType === "point" || geometryType === "multipoint" ? "#f3b562" : "#4ec9b0",
            shape: legendShapeFor(geometryType),
          },
        ],
        popup: {
          title: "{PARCEL_ID}",
          fieldInfos: [
            { fieldName: "PARCEL_ID", label: "Parcel ID", visible: true },
            { fieldName: "LAND_USE", label: "Land use", visible: true },
            { fieldName: "OWNER_TYPE", label: "Owner type", visible: true },
            { fieldName: "ASSESSED_VALUE", label: "Assessed value", visible: true },
            { fieldName: "OBJECTID", label: "OBJECTID", visible: false },
          ],
          mediaInfos: [],
        },
        detailFields: [...PARCEL_DETAIL_FIELDS],
      },
    ],
    initialView: {
      center,
      zoom: sample.initialView.zoom,
      ...(bounds ? { bounds } : {}),
    },
  };
}

function buildRenderLayers(options: {
  geometryType: RenderableFeatureGeometry;
  layerId: string;
  sourceId: string;
}): StyleLayer[] {
  if (options.geometryType === "point" || options.geometryType === "multipoint") {
    return [
      {
        id: `${options.layerId}-circles`,
        type: "circle",
        source: options.sourceId,
        paint: {
          "circle-radius": 6,
          "circle-color": "#f3b562",
          "circle-stroke-color": "#1c2a36",
          "circle-stroke-width": 1.25,
        },
      },
    ];
  }

  if (options.geometryType === "polyline") {
    return [
      {
        id: `${options.layerId}-line`,
        type: "line",
        source: options.sourceId,
        paint: {
          "line-color": "#22806f",
          "line-width": 2.5,
        },
      },
    ];
  }

  return [
    {
      id: `${options.layerId}-fill`,
      type: "fill",
      source: options.sourceId,
      paint: {
        "fill-color": "#4ec9b0",
        "fill-opacity": 0.4,
      },
    },
    {
      id: `${options.layerId}-outline`,
      type: "line",
      source: options.sourceId,
      paint: {
        "line-color": "#22806f",
        "line-width": 2,
      },
    },
  ];
}

function isRenderableFeatureGeometry(
  geometryType: string | null | undefined,
): geometryType is RenderableFeatureGeometry {
  return (
    geometryType === "point" ||
    geometryType === "multipoint" ||
    geometryType === "polyline" ||
    geometryType === "polygon"
  );
}

function legendShapeFor(geometryType: RenderableFeatureGeometry): "fill" | "line" | "point" {
  if (geometryType === "polygon") return "fill";
  if (geometryType === "polyline") return "line";
  return "point";
}

function serviceUrlFor(item: ContentItem): string | undefined {
  if (item.target.type === "service") return item.target.serviceUrl;
  return (
    item.endpoints.geoservices?.accessURL ?? item.endpoints.ogcFeatures?.accessURL ?? item.endpoints.tiles?.accessURL
  );
}

function slugify(value: string): string {
  return (
    value
      .toLowerCase()
      .replace(/[^a-z0-9]+/g, "-")
      .replace(/^-+|-+$/g, "") || "portal-layer"
  );
}
