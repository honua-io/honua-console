import type { ServiceLink } from "../contracts/content-item.js";

/**
 * Admin → portal publish handoff — TypeScript types.
 *
 * `ContentItem` mirrors `schemas/content-item-v1.json` (projection of #10).
 * This module owns the `ServiceTarget` shape and the publish-event input
 * the handoff client accepts. When #10 ships canonical generated types via
 * `json-schema-to-typescript`, replace the hand-rolled envelope and keep
 * the handoff-specific shapes (`ServiceTarget`, `PublishHandoffEvent`,
 * `PublishStatusBadge`) here.
 */

export type ContentItemType = "service" | "layer" | "map" | "scene" | "app" | "document" | "external-url";

export type SharingLevel = "private" | "org" | "group" | "public-link" | "public";

export interface ContentOwner {
  id: string;
  name: string;
  kind: "user" | "org" | "group";
}

export interface ContentTimestamps {
  created: string;
  modified: string;
  published: string | null;
  refreshed: string | null;
}

export interface ContentExtent {
  bbox: [number, number, number, number];
  crs: "EPSG:4326";
}

export interface ContentLicense {
  spdx: string | null;
  name: string;
  url: string | null;
}

export type SourceKind = "import" | "publish" | "admin-job" | "external" | "manual";

export type HistoryEventKind =
  | "import"
  | "publish"
  | "admin-job"
  | "external"
  | "manual"
  | "update"
  | "share-change"
  | "metadata-edit";

export interface ContentSourceHistoryEvent {
  at: string;
  kind: HistoryEventKind;
  actor: string;
}

export interface ContentSource {
  kind: SourceKind;
  sourceId: string | null;
  jobId: string | null;
  publishedBy: string | null;
  history: ContentSourceHistoryEvent[];
}

export interface ContentEndpoints {
  self: ServiceLink;
  geoservices: ServiceLink | null;
  ogcFeatures: ServiceLink | null;
  stac: ServiceLink | null;
  tiles: ServiceLink | null;
}

export interface ContentPreview {
  thumbnail: string | null;
  image: string | null;
}

export interface ContentDependency {
  id: string;
  type: ContentItemType;
  role: string;
}

export interface ContentAccess {
  sharing: SharingLevel;
  embeddable: boolean;
  openData: boolean;
}

export interface ContentItem<TTarget = unknown> {
  id: string;
  slug: string | null;
  type: ContentItemType;
  title: string;
  summary: string;
  description: string;
  tags: string[];
  owner: ContentOwner;
  timestamps: ContentTimestamps;
  extent: ContentExtent | null;
  nativeCrs: string | null;
  license: ContentLicense;
  attribution: string | null;
  source: ContentSource;
  target: TTarget;
  endpoints: ContentEndpoints;
  preview: ContentPreview;
  capabilities: string[];
  dependencies: ContentDependency[];
  access: ContentAccess;
  extensions: Record<string, Record<string, unknown>>;
}

// ── Service target ──────────────────────────────────────────────────────────
//
// User-safe service kinds the portal renders. The admin side may know more
// about the underlying runtime; anything we cannot describe in user terms is
// mapped to "unsupported" so the catalog never leaks operator vocabulary.

export type ServiceType =
  | "feature"
  | "vector-tile"
  | "raster-tile"
  | "image"
  | "ogc-features"
  | "ogc-tiles"
  | "wms"
  | "wmts"
  | "unsupported";

export type ServiceKind = "feature" | "map" | "image" | "tile" | "vector-tile";

/**
 * User-safe service status surfaced in the catalog.
 *
 * Operator vocabulary (e.g. "deploy_pending", "tile_cache_warming",
 * "503_from_origin") is intentionally collapsed to four states so portal
 * stays readable for non-operators and consistent across service kinds.
 */
export type ServiceStatus = "available" | "limited" | "unavailable" | "draft";

export interface ServiceTarget {
  type: "service";
  serviceName: string;
  kind: ServiceKind;
  layerCount: number;
  serviceUrl: string;
  serviceType: ServiceType;
  importJobId: string | null;
  status: ServiceStatus;
  statusDetail: string | null;
  /**
   * Opaque admin-side correlation id. Stored on the item but only rendered
   * for users with the `admin:diagnostics` permission. Used by the helper
   * in `status.ts` to build the admin-only URL — never emitted to the
   * end-user UI.
   */
  adminDiagnosticsRef: string | null;
  lastCheckedAt: string | null;
}

export type ServiceContentItem = ContentItem<ServiceTarget> & {
  type: "service";
};

// ── Publish handoff event (admin → portal) ──────────────────────────────────

