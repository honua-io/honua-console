/**
 * Transitional `content-item/v1` surface for Honua Console.
 *
 * Mirrors honua-portal/src/contracts/content-item.ts. The Console will retire
 * this shim and re-export from @honua/sdk-js once honua-sdk-js#225 lands the
 * browser-safe catalog contract. See docs/studio/PORT.md for the cleanup plan.
 */

import type { ServiceFormat } from "./conforms-to.js";

export const SCHEMA_VERSION = "content-item/v1" as const;

export const ITEM_TYPES = ["service", "layer", "map", "scene", "app", "document", "external-url"] as const;
export type ItemType = (typeof ITEM_TYPES)[number];

export const CAPABILITIES = [
  "query",
  "queryAggregate",
  "queryExtent",
  "queryObjectIds",
  "queryRelated",
  "applyEdits",
  "attachments",
  "render",
  "tiles",
  "sql",
  "stream",
  "pbf",
  "connect",
  "image",
  "geometry",
  "geoprocess",
  "processes",
] as const;
export type Capability = (typeof CAPABILITIES)[number];

export const SHARING_LEVELS = ["private", "org", "group", "public-link", "public"] as const;
export type Sharing = (typeof SHARING_LEVELS)[number];

export const SOURCE_KINDS = ["import", "publish", "admin-job", "external", "manual"] as const;
export type SourceKind = (typeof SOURCE_KINDS)[number];

export const HISTORY_KINDS = [...SOURCE_KINDS, "update", "share-change", "metadata-edit"] as const;
export type HistoryKind = (typeof HISTORY_KINDS)[number];

export type Ulid = string;

export interface Owner {
  readonly id: string;
  readonly name: string;
  readonly kind: "user" | "org" | "group";
}

export interface Timestamps {
  readonly created: string;
  readonly modified: string;
  readonly published: string | null;
  readonly refreshed: string | null;
}

export interface Extent {
  readonly bbox: readonly [number, number, number, number];
  readonly crs: "EPSG:4326";
}

export interface License {
  readonly spdx: string | null;
  readonly name: string;
  readonly url: string | null;
}

export interface HistoryEvent {
  readonly at: string;
  readonly kind: HistoryKind;
  readonly actor: string;
}

export interface Source {
  readonly kind: SourceKind;
  readonly sourceId: string | null;
  readonly jobId: string | null;
  readonly publishedBy: string | null;
  readonly history: readonly HistoryEvent[];
}

export interface ServiceLink {
  readonly accessURL: string;
  readonly format: ServiceFormat;
  readonly mediaType: string | null;
  readonly describedBy: string | null;
  readonly describedByType: string | null;
  readonly conformsTo: readonly string[];
}

export interface Endpoints {
  readonly self: ServiceLink;
  readonly geoservices: ServiceLink | null;
  readonly ogcFeatures: ServiceLink | null;
  readonly stac: ServiceLink | null;
  readonly tiles: ServiceLink | null;
}

export interface Preview {
  readonly thumbnail: string | null;
  readonly image: string | null;
}

export interface Access {
  readonly sharing: Sharing;
  readonly embeddable: boolean;
  readonly openData: boolean;
}

export interface Dependency {
  readonly id: Ulid;
  readonly type: ItemType;
  readonly role: string;
}

export interface ServiceTarget {
  readonly type: "service";
  readonly serviceName: string;
  readonly kind: "feature" | "map" | "image" | "tile" | "vector-tile";
  readonly layerCount: number;
  readonly serviceUrl: string;
  readonly serviceType:
    | "feature"
    | "vector-tile"
    | "raster-tile"
    | "image"
    | "ogc-features"
    | "ogc-tiles"
    | "wms"
    | "wmts"
    | "unsupported";
  readonly importJobId: string | null;
  readonly status: "available" | "limited" | "unavailable" | "draft";
  readonly statusDetail: string | null;
  readonly adminDiagnosticsRef: string | null;
  readonly lastCheckedAt: string | null;
}

