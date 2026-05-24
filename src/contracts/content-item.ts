/**
 * TypeScript surface for `content-item/v1`. The JSON Schema in
 * `schemas/content-item-v1.json` is authoritative; this module mirrors the
 * shape so portal code can consume it without redeclaring enums per call site.
 *
 * Schema parity is enforced by `src/catalog/__tests__/schema-validation.test.ts`,
 * which validates every fixture against the JSON Schema and asserts that the
 * mirrored enum sets here are exactly the union literal members in the schema.
 *
 * Once `honua-sdk-js` publishes the browser-safe `HonuaPortalCatalog` (its
 * child ticket), `Capability` and `Protocol` re-export from
 * `@honua/sdk-js/contract` instead of being mirrored here.
 */

import type { ServiceFormat } from "./conforms-to.js";

export const SCHEMA_VERSION = "content-item/v1" as const;

/**
 * String-length limits mirrored from `schemas/content-item-v1.json` so every
 * Console write path (saved-map create/patch/duplicate, publish handoff)
 * shares one set of constants.
 */
export const TITLE_MAX_LENGTH = 280;
export const SUMMARY_MAX_LENGTH = 280;
export const TAG_MAX_LENGTH = 64;

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

/**
 * Service/API endpoint metadata. All fields are required on the wire — null
 * when unknown — so consumers don't have to defend against missing keys. The
 * tile slot uses a relaxed `accessURL` that allows XYZ/TMS templates with
 * `{z}/{x}/{y}` placeholders.
 */
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

export interface ViewerSupport {
  readonly supported: boolean | null;
  readonly reason: string | null;
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
  readonly viewerSupport: ViewerSupport | null;
}

export const SORT_OPTIONS = ["modified-desc", "modified-asc", "title-asc", "title-desc", "relevance"] as const;
export type SortOption = (typeof SORT_OPTIONS)[number];

export interface ListItemsRequest {
  readonly type?: ItemType;
  readonly owner?: string;
  readonly tag?: string;
  readonly q?: string;
  readonly sharing?: Sharing;
  readonly sort?: SortOption;
  readonly limit?: number;
  readonly cursor?: string | null;
}

export interface ListItemsResponse {
  readonly items: readonly ContentItemSummary[];
  readonly nextCursor: string | null;
}

export interface DependencyNode {
  readonly id: Ulid;
  readonly type: ItemType;
  readonly role: string;
  readonly depth: number;
  readonly summary: ContentItemSummary | null;
}

export interface GetDependenciesResponse {
  readonly nodes: readonly DependencyNode[];
  readonly missing: readonly Dependency[];
  readonly unauthorized: readonly Dependency[];
  readonly unsupported: readonly Dependency[];
  readonly truncated: boolean;
}

export interface PublishHandoff {
  readonly type: ItemType;
  readonly title: string;
  readonly summary: string;
  readonly description?: string;
  readonly tags?: readonly string[];
  readonly owner: Owner;
  readonly extent: Extent | null;
  readonly nativeCrs: string | null;
  readonly license: License;
  readonly attribution: string | null;
  readonly source: {
    readonly kind: "import" | "publish" | "admin-job" | "external";
    readonly sourceId: string | null;
    readonly jobId: string | null;
    readonly publishedBy: string | null;
  };
  readonly target: Target;
  readonly endpoints: {
    readonly geoservices: ServiceLink | null;
    readonly ogcFeatures: ServiceLink | null;
    readonly stac: ServiceLink | null;
    readonly tiles: ServiceLink | null;
  };
  readonly preview: Preview;
  readonly capabilities: readonly Capability[];
  readonly dependencies: readonly Dependency[];
  readonly access: Access;
  readonly extensions?: Readonly<Record<string, Readonly<Record<string, unknown>>>>;
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

/**
 * The four non-`self` endpoint slots in the order callers should iterate. Used
 * by the catalog summary projection and the item detail renderer to avoid
 * stringly-typed slot lists per call site.
 */
export const SERVICE_LINK_SLOTS = ["geoservices", "ogcFeatures", "stac", "tiles"] as const;
export type ServiceLinkSlot = (typeof SERVICE_LINK_SLOTS)[number];

/**
 * Deduplicated list of {@link ServiceFormat} tokens drawn from the populated
 * non-`self` endpoint slots, preserving slot order and first-seen order. The
 * `self` slot's `Honua:Portal:v1` token is intentionally excluded so cards
 * surface real service families rather than the portal-internal token.
 */
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
    viewerSupport: readViewerSupport(item.extensions),
  };
}

export function readViewerSupport(extensions: ContentItem["extensions"] | undefined): ViewerSupport | null {
  if (!extensions) return null;
  const ext = extensions["honua-portal-viewer"];
  if (!ext) return null;
  const supported = typeof ext["supported"] === "boolean" ? (ext["supported"] as boolean) : null;
  const reason = typeof ext["reason"] === "string" ? (ext["reason"] as string) : null;
  if (supported === null && reason === null) return null;
  return { supported, reason };
}
