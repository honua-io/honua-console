import { beforeEach, describe, expect, it } from "vitest";
import { FixtureSavedMapClient, SavedMapForbiddenError, SavedMapNotFoundError } from "../client.js";
import { viewerStateToWebMapDoc, webMapDocToViewerState } from "../serializer.js";
import { ANNOTATION_WORKSPACE_VERSION } from "../types.js";
import {
  TEST_BASEMAP_SERVICE_ID,
  TEST_CENSUS_LAYER_ID,
  TEST_CENSUS_STYLE_ID,
  TEST_SCHOOLS_LAYER_ID,
  deterministicIdGenerator,
  deterministicNow,
  makeViewerState,
} from "./fixtures.js";

function makeClient(actorId = "user-alice") {
  return new FixtureSavedMapClient({
    actorId,
    actorDisplayName: actorId,
    now: deterministicNow(),
    generateId: deterministicIdGenerator("map"),
  });
}

describe("FixtureSavedMapClient.create (AC1: save from a published layer)", () => {
  it("produces a ContentItem of type 'map' with target.webmapJsonRef and recorded dependencies", async () => {
    const client = makeClient();
    const item = await client.create({
      title: "SF demographics",
      summary: "City-wide demographics overlay",
      tags: ["demo", "sf"],
      sharing: "private",
      state: makeViewerState(),
    });

    expect(item.type).toBe("map");
    expect(item.title).toBe("SF demographics");
    expect(item.summary).toBe("City-wide demographics overlay");
    expect(item.tags).toEqual(["demo", "sf"]);
    expect(item.access.sharing).toBe("private");
    expect(item.target.webmapJsonRef).toBe(`/api/v1/portal/maps/${item.id}/webmap`);
    expect(item.target.operationalLayerCount).toBe(2);
    expect(item.endpoints.self.accessURL).toBe(`https://console.honua.example/maps/${item.id}`);
    expect(item.source.kind).toBe("manual");
    expect(item.source.history.at(0)).toMatchObject({
      kind: "manual",
      actor: "user-alice",
    });
    expect(item.dependencies).toEqual(
      expect.arrayContaining([
        { id: TEST_CENSUS_LAYER_ID, type: "layer", role: "operationalLayer" },
        { id: TEST_SCHOOLS_LAYER_ID, type: "layer", role: "operationalLayer" },
        { id: TEST_CENSUS_STYLE_ID, type: "document", role: "style" },
        { id: TEST_BASEMAP_SERVICE_ID, type: "service", role: "baseMap" },
      ]),
    );
  });

  it("rejects when title is empty", async () => {
    const client = makeClient();
    await expect(client.create({ title: "", state: makeViewerState() })).rejects.toThrow(/title is required/);
  });
});

