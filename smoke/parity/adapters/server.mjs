// Server adapter: in-memory stand-in for honua-server's content/metadata,
// share, and embed-token surfaces. Each method mirrors the contract that
// the real server endpoint will honor when honua-server#1162 (Console
// metadata v2) and honua-portal#11/#15/#17 (publish/share/open-data) wire
// the live transport in. Until then the smoke exercises the
// owning-layer-tagged chain end-to-end against this adapter so failures
// stay attributable when components are swapped one by one.

import { findContract } from "../contracts.mjs";

const ULID_ALPHABET = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";

function deterministicUlid(prefix, counter) {
  const ts = Date.UTC(2026, 4, 23) + counter;
  const base = ts.toString(36).padStart(10, "0").toUpperCase();
  const tail = [];
  let v = counter;
  for (let i = 0; i < 16; i += 1) {
    tail.push(ULID_ALPHABET[(v + prefix.charCodeAt(0) * (i + 1)) % ULID_ALPHABET.length]);
    v = (v * 31 + prefix.charCodeAt(i % prefix.length)) >>> 0;
  }
  return (base + tail.join("")).slice(0, 26);
}

function mapAdminStatusToCatalogStatus(adminStatus) {
  const v = String(adminStatus).toLowerCase();
  if (["active", "ok", "ready", "running"].includes(v)) return "available";
  if (["degraded", "partial", "throttled", "warming", "publishing", "deploying"].includes(v)) return "limited";
  if (["draft", "unpublished"].includes(v)) return "draft";
  return "unavailable";
}