/**
 * Operator-side service status as understood by admin. The portal narrows
 * this to the four `ServiceStatus` values via `mapAdminStatus`.
 *
 * The list covers two vocabularies that the admin/server may emit:
 *
 *   - The canonical honua-server `PublishedServiceStatus` enum
 *     (`Provisioning | Active | Suspended | RefreshFailed | Decommissioned`)
 *     — this is what the admin upstream actually serializes today.
 *   - A set of looser operator-side strings (`ok`, `degraded`, `failed`, …)
 *     that the admin handoff adapter may also emit when it summarizes
 *     transient health-check / deploy / refresh state that does not
 *     have a 1:1 server enum value.
 *
 * `mapAdminStatus` matches this list case-insensitively, so admin can
 * send either `Active` or `active` without changing the rendered status.
 *
 * Adding a value here is allowed without a contract change as long as the
 * mapping in `status.ts` is updated to project it onto a portal status.
 */
export type AdminServiceStatus =
  // honua-server canonical PublishedServiceStatus enum (PascalCase).
  | "Provisioning"
  | "Active"
  | "Suspended"
  | "RefreshFailed"
  | "Decommissioned"
  // Looser operator-side aliases (lowercased) for transient/deploy state.
  | "ok"
  | "ready"
  | "running"
  | "publishing"
  | "deploying"
  | "warming"
  | "degraded"
  | "partial"
  | "throttled"
  | "failed"
  | "errored"
  | "broken"
  | "draft"
  | "unpublished";

export interface PublishHandoffOwner {
  id: string;
  kind: "user" | "group" | "org";
  displayName?: string;
}

export interface PublishHandoffMetadata {
  title: string;
  summary?: string | null;
  tags?: string[];
  extent?: ContentExtent | null;
  description?: string | null;
  nativeCrs?: string | null;
  license?: ContentLicense | null;
  attribution?: string | null;
  serviceName?: string | null;
  layerCount?: number | null;
  capabilities?: string[];
}

/**
 * The event admin sends to portal when a publish/import completes or the
 * service metadata/status changes. The same shape covers initial publish,
 * re-publish, and metadata-only updates — `eventKind` distinguishes them
 * so the receiver can record the right history entry.
 */
export interface PublishHandoffEvent {
  /**
   * Stable id for the source service in admin/server. Used as the lookup
   * key when re-publishing or updating: portal upserts on this id, so
   * saved-map references that depend on the resulting portal item id
   * survive re-publishes.
   */
  sourceServiceId: string;
  eventKind: "publish" | "republish" | "metadataUpdate" | "statusChange";
  serviceUrl: string;
  serviceType: ServiceType;
  importJobId?: string | null;
  status: AdminServiceStatus;
  /**
   * Optional operator-side reason for a non-OK status. The receiver
   * sanitizes this into a user-safe `statusDetail` — operator vocabulary
   * never reaches the end-user surface.
   */
  statusReason?: string | null;
  /**
   * Opaque admin correlation id for the admin diagnostics page. Stored on
   * the item; not rendered to non-operators.
   */
  adminDiagnosticsRef?: string | null;
  /**
   * ISO-8601 of the last operator-side health check. If absent, the
   * receiver stamps `now()` for `eventKind="statusChange"` and leaves
   * other event kinds with `lastCheckedAt = null`.
   */
  lastCheckedAt?: string | null;
  owner: PublishHandoffOwner;
  metadata: PublishHandoffMetadata;
  /**
   * Actor that performed the publish in admin (typically the operator's
   * user id). Recorded in the `source.history` audit trail.
   */
  actor: string;
}

// ── Status surfacing ────────────────────────────────────────────────────────
//
// The catalog renders one of these four badges. The mapping from
// `ServiceStatus` is deterministic and lives in `status.ts`. Putting the
// badge type here keeps any consumer (catalog cards, item detail pages,
// embed chrome) aligned without re-deriving the rules.

export type PublishStatusTone = "ok" | "info" | "warn" | "error";

export interface PublishStatusBadge {
  status: ServiceStatus;
  tone: PublishStatusTone;
  /** End-user-safe label, e.g. "Available". */
  label: string;
  /** End-user-safe one-line description; never operator vocabulary. */
  description: string;
}

// ── Surface taxonomy (mirrors #14's missing | unauthorized | unsupported) ──

export type CatalogSurface =
  | { kind: "ok"; item: ServiceContentItem }
  | { kind: "missing"; sourceServiceId: string }
  | { kind: "unauthorized"; sourceServiceId: string }
  | { kind: "unsupported"; sourceServiceId: string; reason: string };