describe("FixtureSavedMapClient metadata validation (content-item/v1 invariants)", () => {
  it("create rejects a title longer than 280 characters and accepts a 280-char title", async () => {
    const client = makeClient();
    await expect(client.create({ title: "x".repeat(281), state: makeViewerState() })).rejects.toThrow(
      /title exceeds 280 characters/,
    );
    const item = await client.create({ title: "x".repeat(280), state: makeViewerState() });
    expect(item.title.length).toBe(280);
  });

  it("create rejects a summary longer than 280 characters", async () => {
    const client = makeClient();
    await expect(
      client.create({
        title: "ok",
        summary: "y".repeat(281),
        state: makeViewerState(),
      }),
    ).rejects.toThrow(/summary exceeds 280 characters/);
  });

  it("create rejects a tag longer than 64 characters", async () => {
    const client = makeClient();
    await expect(
      client.create({
        title: "ok",
        tags: ["valid", "z".repeat(65)],
        state: makeViewerState(),
      }),
    ).rejects.toThrow(/tag exceeds 64 characters/);
  });

  it("create normalizes tags: drops empties and de-duplicates while preserving order", async () => {
    const client = makeClient();
    const item = await client.create({
      title: "tags",
      tags: ["demo", "", "sf", "demo", "sf"],
      state: makeViewerState(),
    });
    expect(item.tags).toEqual(["demo", "sf"]);
  });

  it("create coerces an empty/whitespace summary to a contract-safe default", async () => {
    const client = makeClient();
    const item = await client.create({
      title: "blank summary",
      summary: "   ",
      state: makeViewerState(),
    });
    expect(item.summary).toBe("No summary provided.");
  });

  it("create rejects schema-invalid generated ids and dependency ids before storing a record", async () => {
    const badIdClient = new FixtureSavedMapClient({
      actorId: "user-alice",
      now: deterministicNow(),
      generateId: () => "map-001",
    });
    await expect(badIdClient.create({ title: "Bad id", state: makeViewerState() })).rejects.toThrow(
      /generated saved-map id must be a 26-character Crockford base32 ULID/,
    );
    expect((await badIdClient.list()).items).toEqual([]);

    const badDependencyState = makeViewerState();
    badDependencyState.operationalLayers[0]!.sourceRef.itemId = "svc-census";
    await expect(makeClient().create({ title: "Bad dependency", state: badDependencyState })).rejects.toThrow(
      /operational layer dependency id must be a 26-character Crockford base32 ULID/,
    );
  });

  it("patchMetadata rejects an empty rename", async () => {
    const client = makeClient();
    const item = await client.create({ title: "ok", state: makeViewerState() });
    await expect(client.patchMetadata({ id: item.id, title: "" })).rejects.toThrow(/title is required/);
  });

  it("patchMetadata rejects a title longer than 280 characters", async () => {
    const client = makeClient();
    const item = await client.create({ title: "ok", state: makeViewerState() });
    await expect(client.patchMetadata({ id: item.id, title: "x".repeat(281) })).rejects.toThrow(
      /title exceeds 280 characters/,
    );
  });

  it("patchMetadata rejects an oversized summary and an oversized tag", async () => {
    const client = makeClient();
    const item = await client.create({ title: "ok", state: makeViewerState() });
    await expect(client.patchMetadata({ id: item.id, summary: "y".repeat(281) })).rejects.toThrow(
      /summary exceeds 280 characters/,
    );
    await expect(client.patchMetadata({ id: item.id, tags: ["z".repeat(65)] })).rejects.toThrow(
      /tag exceeds 64 characters/,
    );
  });

  it("patchMetadata normalizes incoming tags consistent with create", async () => {
    const client = makeClient();
    const item = await client.create({ title: "ok", state: makeViewerState() });
    const next = await client.patchMetadata({
      id: item.id,
      tags: ["a", "", "b", "a"],
    });
    expect(next.tags).toEqual(["a", "b"]);
  });

  it("duplicate validates an explicit title override", async () => {
    const client = makeClient();
    const original = await client.create({ title: "ok", state: makeViewerState() });
    await expect(client.duplicate({ fromId: original.id, title: "x".repeat(281) })).rejects.toThrow(
      /title exceeds 280 characters/,
    );
    await expect(client.duplicate({ fromId: original.id, title: "" })).rejects.toThrow(/title is required/);
  });

  it("duplicate rejects a schema-invalid generated id without changing the source", async () => {
    const ids = deterministicIdGenerator("map");
    const client = new FixtureSavedMapClient({
      actorId: "user-alice",
      now: deterministicNow(),
      generateId: () => ids(),
    });
    const original = await client.create({ title: "ok", state: makeViewerState() });
    (client as unknown as { ctx: { generateId: () => string } }).ctx.generateId = () => "map-002";

    await expect(client.duplicate({ fromId: original.id })).rejects.toThrow(
      /generated saved-map id must be a 26-character Crockford base32 ULID/,
    );
    expect((await client.list()).items.map((item) => item.id)).toEqual([original.id]);
  });

  it("duplicate truncates the auto-generated title so it stays within 280 characters", async () => {
    const client = makeClient();
    const longTitle = "x".repeat(280);
    const original = await client.create({ title: longTitle, state: makeViewerState() });
    const copy = await client.duplicate({ fromId: original.id });
    expect(copy.title.length).toBeLessThanOrEqual(280);
    expect(copy.title.endsWith(" (copy)")).toBe(true);
  });
});

