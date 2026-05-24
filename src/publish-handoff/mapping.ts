/**
 * Project a `PublishHandoffEvent` from admin onto a portal `ServiceContentItem`.
 *
 * The mapping is deliberately one-way (admin → portal). Operator-only
 * settings are dropped on the floor here so they cannot leak into the
 * catalog by virtue of being on the input shape.
 *
 * Two helpers are exported because the upsert path needs both:
 *   - `buildServiceContentItem` for the create path.
 *   - `applyHandoffEventToItem` for the re-publish/metadata-update path,
 *     which preserves the existing item id, created timestamp, and
 *     `source.history` so saved-map references and audit trail survive.
 */

import { CONFORMS_TO_URIS, type ServiceFormat } from "../contracts/conforms-to.js";
import { SUMMARY_MAX_LENGTH, type ServiceLink, TAG_MAX_LENGTH, TITLE_MAX_LENGTH } from "../contracts/content-item.js";
import { assertContentItemId } from "../contracts/ids.js";
import { safeHttpUrl } from "../security/url.js";
import { mapAdminStatus, sanitizeStatusReason } from "./status.js";
import type {
  ContentExtent,
  ContentLicense,
  ContentSourceHistoryEvent,
  HistoryEventKind,
  PublishHandoffEvent,
  ServiceContentItem,
  ServiceKind,
  ServiceTarget,
} from "./types.js";

export interface BuildServiceContentItemContext {
  /** Portal id assigned by the upsert layer. */
  id: string;
  /** ISO-8601 stamp used for both `created` and `modified` on first build. */
  now: string;
}

const TITLE_MAX = TITLE_MAX_LENGTH;
const SUMMARY_MAX = SUMMARY_MAX_LENGTH;
const DESCRIPTION_MAX = 2000;
const TAG_MAX = TAG_MAX_LENGTH;
const DEFAULT_CONSOLE_ORIGIN = "https://console.honua.example";
const DEFAULT_SUMMARY = "No summary provided.";
const DEFAULT_LICENSE: ContentLicense = {
  spdx: null,
  name: "Unspecified",
  url: null,
};

export function buildServiceContentItem(
  event: PublishHandoffEvent,
  ctx: BuildServiceContentItemContext,
): ServiceContentItem {
  const id = assertContentItemId(ctx.id, "service item id");
  const title = validateTitle(event.metadata.title);
  const summary = validateSummary(event.metadata.summary);
  const target = buildServiceTarget(event, ctx.now);
  const historyKind = historyKindFor(event.eventKind);
  return {
    id,
    slug: null,
    type: "service",
    title,
    summary,
    description: validateDescription(event.metadata.description, summary),
    tags: validateTags(event.metadata.tags),
    owner: {
      id: event.owner.id,
      name: event.owner.displayName ?? event.owner.id,
      kind: event.owner.kind,
    },
    timestamps: { created: ctx.now, modified: ctx.now, published: ctx.now, refreshed: target.lastCheckedAt },
    extent: cloneExtent(event.metadata.extent),
    nativeCrs: event.metadata.nativeCrs ?? null,
    license: cloneLicense(event.metadata.license),
    attribution: event.metadata.attribution ?? null,
    source: {
      kind: "publish",
      sourceId: event.sourceServiceId,
      jobId: event.importJobId ?? null,
      publishedBy: event.actor,
      history: [{ at: ctx.now, kind: historyKind, actor: event.actor }],
    },
    target,
    endpoints: {
      self: serviceLink(catalogSelfUrlFor(id), "Honua:Portal:v1", "text/html"),
      geoservices: serviceLink(target.serviceUrl, serviceFormatFor(event.serviceType), "application/json"),
      ogcFeatures: null,
      stac: null,
      tiles: null,
    },
    preview: { thumbnail: null, image: null },
    capabilities: validateCapabilities(event.metadata.capabilities),
    dependencies: [],
    access: { sharing: "private", embeddable: false, openData: false },
    extensions: {},
  };
}

