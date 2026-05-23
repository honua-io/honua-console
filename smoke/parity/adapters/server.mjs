// Server adapter: in-memory stand-in for honua-server's content/metadata,
// share, and embed-token surfaces. Each method mirrors the contract that
// the real server endpoint will honor when honua-server#1162 (Console
// metadata v2) and honua-portal#11/#15/#17 (publish/share/open-data) wire
// the live transport in. Until then the smoke exercises the
// owning-layer-tagged chain end-to-end against this adapter so failures
// stay attributable when components are swapped one by one.
//
// Wire shapes mirror the canonical schemas at
// /home/makani/honua-portal/schemas/{content-item,share-access,embed-token,webmap-doc,publish-handoff}-v1.json.

import { findContract } from "../contracts.mjs";
import { summarizeContentItem } from "./sdk.mjs";

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

function portalSelfLink(href) {
  return {
    accessURL: href,
    format: "Honua:Portal:v1",
    mediaType: "application/json",
    describedBy: null,
    describedByType: null,
    conformsTo: ["https://schemas.honua.io/content-item/v1"],
  };
}

// generated-app-lifecycle/v1 enumerates these two source kinds for the
// dependency role; preserve the spelling so the SDK lifecycle projection
// can attribute provenance back to the original saved-map or catalog row.
function dependencyRoleFor(sourceKind) {
  if (sourceKind === "saved-map") return "saved-map";
  if (sourceKind === "catalog-item") return "catalog-item";
  throw new Error(
    `generated-app source.kind must be "saved-map" or "catalog-item" (got "${sourceKind}"); see generated-app-lifecycle/v1 content-item mapping.`,
  );
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
    /** Apply a publish-handoff/v1.1.0 payload from legacy admin. */
    publishService(handoff) {
      // publish-handoff/v1.1.0 idempotency key is (source.kind, source.sourceId);
      // the same sourceId under a different source.kind is a distinct provenance
      // chain and must mint a new content item rather than merge history.
      const existing = [...items.values()].find(
        (i) =>
          i.target?.type === "service" &&
          i.source.kind === handoff.source.kind &&
          i.source.sourceId === handoff.source.sourceId,
      );
      const id = existing?.id ?? nextId("svc");
      const now = new Date().toISOString();
      const selfHref = `${originUrl}/api/v1/items/${id}`;
      const item = {
        id,
        slug: handoff.target.serviceName ?? null,
        type: handoff.type,
        title: handoff.title,
        summary: handoff.summary,
        description: handoff.description ?? "",
        tags: Array.isArray(handoff.tags) ? [...handoff.tags] : [],
        owner: handoff.owner,
        timestamps: {
          created: existing?.timestamps.created ?? now,
          modified: now,
          published: now,
          refreshed: handoff.target.lastCheckedAt ?? null,
        },
        extent: handoff.extent,
        nativeCrs: handoff.nativeCrs,
        license: handoff.license,
        attribution: handoff.attribution,
        source: {
          kind: handoff.source.kind,
          sourceId: handoff.source.sourceId,
          jobId: handoff.source.jobId,
          publishedBy: handoff.source.publishedBy,
          history: [
            ...(existing?.source.history ?? []),
            { at: now, kind: handoff.source.kind, actor: handoff.source.publishedBy ?? "system" },
          ],
        },
        target: handoff.target,
        endpoints: {
          self: portalSelfLink(selfHref),
          geoservices: handoff.endpoints.geoservices,
          ogcFeatures: handoff.endpoints.ogcFeatures,
          stac: handoff.endpoints.stac,
          tiles: handoff.endpoints.tiles,
        },
        preview: handoff.preview,
        capabilities: handoff.capabilities,
        dependencies: handoff.dependencies,
        access: handoff.access,
        extensions: handoff.extensions ?? {},
      };
      items.set(id, item);
      return { item, contract: findContract("content-item") };
    },

    /** List catalog items as content-item/v1.1.0 ContentItemSummary[]. */
    listCatalog() {
      return {
        items: [...items.values()].map((i) => summarizeContentItem(i)),
        contract: findContract("content-item"),
      };
    },

    /** Read a single catalog item by id. */
    getItem(id) {
      const item = items.get(id);
      if (!item) return { kind: "missing" };
      return { kind: "ok", item, contract: findContract("content-item") };
    },

    /** Persist a saved map document conforming to webmap-doc/v1. */
    saveMap({ id, title, owner, sourceItem, extent }) {
      const mapId = id ?? nextId("map");
      const now = new Date().toISOString();
      const [west, south, east, north] = extent?.bbox ?? [-180, -90, 180, 90];
      const document = {
        version: "honua-webmap/v1",
        authoringApp: "honua-console",
        operationalLayers: [
          {
            id: `${sourceItem.id}-layer`,
            title: sourceItem.title,
            layerType: "honua-feature",
            sourceRef: { itemId: sourceItem.id, subLayerId: null },
            styleRef: null,
            visibility: true,
            opacity: 1,
            popupInfo: null,
            minScale: null,
            maxScale: null,
          },
        ],
        baseMap: {
          title: "Honua Basemap",
          baseMapLayers: [
            {
              id: "honua-basemap-vector",
              title: "Honua Vector Basemap",
              layerType: "honua-vector-tile",
              sourceRef: null,
              styleUrl: null,
              visibility: true,
              opacity: 1,
            },
          ],
        },
        initialState: {
          viewpoint: {
            extent: { xmin: west, ymin: south, xmax: east, ymax: north },
            rotation: 0,
          },
        },
      };
      const record = { id: mapId, title, owner, document, createdAt: now, modifiedAt: now };
      savedMaps.set(mapId, record);
      return { savedMap: record, contract: findContract("webmap-doc") };
    },

    /**
     * Record a published generated-app content item. Maps to the
     * generated-app-lifecycle/v1 content-item shape:
     *   target = { type: "app", framework: "honua", url: <preview URL> }
     *   source.kind = "manual" (publish acts on a generated artifact, not a service)
     *   dependencies[] roles are "saved-map" or "catalog-item"
     */
    publishGeneratedApp({ source, manifestVersion, plan, appPackage, owner, title }) {
      const id = nextId("app");
      const now = new Date().toISOString();
      const revisionId = nextId("rev");
      const previewUrl = `${originUrl}/apps/${id}/preview?revision=${revisionId}`;
      const item = {
        id,
        slug: null,
        type: "app",
        title,
        summary: `Studio-generated app from ${source.title}`,
        description: "",
        tags: [],
        owner,
        timestamps: { created: now, modified: now, published: now, refreshed: null },
        extent: null,
        nativeCrs: null,
        license: { spdx: null, name: "All rights reserved", url: null },
        attribution: null,
        source: {
          kind: "manual",
          sourceId: source.itemId,
          jobId: `apply-${plan.id}`,
          publishedBy: owner.id,
          history: [{ at: now, kind: "manual", actor: owner.id }],
        },
        target: { type: "app", url: previewUrl, framework: "honua" },
        endpoints: {
          self: portalSelfLink(`${originUrl}/api/v1/items/${id}`),
          geoservices: null,
          ogcFeatures: null,
          stac: null,
          tiles: null,
        },
        preview: { thumbnail: null, image: null },
        capabilities: [],
        dependencies: [{ id: source.itemId, type: source.itemType, role: dependencyRoleFor(source.kind) }],
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
                serverJob: { id: `apply-${plan.id}`, kind: "apply", status: "succeeded" },
                provenance: [
                  {
                    kind: "source",
                    itemId: source.itemId,
                    role: dependencyRoleFor(source.kind),
                    note: `Generated app derives from ${source.kind} ${source.itemId}`,
                  },
                ],
                previewUrl,
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

    /**
     * Apply a share-access patch. Returns a share-access/v1 descriptor
     * (sharing, embeddable, and tier-conditional groupIds/publicLinkToken).
     * Catalog `access.openData` is updated on the item so the next
     * summary projection reflects the share-tier change, but the
     * share-access response itself stays inside its own schema.
     */
    patchAccess({ itemId, tier, embeddable, groupIds }) {
      const item = items.get(itemId);
      if (!item) return { kind: "missing" };
      const openData = tier === "public";
      item.access = { sharing: tier, embeddable: !!embeddable, openData };
      const descriptor = { sharing: tier, embeddable: !!embeddable };
      if (tier === "group") {
        descriptor.groupIds = [...(groupIds ?? [])];
      }
      shareTiers.set(itemId, descriptor);
      return { kind: "ok", access: descriptor, contract: findContract("share-access") };
    },

    /** Mint a same-origin embed-token/v1 descriptor for an item id. */
    mintEmbedToken({ itemId, audience }) {
      const item = items.get(itemId);
      if (!item) return { kind: "missing" };
      if (!item.access.embeddable) {
        return { kind: "forbidden", reason: "item is not embeddable" };
      }
      counter += 1;
      const token = `embed-${itemId}-${audience}-${counter}`;
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
