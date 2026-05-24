import { describe, expect, it } from "vitest";
import type { ViewerState } from "../../src/viewer/types.js";
import {
  applyHashToState,
  decodeViewerStateFromHash,
  encodeViewerStateToHash,
  mergeViewerState,
} from "../../src/viewer/url-state.js";

const baseState: ViewerState = {
  center: [-157.84, 21.3],
  zoom: 11.5,
  visibleLayerIds: ["districts", "field-stations"],
  selected: undefined,
};

describe("encodeViewerStateToHash", () => {
  it("encodes center, zoom, layer order, and the item id", () => {
    const hash = encodeViewerStateToHash(baseState, { itemId: "sample-published-layer" });
    expect(hash).toContain("item=sample-published-layer");
    expect(hash).toContain("center=-157.84%2C21.3");
    expect(hash).toContain("zoom=11.5");
    expect(hash).toContain("layers=districts%2Cfield-stations");
    expect(hash).not.toContain("selected=");
  });

  it("includes selection when present, layer.feature joined by a dot", () => {
    const hash = encodeViewerStateToHash(
      { ...baseState, selected: { layerId: "field-stations", featureId: "station-makiki" } },
      { itemId: "sample-published-layer" },
    );
    expect(hash).toContain("selected=field-stations.station-makiki");
  });

  it("rounds coordinates to a stable precision so copied URLs are deterministic", () => {
    const hash = encodeViewerStateToHash(
      { ...baseState, center: [-157.8412345678, 21.3098765432], zoom: 11.5123 },
      { itemId: "sample-published-layer" },
    );
    expect(hash).toContain("center=-157.84123%2C21.30988");
    expect(hash).toContain("zoom=11.51");
  });
});

describe("decodeViewerStateFromHash", () => {
  it("ignores out-of-range coordinates and zoom values", () => {
    const result = decodeViewerStateFromHash("#center=400,200&zoom=99");
    expect(result.center).toBeUndefined();
    expect(result.zoom).toBeUndefined();
  });

  it("preserves the empty layers list when the hash explicitly sends one", () => {
    const result = decodeViewerStateFromHash("#layers=");
    expect(result.visibleLayerIds).toEqual([]);
  });

  it("returns an empty record for an empty hash", () => {
    expect(decodeViewerStateFromHash("")).toEqual({});
    expect(decodeViewerStateFromHash("#")).toEqual({});
  });

  it("parses a feature id even when the value contains additional dots", () => {
    const result = decodeViewerStateFromHash("#selected=districts.east.123");
    expect(result.selected).toEqual({ layerId: "districts", featureId: "east.123" });
  });
});

describe("encode + decode round trip", () => {
  it("restores a copied state exactly via mergeViewerState", () => {
    const state: ViewerState = {
      center: [-122.4321, 37.8123],
      zoom: 12.25,
      visibleLayerIds: ["a", "b"],
      selected: { layerId: "a", featureId: "feat-1" },
    };
    const hash = encodeViewerStateToHash(state, { itemId: "demo" });
    const restored = mergeViewerState(
      { center: [0, 0], zoom: 0, visibleLayerIds: [] },
      decodeViewerStateFromHash(hash),
      ["a", "b"],
    );
    expect(restored.center).toEqual(state.center);
    expect(restored.zoom).toEqual(state.zoom);
    expect(restored.visibleLayerIds).toEqual(state.visibleLayerIds);
    expect(restored.selected).toEqual(state.selected);
  });
});

describe("mergeViewerState", () => {
  it("filters unknown layer ids when knownLayerIds is provided", () => {
    const merged = mergeViewerState(baseState, { visibleLayerIds: ["districts", "ghost-layer"] }, [
      "districts",
      "field-stations",
    ]);
    expect(merged.visibleLayerIds).toEqual(["districts"]);
  });

  it("clears the selection when its layer id is no longer known", () => {
    const merged = mergeViewerState(baseState, { selected: { layerId: "ghost-layer", featureId: "x" } }, [
      "districts",
      "field-stations",
    ]);
    expect(merged.selected).toBeUndefined();
  });

  it("preserves base state for keys absent from the override", () => {
    const merged = mergeViewerState(baseState, {}, ["districts", "field-stations"]);
    expect(merged).toEqual(baseState);
  });
});

describe("applyHashToState", () => {
  it("merges hash overrides into the base state", () => {
    const next = applyHashToState(baseState, "#zoom=14&center=-122,37", ["districts", "field-stations"]);
    expect(next.center).toEqual([-122, 37]);
    expect(next.zoom).toEqual(14);
    expect(next.visibleLayerIds).toEqual(baseState.visibleLayerIds);
  });
});
