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

// Default openability gate per item type. NOT part of the summary
// projection — content-item/v1.1.0 ContentItemSummary.viewerSupport is
// `null` when the publisher has not asserted an override (see
// honua-portal/src/contracts/content-item.ts `summarize()`). The viewer
// layer applies this default when deciding whether to render the "Open
// in viewer" button on a card whose summary carries null viewerSupport.
const VIEWER_TYPE_DEFAULTS = Object.freeze({
  service: { supported: true, reason: null },
  layer: { supported: true, reason: null },
  map: { supported: true, reason: null },
  scene: { supported: false, reason: "Viewer does not support scenes yet." },
  app: { supported: false, reason: "Apps open via the generated-app preview route, not the viewer." },
  document: { supported: false, reason: "Documents are not openable in the map viewer." },
  "external-url": { supported: false, reason: "External URLs are not openable in the map viewer." },
});

/**
 * Viewer-layer openability gate. Reads the publisher override from the
 * summary if asserted, otherwise applies the type default. Lives outside
 * `summarizeContentItem` so the summary DTO matches the canonical schema
 * (`viewerSupport: null` when no override is asserted).
 */
export function resolveViewerOpenability(summary) {
  if (!summary) throw new Error("resolveViewerOpenability requires a summary");
  if (summary.viewerSupport) return summary.viewerSupport;
  return VIEWER_TYPE_DEFAULTS[summary.type] ?? { supported: null, reason: null };
}

/**
 * Canonical content-item/v1.1.0 ContentItemSummary projection. Mirrors
 * honua-portal/src/contracts/content-item.ts `summarize()`; viewerSupport
 * is the projection of `extensions['honua-portal-viewer']` and is `null`
 * when the publisher has not asserted an override. Type-default
 * openability lives in `resolveViewerOpenability` on the viewer layer.
 */
export function summarizeContentItem(item) {
  if (!item) throw new Error("summarizeContentItem requires a content item");
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
    viewerSupport: readViewerSupport(item.extensions),
  };
}

/** Console catalog projection — canonical ContentItemSummary plus the contract tag. */
export function projectCatalogSummary(item) {
  const summary = summarizeContentItem(item);
  return { ...summary, contract: findContract("content-item") };
}

// Constants mirroring honua-sdk-js/src/generated-app/manifest.ts so a
// version bump there surfaces here when the test fixtures are regenerated.
// Kept inline (not imported) until #4/#5 wire the SDK package in directly.
const HONUA_GENERATED_APP_MANIFEST_FORMAT_V1 = "honua_generated_app_manifest.v1";
const HONUA_GENERATED_APP_MANIFEST_ARTIFACT_KIND = "honua.generated-app.manifest";
const HONUA_GENERATED_APP_MANIFEST_ARTIFACT_VERSION = 1;
const HONUA_GENERATED_APP_PROFILE_OPERATIONS_DASHBOARD_V1 = "operations-dashboard.v1";

/**
 * BuilderPlan projection. Mirrors @honua/sdk-js
 * `operator/workspace/types.ts` `BuilderPlan` so a porting ticket can
 * swap this fixture for the real SDK structure without changing the
 * smoke scenario shape. Required: id, intentId, kind='builder', steps[].
 */
export function projectBuilderPlan({ source, savedMap }) {
  const planId = `plan-${source.itemId}-${savedMap.id}`;
  return {
    id: planId,
    intentId: `intent-${source.itemId}`,
    kind: "builder",
    steps: [
      {
        id: `${planId}-compose-spec`,
        kind: "builder.compose-spec",
        label: "Compose BuildSpec from saved map",
        inputs: [source.itemId, savedMap.id],
        outputs: [`${planId}-spec`],
      },
      {
        id: `${planId}-materialize-package`,
        kind: "builder.materialize-package",
        label: "Materialize AppPackage from BuildSpec",
        inputs: [`${planId}-spec`],
        outputs: [`${planId}-pkg`],
      },
      {
        id: `${planId}-publish-manifest`,
        kind: "builder.publish-manifest",
        label: "Publish generated-app manifest artifact",
        inputs: [`${planId}-pkg`],
        outputs: [`${planId}-pkg-manifest`],
      },
    ],
    sourceItemId: source.itemId,
    savedMapId: savedMap.id,
    warnings: [],
    contract: findContract("generated-app-lifecycle"),
  };
}

/**
 * AppPackage projection. Satisfies the @honua/sdk-js `AppPackage`
 * structural interface (id, version, assets[]) AND the generated-app
 * preview projection's `HonuaGeneratedAppPackage` extension
 * (manifest_artifact OR manifestArtifact carrying a profile-v1 manifest)
 * so the preview runtime can hydrate without a second fetch. Adding both
 * casings mirrors how the real generated-app projection accepts either.
 */
export function projectAppPackage({ plan }) {
  const packageId = `${plan.id}-pkg`;
  const manifestArtifactId = `${packageId}-manifest`;
  const widgets = [
    { id: `${packageId}-w-map`, kind: "map", sourceId: plan.savedMapId },
    { id: `${packageId}-w-table`, kind: "table", sourceId: plan.sourceItemId },
    { id: `${packageId}-w-filter`, kind: "filter", sourceId: plan.sourceItemId, field: "status" },
  ];
  const manifest = {
    format: HONUA_GENERATED_APP_MANIFEST_FORMAT_V1,
    profile: HONUA_GENERATED_APP_PROFILE_OPERATIONS_DASHBOARD_V1,
    appId: packageId,
    title: `Generated app for ${plan.sourceItemId}`,
    data: { sourceId: plan.sourceItemId },
    layout: { kind: "operations-dashboard", widgets },
  };
  const manifestArtifact = {
    artifactKind: HONUA_GENERATED_APP_MANIFEST_ARTIFACT_KIND,
    artifactVersion: HONUA_GENERATED_APP_MANIFEST_ARTIFACT_VERSION,
    manifest,
  };
  return {
    id: packageId,
    version: "1.0.0",
    assets: [
      { id: manifestArtifactId, kind: "manifest", url: `urn:honua:asset:${manifestArtifactId}` },
      { id: `${packageId}-bundle`, kind: "app-package", url: `urn:honua:asset:${packageId}-bundle` },
    ],
    // Both casings are accepted by the SDK generated-app projection; we
    // emit `manifestArtifact` (camelCase) and re-expose under the
    // snake_case alias so a consumer reading either key sees the same
    // artifact and a regression to a single casing is caught downstream.
    manifestArtifact,
    manifest_artifact: manifestArtifact,
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