describe("FixtureSavedMapClient.get + getWebMap (AC2: reopen restores state)", () => {
  it("reopening the saved map restores viewer state byte-equal", async () => {
    const client = makeClient();
    const original = makeViewerState();
    const created = await client.create({
      title: "Reopen me",
      state: original,
    });

    const item = await client.get(created.id);
    const doc = await client.getWebMap(created.id);
    expect(item).not.toBeNull();
    expect(doc).not.toBeNull();
    if (!item || !doc) return;

    expect(item.target.webmapJsonRef).toContain(item.id);
    const restored = webMapDocToViewerState(doc);
    expect(restored).toEqual(original);
  });

  it("missing item returns null (caller surfaces 'missing')", async () => {
    const client = makeClient();
    expect(await client.get("nonexistent")).toBeNull();
    expect(await client.getWebMap("nonexistent")).toBeNull();
  });

  it("soft-deleted items return null on read", async () => {
    const client = makeClient();
    const item = await client.create({ title: "Doomed", state: makeViewerState() });
    await client.delete(item.id);
    expect(await client.get(item.id)).toBeNull();
    expect(await client.getWebMap(item.id)).toBeNull();
  });
});

describe("FixtureSavedMapClient.duplicate (AC3: duplicate is a true copy)", () => {
  let client: FixtureSavedMapClient;

  beforeEach(() => {
    client = makeClient();
  });

  it("creates a new item with a new id, fresh timestamps, and source.sourceId pointing back", async () => {
    const original = await client.create({ title: "Original", state: makeViewerState() });
    const copy = await client.duplicate({ fromId: original.id });
    expect(copy.id).not.toBe(original.id);
    expect(copy.title).toBe("Original (copy)");
    expect(copy.source.kind).toBe("manual");
    expect(copy.source.sourceId).toBe(original.id);
    expect(copy.access.sharing).toBe("private");
    expect(copy.endpoints.self.accessURL).toBe(`https://console.honua.example/maps/${copy.id}`);
    expect(copy.target.webmapJsonRef).toBe(`/api/v1/portal/maps/${copy.id}/webmap`);
    expect(copy.timestamps.created).not.toBe(original.timestamps.created);
  });

  it("editing the duplicate does not mutate the original (byte-equal check)", async () => {
    const original = await client.create({ title: "Orig", state: makeViewerState() });
    const originalSnapshot = JSON.stringify(await client.get(original.id));
    const originalDocSnapshot = JSON.stringify(await client.getWebMap(original.id));

    const copy = await client.duplicate({ fromId: original.id });
    // Edit the duplicate's metadata + content.
    await client.patchMetadata({
      id: copy.id,
      title: "Heavily edited copy",
      summary: "Diverged",
    });
    const editedDoc = await client.getWebMap(copy.id);
    if (!editedDoc) throw new Error("doc missing");
    editedDoc.operationalLayers = editedDoc.operationalLayers.slice(0, 1);
    editedDoc.operationalLayers[0]!.opacity = 0.1;
    await client.replaceContent(copy.id, editedDoc);

    expect(JSON.stringify(await client.get(original.id))).toBe(originalSnapshot);
    expect(JSON.stringify(await client.getWebMap(original.id))).toBe(originalDocSnapshot);
  });

  it("regenerates operational-layer ids on the duplicate so they don't collide with the source", async () => {
    const original = await client.create({ title: "Orig", state: makeViewerState() });
    const copy = await client.duplicate({ fromId: original.id });
    const originalDoc = await client.getWebMap(original.id);
    const copyDoc = await client.getWebMap(copy.id);
    if (!originalDoc || !copyDoc) throw new Error("docs missing");
    const originalIds = originalDoc.operationalLayers.map((l) => l.id);
    const copyIds = copyDoc.operationalLayers.map((l) => l.id);
    expect(copyIds).toHaveLength(originalIds.length);
    for (const id of copyIds) {
      expect(originalIds).not.toContain(id);
    }
  });

  it("throws SavedMapNotFoundError when source does not exist", async () => {
    await expect(client.duplicate({ fromId: "missing" })).rejects.toBeInstanceOf(SavedMapNotFoundError);
  });

  it("re-keys preview.thumbnail to the duplicate id when the blob is copied", async () => {
    const original = await client.create({
      title: "with thumb",
      state: makeViewerState(),
    });
    const blob = new Blob([new Uint8Array([1, 2, 3])], { type: "image/png" });
    await client.uploadThumbnail(original.id, blob);

    const copy = await client.duplicate({ fromId: original.id });
    expect(copy.preview?.thumbnail).not.toBeNull();
    expect(copy.preview?.thumbnail).toMatch(
      new RegExp(`^https://api\\.honua\\.example/api/v1/portal/maps/${copy.id}/thumb\\.png\\?v=`),
    );
    expect(copy.preview?.thumbnail).not.toContain(`/${original.id}/`);

    // After deleting the source the duplicate's preview URL still points at
    // the duplicate's own id, so its catalog card cannot be broken by source
    // deletion or restriction.
    await client.delete(original.id);
    const reloaded = await client.get(copy.id);
    expect(reloaded?.preview?.thumbnail).toBe(copy.preview?.thumbnail);
  });

  it("leaves preview.thumbnail null when the source has no stored thumbnail", async () => {
    const original = await client.create({
      title: "no thumb",
      state: makeViewerState(),
    });
    const copy = await client.duplicate({ fromId: original.id });
    expect(copy.preview?.thumbnail).toBeNull();
  });
});

