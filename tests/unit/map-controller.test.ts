import { beforeEach, describe, expect, it, vi } from "vitest";
import { buildSamplePortalItem } from "../../src/catalog/sample-portal-item.js";
import { createMapController } from "../../src/viewer/map-controller.js";

const maplibreMock = vi.hoisted(() => {
  type Handler = (event?: unknown) => void;
  const instances: MockMapLibreMap[] = [];

  class MockMapLibreMap {
    readonly handlers = new globalThis.Map<string, Handler[]>();
    readonly canvas = { style: { cursor: "" } };
    readonly dragPan = { enable: vi.fn(), disable: vi.fn() };
    readonly controls: Array<{ control: unknown; position?: string }> = [];
    readonly sources = new globalThis.Map<string, { setData: ReturnType<typeof vi.fn>; data?: unknown }>();
    readonly layers = new globalThis.Map<
      string,
      { source?: string; type: string; filter?: unknown; paint?: unknown }
    >();
    readonly center: [number, number];
    readonly zoom: number;

    constructor(options: { center: [number, number]; zoom: number }) {
      this.center = options.center;
      this.zoom = options.zoom;
      instances.push(this);
    }

    addControl(control: unknown, position?: string): this {
      this.controls.push({ control, position });
      return this;
    }

    fitBounds(): void {}

    getLayer(renderLayerId: string): { type: string } | undefined {
      const typeById: Record<string, string> = {
        "districts-fill": "fill",
        "districts-outline": "line",
        "field-stations-circles": "circle",
      };
      const type = typeById[renderLayerId] ?? this.layers.get(renderLayerId)?.type;
      return type ? { type } : undefined;
    }

    getSource(sourceId: string): { setData: ReturnType<typeof vi.fn> } | undefined {
      return this.sources.get(sourceId);
    }

    addSource(sourceId: string, source: { data?: unknown }): void {
      const record = {
        data: source.data,
        setData: vi.fn((data: unknown) => {
          record.data = data;
        }),
      };
      this.sources.set(sourceId, record);
    }

    addLayer(layer: { id: string; source?: string; type: string; filter?: unknown; paint?: unknown }): void {
      this.layers.set(layer.id, { source: layer.source, type: layer.type, filter: layer.filter, paint: layer.paint });
    }

    setLayoutProperty(): void {}

    setPaintProperty(): void {}

    moveLayer(): void {}

    flyTo(): void {}

    getCenter(): { lng: number; lat: number } {
      return { lng: this.center[0], lat: this.center[1] };
    }

    getZoom(): number {
      return this.zoom;
    }

    getCanvas(): { style: { cursor: string } } {
      return this.canvas;
    }

    remove(): void {}

    on(event: string, layerOrHandler: string | Handler, handler?: Handler): this {
      const key = typeof layerOrHandler === "string" ? `${event}:${layerOrHandler}` : event;
      const eventHandler = typeof layerOrHandler === "string" ? handler : layerOrHandler;
      if (!eventHandler) return this;
      this.handlers.set(key, [...(this.handlers.get(key) ?? []), eventHandler]);
      return this;
    }

    emit(event: string, payload?: unknown): void {
      for (const handler of this.handlers.get(event) ?? []) handler(payload);
    }
  }

  class MockNavigationControl {}
  class MockScaleControl {}
  class MockPopup {
    setLngLat(): this {
      return this;
    }
    setHTML(): this {
      return this;
    }
    addTo(): this {
      return this;
    }
    remove(): void {}
  }
  class MockMarker {
    readonly element: HTMLElement;
    lngLat?: [number, number];

    constructor(options: { element: HTMLElement }) {
      this.element = options.element;
    }

    setLngLat(lngLat: [number, number]): this {
      this.lngLat = lngLat;
      return this;
    }

    addTo(): this {
      return this;
    }

    remove(): void {}
  }

  return {
    instances,
    MockMapLibreMap,
    MockNavigationControl,
    MockScaleControl,
    MockPopup,
    MockMarker,
  };
});

