/**
 * Content item / metadata v2 / provenance projections.
 *
 * `honua-sdk-js#225` will publish the real types from
 * `@honua/sdk-js/content` (or an equivalent subpath). Until then, this
 * file exports `MetadataV2Pending` markers and a `PendingBindingNote` so
 * features can stub a Catalog list/detail surface that renders through
 * `<ResourceState kind="pending-binding" />` without inventing local DTOs.
 *
 * IMPORTANT: do not declare full DTO shapes here. Once #225 lands, this
 * file becomes pure re-exports.
 */

/**
 * Sentinel marker for surfaces blocked on `honua-sdk-js#225`. Replace with
 * the published `ContentItem`/metadata v2 type when available.
 *
 * @internal
 */
export interface MetadataV2Pending {
  readonly status: "pending-binding";
  readonly waitingFor: "honua-sdk-js#225";
}

export const METADATA_V2_PENDING: MetadataV2Pending = Object.freeze({
  status: "pending-binding",
  waitingFor: "honua-sdk-js#225",
});

export interface PendingBindingNote {
  readonly surface: string;
  readonly waitingFor: ReadonlyArray<string>;
  readonly description: string;
}

export const CONTENT_ITEM_PENDING: PendingBindingNote = Object.freeze({
  surface: "catalog.content-item",
  waitingFor: Object.freeze(["honua-sdk-js#225"]) as ReadonlyArray<string>,
  description:
    "Content item list/detail/search/create/update waits on @honua/sdk-js metadata v2 projections. " +
    "Catalog surfaces render <ResourceState kind=\"pending-binding\" /> until the SDK publishes.",
});

export const DASHBOARD_PACKAGE_PENDING: PendingBindingNote = Object.freeze({
  surface: "catalog.dashboard-package",
  waitingFor: Object.freeze(["honua-sdk-js#225"]) as ReadonlyArray<string>,
  description:
    "Dashboard package client waits on @honua/sdk-js#225. Console renders pending-binding " +
    "for dashboard package detail until the SDK publishes the client.",
});

export const REPORT_PACKAGE_PENDING: PendingBindingNote = Object.freeze({
  surface: "catalog.report-package",
  waitingFor: Object.freeze(["honua-sdk-js#225"]) as ReadonlyArray<string>,
  description:
    "Report package client waits on @honua/sdk-js#225. Console renders pending-binding " +
    "for report package detail until the SDK publishes the client.",
});
