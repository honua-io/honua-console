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
    note: "Console authoring projection for prompt clarification and package inspection; draft/validation/preview-plan/saved-version/publish bind to the honua-server package lifecycle (#1180/#1181). Preview is a planning action, not a stored lifecycle state.",
  }),
  Object.freeze({
    name: "build-artifact",
    version: "v1",
    owningLayer: OWNING_LAYERS.devops.id,
    sourceRepo: "honua-console",
    note: "version.json shape emitted by the Blazor Console artifact and consumed by devops promotion.",
  }),
  Object.freeze({
    name: "workflow-package",
    version: "v1",
    owningLayer: OWNING_LAYERS.server.id,
    sourceRepo: "honua-server",
    note: "Server-owned workflow.package graph, parameters, schedule, worker profile, failure routing, output schema, and publication intent.",
  }),
  Object.freeze({
    name: "workflow-dry-run",
    version: "v1",
    owningLayer: OWNING_LAYERS.server.id,
    sourceRepo: "honua-server",
    note: "Workflow dry-run job response with sample data, logs, artifacts, and output schemas.",
  }),
  Object.freeze({
    name: "workflow-publication",
    version: "v1",
    owningLayer: OWNING_LAYERS.server.id,
    sourceRepo: "honua-server",
    note: "Workflow publication record for batch jobs, schedules, and eligible GP/process invocation endpoints.",
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

// Contract-drift detection (honua-console#239, AUD-106). The local registry above is
// hand-maintained, so on its own the smoke can only prove the registry is internally
// consistent — it cannot prove the registry still matches what the deployed server actually
// serves. When a build artifact's `version.json` publishes a `contracts` block (a map of
// contract name -> served version), this compares it against the registry and reports any
// divergence so the smoke FAILS on real contract drift instead of silently shipping a stale
// version. When no `contracts` block is served (local/fixture runs, or a server that does not
// yet publish one) `checked` is 0 and `served` is false — drift detection is a documented
// no-op, never a false pass.
export function compareServedContractVersions(servedContracts) {
  if (servedContracts == null || typeof servedContracts !== "object" || Array.isArray(servedContracts)) {
    return { served: false, checked: 0, drift: [], unknown: [] };
  }
  const drift = [];
  const unknown = [];
  let checked = 0;
  for (const [name, servedVersion] of Object.entries(servedContracts)) {
    const entry = CONTRACT_VERSIONS.find((c) => c.name === name);
    if (!entry) {
      unknown.push({ name, servedVersion });
      continue;
    }
    checked += 1;
    if (entry.version !== servedVersion) {
      drift.push({ name, registryVersion: entry.version, servedVersion });
    }
  }
  return { served: true, checked, drift, unknown };
}
