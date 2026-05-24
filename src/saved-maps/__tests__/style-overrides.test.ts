import { describe, expect, it } from "vitest";

import { MemoryStorage } from "../../../tests/fixtures";
import { buildSamplePortalItem } from "../../catalog/sample-portal-item.js";
import {
  STYLE_EDITOR_DEMO_MAP_ID,
  buildDemoWebMapDoc,
  buildPortalViewerItemFromSavedMap,
  loadFixtureSavedMapForViewer,
  saveFixtureSavedMapDoc,
} from "../fixture-renderer.js";
import {
  applyPortalStyleOverride,
  listEditableStyleTargets,
  resolveDocStyleOrigin,
  resolveSavedMapStyle,
} from "../style-overrides.js";

describe("Maputnik saved-map style overrides (honua-portal#39)", () => {
  it("lists editable saved-map and layer targets with admin/server style provenance", () => {
    const targets = listEditableStyleTargets(buildDemoWebMapDoc());
    expect(targets.map((target) => [target.id, target.origin])).toEqual([
      ["saved-map", "admin-layer-style"],
      ["layer:districts", "admin-layer-style"],
      ["layer:field-stations", "admin-layer-style"],
    ]);
  });

  it("does not offer a saved-map style target when no compatible layer can receive it", () => {
    const doc = buildDemoWebMapDoc();
    doc.operationalLayers = doc.operationalLayers.map((layer) => ({
      ...layer,
      layerType: "unsupported",
    }));

    expect(listEditableStyleTargets(doc)).toEqual([]);
  });

  it("keeps editability tied to WebMapDoc target discovery, not a content-item capability", () => {
    const loaded = loadFixtureSavedMapForViewer(STYLE_EDITOR_DEMO_MAP_ID, new MemoryStorage());
    if (loaded.status !== "ok") throw new Error("fixture failed to load");

    expect(loaded.item.capabilities).toEqual(["render"]);
    expect(listEditableStyleTargets(loaded.doc).map((target) => target.id)).toEqual([
      "saved-map",
      "layer:districts",
      "layer:field-stations",
    ]);
  });

  it("saves a portal override without dropping the admin/server style lineage", () => {
    const base = buildSamplePortalItem();
    const edited = structuredClone(base.style) as unknown as Record<string, unknown>;
    const districtFill = (edited.layers as Array<{ id: string; paint?: Record<string, unknown> }>).find(
      (layer) => layer.id === "districts-fill",
    );
    if (!districtFill) throw new Error("missing districts-fill");
    districtFill.paint = { ...districtFill.paint, "fill-color": "#ff3366" };

    const doc = applyPortalStyleOverride({
      doc: buildDemoWebMapDoc(),
      targetId: "saved-map",
      style: edited,
      sourceStyle: base.style,
    });

    expect(resolveDocStyleOrigin(doc)).toBe("portal-override");
    expect(doc.operationalLayers[0]?.styleRef).toMatchObject({
      itemId: "01HXY3ZK7N1J2Q9V8M0FQ2PWAH",
      origin: "portal-override",
    });
    expect(doc.operationalLayers[0]?.styleRef?.inline).toMatchObject({
      version: 8,
      sources: base.style.sources,
    });
  });

  it("preserves viewer source wiring while applying presentation edits", () => {
    const base = buildSamplePortalItem();
    const edited = structuredClone(base.style) as unknown as Record<string, unknown>;
    const districtFill = getStyleLayer(edited, "districts-fill");
    districtFill.source = "field-stations-source";
    districtFill["source-layer"] = "unexpected-source-layer";
    districtFill.type = "line";
    districtFill.paint = { ...(districtFill.paint as Record<string, unknown>), "fill-color": "#3057ff" };

    const doc = applyPortalStyleOverride({
      doc: buildDemoWebMapDoc(),
      targetId: "saved-map",
      style: edited,
      sourceStyle: base.style,
    });
    const resolved = resolveSavedMapStyle(doc, base.style);
    const resolvedDistrictFill = getStyleLayer(resolved as unknown as Record<string, unknown>, "districts-fill");

    expect(resolvedDistrictFill).toMatchObject({
      id: "districts-fill",
      type: "fill",
      source: "districts-source",
      paint: expect.objectContaining({ "fill-color": "#3057ff" }),
    });
    expect(resolvedDistrictFill["source-layer"]).toBeUndefined();
    expect(resolved.layers.map((layer) => layer.id)).toEqual(base.style.layers.map((layer) => layer.id));
  });

  it("rejects edited styles that add, remove, or duplicate render layers", () => {
    const base = buildSamplePortalItem();
    const withoutLayer = structuredClone(base.style) as unknown as Record<string, unknown>;
    withoutLayer.layers = (withoutLayer.layers as Array<Record<string, unknown>>).filter(
      (layer) => layer.id !== "districts-outline",
    );
    const withExtraLayer = structuredClone(base.style) as unknown as Record<string, unknown>;
    (withExtraLayer.layers as Array<Record<string, unknown>>).push({
      id: "portal-added-layer",
      type: "background",
      paint: { "background-color": "#111111" },
    });
    const withDuplicateLayer = structuredClone(base.style) as unknown as Record<string, unknown>;
    (withDuplicateLayer.layers as Array<Record<string, unknown>>).push({
      ...getStyleLayer(withDuplicateLayer, "districts-fill"),
    });

    const input = {
      doc: buildDemoWebMapDoc(),
      targetId: "saved-map",
      sourceStyle: base.style,
    };
    expect(() => applyPortalStyleOverride({ ...input, style: withoutLayer })).toThrow(
      "Edited style must preserve the original render layer set.",
    );
    expect(() => applyPortalStyleOverride({ ...input, style: withExtraLayer })).toThrow(
      "Edited style must preserve the original render layer set.",
    );
    expect(() => applyPortalStyleOverride({ ...input, style: withDuplicateLayer })).toThrow(
      "Edited style contains a duplicate edited render layer: districts-fill",
    );
  });

  it("replaces the previous portal override when a later layer target is saved", () => {
    const base = buildSamplePortalItem();
    const districtEdit = structuredClone(base.style) as unknown as Record<string, unknown>;
    const districtFill = (districtEdit.layers as Array<{ id: string; paint?: Record<string, unknown> }>).find(
      (layer) => layer.id === "districts-fill",
    );
    if (!districtFill) throw new Error("missing districts-fill");
    districtFill.paint = { ...districtFill.paint, "fill-color": "#ff3366" };

    const districtDoc = applyPortalStyleOverride({
      doc: buildDemoWebMapDoc(),
      targetId: "layer:districts",
      style: districtEdit,
      sourceStyle: base.style,
    });
    const reloadedStyle = resolveSavedMapStyle(districtDoc, base.style);
    const stationEdit = structuredClone(reloadedStyle) as unknown as Record<string, unknown>;
    const stationCircles = (stationEdit.layers as Array<{ id: string; paint?: Record<string, unknown> }>).find(
      (layer) => layer.id === "field-stations-circles",
    );
    if (!stationCircles) throw new Error("missing field-stations-circles");
    stationCircles.paint = { ...stationCircles.paint, "circle-color": "#3057ff" };

    const stationDoc = applyPortalStyleOverride({
      doc: districtDoc,
      targetId: "layer:field-stations",
      style: stationEdit,
      sourceStyle: reloadedStyle,
    });
    const resolved = resolveSavedMapStyle(stationDoc, base.style);
    const resolvedDistrictFill = resolved.layers.find((layer) => layer.id === "districts-fill");
    const resolvedStationCircles = resolved.layers.find((layer) => layer.id === "field-stations-circles");

    expect(stationDoc.operationalLayers[0]?.styleRef).toEqual({
      itemId: "01HXY3ZK7N1J2Q9V8M0FQ2PWAH",
      origin: "admin-layer-style",
    });
    expect(stationDoc.operationalLayers[1]?.styleRef).toMatchObject({
      itemId: "01HXY3ZK7N1J2Q9V8M0FQ2PWAH",
      origin: "portal-override",
    });
    expect(resolvedDistrictFill?.paint?.["fill-color"]).toBe("#ff3366");
    expect(resolvedStationCircles?.paint?.["circle-color"]).toBe("#3057ff");
  });

  it("reloads the saved map and embed renderer through the same persisted override", () => {
    const storage = new MemoryStorage();
    const loaded = loadFixtureSavedMapForViewer(STYLE_EDITOR_DEMO_MAP_ID, storage);
    if (loaded.status !== "ok") throw new Error("fixture failed to load");

    const edited = structuredClone(loaded.viewerItem.style) as unknown as Record<string, unknown>;
    const districtFill = (edited.layers as Array<{ id: string; paint?: Record<string, unknown> }>).find(
      (layer) => layer.id === "districts-fill",
    );
    if (!districtFill) throw new Error("missing districts-fill");
    districtFill.paint = { ...districtFill.paint, "fill-color": "#3057ff" };

    const nextDoc = applyPortalStyleOverride({
      doc: loaded.doc,
      targetId: "layer:districts",
      style: edited,
      sourceStyle: loaded.viewerItem.style,
    });
    saveFixtureSavedMapDoc(STYLE_EDITOR_DEMO_MAP_ID, nextDoc, storage, () => new Date("2026-05-08T12:00:00.000Z"));

    const reloaded = loadFixtureSavedMapForViewer(STYLE_EDITOR_DEMO_MAP_ID, storage);
    if (reloaded.status !== "ok") throw new Error("fixture failed to reload");
    const embedViewer = buildPortalViewerItemFromSavedMap(reloaded.item, reloaded.doc);
    const reloadedFill = reloaded.viewerItem.style.layers.find((layer) => layer.id === "districts-fill");
    const embedFill = embedViewer.style.layers.find((layer) => layer.id === "districts-fill");

    expect(reloadedFill?.paint?.["fill-color"]).toBe("#3057ff");
    expect(embedFill?.paint?.["fill-color"]).toBe("#3057ff");
    expect(reloaded.item.extensions["honua:styleEditing"]).toMatchObject({ effectiveOrigin: "portal-override" });
  });
});

function getStyleLayer(style: Record<string, unknown>, layerId: string): Record<string, unknown> {
  const layers = style["layers"] as Array<Record<string, unknown>>;
  const layer = layers.find((entry) => entry["id"] === layerId);
  if (!layer) throw new Error(`missing style layer: ${layerId}`);
  return layer;
}
