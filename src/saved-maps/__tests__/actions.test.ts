import { describe, expect, it } from "vitest";
import { duplicateMap, renameMap, replaceMapContent, saveMap } from "../actions.js";
import { FixtureSavedMapClient } from "../client.js";
import type { MapHandle } from "../thumbnail.js";
import { deterministicIdGenerator, deterministicNow, makeViewerState } from "./fixtures.js";

function makeClient(actorId = "user-alice") {
  return new FixtureSavedMapClient({
    actorId,
    now: deterministicNow(),
    generateId: deterministicIdGenerator("map"),
  });
}

describe("saveMap end-to-end", () => {
  it("saves with thumbnail when capture succeeds", async () => {
    const client = makeClient();
    const blob = new Blob([new Uint8Array([42])], { type: "image/png" });
    const map: MapHandle = {
      getCanvas: () => ({ width: 800, height: 600 }) as unknown as HTMLCanvasElement,
    };
    const result = await saveMap(client, {
      title: "With thumb",
      state: makeViewerState(),
      map,
      thumbnailOptions: { encoder: async () => blob },
    });
    expect(result.thumbnailUploaded).toBe(true);
    expect(result.thumbnailWarning).toBeUndefined();
    expect(result.item.preview?.thumbnail).toContain(`/maps/${result.item.id}/thumb.png`);
  });

  it("save proceeds even when thumbnail capture fails — preview.thumbnail stays null", async () => {
    const client = makeClient();
    const map: MapHandle = {
      getCanvas: () => ({ width: 800, height: 600 }) as unknown as HTMLCanvasElement,
    };
    const result = await saveMap(client, {
      title: "Save anyway",
      state: makeViewerState(),
      map,
      thumbnailOptions: {
        logger: { warn: () => {} },
        encoder: async () => {
          throw new Error("encoder broken");
        },
      },
    });
    expect(result.thumbnailUploaded).toBe(false);
    expect(result.thumbnailWarning).toBe("encoder broken");
    expect(result.item.preview?.thumbnail).toBeNull();
  });

  it("saves without a map handle (server-side / no viewer)", async () => {
    const client = makeClient();
    const result = await saveMap(client, {
      title: "Headless",
      state: makeViewerState(),
    });
    expect(result.thumbnailUploaded).toBe(false);
    expect(result.item.preview?.thumbnail).toBeNull();
  });
});

describe("renameMap / replaceMapContent / duplicateMap composition", () => {
  it("rename updates title and bumps modified", async () => {
    const client = makeClient();
    const created = await client.create({ title: "Original", state: makeViewerState() });
    const renamed = await renameMap(client, { id: created.id, title: "New" });
    expect(renamed.title).toBe("New");
    expect(renamed.timestamps.modified).not.toBe(created.timestamps.modified);
  });

  it("replaceMapContent persists a new layer order without mutating other items", async () => {
    const client = makeClient();
    const a = await client.create({ title: "A", state: makeViewerState() });
    const b = await client.create({ title: "B", state: makeViewerState() });
    const newState = makeViewerState();
    newState.operationalLayers.reverse();
    const updated = await replaceMapContent(client, a.id, newState);
    expect(updated.target.operationalLayerCount).toBe(2);
    const docB = await client.getWebMap(b.id);
    expect(docB?.operationalLayers[0]?.id).toBe("ol-1");
  });

  it("duplicateMap is a thin wrapper over client.duplicate", async () => {
    const client = makeClient();
    const original = await client.create({ title: "X", state: makeViewerState() });
    const copy = await duplicateMap(client, { fromId: original.id, title: "Custom title" });
    expect(copy.title).toBe("Custom title");
    expect(copy.source.sourceId).toBe(original.id);
  });
});
