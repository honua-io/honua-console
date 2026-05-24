import { describe, expect, it } from "vitest";
import { buildSamplePortalItem, getSampleSourceFeatures } from "../../src/catalog/sample-portal-item.js";
import { buildDetailColumns, buildPopupViewModel, formatValue, renderPopupHtml } from "../../src/viewer/popup.js";

const item = buildSamplePortalItem();
const districts = item.layers.find((layer) => layer.id === "districts")!;
const districtFeatures = getSampleSourceFeatures(item, districts.sourceId);

describe("buildPopupViewModel", () => {
  it("uses popup.title as a template against feature properties", () => {
    const model = buildPopupViewModel(districts, districtFeatures[0], 0);
    expect(model.title).toEqual("East District");
  });

  it("falls back to a NAME-style attribute when no popup.title is configured", () => {
    const layer = { ...districts, popup: undefined };
    const model = buildPopupViewModel(layer, districtFeatures[0], 0);
    expect(model.title).toEqual("East District");
  });

  it("emits one row per detailField, in declared order, with the configured label", () => {
    const model = buildPopupViewModel(districts, districtFeatures[0], 0);
    expect(model.rows.map((row) => row.label)).toEqual([
      "District",
      "Population",
      "Area (km²)",
      "Steward",
      "Established",
    ]);
  });

  it("renders unset values as an em dash so popups don't show 'undefined'", () => {
    const layer = {
      ...districts,
      detailFields: [
        { name: "NAME", label: "District" },
        { name: "missing", label: "Missing" },
      ],
    };
    const model = buildPopupViewModel(layer, districtFeatures[0], 0);
    expect(model.rows.find((row) => row.label === "Missing")?.value).toEqual("—");
  });
});

describe("renderPopupHtml", () => {
  it("escapes attribute values so a hostile property cannot inject markup", () => {
    const html = renderPopupHtml({
      title: "<script>alert(1)</script>",
      subtitle: "Districts",
      rows: [{ label: "key", value: "<img src=x onerror=alert(1)>" }],
    });
    expect(html).not.toContain("<script>");
    expect(html).toContain("&lt;script&gt;");
    expect(html).not.toContain("<img src=x onerror=alert(1)>");
    expect(html).toContain("&lt;img src=x onerror=alert(1)&gt;");
  });
});

describe("buildDetailColumns", () => {
  it("returns the layer's declared detailFields when present", () => {
    expect(buildDetailColumns(districts).map((field) => field.name)).toEqual([
      "NAME",
      "population",
      "land_area_km2",
      "stewards",
      "established",
    ]);
  });
});

describe("formatValue", () => {
  it("formats numbers with up to two decimal places when not integer", () => {
    expect(formatValue(99.4)).toEqual("99.40");
    expect(formatValue(184220)).toEqual("184220");
  });
  it("renders missing values consistently", () => {
    expect(formatValue(undefined)).toEqual("—");
    expect(formatValue(null)).toEqual("—");
  });
});
