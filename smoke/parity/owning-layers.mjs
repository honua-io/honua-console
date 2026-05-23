// Owning-layer taxonomy for the Console parity smoke. Every scenario step
// declares one of these so that a failure points at the repo or runtime
// component that owns the broken contract. The list intentionally matches
// the acceptance-criteria wording on honua-console#9: failures must
// "identify the owning layer: server, SDK, Console, legacy Admin transition,
// or devops routing."

export const OWNING_LAYERS = Object.freeze({
  devops: {
    id: "devops",
    label: "Devops / single deployable artifact",
    repo: "honua-devops",
    description:
      "Single deployable artifact, version.json metadata, same-origin SPA fallback, reverse-proxy routing for /studio, /catalog, /operate, /share, and /api.",
  },
  server: {
    id: "server",
    label: "Honua Server (metadata, content, publish, share, embed)",
    repo: "honua-server",
    description:
      "Server-owned content/metadata, publish-handoff upsert, share/access mutations, embed-token minting, and provenance.",
  },
  sdk: {
    id: "sdk",
    label: "Honua SDK (@honua/sdk-js)",
    repo: "honua-sdk-js",
    description:
      "Browser-safe contract projections: ContentItemSummary, BuilderPlan, AppPackage, generated-app lifecycle, share/embed envelopes.",
  },
  console: {
    id: "console",
    label: "Honua Console (Studio, Catalog, Operate, Share UI)",
    repo: "honua-console",
    description:
      "Console UI surfaces — catalog browse, viewer, saved-map persistence, Studio drafts, share/embed UX.",
  },
  "legacy-admin": {
    id: "legacy-admin",
    label: "Legacy Admin transition surface",
    repo: "honua-server-admin",
    description:
      "Transitional operator publishing flow that emits the publish-handoff event into Console catalog until /operate ports fully replace it.",
  },
});

export const OWNING_LAYER_IDS = Object.freeze(Object.keys(OWNING_LAYERS));

/**
 * Resolve a layer descriptor by id. Throws on unknown values so a typo in a
 * scenario step or adapter cannot silently widen the taxonomy.
 */
export function resolveOwningLayer(id) {
  const descriptor = OWNING_LAYERS[id];
  if (!descriptor) {
    throw new Error(
      `Unknown owning layer "${id}". Allowed: ${OWNING_LAYER_IDS.join(", ")}`,
    );
  }
  return descriptor;
}
