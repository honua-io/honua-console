import { describe, expect, it } from "vitest";
import { buildSamplePortalItem } from "../../src/catalog/sample-portal-item.js";
import {
  buildInitialState,
  deriveLayerOrder,
  isLayerVisible,
  reorderLayer,
  selectFeature,
  setLayerVisibility,
  setView,
} from "../../src/viewer/viewer-state.js";

describe("buildInitialState", () => {
  it("seeds visible layers from default visibility flags and the initial view", () => {
    const item = buildSamplePortalItem();
    const state = buildInitialState(item);
    expect(state.visibleLayerIds).toEqual(["districts", "field-stations"]);
    expect(state.center).toEqual(item.initialView.center);
    expect(state.zoom).toEqual(item.initialView.zoom);
    expect(state.selected).toBeUndefined();
  });
});

describe("deriveLayerOrder", () => {
  it("rehydrates copied URL layer order before appending hidden layers", () => {
    expect(deriveLayerOrder(["districts", "field-stations", "roads"], ["field-stations", "districts"])).toEqual([
      "field-stations",
      "districts",
      "roads",
    ]);
  });

  it("falls back to default order when URL state does not carry an order", () => {
    expect(deriveLayerOrder(["districts", "field-stations"])).toEqual(["districts", "field-stations"]);
    expect(deriveLayerOrder(["districts", "field-stations"], [])).toEqual(["districts", "field-stations"]);
  });

  it("ignores unknown and duplicate layer ids from URL state", () => {
    expect(deriveLayerOrder(["districts", "field-stations"], ["ghost", "field-stations", "field-stations"])).toEqual([
      "field-stations",
      "districts",
    ]);
  });
});

describe("setView", () => {
  it("returns the same reference when no value changes", () => {
    const initial = buildInitialState(buildSamplePortalItem());
    const next = setView(initial, initial.center, initial.zoom);
    expect(next).toBe(initial);
  });
  it("returns an updated reference when center or zoom changes", () => {
    const initial = buildInitialState(buildSamplePortalItem());
    const next = setView(initial, [0, 0], 5);
    expect(next).not.toBe(initial);
    expect(next.center).toEqual([0, 0]);
    expect(next.zoom).toBe(5);
  });
});

describe("setLayerVisibility", () => {
  it("toggles a layer off and clears its selection when applicable", () => {
    const item = buildSamplePortalItem();
    const layerOrder = item.layers.map((layer) => layer.id);
    const withSelection = selectFeature(buildInitialState(item), {
      layerId: "field-stations",
      featureId: "station-makiki",
    });

    const next = setLayerVisibility(withSelection, layerOrder, "field-stations", false);
    expect(isLayerVisible(next, "field-stations")).toBe(false);
    expect(next.selected).toBeUndefined();
  });
  it("preserves render order even when toggling layers in arbitrary order", () => {
    const item = buildSamplePortalItem();
    const layerOrder = item.layers.map((layer) => layer.id);
    let state = buildInitialState(item);
    state = setLayerVisibility(state, layerOrder, "districts", false);
    state = setLayerVisibility(state, layerOrder, "districts", true);
    expect(state.visibleLayerIds).toEqual(layerOrder);
  });
});

describe("reorderLayer", () => {
  it("swaps adjacent layers and rewrites visibility order", () => {
    const item = buildSamplePortalItem();
    let layerOrder = item.layers.map((layer) => layer.id);
    let state = buildInitialState(item);

    const result = reorderLayer(state, layerOrder, "districts", "up");
    expect(result.layerOrder).toEqual(["field-stations", "districts"]);
    expect(result.state.visibleLayerIds).toEqual(["field-stations", "districts"]);

    state = result.state;
    layerOrder = result.layerOrder;

    const noop = reorderLayer(state, layerOrder, "districts", "up");
    expect(noop.layerOrder).toBe(layerOrder);
    expect(noop.state).toBe(state);
  });
});

describe("selectFeature", () => {
  it("returns the same reference when the same feature is selected twice", () => {
    const item = buildSamplePortalItem();
    const initial = buildInitialState(item);
    const selected = selectFeature(initial, { layerId: "districts", featureId: "district-east" });
    const same = selectFeature(selected, { layerId: "districts", featureId: "district-east" });
    expect(same).toBe(selected);
  });
  it("clears selection when called with undefined", () => {
    const item = buildSamplePortalItem();
    const selected = selectFeature(buildInitialState(item), {
      layerId: "districts",
      featureId: "district-east",
    });
    expect(selectFeature(selected, undefined).selected).toBeUndefined();
  });
});