export interface ApplyHandoffEventContext {
  /**
   * ISO-8601 stamp used for `modified` and the new history entry. Created
   * timestamp, item id, sharing, dependencies, and preview are preserved
   * from `existing`.
   */
  now: string;
}

/**
 * Apply a re-publish, metadata update, or status change to an existing
 * portal item.
 *
 * Invariants (see `mapping.test.ts` for assertions):
 *   - `id` never changes — saved-map references that point at the portal
 *     item id must keep working across re-publishes. This is the central
 *     guarantee of AC2.
 *   - `timestamps.created` never changes; only `modified` advances.
 *   - `endpoints.self` never changes (absolute URL with `/catalog/{id}` path).
 *   - `access` (sharing / embeddable / openData) never changes — those
 *     are owned by the portal share/embed flows in #15/#17 and the
 *     publish handoff is not allowed to silently downgrade them on a
 *     republish.
 *   - `source.kind` stays `"publish"`; `source.sourceId` is taken from
 *     the event (will normally already match — guarded for completeness).
 *   - `source.history` is appended to, never replaced.
 *   - `preview` is preserved (admin handoff doesn't carry thumbnails;
 *     dropping a stored thumbnail on a metadata update would be a
 *     visible regression for catalog cards).
 *   - `dependencies` is preserved (portal owns layer/style ties).
 */
export function applyHandoffEventToItem(
  existing: ServiceContentItem,
  event: PublishHandoffEvent,
  ctx: ApplyHandoffEventContext,
): ServiceContentItem {
  assertContentItemId(existing.id, "existing service item id");
  const summary = validateSummary(event.metadata.summary);
  const target = mergeServiceTarget(existing.target, event, ctx.now);
  const historyKind = historyKindFor(event.eventKind);
  const historyEntry: ContentSourceHistoryEvent = {
    at: ctx.now,
    kind: historyKind,
    actor: event.actor,
  };
  return {
    ...existing,
    title: validateTitle(event.metadata.title),
    summary,
    description: validateDescription(event.metadata.description, existing.description),
    tags: validateTags(event.metadata.tags),
    owner: {
      id: event.owner.id,
      name: event.owner.displayName ?? existing.owner.name,
      kind: event.owner.kind,
    },
    timestamps: { ...existing.timestamps, modified: ctx.now, refreshed: target.lastCheckedAt },
    extent: cloneExtent(event.metadata.extent),
    nativeCrs: event.metadata.nativeCrs ?? existing.nativeCrs,
    license: event.metadata.license === undefined ? { ...existing.license } : cloneLicense(event.metadata.license),
    attribution: event.metadata.attribution === undefined ? existing.attribution : event.metadata.attribution,
    source: {
      kind: "publish",
      sourceId: event.sourceServiceId,
      jobId: event.importJobId ?? existing.source.jobId,
      publishedBy: event.actor,
      history: [...existing.source.history, historyEntry],
    },
    target,
    endpoints: {
      ...existing.endpoints,
      geoservices: serviceLink(target.serviceUrl, serviceFormatFor(event.serviceType), "application/json"),
    },
    preview: existing.preview,
    capabilities: validateCapabilities(event.metadata.capabilities ?? existing.capabilities),
    dependencies: existing.dependencies.map((d) => ({ ...d })),
    access: { ...existing.access },
    extensions: cloneExtensions(existing.extensions),
  };
}

// ── target construction ─────────────────────────────────────────────────────

