import { describe, expect, it, vi } from "vitest";

import layerItem from "../../fixtures/catalog/layer.json";
import serviceItem from "../../fixtures/catalog/service.json";
import type { ContentItem } from "../contracts/content-item.js";
import { createPortalViewerSdkFeatureLoader, parseGeoServicesFeatureSource } from "./sdk-feature-loader.js";
import { createFixturePortalViewerSdkFetch } from "./sdk-fixtures.js";

const SERVICE_ITEM = serviceItem as unknown as ContentItem;
const LAYER_ITEM = layerItem as unknown as ContentItem;

describe("parseGeoServicesFeatureSource", () => {
  it("parses a FeatureServer service endpoint into SDK source coordinates", () => {
    const parsed = parseGeoServicesFeatureSource(SERVICE_ITEM, { sourceId: "city-parcels-source" });

    expect(parsed).toMatchObject({
      sourceId: "city-parcels-source",
      baseUrl: "https://api.honua.example/arcgis",
      serviceId: "city/parcels",
      layerId: 0,
      endpointUrl: "https://api.honua.example/arcgis/rest/services/city/parcels/FeatureServer",
    });
  });

  it("uses the layer target id when parsing a FeatureServer layer endpoint", () => {
    const parsed = parseGeoServicesFeatureSource(LAYER_ITEM, { sourceId: "city-parcels-active-source" });

    expect(parsed).toMatchObject({
      sourceId: "city-parcels-active-source",
      baseUrl: "https://api.honua.example/arcgis",
      serviceId: "city/parcels",
      layerId: 0,
      endpointUrl: "https://api.honua.example/arcgis/rest/services/city/parcels/FeatureServer/0",
    });
  });
});

describe("createPortalViewerSdkFeatureLoader", () => {
  it("queries through the SDK source contract and returns GeoJSON features", async () => {
    const parsed = parseGeoServicesFeatureSource(SERVICE_ITEM, { sourceId: "city-parcels-source" });
    if (!parsed) throw new Error("fixture service should parse");
    const fixtureFetch = createFixturePortalViewerSdkFetch();
    const fetchFn = vi.fn((input: Parameters<typeof fetch>[0], init?: Parameters<typeof fetch>[1]) => {
      return fixtureFetch(input, init);
    });
    const loader = createPortalViewerSdkFeatureLoader({ fetchFn });

    const features = await loader(parsed);

    expect(fetchFn.mock.calls[0]?.[0].toString()).toContain("/rest/services/city%2Fparcels/FeatureServer/0/query");
    expect(features).toHaveLength(3);
    expect(features[0]).toMatchObject({
      id: 1,
      properties: { PARCEL_ID: "HON-001", LAND_USE: "Residential" },
      geometry: { type: "Polygon" },
    });
  });
});
