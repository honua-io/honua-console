import { describe, expect, it } from "vitest";

import { CatalogError, type ContentItem, type ServiceLink } from "../../contracts/content-item.js";
import type { CatalogClient } from "../client.js";
import { FixtureCatalogClient } from "../client.js";
import { getDependencyClosure } from "../dependencies.js";
import {
  MISSING_FIXTURE_ID,
  UNAUTHORIZED_FIXTURE_IDS,
  UNSUPPORTED_FIXTURE_IDS,
  loadCatalogFixtures,
} from "../fixtures.js";

const FANOUT_ID = "01HXY3ZK7N1J2Q9V8M0FQ2PWAK";
const PARCELS_OVERVIEW_MAP_ID = "01HXY3ZK7N1J2Q9V8M0FQ2PWAD";

describe("getDependencyClosure", () => {
  it("walks a 3-deep tree and categorizes every empty/error surface", async () => {
    const data = loadCatalogFixtures();
    const client = new FixtureCatalogClient(data);
    const root = await client.getItem(FANOUT_ID);

    const result = await getDependencyClosure(root, client);

    const okIds = result.nodes.map((n) => n.id);
    expect(okIds).toContain(PARCELS_OVERVIEW_MAP_ID);
    // The transitive children of the parcels overview map are also reached.
    expect(okIds).toContain("01HXY3ZK7N1J2Q9V8M0FQ2PWAC"); // active parcels layer
    expect(okIds).toContain("01HXY3ZK7N1J2Q9V8M0FQ2PWAE"); // basemap

    expect(result.missing.map((d) => d.id)).toEqual([MISSING_FIXTURE_ID]);
    expect(result.unauthorized.map((d) => d.id)).toEqual([...UNAUTHORIZED_FIXTURE_IDS]);
    expect(result.unsupported.map((d) => d.id)).toEqual([...UNSUPPORTED_FIXTURE_IDS]);

    for (const node of result.nodes) {
      expect(node.depth).toBeGreaterThanOrEqual(1);
      expect(node.depth).toBeLessThanOrEqual(5);
      expect(node.summary).not.toBeNull();
    }
  });

  it("terminates on a cycle without revisiting a node", async () => {
    const cyclic = makeItem("01CYCLE000000000000000ABCD", [
      { id: "01CYCLE000000000000000WXYZ", type: "layer", role: "operationalLayer" },
    ]);
    const partner = makeItem("01CYCLE000000000000000WXYZ", [
      { id: "01CYCLE000000000000000ABCD", type: "map", role: "operationalLayer" },
    ]);
    const cyclicClient = makeClient([cyclic, partner]);

    const result = await getDependencyClosure(cyclic, cyclicClient);
    expect(result.nodes).toHaveLength(1);
    expect(result.nodes[0]?.id).toBe(partner.id);
  });

  it("respects the depth cap and reports truncated when items remain", async () => {
    const data = loadCatalogFixtures();
    const client = new FixtureCatalogClient(data);
    const root = await client.getItem(FANOUT_ID);

    const result = await getDependencyClosure(root, client, { depth: 1 });
    expect(result.nodes.every((n) => n.depth === 1)).toBe(true);
    expect(result.truncated).toBe(true);
  });

  it("respects the node limit and reports truncated", async () => {
    const data = loadCatalogFixtures();
    const client = new FixtureCatalogClient(data);
    const root = await client.getItem(FANOUT_ID);

    const result = await getDependencyClosure(root, client, { limit: 1 });
    expect(result.nodes).toHaveLength(1);
    expect(result.truncated).toBe(true);
  });

  it("treats items with extensions['honua-portal-viewer'].supported=false as unsupported", async () => {
    const data = loadCatalogFixtures();
    const client = new FixtureCatalogClient({
      ...data,
      // Empty the unsupportedIds set so the predicate has to do the work.
      unsupportedIds: new Set(),
    });
    const root = await client.getItem(FANOUT_ID);
    const result = await getDependencyClosure(root, client);

    expect(result.unsupported.map((d) => d.id)).toEqual([...UNSUPPORTED_FIXTURE_IDS]);
  });

  it("counts missing/unauthorized/unsupported toward the node limit", async () => {
    const data = loadCatalogFixtures();
    const client = new FixtureCatalogClient(data);
    const root = await client.getItem(FANOUT_ID);

    // Limit of 2 must stop after two outcomes total — even though the fan-out
    // has one ok + one unsupported + one unauthorized + one missing.
    const result = await getDependencyClosure(root, client, { limit: 2 });
    const total = result.nodes.length + result.missing.length + result.unauthorized.length + result.unsupported.length;
    expect(total).toBeLessThanOrEqual(2);
    expect(result.truncated).toBe(true);
  });

  it("FixtureCatalogClient.getDependencies and getDependencyClosure agree on depth truncation", async () => {
    const data = loadCatalogFixtures();
    const client = new FixtureCatalogClient(data);
    const root = await client.getItem(FANOUT_ID);

    const standalone = await getDependencyClosure(root, client, { depth: 1 });
    const viaFixture = await client.getDependencies(FANOUT_ID, { depth: 1 });
    expect(viaFixture.truncated).toBe(standalone.truncated);
    expect(viaFixture.nodes.map((n) => n.id).sort()).toEqual(standalone.nodes.map((n) => n.id).sort());
    expect(viaFixture.missing.map((d) => d.id).sort()).toEqual(standalone.missing.map((d) => d.id).sort());
    expect(viaFixture.unauthorized.map((d) => d.id).sort()).toEqual(standalone.unauthorized.map((d) => d.id).sort());
    expect(viaFixture.unsupported.map((d) => d.id).sort()).toEqual(standalone.unsupported.map((d) => d.id).sort());
  });
});

function makeItem(id: string, deps: ContentItem["dependencies"]): ContentItem {
  return {
    id,
    slug: null,
    type: "map",
    title: `Item ${id}`,
    summary: "Synthetic test item.",
    description: "Synthetic test item.",
    tags: [],
    owner: { id: "test", name: "Test", kind: "user" },
    timestamps: { created: "2026-01-01T00:00:00Z", modified: "2026-01-01T00:00:00Z", published: null, refreshed: null },
    extent: null,
    nativeCrs: null,
    license: { spdx: null, name: "Test", url: null },
    attribution: null,
    source: { kind: "manual", sourceId: null, jobId: null, publishedBy: null, history: [] },
    target: { type: "map", webmapJsonRef: "test://", operationalLayerCount: 0 },
    endpoints: {
      self: portalLink(`https://example/items/${id}`),
      geoservices: null,
      ogcFeatures: null,
      stac: null,
      tiles: null,
    },
    preview: { thumbnail: null, image: null },
    capabilities: [],
    dependencies: deps,
    access: { sharing: "private", embeddable: false, openData: false },
    extensions: {},
  };
}

function portalLink(accessURL: string): ServiceLink {
  return {
    accessURL,
    format: "Honua:Portal:v1",
    mediaType: "text/html",
    describedBy: null,
    describedByType: null,
    conformsTo: ["https://schemas.honua.io/content-item/v1"],
  };
}

function makeClient(items: ContentItem[]): CatalogClient {
  const byId = new Map(items.map((i) => [i.id, i]));
  return {
    async listItems() {
      return { items: [], nextCursor: null };
    },
    async getItem(id: string) {
      const item = byId.get(id);
      if (!item) throw new CatalogError("missing", `no item with id ${id}`);
      return item;
    },
    async getDependencies() {
      return { nodes: [], missing: [], unauthorized: [], unsupported: [], truncated: false };
    },
  };
}
