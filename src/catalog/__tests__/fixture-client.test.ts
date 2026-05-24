import { describe, expect, it } from "vitest";

import { CatalogError } from "../../contracts/content-item.js";
import { FixtureCatalogClient } from "../client.js";
import { MISSING_FIXTURE_ID, UNAUTHORIZED_FIXTURE_IDS, loadCatalogFixtures } from "../fixtures.js";

describe("FixtureCatalogClient.listItems", () => {
  it("returns a card-sized summary for every catalog item", async () => {
    const client = new FixtureCatalogClient(loadCatalogFixtures());
    const response = await client.listItems();

    expect(response.items.length).toBeGreaterThan(0);
    expect(response.nextCursor).toBeNull();
    for (const summary of response.items) {
      expect(summary).toMatchObject({
        id: expect.any(String),
        type: expect.any(String),
        title: expect.any(String),
        summary: expect.any(String),
      });
      // ContentItemSummary MUST NOT carry the heavyweight detail fields.
      expect(Object.hasOwn(summary, "description")).toBe(false);
      expect(Object.hasOwn(summary, "endpoints")).toBe(false);
      expect(Object.hasOwn(summary, "dependencies")).toBe(false);
    }
  });

  it("filters by type, owner, tag, and free-text query", async () => {
    const client = new FixtureCatalogClient(loadCatalogFixtures());

    const services = await client.listItems({ type: "service" });
    expect(services.items.every((it) => it.type === "service")).toBe(true);
    expect(services.items.length).toBeGreaterThanOrEqual(2);

    const byOwner = await client.listItems({ owner: "user_alex" });
    expect(byOwner.items.every((it) => it.owner.id === "user_alex")).toBe(true);

    const byTag = await client.listItems({ tag: "basemap" });
    expect(byTag.items.every((it) => it.tags.includes("basemap"))).toBe(true);

    const byQuery = await client.listItems({ q: "parcels" });
    expect(byQuery.items.length).toBeGreaterThan(0);
    expect(byQuery.items.every((it) => `${it.title} ${it.summary}`.toLowerCase().includes("parcels"))).toBe(true);
  });

  it("paginates with an opaque cursor and stable ordering", async () => {
    const client = new FixtureCatalogClient(loadCatalogFixtures());
    const first = await client.listItems({ limit: 3 });
    expect(first.items).toHaveLength(3);
    expect(first.nextCursor).not.toBeNull();

    const second = await client.listItems({ limit: 3, cursor: first.nextCursor });
    expect(second.items).toHaveLength(3);
    const overlap = first.items.some((a) => second.items.some((b) => a.id === b.id));
    expect(overlap).toBe(false);
  });

  it("clamps limit to [1, 100]", async () => {
    const client = new FixtureCatalogClient(loadCatalogFixtures());
    const tooLarge = await client.listItems({ limit: 9999 });
    expect(tooLarge.items.length).toBeLessThanOrEqual(100);
    const tooSmall = await client.listItems({ limit: 0 });
    expect(tooSmall.items.length).toBe(1);
  });
});

describe("FixtureCatalogClient.getItem", () => {
  it("returns the full ContentItem", async () => {
    const client = new FixtureCatalogClient(loadCatalogFixtures());
    const item = await client.getItem("01HXY3ZK7N1J2Q9V8M0FQ2PWAB");
    expect(item.title).toBe("City Parcels 2026");
    expect(item.target.type).toBe("service");
    expect(item.endpoints.geoservices?.accessURL).toMatch(/FeatureServer$/);
    expect(item.dependencies).toEqual([]);
  });

  it("throws CatalogError(missing) for an unknown id", async () => {
    const client = new FixtureCatalogClient(loadCatalogFixtures());
    await expect(client.getItem(MISSING_FIXTURE_ID)).rejects.toMatchObject({
      name: "CatalogError",
      code: "missing",
    });
  });

  it("throws CatalogError(unauthorized) for the unauthorized fixture", async () => {
    const client = new FixtureCatalogClient(loadCatalogFixtures());
    for (const id of UNAUTHORIZED_FIXTURE_IDS) {
      await expect(client.getItem(id)).rejects.toBeInstanceOf(CatalogError);
      await expect(client.getItem(id)).rejects.toMatchObject({ code: "unauthorized" });
    }
  });
});

describe("FixtureCatalogClient.patchAccess", () => {
  it("applies a permitted access patch and updates subsequent reads", async () => {
    const client = new FixtureCatalogClient(loadCatalogFixtures());

    const result = await client.patchAccess?.({
      id: "01HXY3ZK7N1J2Q9V8M0FQ2PWAD",
      access: { sharing: "org", embeddable: false },
    });

    expect(result).toMatchObject({ kind: "ok", access: { sharing: "org", embeddable: false } });
    await expect(client.getItem("01HXY3ZK7N1J2Q9V8M0FQ2PWAD")).resolves.toMatchObject({
      access: { sharing: "org", embeddable: false },
    });
  });
});