function buildServiceTarget(event: PublishHandoffEvent, now: string): ServiceTarget {
  const status = mapAdminStatus(event.status);
  const statusDetail = status === "available" ? null : sanitizeStatusReason(event.statusReason);
  return {
    type: "service",
    serviceName: serviceNameFor(event),
    kind: serviceKindFor(event.serviceType),
    layerCount: layerCountFor(event, 0),
    serviceUrl: validateServiceUrl(event.serviceUrl),
    serviceType: event.serviceType,
    importJobId: event.importJobId ?? null,
    status,
    statusDetail,
    adminDiagnosticsRef: event.adminDiagnosticsRef ?? null,
    lastCheckedAt: lastCheckedFor(event, now),
  };
}

function mergeServiceTarget(existing: ServiceTarget, event: PublishHandoffEvent, now: string): ServiceTarget {
  const status = mapAdminStatus(event.status);
  const statusDetail = status === "available" ? null : sanitizeStatusReason(event.statusReason);
  return {
    ...existing,
    serviceName: serviceNameFor(event, existing.serviceName),
    kind: serviceKindFor(event.serviceType),
    layerCount: layerCountFor(event, existing.layerCount),
    serviceUrl: validateServiceUrl(event.serviceUrl),
    serviceType: event.serviceType,
    importJobId: event.importJobId ?? existing.importJobId,
    status,
    statusDetail,
    adminDiagnosticsRef:
      event.adminDiagnosticsRef !== undefined ? event.adminDiagnosticsRef : existing.adminDiagnosticsRef,
    lastCheckedAt: lastCheckedFor(event, now),
  };
}

function lastCheckedFor(event: PublishHandoffEvent, now: string): string | null {
  if (event.lastCheckedAt !== undefined && event.lastCheckedAt !== null) {
    return event.lastCheckedAt;
  }
  return event.eventKind === "statusChange" ? now : null;
}

function historyKindFor(eventKind: PublishHandoffEvent["eventKind"]): HistoryEventKind {
  switch (eventKind) {
    case "publish":
    case "republish":
      return "publish";
    case "metadataUpdate":
      return "metadata-edit";
    case "statusChange":
      return "update";
  }
}

// ── validation ──────────────────────────────────────────────────────────────
// Mirrors content-item/v1 via the shared constants in
// `src/contracts/content-item.ts`: title 1–280, summary ≤ 280, tags unique
// non-empty ≤ 64.

function validateTitle(title: unknown): string {
  if (typeof title !== "string" || title.trim().length === 0) {
    throw new Error("title is required");
  }
  if (title.length > TITLE_MAX) {
    throw new Error(`title exceeds ${TITLE_MAX} characters (got ${title.length})`);
  }
  return title;
}

function validateSummary(summary: unknown): string {
  if (summary == null) return DEFAULT_SUMMARY;
  if (typeof summary !== "string") {
    throw new Error("summary must be a string or null");
  }
  if (summary.length > SUMMARY_MAX) {
    throw new Error(`summary exceeds ${SUMMARY_MAX} characters (got ${summary.length})`);
  }
  return summary.trim().length === 0 ? DEFAULT_SUMMARY : summary;
}

function validateDescription(description: unknown, fallback: string): string {
  if (description == null) return fallback;
  if (typeof description !== "string") {
    throw new Error("description must be a string or null");
  }
  if (description.length > DESCRIPTION_MAX) {
    throw new Error(`description exceeds ${DESCRIPTION_MAX} characters (got ${description.length})`);
  }
  return description;
}

function validateTags(tags: unknown): string[] {
  if (tags == null) return [];
  if (!Array.isArray(tags)) {
    throw new Error("tags must be an array of strings");
  }
  const seen = new Set<string>();
  const out: string[] = [];
  for (const tag of tags) {
    if (typeof tag !== "string" || tag.length === 0) continue;
    if (tag.length > TAG_MAX) {
      throw new Error(`tag exceeds ${TAG_MAX} characters: ${JSON.stringify(tag)} (got ${tag.length})`);
    }
    if (seen.has(tag)) continue;
    seen.add(tag);
    out.push(tag);
  }
  return out;
}

