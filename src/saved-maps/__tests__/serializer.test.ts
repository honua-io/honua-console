import { describe, expect, it } from "vitest";
import { cloneWebMapDoc, viewerStateToWebMapDoc, webMapDocToViewerState } from "../serializer.js";
import { ANNOTATION_WORKSPACE_VERSION, WEBMAP_DOC_VERSION } from "../types.js";
import { TEST_BASEMAP_SERVICE_ID, TEST_CENSUS_LAYER_ID, TEST_CENSUS_STYLE_ID, makeViewerState } from "./fixtures.js";

describe("viewerStateToWebMapDoc", () => {
  it("captures layer order, visibility, opacity, style refs, popups, basemap, and extent", () => {
    const state = makeViewerState();
    const doc = viewerStateToWebMapDoc(state);

    expect(doc.version).toBe(WEBMAP_DOC_VERSION);
    expect(doc.operationalLayers).toHaveLength(2);
    expect(doc.operationalLayers[0]?.id).toBe("ol-1");
    expect(doc.operationalLayers[0]?.visibility).toBe(true);
    expect(doc.operationalLayers[0]?.opacity).toBe(0.9);
    expect(doc.operationalLayers[0]?.popupInfo).toEqual({ title: "{NAME}" });
    expect(doc.operationalLayers[0]?.styleRef).toEqual({
      itemId: TEST_CENSUS_STYLE_ID,
    });
    expect(doc.operationalLayers[1]?.visibility).toBe(false);
    expect(doc.baseMap.baseMapLayers[0]?.sourceRef).toEqual({
      itemId: TEST_BASEMAP_SERVICE_ID,
    });
    expect(doc.initialState.viewpoint.extent).toEqual({
      xmin: -122.6,
      ymin: 37.6,
      xmax: -122.3,
      ymax: 37.9,
    });
  });

  it("round-trips: viewer → doc → viewer is structurally equal", () => {
    const state = makeViewerState();
    const doc = viewerStateToWebMapDoc(state);
    const restored = webMapDocToViewerState(doc);
    expect(restored).toEqual(state);
  });

  it("round-trips optional annotation workspace state without requiring UI support", () => {
    const state = makeViewerState({
      annotations: {
        version: ANNOTATION_WORKSPACE_VERSION,
        visibility: { defaultAudience: "map", publicComments: false },
        annotationSets: [{ id: "set-1", title: "Review notes", features: [] }],
        commentThreads: [{ id: "thread-1", status: "open", comments: [] }],
      },
    });
    const doc = viewerStateToWebMapDoc(state);
    const restored = webMapDocToViewerState(doc);

    expect(doc.annotations).toEqual(state.annotations);
    expect(restored.annotations).toEqual(state.annotations);
    expect(doc.annotations).not.toBe(state.annotations);
  });

  it("keeps annotation workspace state optional for older saved maps", () => {
    const doc = viewerStateToWebMapDoc(makeViewerState());
    expect(doc.annotations).toBeUndefined();
    expect(webMapDocToViewerState(doc).annotations).toBeUndefined();
  });

  it("does not retain references between input and output (immutability)", () => {
    const state = makeViewerState();
    const doc = viewerStateToWebMapDoc(state);
    const layer = doc.operationalLayers[0];
    if (!layer) throw new Error("layer missing");
    layer.opacity = 0.1;
    layer.sourceRef.itemId = "tampered";
    expect(state.operationalLayers[0]?.opacity).toBe(0.9);
    expect(state.operationalLayers[0]?.sourceRef.itemId).toBe(TEST_CENSUS_LAYER_ID);
  });
});

describe("cloneWebMapDoc", () => {
  it("regenerates layer ids while preserving order, refs, and metadata", () => {
    const state = makeViewerState();
    const original = viewerStateToWebMapDoc(state);
    let i = 0;
    const cloned = cloneWebMapDoc(original, {
      layerIdFactory: () => `clone-${++i}`,
    });
    expect(cloned.operationalLayers.map((l) => l.id)).toEqual(["clone-1", "clone-2"]);
    expect(cloned.operationalLayers[0]?.sourceRef.itemId).toBe(TEST_CENSUS_LAYER_ID);
    expect(cloned.operationalLayers[0]?.opacity).toBe(0.9);
    expect(cloned.baseMap).toEqual(original.baseMap);
    expect(cloned.initialState).toEqual(original.initialState);
  });

  it("does not mutate the source doc", () => {
    const original = viewerStateToWebMapDoc(makeViewerState());
    const before = JSON.stringify(original);
    cloneWebMapDoc(original, { layerIdFactory: () => "x" });
    expect(JSON.stringify(original)).toBe(before);
  });

  it("preserves annotation workspace state when cloning a saved map", () => {
    const original = viewerStateToWebMapDoc(
      makeViewerState({
        annotations: {
          version: ANNOTATION_WORKSPACE_VERSION,
          visibility: { defaultAudience: "map", publicComments: false },
          annotationSets: [{ id: "set-1", features: [] }],
          commentThreads: [],
        },
      }),
    );
    const cloned = cloneWebMapDoc(original, { layerIdFactory: () => "clone-layer" });
    expect(cloned.annotations).toEqual(original.annotations);
    expect(cloned.annotations).not.toBe(original.annotations);
  });
});
