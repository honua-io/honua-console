import {
  type ContentItem,
  type ContentItemSummary,
  type ItemType,
  SERVICE_LINK_SLOTS,
  type ServiceLink,
  type ServiceLinkSlot,
} from "../contracts/content-item.js";
import { safeHttpUrl } from "../security/url.js";

export const PUBLIC_OPEN_DATA_TYPES = ["service", "layer", "document"] as const;
type PublicOpenDataType = (typeof PUBLIC_OPEN_DATA_TYPES)[number];

export interface PublicAccessRow {
  readonly id: string;
  readonly label: string;
  readonly value: string;
  readonly description: string;
}

export interface PublicApiExample {
  readonly id: string;
  readonly label: string;
  readonly command: string;
  readonly description: string;
}

export function publicOpenDataPath(item: Pick<ContentItem | ContentItemSummary, "id" | "slug">): string {
  return `/public/items/${encodeURIComponent(item.slug ?? item.id)}`;
}

export function isPublicOpenDataSummary(item: ContentItemSummary): boolean {
  return item.sharing === "public" && item.openData && isPublicOpenDataType(item.type);
}

export function isPublicOpenDataItem(item: ContentItem): boolean {
  return item.access.sharing === "public" && item.access.openData && isPublicOpenDataType(item.type);
}

export function collectPublicAccessRows(item: ContentItem): PublicAccessRow[] {
  const rows: PublicAccessRow[] = [];

  for (const slot of SERVICE_LINK_SLOTS) {
    const link = item.endpoints[slot];
    const accessURL = safeHttpUrl(link?.accessURL);
    if (!link || !accessURL) continue;
    rows.push({
      id: slot,
      label: endpointLabel(slot),
      value: accessURL,
      description: endpointDescription(slot, link),
    });
  }

  if (item.target.type === "document") {
    const downloadUrl = safeHttpUrl(item.target.url);
    if (downloadUrl) {
      rows.push({
        id: "download",
        label: "Document download",
        value: downloadUrl,
        description: item.target.mediaType,
      });
    }
  }

  return rows;
}

export function collectPublicApiExamples(item: ContentItem): PublicApiExample[] {
  const examples: PublicApiExample[] = [];
  const geoservices = safeHttpUrl(item.endpoints.geoservices?.accessURL);
  const ogcFeatures = safeHttpUrl(item.endpoints.ogcFeatures?.accessURL);
  const stac = safeHttpUrl(item.endpoints.stac?.accessURL);
  const download = item.target.type === "document" ? safeHttpUrl(item.target.url) : null;

  if (geoservices) {
    examples.push({
      id: "arcgis-geojson",
      label: "ArcGIS REST GeoJSON",
      command: `curl "${arcgisGeoJsonQueryUrl(geoservices)}"`,
      description: "Fetch a small GeoJSON response from the public FeatureServer endpoint.",
    });
  }

  if (ogcFeatures) {
    examples.push({
      id: "ogc-features",
      label: "OGC API Features",
      command: `curl "${withQueryParam(ogcFeatures, "limit", "10")}"`,
      description: "Fetch the first page of features from the public OGC collection.",
    });
  }

  if (stac) {
    examples.push({
      id: "stac",
      label: "STAC metadata",
      command: `curl "${stac}"`,
      description: "Fetch the public STAC catalog or item metadata.",
    });
  }

  if (download) {
    examples.push({
      id: "document-download",
      label: "Download file",
      command: `curl -L "${download}" -o "${downloadFilename(item)}"`,
      description: "Download the public document distribution.",
    });
  }

  return examples;
}

export function formatPublicTypeList(): string {
  return PUBLIC_OPEN_DATA_TYPES.map((type) => (type === "service" ? "services" : `${type}s`)).join(", ");
}

function isPublicOpenDataType(type: ItemType): type is PublicOpenDataType {
  return PUBLIC_OPEN_DATA_TYPES.includes(type as PublicOpenDataType);
}

function endpointLabel(slot: ServiceLinkSlot): string {
  switch (slot) {
    case "geoservices":
      return "ArcGIS REST endpoint";
    case "ogcFeatures":
      return "OGC API Features endpoint";
    case "stac":
      return "STAC endpoint";
    case "tiles":
      return "Tile endpoint";
  }
}

function endpointDescription(slot: ServiceLinkSlot, link: ServiceLink): string {
  const suffix = link.mediaType ? ` (${link.mediaType})` : "";
  switch (slot) {
    case "geoservices":
      return `GeoServices / ArcGIS REST service${suffix}.`;
    case "ogcFeatures":
      return `OGC API Features collection${suffix}.`;
    case "stac":
      return `STAC catalog or item metadata${suffix}.`;
    case "tiles":
      return `Tile URL template${suffix}.`;
  }
}

function arcgisGeoJsonQueryUrl(serviceUrl: string): string {
  const queryBase = serviceUrl.includes("/FeatureServer/") ? serviceUrl : `${serviceUrl.replace(/\/+$/, "")}/0/query`;
  const withWhere = withQueryParam(queryBase, "where", "1=1");
  const withFields = withQueryParam(withWhere, "outFields", "*");
  const withFormat = withQueryParam(withFields, "f", "geojson");
  return withQueryParam(withFormat, "resultRecordCount", "10");
}

function withQueryParam(url: string, key: string, value: string): string {
  try {
    const parsed = new URL(url);
    parsed.searchParams.set(key, value);
    return parsed.toString();
  } catch {
    const joiner = url.includes("?") ? "&" : "?";
    return `${url}${joiner}${encodeURIComponent(key)}=${encodeURIComponent(value)}`;
  }
}

function downloadFilename(item: ContentItem): string {
  const slug = item.slug ?? item.id;
  if (item.target.type !== "document") return slug;
  const extension = extensionFromMediaType(item.target.mediaType);
  return extension ? `${slug}.${extension}` : slug;
}

function extensionFromMediaType(mediaType: string): string | null {
  switch (mediaType) {
    case "application/pdf":
      return "pdf";
    case "application/geo+json":
    case "application/geojson":
      return "geojson";
    case "application/json":
      return "json";
    case "text/csv":
      return "csv";
    default:
      return null;
  }
}
