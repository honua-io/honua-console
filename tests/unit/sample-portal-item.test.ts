import { validateHonuaStyle } from "@honua/sdk-js";
import { describe, expect, it } from "vitest";
import { DEFAULT_PORTAL_ITEM_ID, loadPortalItem } from "../../src/catalog/portal-item-loader.js";
import {
  SAMPLE_PORTAL_ITEM_ID,
  buildSamplePortalItem,
  getSampleSourceFeatures,
} from "../../src/catalog/sample-portal-item.js";

describe("sample portal item", () => {
  it("declares metadata fields the viewer surfaces in the metadata panel", () => {
    const item = buildSamplePortalItem();
    expect(item.metadata.id).toEqual(SAMPLE_PORTAL_ITEM_ID);
    expect(item.metadata.title).toBeTruthy();
    expect(item.metadata.summary).toBeTruthy();
    expect(item.metadata.license).toBeTruthy();
    expect(item.metadata.attribution).toBeTruthy();
    expect(item.metadata.coordinateSystem).toBeTruthy();
  });

  it("validates as a HonuaStyleSpecification (no SDK contract drift)", () => {
    const item = buildSamplePortalItem();
    const errors = validateHonuaStyle(item.style);
    expect(errors).toEqual([]);
  });

  it("declares each layer's render layer ids and source", () => {
    const item = buildSamplePortalItem();
    for (const layer of item.layers) {
      expect(layer.id).toBeTruthy();
      expect(layer.sourceId).toBeTruthy();
      expect(layer.renderLayerIds.length).toBeGreaterThan(0);
      expect(item.style.sources[layer.sourceId]).toBeTruthy();
      for (const renderLayerId of layer.renderLayerIds) {
        const found = item.style.layers.find((l) => l.id === renderLayerId);
        expect(found, `style layer ${renderLayerId} should exist for ${layer.id}`).toBeDefined();
        expect(found?.source).toEqual(layer.sourceId);
      }
    }
  });

  it("exposes inline GeoJSON features for inspectable layers", () => {
    const item = buildSamplePortalItem();
    for (const layer of item.layers.filter((l) => l.inspectable)) {
      const features = getSampleSourceFeatures(item, layer.sourceId);
      expect(features.length).toBeGreaterThan(0);
    }
  });
});

describe("loadPortalItem", () => {
  it("returns the sample item when no id is provided", () => {
    const result = loadPortalItem(undefined);
    expect(result.status).toBe("ok");
    if (result.status === "ok") {
      expect(result.item.metadata.id).toEqual(DEFAULT_PORTAL_ITEM_ID);
    }
  });

  it("returns a not-found result for unknown ids", () => {
    const result = loadPortalItem("does-not-exist");
    expect(result.status).toBe("not-found");
    if (result.status === "not-found") {
      expect(result.itemId).toEqual("does-not-exist");
    }
  });

  it("returns an SDK-backed GeoServices source for catalog service fixtures", () => {
    const result = loadPortalItem("01HXY3ZK7N1J2Q9V8M0FQ2PWAB");

    expect(result.status).toBe("ok");
    if (result.status === "ok") {
      const layer = result.item.layers[0];
      expect(layer.sdkSource).toMatchObject({
        baseUrl: "https://api.honua.example/arcgis",
        serviceId: "city/parcels",
        layerId: 0,
      });
      expect(getSampleSourceFeatures(result.item, layer.sourceId)).toEqual([]);
      expect((result.item.style.sources[layer.sourceId] as { type?: string }).type).toBe("geojson");
    }
  });
});
