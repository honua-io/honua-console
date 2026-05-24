import { describe, expect, it } from "vitest";

import { summarize } from "../../contracts/content-item.js";
import type { ContentItem, Sharing } from "../../contracts/content-item.js";
import { loadCatalogFixtures } from "../fixtures.js";
import { getOpenAction } from "../openability.js";

const fixtures = loadCatalogFixtures();
const byId = (id: string): ContentItem => {
  const item = fixtures.items.get(id);
  if (!item) throw new Error(`fixture ${id} missing`);
  return item;
};

describe("getOpenAction — full item type matrix", () => {
  it("opens a feature service in the map when render+query+endpoint present", () => {
    const action = getOpenAction(byId("01HXY3ZK7N1J2Q9V8M0FQ2PWAB"));
    expect(action.kind).toBe("open-in-map");
    expect(action.href).toBe("/maps/new?from=01HXY3ZK7N1J2Q9V8M0FQ2PWAB");
  });

  it("opens a vector tile basemap service in the map", () => {
    const action = getOpenAction(byId("01HXY3ZK7N1J2Q9V8M0FQ2PWAE"));
    expect(action.kind).toBe("open-in-map");
  });

  it("opens a query+render layer in the map", () => {
    const action = getOpenAction(byId("01HXY3ZK7N1J2Q9V8M0FQ2PWAC"));
    expect(action.kind).toBe("open-in-map");
    expect(action.href).toBe("/maps/new?from=01HXY3ZK7N1J2Q9V8M0FQ2PWAC");
  });

  it("opens a saved web map at its webmapJsonRef", () => {
    const map = byId("01HXY3ZK7N1J2Q9V8M0FQ2PWAD");
    const action = getOpenAction(map);
    expect(action.kind).toBe("open-in-map");
    expect(action.href).toContain("/maps/");
    if (map.target.type === "map") {
      expect(action.href).toBe(`/maps/${encodeURIComponent(map.target.webmapJsonRef)}`);
    }
  });

  it("marks scenes as unsupported in the Beta viewer", () => {
    const action = getOpenAction(byId("01HXY3ZK7N1J2Q9V8M0FQ2PWAF"));
    expect(action.kind).toBe("unsupported");
    expect(action.reason).toMatch(/scene/i);
    expect(action.href).toBeNull();
  });

  it("opens an app externally at its target.url", () => {
    const action = getOpenAction(byId("01HXY3ZK7N1J2Q9V8M0FQ2PWAG"));
    expect(action.kind).toBe("open-external");
    expect(action.href).toBe("https://apps.honua.example/permit-finder");
  });

  it("opens a document externally at its target.url", () => {
    const action = getOpenAction(byId("01HXY3ZK7N1J2Q9V8M0FQ2PWAH"));
    expect(action.kind).toBe("open-external");
    expect(action.href).toBe("https://docs.honua.example/parcels-data-dictionary.pdf");
  });

  it("opens an external-url at its target.url", () => {
    const action = getOpenAction(byId("01HXY3ZK7N1J2Q9V8M0FQ2PWAJ"));
    expect(action.kind).toBe("open-external");
    expect(action.href).toBe("https://state.honua.example/items/dem-mosaic-2024");
  });

  it("does not return unsafe external URL schemes", () => {
    const base = byId("01HXY3ZK7N1J2Q9V8M0FQ2PWAJ");
    const unsafe: ContentItem = { ...base, target: { type: "external-url", url: "javascript:alert(1)" } };
    const action = getOpenAction(unsafe);
    expect(action.kind).toBe("unsupported");
    expect(action.href).toBeNull();
  });

  it("respects publisher-asserted unsupported override (extensions['honua-portal-viewer'])", () => {
    const action = getOpenAction(byId("01HXY3ZK7N1J2Q9V8M0FQ2PWAM"));
    expect(action.kind).toBe("unsupported");
    expect(action.reason).toMatch(/WMS/);
  });

  it("never returns an action.href for an unsupported result", () => {
    for (const item of fixtures.items.values()) {
      const action = getOpenAction(item);
      if (action.kind === "unsupported") {
        expect(action.href).toBeNull();
        expect(action.reason).toBeTruthy();
      }
    }
  });

  it("works against summary objects (no target/endpoints/extensions present)", () => {
    const summary = summarize(byId("01HXY3ZK7N1J2Q9V8M0FQ2PWAB"));
    const action = getOpenAction(summary);
    expect(action.kind).toBe("open-in-map");
    expect(action.href).toContain("/maps/new?from=");
  });

  it("treats a query-only service summary as unsupported (no endpoints to verify on the wire)", () => {
    const base = byId("01HXY3ZK7N1J2Q9V8M0FQ2PWAB");
    const queryOnly: ContentItem = { ...base, capabilities: ["query"] };
    const summary = summarize(queryOnly);
    expect(summary.viewerSupport).toBeNull();
    const cardAction = getOpenAction(summary);
    expect(cardAction.kind).toBe("unsupported");
    const detailAction = getOpenAction(queryOnly);
    expect(detailAction.kind).toBe("open-in-map");
  });

  it("keeps a render-capable service summary openable even though endpoints are absent", () => {
    const summary = summarize(byId("01HXY3ZK7N1J2Q9V8M0FQ2PWAE"));
    const action = getOpenAction(summary);
    expect(action.kind).toBe("open-in-map");
  });

  it("flags a service with no viewer-supported endpoints as unsupported", () => {
    const base = byId("01HXY3ZK7N1J2Q9V8M0FQ2PWAB");
    const stripped: ContentItem = {
      ...base,
      capabilities: ["query"],
      endpoints: { ...base.endpoints, geoservices: null, ogcFeatures: null, stac: null, tiles: null },
    };
    const action = getOpenAction(stripped);
    expect(action.kind).toBe("unsupported");
  });

  it("returns the user-facing label for each kind", () => {
    expect(getOpenAction(byId("01HXY3ZK7N1J2Q9V8M0FQ2PWAB")).label).toBe("Open in map");
    expect(getOpenAction(byId("01HXY3ZK7N1J2Q9V8M0FQ2PWAJ")).label).toBe("Open external link");
    expect(getOpenAction(byId("01HXY3ZK7N1J2Q9V8M0FQ2PWAH")).label).toBe("Open document");
    expect(getOpenAction(byId("01HXY3ZK7N1J2Q9V8M0FQ2PWAG")).label).toBe("Open app");
    expect(getOpenAction(byId("01HXY3ZK7N1J2Q9V8M0FQ2PWAF")).label).toBe("Unsupported");
  });

  it("never wires open-in-map for non-map types", () => {
    const nonMapTypes: Array<{ id: string; type: string }> = [
      { id: "01HXY3ZK7N1J2Q9V8M0FQ2PWAG", type: "app" },
      { id: "01HXY3ZK7N1J2Q9V8M0FQ2PWAH", type: "document" },
      { id: "01HXY3ZK7N1J2Q9V8M0FQ2PWAJ", type: "external-url" },
      { id: "01HXY3ZK7N1J2Q9V8M0FQ2PWAF", type: "scene" },
    ];
    for (const { id } of nonMapTypes) {
      const action = getOpenAction(byId(id));
      expect(action.kind).not.toBe("open-in-map");
    }
  });

  it("treats every sharing tier as an openability-orthogonal concern", () => {
    const sharings: Sharing[] = ["private", "org", "group", "public-link", "public"];
    const base = byId("01HXY3ZK7N1J2Q9V8M0FQ2PWAB");
    for (const sharing of sharings) {
      const altered: ContentItem = { ...base, access: { ...base.access, sharing } };
      const action = getOpenAction(altered);
      expect(action.kind).toBe("open-in-map");
    }
  });
});