export interface LayerTarget {
  readonly type: "layer";
  readonly serviceId: Ulid;
  readonly layerId: number;
  readonly geometryType: "point" | "multipoint" | "polyline" | "polygon" | "envelope" | "raster" | null;
  readonly fieldCount: number;
}

export interface MapTarget {
  readonly type: "map";
  readonly webmapJsonRef: string;
  readonly operationalLayerCount: number;
}

export interface SceneTarget {
  readonly type: "scene";
  readonly webSceneJsonRef: string;
  readonly layers: number;
}

export interface AppTarget {
  readonly type: "app";
  readonly url: string;
  readonly framework: "honua" | "external";
}

export interface DocumentTarget {
  readonly type: "document";
  readonly url: string;
  readonly mediaType: string;
}

export interface ExternalUrlTarget {
  readonly type: "external-url";
  readonly url: string;
}

export type Target =
  | ServiceTarget
  | LayerTarget
  | MapTarget
  | SceneTarget
  | AppTarget
  | DocumentTarget
  | ExternalUrlTarget;

export interface ContentItem {
  readonly id: Ulid;
  readonly slug: string | null;
  readonly type: ItemType;
  readonly title: string;
  readonly summary: string;
  readonly description: string;
  readonly tags: readonly string[];
  readonly owner: Owner;
  readonly timestamps: Timestamps;
  readonly extent: Extent | null;
  readonly nativeCrs: string | null;
  readonly license: License;
  readonly attribution: string | null;
  readonly source: Source;
  readonly target: Target;
  readonly endpoints: Endpoints;
  readonly preview: Preview;
  readonly capabilities: readonly Capability[];
  readonly dependencies: readonly Dependency[];
  readonly access: Access;
  readonly extensions: Readonly<Record<string, Readonly<Record<string, unknown>>>>;
}

export interface ContentItemSummary {
  readonly id: Ulid;
  readonly slug: string | null;
  readonly type: ItemType;
  readonly title: string;
  readonly summary: string;
  readonly owner: Owner;
  readonly tags: readonly string[];
  readonly extent: Extent | null;
  readonly preview: { readonly thumbnail: string | null };
  readonly modified: string;
  readonly capabilities: readonly Capability[];
  readonly formats: readonly ServiceFormat[];
  readonly sharing: Sharing;
  readonly openData: boolean;
}

export type CatalogErrorCode = "missing" | "unauthorized" | "unsupported" | "invalid" | "conflict" | "server";

export interface CatalogErrorEnvelope {
  readonly error: {
    readonly code: CatalogErrorCode;
    readonly message: string;
    readonly details?: Readonly<Record<string, unknown>>;
  };
}

export class CatalogError extends Error {
  readonly code: CatalogErrorCode;
  readonly details: Readonly<Record<string, unknown>> | undefined;

  constructor(code: CatalogErrorCode, message: string, details?: Readonly<Record<string, unknown>>) {
    super(message);
    this.name = "CatalogError";
    this.code = code;
    this.details = details;
  }
}

export const SERVICE_LINK_SLOTS = ["geoservices", "ogcFeatures", "stac", "tiles"] as const;
export type ServiceLinkSlot = (typeof SERVICE_LINK_SLOTS)[number];

export function collectFormats(item: ContentItem): readonly ServiceFormat[] {
  const formats: ServiceFormat[] = [];
  const seen = new Set<ServiceFormat>();
  for (const slot of SERVICE_LINK_SLOTS) {
    const link = item.endpoints[slot];
    if (!link) continue;
    if (seen.has(link.format)) continue;
    seen.add(link.format);
    formats.push(link.format);
  }
  return formats;
}

export function summarize(item: ContentItem): ContentItemSummary {
  return {
    id: item.id,
    slug: item.slug,
    type: item.type,
    title: item.title,
    summary: item.summary,
    owner: item.owner,
    tags: item.tags,
    extent: item.extent,
    preview: { thumbnail: item.preview.thumbnail },
    modified: item.timestamps.modified,
    capabilities: item.capabilities,
    formats: collectFormats(item),
    sharing: item.access.sharing,
    openData: item.access.openData,
  };
}