describe("FixtureSavedMapClient ownership enforcement", () => {
  it("blocks rename and delete by a non-owner against shared storage", async () => {
    const owner = new FixtureSavedMapClient({
      actorId: "user-alice",
      now: deterministicNow(),
      generateId: deterministicIdGenerator("alice"),
    });
    const item = await owner.create({ title: "Mine", state: makeViewerState() });

    // Simulate two actors against the same server-side storage by sharing
    // the underlying records map across instances. Production enforcement
    // lives server-side; this only exercises the in-fixture seam.
    const ownerRecords = (owner as unknown as { records: Map<string, unknown> }).records;
    const intruder = new FixtureSavedMapClient({
      actorId: "user-mallory",
      now: deterministicNow(),
      generateId: deterministicIdGenerator("mallory"),
    });
    (intruder as unknown as { records: Map<string, unknown> }).records = ownerRecords;

    await expect(intruder.delete(item.id)).rejects.toBeInstanceOf(SavedMapForbiddenError);
    await expect(intruder.patchMetadata({ id: item.id, title: "hijacked" })).rejects.toBeInstanceOf(
      SavedMapForbiddenError,
    );
  });
});

describe("FixtureSavedMapClient read permission enforcement (shared storage)", () => {
  // Build two actors against the same in-memory storage so we can exercise
  // the read paths the way two real callers would hit a shared server.
  function makeSharedActors(sharing: "private" | "org" | "public-link" | "public", intruderActorId = "user-mallory") {
    const owner = new FixtureSavedMapClient({
      actorId: "user-alice",
      now: deterministicNow(),
      generateId: deterministicIdGenerator("alice"),
    });
    const intruder = new FixtureSavedMapClient({
      actorId: intruderActorId,
      now: deterministicNow(),
      generateId: deterministicIdGenerator("mallory"),
    });
    (intruder as unknown as { records: Map<string, unknown> }).records = (
      owner as unknown as { records: Map<string, unknown> }
    ).records;
    (intruder as unknown as { thumbnails: Map<string, Blob> }).thumbnails = (
      owner as unknown as { thumbnails: Map<string, Blob> }
    ).thumbnails;
    return {
      owner,
      intruder,
      create: () => owner.create({ title: "Shared", sharing, state: makeViewerState() }),
    };
  }

  it("private map: owner reads, non-owner is forbidden on get/getWebMap and excluded from list", async () => {
    const { owner, intruder, create } = makeSharedActors("private");
    const item = await create();
    expect(await owner.get(item.id)).not.toBeNull();
    expect(await owner.getWebMap(item.id)).not.toBeNull();
    await expect(intruder.get(item.id)).rejects.toBeInstanceOf(SavedMapForbiddenError);
    await expect(intruder.getWebMap(item.id)).rejects.toBeInstanceOf(SavedMapForbiddenError);
    expect((await intruder.list()).items).toEqual([]);
    expect((await owner.list()).items.map((i) => i.id)).toEqual([item.id]);
  });

  it("org map: any authenticated actor can read; anonymous cannot", async () => {
    const { intruder, create } = makeSharedActors("org");
    const item = await create();
    expect(await intruder.get(item.id)).not.toBeNull();
    expect(await intruder.getWebMap(item.id)).not.toBeNull();
    expect((await intruder.list()).items.map((i) => i.id)).toEqual([item.id]);

    const anonReader = new FixtureSavedMapClient({
      actorId: "",
      now: deterministicNow(),
      generateId: deterministicIdGenerator("anon"),
    });
    (anonReader as unknown as { records: Map<string, unknown> }).records = (
      intruder as unknown as { records: Map<string, unknown> }
    ).records;
    await expect(anonReader.get(item.id)).rejects.toBeInstanceOf(SavedMapForbiddenError);
  });

  it("public-link / public maps: any actor (including anonymous) can read", async () => {
    for (const sharing of ["public-link", "public"] as const) {
      const { intruder, create } = makeSharedActors(sharing);
      const item = await create();
      const anonReader = new FixtureSavedMapClient({
        actorId: "",
        now: deterministicNow(),
        generateId: deterministicIdGenerator("anon"),
      });
      (anonReader as unknown as { records: Map<string, unknown> }).records = (
        intruder as unknown as { records: Map<string, unknown> }
      ).records;
      expect(await intruder.get(item.id)).not.toBeNull();
      expect(await anonReader.get(item.id)).not.toBeNull();
      expect(await anonReader.getWebMap(item.id)).not.toBeNull();
    }
  });

  it("duplicate: non-owner is forbidden on a private source but allowed on a shared source", async () => {
    const privateShared = makeSharedActors("private");
    const privateItem = await privateShared.create();
    await expect(privateShared.intruder.duplicate({ fromId: privateItem.id })).rejects.toBeInstanceOf(
      SavedMapForbiddenError,
    );

    const orgShared = makeSharedActors("org");
    const orgItem = await orgShared.create();
    const copy = await orgShared.intruder.duplicate({ fromId: orgItem.id });
    expect(copy.owner.id).toBe("user-mallory");
    expect(copy.access.sharing).toBe("private");
    expect(copy.source.kind).toBe("manual");
    expect(copy.source.sourceId).toBe(orgItem.id);
  });
});

