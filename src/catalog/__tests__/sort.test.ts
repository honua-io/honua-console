import { describe, expect, it } from "vitest";

import { FixtureCatalogClient } from "../client.js";
import { loadCatalogFixtures } from "../fixtures.js";

describe("FixtureCatalogClient sort", () => {
  it("defaults to modified-desc when no sort is supplied", async () => {
    const client = new FixtureCatalogClient(loadCatalogFixtures());
    const response = await client.listItems();
    const dates = response.items.map((it) => it.modified);
    const sorted = [...dates].sort((a, b) => b.localeCompare(a));
    expect(dates).toEqual(sorted);
  });

  it("sorts by title ascending when sort=title-asc", async () => {
    const client = new FixtureCatalogClient(loadCatalogFixtures());
    const response = await client.listItems({ sort: "title-asc" });
    const titles = response.items.map((it) => it.title);
    const sorted = [...titles].sort((a, b) => a.localeCompare(b));
    expect(titles).toEqual(sorted);
  });

  it("ranks q-matching items higher under sort=relevance", async () => {
    const client = new FixtureCatalogClient(loadCatalogFixtures());
    const response = await client.listItems({ q: "permit", sort: "relevance" });
    expect(response.items.length).toBeGreaterThan(0);
    expect(response.items[0]!.title).toBe("Permit Finder");
  });
});
