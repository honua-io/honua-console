import { describe, expect, it } from "vitest";
import { FixtureShareClient, classifyPatchResult } from "../client.js";
import type { ShareAccess } from "../types.js";
import { FIXTURE_ITEMS, FIXTURE_OWNER_OF } from "./fixtures.js";

const baseClient = (overrides: { failPatchFor?: string } = {}) =>
  new FixtureShareClient({
    items: FIXTURE_ITEMS,
    callerId: "user-1",
    ownerOf: FIXTURE_OWNER_OF,
    failPatchFor: overrides.failPatchFor,
    groups: [{ id: "g-pilots", name: "Pilots" }],
  });

const access = (sharing: ShareAccess["sharing"]): ShareAccess => ({
  sharing,
  embeddable: false,
});

describe("FixtureShareClient.patchAccess (AC1: tier round-trips)", () => {
  it("private → org succeeds for an editor when no dep is narrower", async () => {
    const client = baseClient();
    const result = await client.patchAccess({
      id: "map-clean",
      access: access("org"),
    });
    expect(result.kind).toBe("ok");
    expect(client.getItem("map-clean")?.access).toEqual({
      sharing: "org",
      embeddable: false,
    });
  });

  it("org → public-link blocks when any dep is still narrower", async () => {
    // layer-a has only an org service dep; escalating layer-a to
    // public-link must be blocked by service-a.
    const client = baseClient();
    const result = await client.patchAccess({
      id: "layer-a",
      access: access("public-link"),
    });
    expect(result.kind).toBe("closureBlocked");
    if (result.kind !== "closureBlocked") return;
    expect(result.blockers.map((b) => b.id)).toEqual(["service-a"]);
  });

  it("public escalation surfaces every dep narrower than public", async () => {
    const client = baseClient();
    const result = await client.patchAccess({
      id: "map-1",
      access: access("public"),
    });
    expect(result.kind).toBe("closureBlocked");
    if (result.kind !== "closureBlocked") return;
    // All four typed deps (layer-a/org, layer-b/private, service-a/org,
    // service-b/private) are narrower than public; style-1 is
    // unsupported and skipped. Order is BFS from `getDependencyClosure`.
    expect(result.blockers.map((b) => b.id)).toEqual(["layer-a", "layer-b", "service-a", "service-b"]);
  });

  it("non-editor receives 403 forbidden", async () => {
    const client = new FixtureShareClient({
      items: FIXTURE_ITEMS,
      callerId: "user-2",
      ownerOf: FIXTURE_OWNER_OF,
    });
    const result = await client.patchAccess({
      id: "map-1",
      access: access("org"),
    });
    expect(result.kind).toBe("forbidden");
  });

  it("network failure surfaces as `error`", async () => {
    const client = baseClient({ failPatchFor: "map-1" });
    const result = await client.patchAccess({
      id: "map-1",
      access: access("org"),
    });
    expect(result.kind).toBe("error");
  });

  it("classifyPatchResult maps onto the empty-state vocabulary", () => {
    expect(classifyPatchResult({ kind: "forbidden" })).toEqual({
      kind: "unauthorized",
    });
    expect(classifyPatchResult({ kind: "closureBlocked", blockers: [] })).toEqual({ kind: "blocked", blockers: [] });
    expect(classifyPatchResult({ kind: "error", message: "boom" })).toEqual({ kind: "error", message: "boom" });
  });
});

describe("FixtureShareClient.listMyGroups", () => {
  it("returns groups when the surface is wired", async () => {
    const client = baseClient();
    const result = await client.listMyGroups();
    expect(result.kind).toBe("ok");
  });

  it("falls back to unsupported when no groups surface", async () => {
    const client = new FixtureShareClient({
      items: FIXTURE_ITEMS,
      callerId: "user-1",
      ownerOf: FIXTURE_OWNER_OF,
      groups: "unsupported",
    });
    const result = await client.listMyGroups();
    expect(result.kind).toBe("unsupported");
  });
});