describe("FixtureSavedMapClient extent CRS normalization", () => {
  function makeExtentClient() {
    return new FixtureSavedMapClient({
      actorId: "user-alice",
      now: deterministicNow(),
      generateId: deterministicIdGenerator("map"),
    });
  }

  it("WGS84 viewpoint extent passes through unchanged on the catalog item", async () => {
    const client = makeExtentClient();
    const state = makeViewerState();
    const item = await client.create({ title: "WGS84", state });
    expect(item.extent).toEqual({
      bbox: [state.extent.xmin, state.extent.ymin, state.extent.xmax, state.extent.ymax],
      crs: "EPSG:4326",
    });
  });

  it("WGS84 antimeridian-crossing extent is preserved on the catalog item", async () => {
    const client = makeExtentClient();
    const state = makeViewerState({
      extent: {
        xmin: 170,
        ymin: -10,
        xmax: -170,
        ymax: 10,
        rotation: 0,
      },
    });

    const item = await client.create({ title: "Antimeridian", state });

    expect(item.extent).toEqual({
      bbox: [170, -10, -170, 10],
      crs: "EPSG:4326",
    });
  });

  it("Web Mercator (3857) viewpoint extent is reprojected to WGS84 on the catalog item", async () => {
    const client = makeExtentClient();
    const item = await client.create({ title: "WebMerc", state: makeViewerState() });
    // Inject a 3857 viewpoint extent on the underlying webmap doc and
    // re-run the extent derivation through replaceContent.
    const doc = await client.getWebMap(item.id);
    if (!doc) throw new Error("doc missing");
    doc.initialState.viewpoint.extent = {
      // San Francisco bbox in EPSG:3857 (approx).
      xmin: -13649895,
      ymin: 4517800,
      xmax: -13615734,
      ymax: 4554157,
      spatialReference: { wkid: 102100, latestWkid: 3857 },
    };
    const updated = await client.replaceContent(item.id, doc);
    expect(updated.extent).not.toBeNull();
    if (!updated.extent) return;
    expect(updated.extent.crs).toBe("EPSG:4326");
    const [west, south, east, north] = updated.extent.bbox;
    // Inverse Web Mercator of the input bbox above. Tolerance is loose
    // because the input coordinates are picked to bracket San Francisco,
    // not to be exact survey points; the assertion is that the values are
    // *lon/lat degrees*, not metres.
    expect(west).toBeCloseTo(-122.61, 1);
    expect(east).toBeCloseTo(-122.3, 1);
    expect(south).toBeCloseTo(37.56, 1);
    expect(north).toBeCloseTo(37.83, 1);
    // Sanity: nothing should escape lon/lat bounds.
    expect(west).toBeGreaterThan(-180);
    expect(east).toBeLessThan(180);
    expect(south).toBeGreaterThan(-90);
    expect(north).toBeLessThan(90);
  });

  it("Unknown CRS produces a null extent rather than mislabelling coordinates as WGS84", async () => {
    const client = makeExtentClient();
    const item = await client.create({ title: "Unknown CRS", state: makeViewerState() });
    const doc = await client.getWebMap(item.id);
    if (!doc) throw new Error("doc missing");
    doc.initialState.viewpoint.extent = {
      xmin: 100,
      ymin: 200,
      xmax: 300,
      ymax: 400,
      spatialReference: { wkid: 27700 }, // British National Grid; we don't normalize this.
    };
    const updated = await client.replaceContent(item.id, doc);
    expect(updated.extent).toBeNull();
  });
});