export function createServerAdapter({ originUrl } = {}) {
  if (!originUrl) throw new Error("createServerAdapter requires originUrl");
  const items = new Map();
  const savedMaps = new Map();
  const shareTiers = new Map();
  const embedTokens = new Map();
  let counter = 0;
  const nextId = (prefix) => {
    counter += 1;
    return deterministicUlid(prefix, counter);
  };

  return {
    /** Apply a publish-handoff event from legacy admin. */
    publishService(event) {
      const existing = [...items.values()].find(
        (i) => i.target?.type === "service" && i.source.sourceId === event.sourceServiceId,
      );
      const id = existing?.id ?? nextId("svc");
      const now = new Date().toISOString();
      const item = {
        id,
        slug: event.metadata.serviceName ?? null,
        type: "service",
        title: event.metadata.title,
        summary: event.metadata.summary ?? "",
        description: event.metadata.description ?? "",
        tags: Array.isArray(event.metadata.tags) ? [...event.metadata.tags] : [],
        owner: { id: event.owner.id, name: event.owner.displayName ?? event.owner.id, kind: event.owner.kind },
        timestamps: {
          created: existing?.timestamps.created ?? now,
          modified: now,
          published: now,
          refreshed: event.lastCheckedAt ?? now,
        },
        extent: event.metadata.extent ?? null,
        nativeCrs: event.metadata.nativeCrs ?? null,
        license: event.metadata.license ?? { spdx: null, name: "All rights reserved", url: null },
        attribution: event.metadata.attribution ?? null,
        source: {
          kind: "publish",
          sourceId: event.sourceServiceId,
          jobId: event.importJobId ?? null,
          publishedBy: event.actor,
          history: [
            ...(existing?.source.history ?? []),
            { at: now, kind: existing ? "publish" : "publish", actor: event.actor },
          ],
        },
        target: {
          type: "service",
          serviceName: event.metadata.serviceName ?? event.sourceServiceId,
          kind: "feature",
          layerCount: event.metadata.layerCount ?? 1,
          serviceUrl: event.serviceUrl,
          serviceType: event.serviceType,
          importJobId: event.importJobId ?? null,
          status: mapAdminStatusToCatalogStatus(event.status),
          statusDetail: event.statusReason ?? null,
          adminDiagnosticsRef: event.adminDiagnosticsRef ?? null,
          lastCheckedAt: event.lastCheckedAt ?? now,
        },
        endpoints: {
          self: { href: `${originUrl}/api/v1/items/${id}`, type: "application/json" },
          geoservices: { href: event.serviceUrl, type: "application/json" },
          ogcFeatures: null,
          stac: null,
          tiles: null,
        },
        preview: { thumbnail: null, image: null },
        capabilities: event.metadata.capabilities ?? [],
        dependencies: [],
        access: { sharing: "private", embeddable: false, openData: false },
        extensions: {},
      };
      items.set(id, item);
      return { item, contract: findContract("content-item") };
    },

    /** List catalog items, mirroring the read shape Console consumes. */
    listCatalog() {
      return {
        items: [...items.values()].map((i) => ({
          id: i.id,
          slug: i.slug,
          type: i.type,
          title: i.title,
          summary: i.summary,
          owner: i.owner,
          status: i.target?.type === "service" ? i.target.status : null,
        })),
        contract: findContract("content-item"),
      };
    },

    /** Read a single catalog item by id. */
    getItem(id) {
      const item = items.get(id);
      if (!item) return { kind: "missing" };
      return { kind: "ok", item, contract: findContract("content-item") };
    },

    /** Persist a saved map document. */
    saveMap({ id, title, owner, operationalLayers, extent }) {
      const mapId = id ?? nextId("map");
      const now = new Date().toISOString();
      const doc = {
        id: mapId,
        type: "map",
        title,
        owner,
        document: {
          schema: "honua-webmap/v1",
          extent,
          operationalLayers,
        },
        timestamps: { created: now, modified: now, published: null, refreshed: now },
        access: { sharing: "private", embeddable: false, openData: false },
      };
      savedMaps.set(mapId, doc);
      return { savedMap: doc, contract: findContract("webmap-doc") };
    },

    /** Record a published generated-app content item. */
    publishGeneratedApp({ source, manifestVersion, plan, appPackage, owner, title }) {
      const id = nextId("app");
      const now = new Date().toISOString();
      const revisionId = nextId("rev");
      const item = {
        id,
        slug: null,
        type: "app",
        title,
        summary: `Studio-generated app from ${source.title}`,
        description: "",
        tags: [],
        owner,
        timestamps: { created: now, modified: now, published: now, refreshed: now },
        extent: null,
        nativeCrs: null,
        license: { spdx: null, name: "All rights reserved", url: null },
        attribution: null,
        source: {
          kind: "publish",
          sourceId: source.itemId,
          jobId: null,
          publishedBy: owner.id,
          history: [{ at: now, kind: "publish", actor: owner.id }],
        },
        target: { type: "app", manifestVersion },
        endpoints: { self: { href: `${originUrl}/api/v1/items/${id}`, type: "application/json" } },
        preview: { thumbnail: null, image: null },
        capabilities: [],
        dependencies: [{ id: source.itemId, type: source.itemType, role: "datasource" }],
        access: { sharing: "private", embeddable: false, openData: false },
        extensions: {
          "honua-generated-app": {
            schema: "honua-generated-app-lifecycle/v1",
            state: "published",
            source: { kind: source.kind, itemId: source.itemId, itemType: source.itemType, title: source.title },
            activeRevisionId: revisionId,
            revisions: [
              {
                id: revisionId,
                sequence: 1,
                label: "v1",
                createdAt: now,
                actor: owner.id,
                manifestVersion,
                planRef: { id: plan.id, warnings: plan.warnings ?? [] },
                appPackageRef: { id: appPackage.id, kind: "app-package", version: appPackage.version ?? "1" },
                buildSpecRef: { id: `${plan.id}-spec`, kind: "build-spec", version: "1" },
                manifestArtifact: { id: `${id}-manifest`, kind: "manifest", version: manifestVersion },
                serverJob: null,
                provenance: [
                  {
                    kind: "source",
                    itemId: source.itemId,
                    note: `Generated app derives from ${source.kind} ${source.itemId}`,
                  },
                ],
                previewUrl: `${originUrl}/studio/preview/${id}`,
                rollbackOf: null,
              },
            ],
            unsupportedReason: null,
          },
        },
      };
      items.set(id, item);
      return { item, contract: findContract("generated-app-lifecycle") };
    },

    /** Apply a share-access patch to a catalog item id. */
    patchAccess({ itemId, tier, embeddable }) {
      const item = items.get(itemId);
      if (!item) return { kind: "missing" };
      const next = { sharing: tier, embeddable: !!embeddable, openData: tier === "public" };
      item.access = next;
      shareTiers.set(itemId, next);
      return { kind: "ok", access: next, contract: findContract("share-access") };
    },

    /** Mint a same-origin embed token for an item id. */
    mintEmbedToken({ itemId, audience }) {
      const item = items.get(itemId);
      if (!item) return { kind: "missing" };
      if (!item.access.embeddable) {
        return { kind: "forbidden", reason: "item is not embeddable" };
      }
      const token = `embed-${itemId}-${audience}-${counter += 1}`;
      const descriptor = {
        token,
        itemId,
        audience,
        expiresAt: new Date(Date.now() + 30 * 60_000).toISOString(),
        closure: item.dependencies.map((d) => d.id),
      };
      embedTokens.set(token, descriptor);
      return { kind: "ok", descriptor, contract: findContract("embed-token") };
    },
  };
}
