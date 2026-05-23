// SDK adapter: emulates the browser-safe projections that
// @honua/sdk-js ships for catalog summaries, builder plans, and
// generated-app packages. The smoke uses this projection layer because
// Console (the browser surface) never talks to honua-server's raw row
// shapes directly — it consumes the SDK projections. Tagging projection
// failures as `sdk` keeps a contract-version mismatch attributable to
// honua-sdk-js rather than to Console or server.
//
// Wire shapes mirror the canonical content-item/v1.1.0 schema at
// /home/makani/honua-portal/schemas/content-item-v1.json and the
// `summarize()` projection at honua-portal/src/contracts/content-item.ts.

import { findContract } from "../contracts.mjs";

// Mirrors SERVICE_LINK_SLOTS in honua-portal/src/contracts/content-item.ts.
// `self` is deliberately excluded so cards surface real service families
// rather than the portal-internal token.
const NON_SELF_ENDPOINT_SLOTS = Object.freeze(["geoservices", "ogcFeatures", "stac", "tiles"]);

/** Deduplicated ServiceFormat tokens drawn from populated non-self endpoint slots. */
export function collectFormats(item) {
  const formats = [];
  const seen = new Set();
  for (const slot of NON_SELF_ENDPOINT_SLOTS) {
    const link = item.endpoints?.[slot];
    if (!link) continue;
    if (seen.has(link.format)) continue;
    seen.add(link.format);
    formats.push(link.format);
  }
  return formats;
}

/** Project the viewer-support extension if the publisher asserted it. */
export function readViewerSupport(extensions) {
  if (!extensions) return null;
  const ext = extensions["honua-portal-viewer"];
  if (!ext) return null;
  const supported = typeof ext.supported === "boolean" ? ext.supported : null;
  const reason = typeof ext.reason === "string" ? ext.reason : null;
  if (supported === null && reason === null) return null;
  return { supported, reason };
}

const VIEWER_DEFAULTS = Object.freeze({
  service: { supported: true, reason: null },
  layer: { supported: true, reason: null },
  map: { supported: true, reason: null },
  scene: { supported: false, reason: "Viewer does not support scenes yet." },
  app: { supported: false, reason: "Apps open via the generated-app preview route, not the viewer." },
  document: { supported: false, reason: "Documents are not openable in the map viewer." },
  "external-url": { supported: false, reason: "External URLs are not openable in the map viewer." },
});

/**
 * Canonical content-item/v1.1.0 ContentItemSummary projection. Mirrors
 * honua-portal/src/contracts/content-item.ts `summarize()`; viewerSupport
 * falls back to the type-default openability gate when the publisher has
 * not asserted an override via extensions["honua-portal-viewer"].
 */
export function summarizeContentItem(item) {
  if (!item) throw new Error("summarizeContentItem requires a content item");
  const viewerSupport = readViewerSupport(item.extensions) ?? VIEWER_DEFAULTS[item.type] ?? { supported: null, reason: null };
  return {
    id: item.id,
    slug: item.slug,
    type: item.type,
    title: item.title,
    summary: item.summary,
    owner: item.owner,
    tags: item.tags ?? [],
    extent: item.extent,
    preview: { thumbnail: item.preview?.thumbnail ?? null },
    modified: item.timestamps.modified,
    capabilities: item.capabilities ?? [],
    formats: collectFormats(item),
    sharing: item.access.sharing,
    openData: item.access.openData,
    viewerSupport,
  };
}

/** Console catalog projection — canonical ContentItemSummary plus the contract tag. */
export function projectCatalogSummary(item) {
  const summary = summarizeContentItem(item);
  return { ...summary, contract: findContract("content-item") };
}

export function projectBuilderPlan({ source, savedMap }) {
  return {
    id: `plan-${source.itemId}-${savedMap.id}`,
    sourceItemId: source.itemId,
    savedMapId: savedMap.id,
    warnings: [],
    contract: findContract("generated-app-lifecycle"),
  };
}

export function projectAppPackage({ plan }) {
  return {
    id: `${plan.id}-pkg`,
    version: "1",
    widgets: [
      { kind: "map", binding: plan.savedMapId },
      { kind: "list", binding: plan.sourceItemId },
      { kind: "filter", binding: plan.sourceItemId },
    ],
    contract: findContract("generated-app-lifecycle"),
  };
}

export function projectGeneratedAppRecord(serverItem) {
  const ext = serverItem.extensions?.["honua-generated-app"];
  if (!ext || ext.schema !== "honua-generated-app-lifecycle/v1") {
    throw new Error(`generated-app extension missing or schema mismatch on item ${serverItem.id}`);
  }
  const active = ext.revisions.find((r) => r.id === ext.activeRevisionId);
  if (!active) {
    throw new Error(`active generated-app revision ${ext.activeRevisionId} not found on item ${serverItem.id}`);
  }
  // generated-app-lifecycle/v1 requires the published item carry a
  // saved-map or catalog-item dependency back to the source it was
  // generated from; the smoke fails fast here so a missing provenance
  // edge is attributed to the SDK projection rather than the server.
  const expectedRole = ext.source.kind === "saved-map" ? "saved-map" : "catalog-item";
  const provenanceDep = serverItem.dependencies.find(
    (d) => d.id === ext.source.itemId && d.role === expectedRole,
  );
  if (!provenanceDep) {
    throw new Error(
      `generated-app ${serverItem.id} missing ${expectedRole} dependency back to source ${ext.source.itemId}`,
    );
  }
  return {
    item: serverItem,
    summary: summarizeContentItem(serverItem),
    lifecycle: ext,
    activeRevision: active,
    contract: findContract("generated-app-lifecycle"),
  };
}