vi.mock("maplibre-gl", () => ({
  default: {
    Map: maplibreMock.MockMapLibreMap,
    NavigationControl: maplibreMock.MockNavigationControl,
    ScaleControl: maplibreMock.MockScaleControl,
    Popup: maplibreMock.MockPopup,
    Marker: maplibreMock.MockMarker,
  },
}));

function latestMap(): InstanceType<typeof maplibreMock.MockMapLibreMap> {
  const map = maplibreMock.instances.at(-1);
  if (!map) throw new Error("Expected MapLibre map to be constructed");
  return map;
}

describe("createMapController readiness", () => {
  beforeEach(() => {
    maplibreMock.instances.length = 0;
  });

  it("rejects readiness when MapLibre emits an error before load", async () => {
    const item = buildSamplePortalItem();
    const controller = createMapController({
      container: document.createElement("div"),
      item,
      initialView: item.initialView,
    });
    const errors: string[] = [];
    controller.onError((message) => errors.push(message));

    const readiness = expect(controller.ready).rejects.toThrow("Style source failed");
    latestMap().emit("error", { error: { message: "Style source failed" } });

    await readiness;
    expect(errors).toEqual(["Style source failed"]);
  });

  it("keeps post-load map errors as notifications without changing readiness", async () => {
    const item = buildSamplePortalItem();
    const controller = createMapController({
      container: document.createElement("div"),
      item,
      initialView: item.initialView,
    });

    latestMap().emit("style.load");
    await expect(controller.ready).resolves.toBeUndefined();

    const errors: string[] = [];
    controller.onError((message) => errors.push(message));
    latestMap().emit("error", { error: { message: "Tile fetch failed" } });

    expect(errors).toEqual(["Tile fetch failed"]);
    await expect(controller.ready).resolves.toBeUndefined();
  });

  it("omits zoom controls when embed chrome disables them", () => {
    const item = buildSamplePortalItem();
    createMapController({
      container: document.createElement("div"),
      item,
      initialView: item.initialView,
      showZoomControls: false,
    });

    const map = latestMap();
    expect(map.controls.some(({ control }) => control instanceof maplibreMock.MockNavigationControl)).toBe(false);
    expect(map.controls.some(({ control }) => control instanceof maplibreMock.MockScaleControl)).toBe(true);
  });

  it("hydrates SDK-backed sources before resolving readiness", async () => {
    const item = buildSamplePortalItem();
    const feature = {
      type: "Feature" as const,
      id: 1,
      properties: { OBJECTID: 1, PARCEL_ID: "HON-001" },
      geometry: {
        type: "Polygon" as const,
        coordinates: [
          [
            [-157.848, 21.306],
            [-157.842, 21.306],
            [-157.842, 21.312],
            [-157.848, 21.312],
            [-157.848, 21.306],
          ],
        ],
      },
    };
    item.layers[0] = {
      ...item.layers[0],
      sdkSource: {
        id: "city-parcels",
        itemId: item.metadata.id,
        sourceId: "districts-source",
        baseUrl: "https://api.honua.example/arcgis",
        endpointUrl: "https://api.honua.example/arcgis/rest/services/city/parcels/FeatureServer/0",
        serviceId: "city/parcels",
        layerId: 0,
      },
    };
    const loadSdkSource = vi.fn().mockResolvedValue([feature]);
    const onSourceFeatures = vi.fn();
    const controller = createMapController({
      container: document.createElement("div"),
      item,
      initialView: item.initialView,
      loadSdkSource,
      onSourceFeatures,
    });
    const source = { setData: vi.fn() };
    latestMap().sources.set("districts-source", source);

    latestMap().emit("load");
    await expect(controller.ready).resolves.toBeUndefined();

    expect(loadSdkSource).toHaveBeenCalledWith(item.layers[0].sdkSource);
    expect(source.setData).toHaveBeenCalledWith({ type: "FeatureCollection", features: [feature] });
    expect(onSourceFeatures).toHaveBeenCalledWith("districts-source", [feature]);
    expect((item.style.sources["districts-source"] as { data?: unknown }).data).toEqual({
      type: "FeatureCollection",
      features: [feature],
    });
  });

  it("keeps annotation pins in a separate MapLibre source and reports map clicks", async () => {
    const item = buildSamplePortalItem();
    const controller = createMapController({
      container: document.createElement("div"),
      item,
      initialView: item.initialView,
    });
    const clicks: Array<[number, number]> = [];
    controller.onMapClick((event) => clicks.push(event.lngLat));

    controller.setAnnotationPins([
      {
        id: "pin-1",
        threadId: "thread-1",
        title: "Review shoreline access",
        status: "open",
        anchorKind: "map",
        lngLat: [-157.8, 21.3],
      },
    ]);

    latestMap().emit("load");
    await expect(controller.ready).resolves.toBeUndefined();

    const source = latestMap().sources.get("honua-annotation-pins");
    expect(source?.data).toEqual({
      type: "FeatureCollection",
      features: [
        {
          type: "Feature",
          id: "pin-1",
          properties: {
            pinId: "pin-1",
            threadId: "thread-1",
            title: "Review shoreline access",
            status: "open",
            anchorKind: "map",
          },
          geometry: { type: "Point", coordinates: [-157.8, 21.3] },
        },
      ],
    });

    controller.setAnnotationPins([
      {
        id: "pin-2",
        threadId: "thread-2",
        title: "Resolved field note",
        status: "resolved",
        anchorKind: "feature",
        lngLat: [-157.825, 21.32],
      },
    ]);
    expect(source?.setData).toHaveBeenCalledWith({
      type: "FeatureCollection",
      features: [
        expect.objectContaining({
          id: "pin-2",
          properties: expect.objectContaining({ threadId: "thread-2", status: "resolved", anchorKind: "feature" }),
          geometry: { type: "Point", coordinates: [-157.825, 21.32] },
        }),
      ],
    });

    latestMap().emit("click", { lngLat: { lng: -157.81, lat: 21.31 } });
    expect(clicks).toEqual([[-157.81, 21.31]]);
  });

  it("renders collaboration cursors in a separate MapLibre source and reports pointer movement", async () => {
    const item = buildSamplePortalItem();
    const controller = createMapController({
      container: document.createElement("div"),
      item,
      initialView: item.initialView,
    });
    const moves: Array<[number, number]> = [];
    controller.onMapPointerMove((event) => moves.push(event.lngLat));

    controller.setCollaborationCursors([
      {
        participantId: "u-1",
        name: "Mira Chen",
        color: "#4ec9b0",
        lngLat: [-157.8, 21.3],
      },
    ]);

    latestMap().emit("load");
    await expect(controller.ready).resolves.toBeUndefined();

    const map = latestMap();
    expect(map.layers.get("honua-collaboration-cursors-dot")).toMatchObject({
      source: "honua-collaboration-cursors",
      type: "circle",
    });

    const source = map.sources.get("honua-collaboration-cursors");
    expect(source?.data).toEqual({
      type: "FeatureCollection",
      features: [
        {
          type: "Feature",
          id: "u-1",
          properties: {
            participantId: "u-1",
            name: "Mira Chen",
            color: "#4ec9b0",
          },
          geometry: { type: "Point", coordinates: [-157.8, 21.3] },
        },
      ],
    });

    controller.setCollaborationCursors([
      {
        participantId: "u-2",
        name: "Kai Torres",
        color: "#f3b562",
        lngLat: [-157.825, 21.32],
      },
    ]);
    expect(source?.setData).toHaveBeenCalledWith({
      type: "FeatureCollection",
      features: [
        expect.objectContaining({
          id: "u-2",
          properties: expect.objectContaining({ participantId: "u-2", name: "Kai Torres", color: "#f3b562" }),
          geometry: { type: "Point", coordinates: [-157.825, 21.32] },
        }),
      ],
    });

    latestMap().emit("mousemove", { lngLat: { lng: -157.81, lat: 21.31 } });
    expect(moves).toEqual([[-157.81, 21.31]]);
  });

  it("emits freehand draw events only while drawing is enabled", async () => {
    const item = buildSamplePortalItem();
    const controller = createMapController({
      container: document.createElement("div"),
      item,
      initialView: item.initialView,
    });
    const events: Array<{ phase: string; lngLat: [number, number] }> = [];
    controller.onFreehandDraw((event) => events.push(event));

    latestMap().emit("load");
    await expect(controller.ready).resolves.toBeUndefined();

    latestMap().emit("mousedown", { lngLat: { lng: -157.82, lat: 21.3 } });
    expect(events).toEqual([]);

    controller.setFreehandDrawingEnabled(true);
    latestMap().emit("mousedown", { lngLat: { lng: -157.82, lat: 21.3 } });
    latestMap().emit("mousemove", { lngLat: { lng: -157.81, lat: 21.31 } });
    latestMap().emit("mouseup", { lngLat: { lng: -157.8, lat: 21.32 } });

    expect(events).toEqual([
      { phase: "start", lngLat: [-157.82, 21.3] },
      { phase: "move", lngLat: [-157.81, 21.31] },
      { phase: "end", lngLat: [-157.8, 21.32] },
    ]);
    expect(latestMap().dragPan.disable).toHaveBeenCalledTimes(1);
    expect(latestMap().dragPan.enable).toHaveBeenCalledTimes(1);

    controller.setFreehandDrawingEnabled(false);
    expect(latestMap().dragPan.enable).toHaveBeenCalledTimes(2);
  });

  it("renders annotation shapes in a separate MapLibre source and updates polygon and line features", async () => {
    const item = buildSamplePortalItem();
    const controller = createMapController({
      container: document.createElement("div"),
      item,
      initialView: item.initialView,
    });

    controller.setAnnotationShapes([
      {
        id: "shape-1",
        threadId: "thread-1",
        title: "Survey area",
        status: "open",
        geometry: {
          type: "Polygon",
          coordinates: [
            [
              [-157.85, 21.3],
              [-157.84, 21.3],
              [-157.84, 21.31],
              [-157.85, 21.31],
              [-157.85, 21.3],
            ],
          ],
        },
      },
    ]);

    latestMap().emit("load");
    await expect(controller.ready).resolves.toBeUndefined();

    const map = latestMap();
    expect(map.layers.get("honua-annotation-shapes-fill")).toMatchObject({
      source: "honua-annotation-shapes",
      type: "fill",
      filter: ["==", ["geometry-type"], "Polygon"],
    });
    expect(map.layers.get("honua-annotation-shapes-outline")).toMatchObject({
      source: "honua-annotation-shapes",
      type: "line",
      filter: ["==", ["geometry-type"], "Polygon"],
    });
    expect(map.layers.get("honua-annotation-shapes-line")).toMatchObject({
      source: "honua-annotation-shapes",
      type: "line",
      filter: ["==", ["geometry-type"], "LineString"],
    });

    const source = map.sources.get("honua-annotation-shapes");
    expect(source?.data).toEqual({
      type: "FeatureCollection",
      features: [
        {
          type: "Feature",
          id: "shape-1",
          properties: {
            shapeId: "shape-1",
            threadId: "thread-1",
            title: "Survey area",
            status: "open",
            geometryType: "Polygon",
          },
          geometry: {
            type: "Polygon",
            coordinates: [
              [
                [-157.85, 21.3],
                [-157.84, 21.3],
                [-157.84, 21.31],
                [-157.85, 21.31],
                [-157.85, 21.3],
              ],
            ],
          },
        },
      ],
    });

    controller.setAnnotationShapes([
      {
        id: "shape-2",
        threadId: "thread-2",
        title: "Transect",
        status: "resolved",
        geometry: {
          type: "LineString",
          coordinates: [
            [-157.82, 21.3],
            [-157.81, 21.32],
          ],
        },
      },
    ]);

    expect(source?.setData).toHaveBeenCalledWith({
      type: "FeatureCollection",
      features: [
        {
          type: "Feature",
          id: "shape-2",
          properties: {
            shapeId: "shape-2",
            threadId: "thread-2",
            title: "Transect",
            status: "resolved",
            geometryType: "LineString",
          },
          geometry: {
            type: "LineString",
            coordinates: [
              [-157.82, 21.3],
              [-157.81, 21.32],
            ],
          },
        },
      ],
    });
  });
});
