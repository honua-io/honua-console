// SDK adapter: emulates the browser-safe projections that
// @honua/sdk-js ships for catalog summaries, builder plans, and
// generated-app packages. The smoke uses this projection layer because
// Console (the browser surface) never talks to honua-server's raw row
// shapes directly — it consumes the SDK projections. Tagging projection
// failures as `sdk` keeps a contract-version mismatch attributable to
// honua-sdk-js rather than to Console or server.

import { findContract } from "../contracts.mjs";

export function projectCatalogSummary(item) {
  if (!item) throw new Error("projectCatalogSummary requires a content item");
  return {
    id: item.id,
    slug: item.slug,
    type: item.type,
    title: item.title,
    summary: item.summary,
    owner: item.owner,
    modified: item.timestamps.modified,
    extent: item.extent,
    viewerSupport:
      item.type === "service" || item.type === "layer" || item.type === "map"
        ? { supported: true }
        : { supported: false, reason: `Viewer does not support ${item.type}` },
    capabilities: item.capabilities ?? [],
    access: item.access,
    contract: findContract("content-item"),
  };
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
  return { item: serverItem, lifecycle: ext, activeRevision: active, contract: findContract("generated-app-lifecycle") };
}