function validateServiceUrl(url: unknown): string {
  if (typeof url !== "string" || url.trim().length === 0) {
    throw new Error("serviceUrl is required");
  }
  const safe = safeHttpUrl(url);
  if (!safe) {
    throw new Error("serviceUrl must be an absolute http(s) URL");
  }
  return safe;
}

function validateCapabilities(capabilities: unknown): string[] {
  if (capabilities == null) return [];
  if (!Array.isArray(capabilities)) {
    throw new Error("capabilities must be an array of strings");
  }
  return capabilities.filter((c): c is string => typeof c === "string" && c.length > 0);
}

function cloneExtent(extent: PublishHandoffEvent["metadata"]["extent"]): ContentExtent | null {
  if (extent == null) return null;
  return {
    bbox: [extent.bbox[0], extent.bbox[1], extent.bbox[2], extent.bbox[3]],
    crs: "EPSG:4326",
  };
}

function cloneLicense(license: ContentLicense | null | undefined): ContentLicense {
  if (!license) return { ...DEFAULT_LICENSE };
  return {
    spdx: license.spdx ?? null,
    name: license.name,
    url: license.url ?? null,
  };
}

function cloneExtensions(extensions: ServiceContentItem["extensions"]): ServiceContentItem["extensions"] {
  return JSON.parse(JSON.stringify(extensions)) as ServiceContentItem["extensions"];
}

function serviceNameFor(event: PublishHandoffEvent, fallback?: string): string {
  const explicit = event.metadata.serviceName?.trim();
  if (explicit) return explicit;
  try {
    const url = new URL(event.serviceUrl);
    const parts = url.pathname.split("/").filter(Boolean);
    const restIndex = parts.findIndex((part) => part.toLowerCase() === "services");
    if (restIndex >= 0 && parts[restIndex + 1]) {
      return parts.slice(restIndex + 1).join("/");
    }
    return parts.slice(-2).join("/") || fallback || url.hostname;
  } catch {
    return fallback || event.sourceServiceId;
  }
}

function serviceKindFor(type: PublishHandoffEvent["serviceType"]): ServiceKind {
  switch (type) {
    case "feature":
    case "ogc-features":
      return "feature";
    case "vector-tile":
      return "vector-tile";
    case "raster-tile":
    case "ogc-tiles":
    case "wmts":
      return "tile";
    case "image":
    case "wms":
      return "image";
    case "unsupported":
      return "map";
  }
}

function serviceFormatFor(type: PublishHandoffEvent["serviceType"]): ServiceFormat {
  switch (type) {
    case "feature":
      return "GeoServices:FeatureService";
    case "vector-tile":
      return "GeoServices:VectorTileService";
    case "raster-tile":
      return "GeoServices:TileService";
    case "image":
      return "GeoServices:ImageService";
    case "ogc-features":
      return "OGC:API:Features";
    case "ogc-tiles":
    case "wmts":
      return "OGC:API:Tiles";
    case "wms":
      return "OGC:WMS:1.3.0";
    case "unsupported":
      return "Honua:API:v1";
  }
}

function serviceLink(accessURL: string, format: ServiceFormat, mediaType: string | null): ServiceLink {
  const safeAccessURL = safeHttpUrl(accessURL);
  if (!safeAccessURL) {
    throw new Error("service link accessURL must be an absolute http(s) URL");
  }
  return {
    accessURL: safeAccessURL,
    format,
    mediaType,
    describedBy: null,
    describedByType: null,
    conformsTo: CONFORMS_TO_URIS[format],
  };
}

function catalogSelfUrlFor(id: string): string {
  return new URL(`/catalog/${encodeURIComponent(id)}`, DEFAULT_CONSOLE_ORIGIN).toString();
}

function layerCountFor(event: PublishHandoffEvent, fallback: number): number {
  const count = event.metadata.layerCount;
  if (typeof count !== "number" || !Number.isFinite(count)) return fallback;
  return Math.max(0, Math.trunc(count));
}
