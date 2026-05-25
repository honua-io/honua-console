// Contract-version registry exercised by the Console parity smoke.
//
// The acceptance criteria on honua-console#9 require the smoke evidence to
// include API contract versions so release-promotion tooling can attribute
// behavior to a specific contract revision. Each entry names the schema as
// published in the canonical schemas/ directory of its owning repo and the
// `owningLayer` that ships it.
//
// When a contract version changes in its source repo (honua-server,
// honua-sdk-js, honua-portal during transition), bump the `version` field
// here in the same PR so the smoke evidence stays truthful.

import { OWNING_LAYERS } from "./owning-layers.mjs";

export const CONTRACT_VERSIONS = Object.freeze([
  Object.freeze({
    name: "content-item",
    version: "v1.1.0",
    owningLayer: OWNING_LAYERS.server.id,
    sourceRepo: "honua-portal",
    note: "Catalog item envelope; nests endpoints in ServiceLink records and adds the `formats` summary projection.",
  }),
  Object.freeze({
    name: "publish-handoff",
    version: "v1",
    owningLayer: OWNING_LAYERS["legacy-admin"].id,
    sourceRepo: "honua-portal",
    note: "Admin -> Console publish event; upserts on (source.kind, source.sourceId).",
  }),
  Object.freeze({
    name: "webmap-doc",
    version: "v1",
    owningLayer: OWNING_LAYERS.console.id,
    sourceRepo: "honua-portal",
    note: "Saved-map document; viewer hydrates and Studio drafts read this shape.",
  }),
  Object.freeze({
    name: "share-access",
    version: "v1",
    owningLayer: OWNING_LAYERS.server.id,
    sourceRepo: "honua-portal",
    note: "ShareAccess tier ladder response; openData stays on content-item.access.",
  }),
  Object.freeze({
    name: "embed-token",
    version: "v1",
    owningLayer: OWNING_LAYERS.server.id,
    sourceRepo: "honua-portal",
    note: "Same-origin embed token descriptor with closure manifest.",
  }),
  Object.freeze({
    name: "generated-app-lifecycle",
    version: "v1",
    owningLayer: OWNING_LAYERS.sdk.id,
    sourceRepo: "honua-portal",
    note: "Generated-app revisions, plan/spec/package refs, and provenance — projected by the SDK.",
  }),
  Object.freeze({
    name: "studio-authoring-shell",
    version: "v1",
    owningLayer: OWNING_LAYERS.console.id,
    sourceRepo: "honua-console",
    note: "Console-owned authoring projection for prompt clarification, package inspection, preview, saved-version, and publish state.",
  }),
  Object.freeze({
    name: "build-artifact",
    version: "v1",
    owningLayer: OWNING_LAYERS.devops.id,
    sourceRepo: "honua-console",
    note: "version.json shape emitted by the Blazor Console artifact and consumed by devops promotion.",
  }),
]);

export function findContract(name) {
  const entry = CONTRACT_VERSIONS.find((c) => c.name === name);
  if (!entry) {
    throw new Error(
      `Unknown contract "${name}". Known: ${CONTRACT_VERSIONS.map((c) => c.name).join(", ")}`,
    );
  }
  return entry;
}
