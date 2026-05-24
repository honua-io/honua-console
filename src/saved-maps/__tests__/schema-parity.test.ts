import type { ValidateFunction } from "ajv";
/**
 * Schema parity tests.
 *
 * These tests guard the cross-repo contract:
 *   - the WebMapDoc the serializer emits must validate against
 *     `schemas/webmap-doc-v1.json`.
 *   - the SavedMapItem the FixtureSavedMapClient emits must validate against
 *     `schemas/content-item-v1.json`.
 */
import addFormats from "ajv-formats";
import Ajv2020 from "ajv/dist/2020.js";
import { describe, expect, it } from "vitest";
import contentItemV1 from "../../../schemas/content-item-v1.json";
import webMapDocV1 from "../../../schemas/webmap-doc-v1.json";
import { FixtureSavedMapClient } from "../client.js";
import {
  STYLE_EDITOR_DEMO_CONTENT_ITEM_ID,
  STYLE_EDITOR_DEMO_MAP_ID,
  loadFixtureSavedMapForViewer,
} from "../fixture-renderer.js";
import { viewerStateToWebMapDoc } from "../serializer.js";
import { ANNOTATION_WORKSPACE_VERSION } from "../types.js";
import { deterministicIdGenerator, deterministicNow, makeViewerState } from "./fixtures.js";

function buildAjv(): Ajv2020 {
  const ajv = new Ajv2020({ allErrors: true, strict: false });
  addFormats(ajv);
  return ajv;
}

function expectValid(validate: ValidateFunction, value: unknown): void {
  const ok = validate(value);
  expect(validate.errors ?? []).toEqual([]);
  expect(ok).toBe(true);
}

function expectInvalid(validate: ValidateFunction, value: unknown): string {
  const ok = validate(value);
  expect(ok).toBe(false);
  const errors = JSON.stringify(validate.errors ?? []);
  expect(errors).not.toBe("[]");
  return errors;
}

describe("schema parity: WebMapDoc", () => {
  const validateDoc = buildAjv().compile(webMapDocV1);

  it("the serializer's output validates against webmap-doc-v1", () => {
    expectValid(validateDoc, viewerStateToWebMapDoc(makeViewerState()));
  });

  it("accepts recorded style ownership for portal overrides", () => {
    const state = makeViewerState();
    state.operationalLayers[0]!.styleRef = {
      itemId: "style-census-default",
      inline: { version: 8, sources: {}, layers: [] },
      origin: "portal-override",
    };
    const doc = viewerStateToWebMapDoc(state);
    expectValid(validateDoc, doc);
    expect(doc.operationalLayers[0]?.styleRef?.origin).toBe("portal-override");
  });

  it("accepts optional annotation workspace state and still rejects unknown top-level fields", () => {
    const doc = viewerStateToWebMapDoc(
      makeViewerState({
        annotations: {
          version: ANNOTATION_WORKSPACE_VERSION,
          visibility: { defaultAudience: "map", publicComments: true },
          annotationSets: [],
          commentThreads: [
            {
              id: "thread-public",
              kind: "comment-thread",
              title: "Needs review",
              status: "open",
              moderation: { state: "pending", submittedBy: "guest" },
              comments: [],
            },
          ],
        },
      }),
    );
    expectValid(validateDoc, doc);

    const broken = { ...doc, madeUpWorkspaceState: {} };
    expect(expectInvalid(validateDoc, broken)).toMatch(/madeUpWorkspaceState/);
  });

  it("rejects a doc with a different version", () => {
    const doc = viewerStateToWebMapDoc(makeViewerState()) as unknown as Record<string, unknown>;
    doc.version = "honua-webmap/v2";
    expect(expectInvalid(validateDoc, doc)).toMatch(/const/);
  });
});

describe("schema parity: ContentItem", () => {
  const validateItem = buildAjv().compile(contentItemV1);

  it("a saved map produced by FixtureSavedMapClient validates against content-item-v1", async () => {
    const client = new FixtureSavedMapClient({
      actorId: "u",
      now: deterministicNow(),
      generateId: deterministicIdGenerator("map"),
    });
    const item = await client.create({ title: "Schema parity", state: makeViewerState() });
    expectValid(validateItem, item);
  });

  it("a duplicated saved map also validates with sourceId pointing at the original", async () => {
    const client = new FixtureSavedMapClient({
      actorId: "u",
      now: deterministicNow(),
      generateId: deterministicIdGenerator("map"),
    });
    const original = await client.create({ title: "Orig", state: makeViewerState() });
    const copy = await client.duplicate({ fromId: original.id });
    expectValid(validateItem, copy);
  });

  it("the style-editor demo saved-map fixture validates against content-item-v1", () => {
    const loaded = loadFixtureSavedMapForViewer(STYLE_EDITOR_DEMO_MAP_ID, null);
    expect(loaded.status).toBe("ok");
    if (loaded.status !== "ok") throw new Error("style-editor saved-map fixture failed to load");

    expect(loaded.item.id).toBe(STYLE_EDITOR_DEMO_CONTENT_ITEM_ID);
    expectValid(validateItem, loaded.item);
  });

  it("rejects schema-invalid ids and relative self URLs", async () => {
    const client = new FixtureSavedMapClient({
      actorId: "u",
      now: deterministicNow(),
      generateId: deterministicIdGenerator("map"),
    });
    const item = await client.create({ title: "Contract guard", state: makeViewerState() });

    expect(expectInvalid(validateItem, { ...item, id: "map-001" })).toMatch(/pattern/);
    expect(
      expectInvalid(validateItem, {
        ...item,
        endpoints: {
          ...item.endpoints,
          self: { ...item.endpoints.self, accessURL: `/maps/${item.id}` },
        },
      }),
    ).toMatch(/accessURL/);
  });

  it("rejects type='map' items whose target lacks the map contract (webmapJsonRef + operationalLayerCount)", async () => {
    const client = new FixtureSavedMapClient({
      actorId: "u",
      now: deterministicNow(),
      generateId: deterministicIdGenerator("map"),
    });
    const item = await client.create({ title: "Empty target", state: makeViewerState() });
    const broken = {
      ...item,
      target: { type: "map" } as Record<string, unknown>,
    };
    const errors = expectInvalid(validateItem, broken);
    expect(errors).toMatch(/webmapJsonRef/);
    expect(errors).toMatch(/operationalLayerCount/);
  });
});