describe("FixtureSavedMapClient.uploadThumbnail", () => {
  it("attaches a thumbnail URL keyed off timestamps.modified for cache busting", async () => {
    const client = makeClient();
    const item = await client.create({ title: "Thumb me", state: makeViewerState() });
    expect(item.preview?.thumbnail).toBeNull();
    const blob = new Blob([new Uint8Array([1, 2, 3])], { type: "image/png" });
    const url = await client.uploadThumbnail(item.id, blob);
    expect(url).toMatch(new RegExp(`^https://api\\.honua\\.example/api/v1/portal/maps/${item.id}/thumb\\.png\\?v=`));
    const reloaded = await client.get(item.id);
    expect(reloaded?.preview?.thumbnail).toBe(url);
  });
});

describe("FixtureSavedMapClient.list", () => {
  it("excludes soft-deleted items and sorts by modified desc", async () => {
    const client = makeClient();
    const a = await client.create({ title: "A", state: makeViewerState() });
    const b = await client.create({ title: "B", state: makeViewerState() });
    await client.delete(a.id);
    const result = await client.list();
    expect(result.items.map((i) => i.id)).toEqual([b.id]);
  });
});

describe("WebMap doc shape parity", () => {
  it("the serializer's output is the same shape that getWebMap returns after save", async () => {
    const client = makeClient();
    const state = makeViewerState();
    const expected = viewerStateToWebMapDoc(state);
    const item = await client.create({ title: "Parity", state });
    const stored = await client.getWebMap(item.id);
    expect(stored).toEqual(expected);
  });

  it("preserves annotation workspace state through create, replace, and duplicate", async () => {
    const client = makeClient();
    const annotations = {
      version: ANNOTATION_WORKSPACE_VERSION,
      visibility: { defaultAudience: "map" as const, publicComments: false as const },
      annotationSets: [{ id: "set-1", title: "Review notes" }],
      commentThreads: [{ id: "thread-1", status: "open" }],
    };
    const item = await client.create({
      title: "Annotated",
      state: makeViewerState({ annotations }),
    });

    const stored = await client.getWebMap(item.id);
    expect(stored?.annotations).toEqual(annotations);

    if (!stored) throw new Error("doc missing");
    const replaced = {
      ...stored,
      annotations: {
        ...annotations,
        annotationSets: [{ id: "set-2", title: "Field notes" }],
      },
    };
    await client.replaceContent(item.id, replaced);
    expect((await client.getWebMap(item.id))?.annotations?.annotationSets).toEqual([
      { id: "set-2", title: "Field notes" },
    ]);

    const copy = await client.duplicate({ fromId: item.id });
    expect((await client.getWebMap(copy.id))?.annotations).toEqual(replaced.annotations);
  });
});
