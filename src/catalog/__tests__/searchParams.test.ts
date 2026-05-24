import { describe, expect, it } from "vitest";

import { DEFAULT_SEARCH_STATE, readSearchParams, toListItemsRequest, writeSearchParams } from "../searchParams.js";

describe("catalog searchParams", () => {
  it("round-trips empty defaults to an empty URL", () => {
    const params = writeSearchParams(DEFAULT_SEARCH_STATE);
    expect(params.toString()).toBe("");
  });

  it("reads each filter from URL search params", () => {
    const url = new URLSearchParams({
      q: "parcels",
      type: "service",
      tag: "basemap",
      owner: "org_honua",
      visibility: "public",
      sort: "title-asc",
      cursor: "12",
    });
    const state = readSearchParams(url);
    expect(state).toEqual({
      q: "parcels",
      type: "service",
      tag: "basemap",
      owner: "org_honua",
      visibility: "public",
      sort: "title-asc",
      cursor: "12",
    });
  });

  it("ignores unknown enum values rather than crashing", () => {
    const url = new URLSearchParams({ type: "spaceship", visibility: "rumored", sort: "stars" });
    const state = readSearchParams(url);
    expect(state.type).toBeNull();
    expect(state.visibility).toBeNull();
    expect(state.sort).toBe("modified-desc");
  });

  it("defaults sort to relevance when q is set and no sort param given", () => {
    const url = new URLSearchParams({ q: "parcels" });
    const state = readSearchParams(url);
    expect(state.sort).toBe("relevance");
  });

  it("writes sort when it deviates from the q-driven default", () => {
    const params = writeSearchParams({
      ...DEFAULT_SEARCH_STATE,
      q: "parcels",
      sort: "title-asc",
    });
    expect(params.get("sort")).toBe("title-asc");
  });

  it("omits sort from URL when it equals the q-driven default", () => {
    const params = writeSearchParams({
      ...DEFAULT_SEARCH_STATE,
      q: "parcels",
      sort: "relevance",
    });
    expect(params.get("sort")).toBeNull();
  });

  it("translates visibility URL param to sharing on the wire request", () => {
    const url = new URLSearchParams({ visibility: "org" });
    const state = readSearchParams(url);
    const request = toListItemsRequest(state);
    expect(request).toMatchObject({ sharing: "org" });
    expect(request).not.toHaveProperty("visibility");
  });

  it("strips empty filters from the wire request", () => {
    const request = toListItemsRequest(DEFAULT_SEARCH_STATE);
    expect(request).toEqual({ sort: "modified-desc" });
  });
});
