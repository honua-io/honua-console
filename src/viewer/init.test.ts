import { beforeEach, describe, expect, it, vi } from "vitest";
import { MemoryStorage } from "../../tests/fixtures";
import { parseEmbedParams } from "../embed/route.js";
import {
  STYLE_EDITOR_DEMO_CONTENT_ITEM_ID,
  buildDemoWebMapDoc,
  loadFixtureSavedMapForViewer,
} from "../saved-maps/index.js";
import {
  addMapCommentThread,
  createEmptyAnnotationWorkspace,
  createShapeAnnotation,
  setAnnotationPublicComments,
} from "./annotation-state.js";
import { initMapViewer } from "./init.js";

const mapControllerMock = vi.hoisted(() => {
  type ViewChangeHandler = (event: { center: [number, number]; zoom: number }) => void;
  type FeatureClickHandler = (event: { layerId: string; featureId: string; lngLat: [number, number] }) => void;
  type MapClickHandler = (event: { lngLat: [number, number] }) => void;
  type MapPointerMoveHandler = (event: { lngLat: [number, number] }) => void;
  type AnnotationPinClickHandler = (event: { threadId: string }) => void;
  type FreehandDrawHandler = (event: { phase: "start" | "move" | "end"; lngLat: [number, number] }) => void;

  const controllers: Array<{
    emitViewChange: (event: { center: [number, number]; zoom: number }) => void;
    emitFeatureClick: (event: { layerId: string; featureId: string; lngLat: [number, number] }) => void;
    emitMapClick: (event: { lngLat: [number, number] }) => void;
    emitMapPointerMove: (event: { lngLat: [number, number] }) => void;
    emitAnnotationPinClick: (event: { threadId: string }) => void;
    emitFreehandDraw: (event: { phase: "start" | "move" | "end"; lngLat: [number, number] }) => void;
    setCollaborationCursors: ReturnType<typeof vi.fn>;
    setAnnotationPins: ReturnType<typeof vi.fn>;
    setAnnotationShapes: ReturnType<typeof vi.fn>;
    setFreehandDrawingEnabled: ReturnType<typeof vi.fn>;
    destroy: ReturnType<typeof vi.fn>;
  }> = [];
  const createMapController = vi.fn((options: unknown) => {
    const viewHandlers: ViewChangeHandler[] = [];
    const featureClickHandlers: FeatureClickHandler[] = [];
    const mapClickHandlers: MapClickHandler[] = [];
    const mapPointerMoveHandlers: MapPointerMoveHandler[] = [];
    const annotationPinClickHandlers: AnnotationPinClickHandler[] = [];
    const freehandDrawHandlers: FreehandDrawHandler[] = [];
    const controller = {
      ready: Promise.resolve(),
      setLayerVisibility: vi.fn(),
      setLayerOpacity: vi.fn(),
      applyLayerOrder: vi.fn(),
      setCollaborationCursors: vi.fn(),
      setAnnotationPins: vi.fn(),
      setAnnotationShapes: vi.fn(),
      setFreehandDrawingEnabled: vi.fn(),
      flyTo: vi.fn(),
      showPopup: vi.fn(),
      closePopup: vi.fn(),
      onFeatureClick: vi.fn((handler: FeatureClickHandler) => {
        featureClickHandlers.push(handler);
      }),
      onMapClick: vi.fn((handler: MapClickHandler) => {
        mapClickHandlers.push(handler);
      }),
      onMapPointerMove: vi.fn((handler: MapPointerMoveHandler) => {
        mapPointerMoveHandlers.push(handler);
      }),
      onAnnotationPinClick: vi.fn((handler: AnnotationPinClickHandler) => {
        annotationPinClickHandlers.push(handler);
      }),
      onFreehandDraw: vi.fn((handler: FreehandDrawHandler) => {
        freehandDrawHandlers.push(handler);
      }),
      onViewChange: vi.fn((handler: ViewChangeHandler) => {
        viewHandlers.push(handler);
      }),
      onError: vi.fn(),
      destroy: vi.fn(),
      emitViewChange(event: { center: [number, number]; zoom: number }) {
        for (const handler of viewHandlers) handler(event);
      },
      emitFeatureClick(event: { layerId: string; featureId: string; lngLat: [number, number] }) {
        for (const handler of featureClickHandlers) handler(event);
      },
      emitMapClick(event: { lngLat: [number, number] }) {
        for (const handler of mapClickHandlers) handler(event);
      },
      emitMapPointerMove(event: { lngLat: [number, number] }) {
        for (const handler of mapPointerMoveHandlers) handler(event);
      },
      emitAnnotationPinClick(event: { threadId: string }) {
        for (const handler of annotationPinClickHandlers) handler(event);
      },
      emitFreehandDraw(event: { phase: "start" | "move" | "end"; lngLat: [number, number] }) {
        for (const handler of freehandDrawHandlers) handler(event);
      },
      options,
    };
    controllers.push(controller);
    return controller;
  });

  return {
    controllers,
    createMapController,
  };
});

vi.mock("./map-controller.js", () => ({
  createMapController: mapControllerMock.createMapController,
}));

describe("initMapViewer embed contract", () => {
  beforeEach(() => {
    mapControllerMock.controllers.length = 0;
    mapControllerMock.createMapController.mockClear();
    removeStoredDemoMap();
    window.history.replaceState(null, "", "/");
  });

  it("preserves embedToken fragments instead of replacing them with viewer hash state", () => {
    window.history.replaceState(null, "", "/embed/maps/map-style-demo?chrome=none#embedToken=tok%2Bslash%2F%3D");
    const replaceSpy = vi.spyOn(window.history, "replaceState");

    const handle = initMapViewer(createViewerRoot(), {
      savedMapId: "map-style-demo",
      mode: "embed",
      embedParams: parseEmbedParams("chrome=none"),
    });

    expect(window.location.hash).toBe("#embedToken=tok%2Bslash%2F%3D");
    expect(replaceSpy).not.toHaveBeenCalled();

    mapControllerMock.controllers[0]?.emitViewChange({ center: [-157.8, 21.31], zoom: 13 });

    expect(window.location.hash).toBe("#embedToken=tok%2Bslash%2F%3D");
    expect(replaceSpy).not.toHaveBeenCalled();
    handle.dispose();
  });

  it("uses embed query extent and zoom-control settings when mounting", () => {
    initMapViewer(createViewerRoot(), {
      savedMapId: "map-style-demo",
      mode: "embed",
      embedParams: parseEmbedParams("zoom=off&extent=-157.9,21.2,-157.7,21.4"),
    });

    expect(mapControllerMock.createMapController).toHaveBeenCalledWith(
      expect.objectContaining({
        initialView: expect.objectContaining({
          bounds: [-157.9, 21.2, -157.7, 21.4],
        }),
        showZoomControls: false,
      }),
    );
  });

  it("loads a catalog item from the explicit itemId option", () => {
    const root = createViewerRoot();
    const sdkFeatureLoader = vi.fn().mockResolvedValue([]);
    const handle = initMapViewer(root, {
      itemId: "01HXY3ZK7N1J2Q9V8M0FQ2PWAB",
      sdkFeatureLoader,
    });

    expect(getRequiredElement(root, "[data-portal-item-title]")).toHaveTextContent("City Parcels 2026");
    expect(mapControllerMock.createMapController).toHaveBeenCalledWith(
      expect.objectContaining({
        item: expect.objectContaining({
          metadata: expect.objectContaining({ id: "01HXY3ZK7N1J2Q9V8M0FQ2PWAB" }),
        }),
        loadSdkSource: sdkFeatureLoader,
      }),
    );
    handle.dispose();
  });

  it("renders the annotation panel as editable for saved-map viewer routes", () => {
    const root = createViewerRoot();
    const handle = initMapViewer(root, {
      savedMapId: "map-style-demo",
      actorId: "u-member",
      actorName: "Mira Chen",
    });

    const panel = getRequiredElement(root, "[data-annotation-panel]");
    expect(panel).toHaveTextContent("No annotations yet.");
    expect(panel).toHaveTextContent("Pins");
    expect(panel).toHaveTextContent("Shapes");
    expect(panel.querySelector("[data-annotation-body]")).not.toBeNull();
    expect(getButtonByText(panel, "Export JSON")).toBeDisabled();
    expect(getButtonByText(panel, "Export GeoJSON")).toBeDisabled();
    expect(mapControllerMock.controllers[0]?.setAnnotationPins).toHaveBeenCalledWith([]);
    expect(mapControllerMock.controllers[0]?.setAnnotationShapes).toHaveBeenCalledWith([]);

    handle.dispose();
  });

  it("renders saved-map collaboration presence and shares live cursors across viewer sessions", () => {
    const restoreStorage = installWindowStorage();
    vi.stubGlobal("BroadcastChannel", undefined);
    const memberRoot = createViewerRoot();
    const ownerRoot = createViewerRoot();
    const memberHandle = initMapViewer(memberRoot, {
      savedMapId: "map-style-demo",
      actorId: "u-member",
      actorName: "Mira Chen",
    });
    const ownerHandle = initMapViewer(ownerRoot, {
      savedMapId: "map-style-demo",
      actorId: "user_alex",
      actorName: "Alex Lee",
    });

    try {
      expect(getRequiredElement(memberRoot, "[data-collaboration-panel]")).toHaveTextContent("You: Mira Chen");
      expect(getRequiredElement(ownerRoot, "[data-collaboration-panel]")).toHaveTextContent("Mira Chen");

      mapControllerMock.controllers[0]?.emitMapPointerMove({ lngLat: [-157.812, 21.312] });

      expect(mapControllerMock.controllers[1]?.setCollaborationCursors).toHaveBeenLastCalledWith([
        expect.objectContaining({
          participantId: "u-member",
          name: "Mira Chen",
          lngLat: [-157.812, 21.312],
        }),
      ]);
    } finally {
      memberHandle.dispose();
      ownerHandle.dispose();
      vi.unstubAllGlobals();
      restoreStorage();
    }
  });

  it("persists a map pin comment into the saved-map WebMapDoc", () => {
    const restoreStorage = installWindowStorage();
    const root = createViewerRoot();
    const handle = initMapViewer(root, {
      savedMapId: "map-style-demo",
      actorId: "u-member",
      actorName: "Mira Chen",
    });

    try {
      const panel = getRequiredElement(root, "[data-annotation-panel]");
      const body = getRequiredElement<HTMLTextAreaElement>(panel, "[data-annotation-body]");
      body.value = "Smoke-test map review note.";
      panel.querySelector<HTMLButtonElement>(".annotation-panel__action")?.click();
      expect(panel).toHaveTextContent("Click the map to place the pin");

      mapControllerMock.controllers[0]?.emitMapClick({ lngLat: [-157.81234, 21.31234] });

      expect(panel).toHaveTextContent("Smoke-test map review note.");
      expect(panel).toHaveTextContent("Annotation saved.");
      const reloaded = loadFixtureSavedMapForViewer("map-style-demo");
      if (reloaded.status !== "ok") throw new Error("fixture failed to reload");
      expect(reloaded.doc.annotations?.commentThreads[0]).toMatchObject({
        kind: "comment-thread",
        title: "Smoke-test map review note.",
        anchor: { kind: "map", lngLat: [-157.81234, 21.31234] },
        createdBy: { id: "u-member", name: "Mira Chen" },
      });
      expect(reloaded.doc.annotations?.annotationSets[0]).toMatchObject({
        kind: "point",
        threadId: expect.any(String),
        anchor: { kind: "map", lngLat: [-157.81234, 21.31234] },
      });
    } finally {
      handle.dispose();
      restoreStorage();
    }
  });

  it("persists a rectangle shape annotation into the saved-map WebMapDoc", () => {
    const restoreStorage = installWindowStorage();
    const root = createViewerRoot();
    const handle = initMapViewer(root, {
      savedMapId: "map-style-demo",
      actorId: "u-member",
      actorName: "Mira Chen",
    });

    try {
      const panel = getRequiredElement(root, "[data-annotation-panel]");
      const body = getRequiredElement<HTMLTextAreaElement>(panel, "[data-annotation-body]");
      body.value = "Driveway review area";
      getButtonByText(panel, "Place rectangle").click();
      expect(panel).toHaveTextContent("Click the map to place the rectangle");

      mapControllerMock.controllers[0]?.emitMapClick({ lngLat: [-157.81234, 21.31234] });

      expect(panel).toHaveTextContent("Driveway review area");
      expect(panel).toHaveTextContent("Rectangle annotation saved.");
      expect(mapControllerMock.controllers[0]?.setAnnotationShapes).toHaveBeenLastCalledWith([
        expect.objectContaining({
          id: expect.any(String),
          title: "Driveway review area",
          status: "open",
          geometry: expect.objectContaining({ type: "Polygon" }),
        }),
      ]);

      const reloaded = loadFixtureSavedMapForViewer("map-style-demo");
      if (reloaded.status !== "ok") throw new Error("fixture failed to reload");
      expect(reloaded.doc.annotations?.annotationSets[0]).toMatchObject({
        kind: "shape",
        shape: "rectangle",
        title: "Driveway review area",
        createdBy: { id: "u-member", name: "Mira Chen" },
        geometry: {
          type: "rectangle",
          coordinates: [
            [-157.81634, 21.31484],
            [-157.80834, 21.31484],
            [-157.80834, 21.30984],
            [-157.81634, 21.30984],
            [-157.81634, 21.31484],
          ],
        },
      });
    } finally {
      handle.dispose();
      restoreStorage();
    }
  });

  it("persists a polygon shape annotation from clicked map vertices", () => {
    const restoreStorage = installWindowStorage();
    const root = createViewerRoot();
    const handle = initMapViewer(root, {
      savedMapId: "map-style-demo",
      actorId: "u-member",
      actorName: "Mira Chen",
    });

    try {
      const panel = getRequiredElement(root, "[data-annotation-panel]");
      const body = getRequiredElement<HTMLTextAreaElement>(panel, "[data-annotation-body]");
      body.value = "Potential staging area";
      getButtonByText(panel, "Start polygon").click();
      expect(panel).toHaveTextContent("Click the map to add polygon vertices");
      expect(getButtonByText(panel, "Finish polygon")).toBeDisabled();
      expect(getButtonByText(panel, "Cancel polygon")).toBeEnabled();

      mapControllerMock.controllers[0]?.emitMapClick({ lngLat: [-157.81234, 21.31234] });
      mapControllerMock.controllers[0]?.emitMapClick({ lngLat: [-157.80234, 21.31234] });
      expect(mapControllerMock.controllers[0]?.setAnnotationShapes).toHaveBeenLastCalledWith([
        expect.objectContaining({
          id: "__polygon-draft",
          title: "Potential staging area",
          geometry: {
            type: "LineString",
            coordinates: [
              [-157.81234, 21.31234],
              [-157.80234, 21.31234],
            ],
          },
        }),
      ]);

      mapControllerMock.controllers[0]?.emitMapClick({ lngLat: [-157.80734, 21.30234] });
      expect(getButtonByText(panel, "Finish polygon")).toBeEnabled();
      getButtonByText(panel, "Finish polygon").click();

      expect(panel).toHaveTextContent("Potential staging area");
      expect(panel).toHaveTextContent("Polygon annotation saved.");
      expect(mapControllerMock.controllers[0]?.setAnnotationShapes).toHaveBeenLastCalledWith([
        expect.objectContaining({
          id: expect.any(String),
          title: "Potential staging area",
          status: "open",
          geometry: expect.objectContaining({ type: "Polygon" }),
        }),
      ]);

      const reloaded = loadFixtureSavedMapForViewer("map-style-demo");
      if (reloaded.status !== "ok") throw new Error("fixture failed to reload");
      expect(reloaded.doc.annotations?.annotationSets[0]).toMatchObject({
        kind: "shape",
        shape: "polygon",
        title: "Potential staging area",
        createdBy: { id: "u-member", name: "Mira Chen" },
        geometry: {
          type: "polygon",
          coordinates: [
            [-157.81234, 21.31234],
            [-157.80234, 21.31234],
            [-157.80734, 21.30234],
            [-157.81234, 21.31234],
          ],
        },
      });
    } finally {
      handle.dispose();
      restoreStorage();
    }
  });

  it("persists a freehand shape annotation from dragged map points", () => {
    const restoreStorage = installWindowStorage();
    const root = createViewerRoot();
    const handle = initMapViewer(root, {
      savedMapId: "map-style-demo",
      actorId: "u-member",
      actorName: "Mira Chen",
    });

    try {
      const panel = getRequiredElement(root, "[data-annotation-panel]");
      const body = getRequiredElement<HTMLTextAreaElement>(panel, "[data-annotation-body]");
      body.value = "Field sketch";
      getButtonByText(panel, "Start freehand").click();
      expect(panel).toHaveTextContent("Drag on the map to draw freehand");
      expect(getButtonByText(panel, "Finish freehand")).toBeDisabled();
      expect(getButtonByText(panel, "Cancel freehand")).toBeEnabled();
      expect(mapControllerMock.controllers[0]?.setFreehandDrawingEnabled).toHaveBeenLastCalledWith(true);

      mapControllerMock.controllers[0]?.emitFreehandDraw({ phase: "start", lngLat: [-157.81234, 21.31234] });
      mapControllerMock.controllers[0]?.emitFreehandDraw({ phase: "move", lngLat: [-157.81134, 21.31334] });
      mapControllerMock.controllers[0]?.emitFreehandDraw({ phase: "end", lngLat: [-157.81034, 21.31434] });
      expect(getButtonByText(panel, "Finish freehand")).toBeEnabled();
      expect(mapControllerMock.controllers[0]?.setAnnotationShapes).toHaveBeenLastCalledWith([
        expect.objectContaining({
          id: "__freehand-draft",
          title: "Field sketch",
          geometry: {
            type: "LineString",
            coordinates: [
              [-157.81234, 21.31234],
              [-157.81134, 21.31334],
              [-157.81034, 21.31434],
            ],
          },
        }),
      ]);

      getButtonByText(panel, "Finish freehand").click();

      expect(panel).toHaveTextContent("Field sketch");
      expect(panel).toHaveTextContent("Freehand annotation saved.");
      expect(mapControllerMock.controllers[0]?.setFreehandDrawingEnabled).toHaveBeenLastCalledWith(false);
      expect(mapControllerMock.controllers[0]?.setAnnotationShapes).toHaveBeenLastCalledWith([
        expect.objectContaining({
          id: expect.any(String),
          title: "Field sketch",
          status: "open",
          geometry: expect.objectContaining({ type: "LineString" }),
        }),
      ]);

      const reloaded = loadFixtureSavedMapForViewer("map-style-demo");
      if (reloaded.status !== "ok") throw new Error("fixture failed to reload");
      expect(reloaded.doc.annotations?.annotationSets[0]).toMatchObject({
        kind: "shape",
        shape: "freehand",
        title: "Field sketch",
        createdBy: { id: "u-member", name: "Mira Chen" },
        geometry: {
          type: "freehand",
          coordinates: [
            [-157.81234, 21.31234],
            [-157.81134, 21.31334],
            [-157.81034, 21.31434],
          ],
        },
      });
    } finally {
      handle.dispose();
      restoreStorage();
    }
  });

  it("downloads a JSON annotation export without rewriting the saved-map document", async () => {
    const restoreStorage = installWindowStorage();
    const downloads = installDownloadMocks();
    const root = createViewerRoot();
    const handle = initMapViewer(root, {
      savedMapId: "map-style-demo",
      actorId: "u-member",
      actorName: "Mira Chen",
    });

    try {
      const panel = getRequiredElement(root, "[data-annotation-panel]");
      const body = getRequiredElement<HTMLTextAreaElement>(panel, "[data-annotation-body]");
      body.value = "Smoke-test map review note.";
      getButtonByText(panel, "Place pin").click();
      mapControllerMock.controllers[0]?.emitMapClick({ lngLat: [-157.81234, 21.31234] });

      const storedBeforeExport = window.localStorage.getItem("honua.portal.saved-map.map-style-demo");
      getButtonByText(panel, "Export JSON").click();

      expect(downloads.blobs).toHaveLength(1);
      expect(downloads.clickedAnchors).toEqual([
        { href: "blob:annotation-export-1", download: "map-style-demo-annotations.json" },
      ]);
      expect(window.localStorage.getItem("honua.portal.saved-map.map-style-demo")).toBe(storedBeforeExport);
      expect(getRequiredElement(root, "[data-map-status]")).toHaveTextContent("Annotation export downloaded.");

      const text = await readBlobAsText(downloads.blobs[0]);
      const parsed = JSON.parse(text);
      expect(parsed).toMatchObject({
        version: "honua-annotation-export/v1",
        map: {
          id: "map-style-demo",
          title: "Honolulu style demo",
          webMapVersion: "honua-webmap/v1",
        },
        counts: {
          annotations: 1,
          threads: 1,
          openThreads: 1,
          resolvedThreads: 0,
        },
      });
      expect(parsed.commentThreads[0].comments[0]).toMatchObject({
        body: "Smoke-test map review note.",
        author: { id: "u-member", name: "Mira Chen" },
      });
    } finally {
      handle.dispose();
      downloads.restore();
      restoreStorage();
    }
  });

  it("renders saved annotations read-only in embed mode", () => {
    const restoreStorage = installWindowStorage();
    const doc = buildDemoWebMapDoc();
    doc.annotations = addMapCommentThread(createEmptyAnnotationWorkspace(), {
      body: "Public works should review this driveway label.",
      lngLat: [-157.81, 21.31],
      actor: { id: "u-owner", name: "Nanea Lee" },
      now: () => new Date("2026-05-10T12:00:00.000Z"),
      generateId: (prefix) => `${prefix}-embed`,
    });
    doc.annotations = createShapeAnnotation(doc.annotations, {
      title: "Embed review area",
      shape: "rectangle",
      coordinates: [
        [-157.82, 21.32],
        [-157.81, 21.32],
        [-157.81, 21.31],
        [-157.82, 21.31],
      ],
      actor: { id: "u-owner", name: "Nanea Lee" },
      now: () => new Date("2026-05-10T12:30:00.000Z"),
      generateId: (prefix) => `${prefix}-embed`,
    });
    doc.annotations = createShapeAnnotation(doc.annotations, {
      title: "Embed field sketch",
      shape: "freehand",
      coordinates: [
        [-157.82, 21.32],
        [-157.815, 21.318],
        [-157.81, 21.319],
      ],
      actor: { id: "u-owner", name: "Nanea Lee" },
      now: () => new Date("2026-05-10T12:45:00.000Z"),
      generateId: (prefix) => `${prefix}-embed-line`,
    });
    window.localStorage.setItem(
      "honua.portal.saved-map.map-style-demo",
      JSON.stringify({ doc, modified: "2026-05-10T12:00:00.000Z" }),
    );
    const root = createViewerRoot();
    const handle = initMapViewer(root, {
      savedMapId: "map-style-demo",
      mode: "embed",
      embedParams: parseEmbedParams(""),
    });

    try {
      const panel = getRequiredElement(root, "[data-annotation-panel]");
      expect(panel).toHaveTextContent("Public works should review this driveway label.");
      expect(panel).toHaveTextContent("Embed review area");
      expect(panel).toHaveTextContent("Embed field sketch");
      expect(panel).toHaveTextContent("Annotations follow this saved map's sharing settings.");
      expect(panel.querySelector("[data-annotation-body]")).toBeNull();
      expect(panel).not.toHaveTextContent("Export JSON");
      expect(panel).not.toHaveTextContent("Export GeoJSON");
      expect(mapControllerMock.controllers[0]?.setAnnotationPins).toHaveBeenCalledWith([
        expect.objectContaining({ threadId: "thread-embed", lngLat: [-157.81, 21.31] }),
      ]);
      expect(mapControllerMock.controllers[0]?.setAnnotationShapes).toHaveBeenCalledWith([
        expect.objectContaining({
          id: "shape-embed",
          title: "Embed review area",
          geometry: expect.objectContaining({ type: "Polygon" }),
        }),
        expect.objectContaining({
          id: "shape-embed-line",
          title: "Embed field sketch",
          geometry: expect.objectContaining({ type: "LineString" }),
        }),
      ]);
    } finally {
      handle.dispose();
      restoreStorage();
    }
  });

  it("lets a map moderator enable public comments", () => {
    const restoreStorage = installWindowStorage();
    const root = createViewerRoot();
    const handle = initMapViewer(root, {
      savedMapId: "map-style-demo",
      actorId: "u-member",
      actorName: "Mira Chen",
      canModerateAnnotations: true,
    });

    try {
      const panel = getRequiredElement(root, "[data-annotation-panel]");
      const checkbox = getRequiredElement<HTMLInputElement>(panel, ".annotation-panel__check input");

      expect(checkbox.checked).toBe(false);
      checkbox.click();

      expect(panel).toHaveTextContent("Public comments enabled.");
      const reloaded = loadFixtureSavedMapForViewer("map-style-demo");
      if (reloaded.status !== "ok") throw new Error("fixture failed to reload");
      expect(reloaded.doc.annotations?.visibility).toEqual({
        defaultAudience: "map",
        publicComments: true,
      });
    } finally {
      handle.dispose();
      restoreStorage();
    }
  });

  it("stores public embed comments as pending until a moderator approves them", () => {
    const restoreStorage = installWindowStorage();
    const doc = buildDemoWebMapDoc();
    doc.annotations = setAnnotationPublicComments(createEmptyAnnotationWorkspace(), {
      enabled: true,
      actor: { id: "u-member", name: "Mira Chen" },
    });
    window.localStorage.setItem(
      "honua.portal.saved-map.map-style-demo",
      JSON.stringify({ doc, modified: "2026-05-10T12:00:00.000Z" }),
    );

    const publicRoot = createViewerRoot();
    const publicHandle = initMapViewer(publicRoot, {
      savedMapId: "map-style-demo",
      mode: "embed",
      embedParams: parseEmbedParams(""),
    });

    try {
      const publicPanel = getRequiredElement(publicRoot, "[data-annotation-panel]");
      const body = getRequiredElement<HTMLTextAreaElement>(publicPanel, "[data-annotation-body]");
      body.value = "Please add a crosswalk access note.";
      getButtonByText(publicPanel, "Place public comment").click();
      expect(publicPanel).toHaveTextContent("Click the map to place the public comment");

      mapControllerMock.controllers[0]?.emitMapClick({ lngLat: [-157.81234, 21.31234] });

      expect(publicPanel).toHaveTextContent("Public comment submitted for approval.");
      expect(publicPanel).not.toHaveTextContent("Please add a crosswalk access note.");
      expect(mapControllerMock.controllers[0]?.setAnnotationPins).toHaveBeenLastCalledWith([]);

      const pending = loadFixtureSavedMapForViewer("map-style-demo");
      if (pending.status !== "ok") throw new Error("fixture failed to reload");
      expect(pending.doc.annotations?.commentThreads[0]).toMatchObject({
        title: "Please add a crosswalk access note.",
        moderation: { state: "pending", submittedBy: "guest" },
        createdBy: { id: "guest", name: "Guest visitor" },
      });
    } finally {
      publicHandle.dispose();
    }

    const ownerRoot = createViewerRoot();
    const ownerHandle = initMapViewer(ownerRoot, {
      savedMapId: "map-style-demo",
      actorId: "u-member",
      actorName: "Mira Chen",
      canModerateAnnotations: true,
    });

    try {
      const ownerPanel = getRequiredElement(ownerRoot, "[data-annotation-panel]");
      expect(ownerPanel).toHaveTextContent("Please add a crosswalk access note.");
      expect(ownerPanel).toHaveTextContent("Pending approval");
      getButtonByText(ownerPanel, "Approve").click();
      expect(ownerPanel).toHaveTextContent("Comment approved.");

      const approved = loadFixtureSavedMapForViewer("map-style-demo");
      if (approved.status !== "ok") throw new Error("fixture failed to reload");
      expect(approved.doc.annotations?.commentThreads[0]).toMatchObject({
        moderation: {
          state: "approved",
          submittedBy: "guest",
          moderatedBy: { id: "u-member", name: "Mira Chen" },
        },
      });
    } finally {
      ownerHandle.dispose();
      restoreStorage();
    }
  });

  it("falls back to the persisted saved-map extent when the embed query has no valid extent", () => {
    initMapViewer(createViewerRoot(), {
      savedMapId: "map-style-demo",
      mode: "embed",
      embedParams: parseEmbedParams("extent=not-a-bounds"),
    });

    expect(mapControllerMock.createMapController).toHaveBeenCalledWith(
      expect.objectContaining({
        initialView: expect.objectContaining({
          bounds: [-157.91, 21.26, -157.77, 21.35],
        }),
      }),
    );
  });

  it("hides the style editor when a saved map has no editable targets", () => {
    const restoreStorage = installWindowStorage();
    const doc = buildDemoWebMapDoc();
    doc.operationalLayers = doc.operationalLayers.map((layer) => ({
      ...layer,
      layerType: "unsupported" as const,
      styleRef: null,
    }));
    window.localStorage.setItem(
      "honua.portal.saved-map.map-style-demo",
      JSON.stringify({ doc, modified: "2026-05-09T00:00:00.000Z" }),
    );
    const root = createViewerRoot({ styleEditor: true });
    const handle = initMapViewer(root, {
      savedMapId: "map-style-demo",
      maputnikEditorUrl: "/maputnik-test.html",
    });

    try {
      const button = getRequiredElement<HTMLButtonElement>(root, "[data-style-editor-button]");
      const panel = getRequiredElement<HTMLElement>(root, "[data-style-editor-panel]");

      expect(button.hidden).toBe(true);
      expect(button.disabled).toBe(true);
      expect(panel.hidden).toBe(true);
      button.click();
      expect(panel.hidden).toBe(true);
    } finally {
      handle.dispose();
      restoreStorage();
    }
  });

  it("starts a second same-session style edit from the latest saved override", () => {
    const restoreStorage = installWindowStorage();
    const root = createViewerRoot({ styleEditor: true });
    const handle = initMapViewer(root, {
      savedMapId: "map-style-demo",
      maputnikEditorUrl: "/maputnik-test.html",
    });
    const frame = getRequiredElement<HTMLIFrameElement>(root, "[data-maputnik-frame]");
    const bridge = installFrameBridgeWindow(frame);

    try {
      getRequiredElement<HTMLButtonElement>(root, "[data-style-editor-button]").click();
      const initialStyle = latestPostedStyle(bridge.postMessage);
      const districtEdit = cloneStyleWithPaint(initialStyle, "districts-fill", { "fill-color": "#ff3366" });

      dispatchStyleChange(frame, districtEdit);
      getRequiredElement<HTMLButtonElement>(root, "[data-style-save-button]").click();
      expect(getRequiredElement(root, "[data-style-editor-status]")).toHaveTextContent("Style saved");

      getRequiredElement<HTMLButtonElement>(root, "[data-style-editor-button]").click();
      const secondSessionStyle = latestPostedStyle(bridge.postMessage);
      expect(getLayerPaint(secondSessionStyle, "districts-fill")?.["fill-color"]).toBe("#ff3366");

      const stationEdit = cloneStyleWithPaint(secondSessionStyle, "field-stations-circles", {
        "circle-color": "#3057ff",
      });
      dispatchStyleChange(frame, stationEdit);
      getRequiredElement<HTMLButtonElement>(root, "[data-style-save-button]").click();

      const reloaded = loadFixtureSavedMapForViewer("map-style-demo");
      if (reloaded.status !== "ok") throw new Error("fixture failed to reload");
      expect(
        getLayerPaint(reloaded.viewerItem.style as unknown as Record<string, unknown>, "districts-fill")?.[
          "fill-color"
        ],
      ).toBe("#ff3366");
      expect(
        getLayerPaint(reloaded.viewerItem.style as unknown as Record<string, unknown>, "field-stations-circles")?.[
          "circle-color"
        ],
      ).toBe("#3057ff");
    } finally {
      handle.dispose();
      bridge.restore();
      restoreStorage();
    }
  });

  it("surfaces Maputnik bridge errors without creating a pending style draft", () => {
    const root = createViewerRoot({ styleEditor: true });
    const handle = initMapViewer(root, {
      savedMapId: "map-style-demo",
      maputnikEditorUrl: "/maputnik-test.html",
    });
    const frame = getRequiredElement<HTMLIFrameElement>(root, "[data-maputnik-frame]");
    const bridge = installFrameBridgeWindow(frame);

    try {
      getRequiredElement<HTMLButtonElement>(root, "[data-style-editor-button]").click();
      dispatchBridgeError(frame, "Unable to load the portal style into Maputnik");
      expect(getRequiredElement(root, "[data-style-editor-status]")).toHaveTextContent(
        "Unable to load the portal style into Maputnik",
      );

      getRequiredElement<HTMLButtonElement>(root, "[data-style-save-button]").click();
      expect(getRequiredElement(root, "[data-style-editor-status]")).toHaveTextContent("No style changes to save");
    } finally {
      handle.dispose();
      bridge.restore();
    }
  });

  it("clears an existing pending draft when the Maputnik bridge reports an error", () => {
    const restoreStorage = installWindowStorage();
    const root = createViewerRoot({ styleEditor: true });
    const handle = initMapViewer(root, {
      savedMapId: "map-style-demo",
      maputnikEditorUrl: "/maputnik-test.html",
    });
    const frame = getRequiredElement<HTMLIFrameElement>(root, "[data-maputnik-frame]");
    const bridge = installFrameBridgeWindow(frame);

    try {
      getRequiredElement<HTMLButtonElement>(root, "[data-style-editor-button]").click();
      const edited = cloneStyleWithPaint(latestPostedStyle(bridge.postMessage), "districts-fill", {
        "fill-color": "#ff3366",
      });
      dispatchStyleChange(frame, edited);
      expect(getRequiredElement(root, "[data-style-editor-status]")).toHaveTextContent("Unsaved style changes");

      dispatchBridgeError(frame, "Unable to read the edited Maputnik style");
      getRequiredElement<HTMLButtonElement>(root, "[data-style-save-button]").click();
      expect(getRequiredElement(root, "[data-style-editor-status]")).toHaveTextContent("No style changes to save");

      const reloaded = loadFixtureSavedMapForViewer("map-style-demo");
      if (reloaded.status !== "ok") throw new Error("fixture failed to reload");
      expect(reloaded.item.extensions["honua:styleEditing"]).toMatchObject({
        effectiveOrigin: "admin-layer-style",
      });
    } finally {
      handle.dispose();
      bridge.restore();
      restoreStorage();
    }
  });

  it("ignores style changes that belong to a stale Maputnik target", () => {
    const restoreStorage = installWindowStorage();
    const root = createViewerRoot({ styleEditor: true });
    const handle = initMapViewer(root, {
      savedMapId: "map-style-demo",
      maputnikEditorUrl: "/maputnik-test.html",
    });
    const frame = getRequiredElement<HTMLIFrameElement>(root, "[data-maputnik-frame]");
    const bridge = installFrameBridgeWindow(frame);

    try {
      getRequiredElement<HTMLButtonElement>(root, "[data-style-editor-button]").click();
      const staleEdit = cloneStyleWithPaint(latestPostedStyle(bridge.postMessage), "districts-fill", {
        "fill-color": "#ff3366",
      });

      const targetSelect = getRequiredElement<HTMLSelectElement>(root, "[data-style-target-select]");
      targetSelect.value = "layer:field-stations";
      targetSelect.dispatchEvent(new Event("change"));

      dispatchStyleChange(frame, staleEdit, "saved-map");
      getRequiredElement<HTMLButtonElement>(root, "[data-style-save-button]").click();
      expect(getRequiredElement(root, "[data-style-editor-status]")).toHaveTextContent("No style changes to save");

      const currentEdit = cloneStyleWithPaint(latestPostedStyle(bridge.postMessage), "field-stations-circles", {
        "circle-color": "#3057ff",
      });
      dispatchStyleChange(frame, currentEdit, "layer:field-stations");
      getRequiredElement<HTMLButtonElement>(root, "[data-style-save-button]").click();

      const reloaded = loadFixtureSavedMapForViewer("map-style-demo");
      if (reloaded.status !== "ok") throw new Error("fixture failed to reload");
      expect(
        getLayerPaint(reloaded.viewerItem.style as unknown as Record<string, unknown>, "districts-fill")?.[
          "fill-color"
        ],
      ).not.toBe("#ff3366");
      expect(
        getLayerPaint(reloaded.viewerItem.style as unknown as Record<string, unknown>, "field-stations-circles")?.[
          "circle-color"
        ],
      ).toBe("#3057ff");
    } finally {
      handle.dispose();
      bridge.restore();
      restoreStorage();
    }
  });
});

function removeStoredDemoMap(): void {
  const storage = window.localStorage as Storage | undefined;
  if (storage && typeof storage.removeItem === "function") {
    storage.removeItem("honua.portal.saved-map.map-style-demo");
    storage.removeItem(`honua.portal.saved-map.${STYLE_EDITOR_DEMO_CONTENT_ITEM_ID}`);
  }
}

function createViewerRoot(options: { styleEditor?: boolean } = {}): HTMLElement {
  const root = document.createElement("div");
  root.innerHTML = `
    <div data-metadata-grid></div>
    <ul data-layer-list></ul>
    <div data-feature-detail></div>
    <div data-annotation-panel></div>
    <div data-collaboration-panel></div>
    <div data-portal-item-title></div>
    <button data-share-url-button type="button"></button>
    <output data-map-status></output>
    <div data-map-container></div>
    <table>
      <thead data-feature-table-head></thead>
      <tbody data-feature-table-body></tbody>
    </table>
    <span data-table-layer-label></span>
    <span data-table-row-count></span>
    ${
      options.styleEditor
        ? `
          <button data-style-editor-button type="button">Edit style</button>
          <section data-style-editor-panel hidden>
            <select data-style-target-select></select>
            <span data-style-origin></span>
            <iframe data-maputnik-frame title="Self-hosted Maputnik editor"></iframe>
            <button data-style-save-button type="button">Save style</button>
            <button data-style-close-button type="button">Close style editor</button>
            <output data-style-editor-status></output>
          </section>
        `
        : ""
    }
  `;
  document.body.appendChild(root);
  return root;
}

function getRequiredElement<T extends HTMLElement>(root: ParentNode, selector: string): T {
  const element = root.querySelector<T>(selector);
  if (!element) throw new Error(`Missing required element: ${selector}`);
  return element;
}

function getButtonByText(root: ParentNode, text: string): HTMLButtonElement {
  const button = Array.from(root.querySelectorAll<HTMLButtonElement>("button")).find(
    (candidate) => candidate.textContent === text,
  );
  if (!button) throw new Error(`Missing button: ${text}`);
  return button;
}

type PostMessageMock = ReturnType<typeof vi.spyOn>;

function installWindowStorage(): () => void {
  const originalDescriptor = Object.getOwnPropertyDescriptor(window, "localStorage");
  Object.defineProperty(window, "localStorage", {
    configurable: true,
    value: new MemoryStorage(),
  });
  return () => {
    if (originalDescriptor) {
      Object.defineProperty(window, "localStorage", originalDescriptor);
    } else {
      Reflect.deleteProperty(window, "localStorage");
    }
  };
}

function installDownloadMocks(): {
  blobs: Blob[];
  clickedAnchors: Array<{ href: string; download: string }>;
  restore: () => void;
} {
  const blobs: Blob[] = [];
  const clickedAnchors: Array<{ href: string; download: string }> = [];
  const createObjectUrlDescriptor = Object.getOwnPropertyDescriptor(URL, "createObjectURL");
  const revokeObjectUrlDescriptor = Object.getOwnPropertyDescriptor(URL, "revokeObjectURL");
  const clickSpy = vi.spyOn(HTMLAnchorElement.prototype, "click").mockImplementation(function (
    this: HTMLAnchorElement,
  ) {
    clickedAnchors.push({ href: this.href, download: this.download });
  });

  Object.defineProperty(URL, "createObjectURL", {
    configurable: true,
    value: vi.fn((blob: Blob) => {
      blobs.push(blob);
      return `blob:annotation-export-${blobs.length}`;
    }),
  });
  Object.defineProperty(URL, "revokeObjectURL", {
    configurable: true,
    value: vi.fn(),
  });

  return {
    blobs,
    clickedAnchors,
    restore: () => {
      clickSpy.mockRestore();
      if (createObjectUrlDescriptor) {
        Object.defineProperty(URL, "createObjectURL", createObjectUrlDescriptor);
      } else {
        Reflect.deleteProperty(URL, "createObjectURL");
      }
      if (revokeObjectUrlDescriptor) {
        Object.defineProperty(URL, "revokeObjectURL", revokeObjectUrlDescriptor);
      } else {
        Reflect.deleteProperty(URL, "revokeObjectURL");
      }
    },
  };
}

function readBlobAsText(blob: Blob | undefined): Promise<string> {
  if (!blob) throw new Error("missing downloaded blob");
  return new Promise((resolve, reject) => {
    const reader = new FileReader();
    reader.addEventListener("load", () => resolve(String(reader.result ?? "")));
    reader.addEventListener("error", () => reject(reader.error ?? new Error("failed to read downloaded blob")));
    reader.readAsText(blob);
  });
}

function latestPostedStyle(postMessageMock: PostMessageMock): Record<string, unknown> {
  const message = postMessageMock.mock.calls.at(-1)?.[0] as { style?: Record<string, unknown> } | undefined;
  if (!message?.style) throw new Error("missing posted style");
  return message.style;
}

function installFrameBridgeWindow(frame: HTMLIFrameElement): { postMessage: PostMessageMock; restore: () => void } {
  const originalDescriptor = Object.getOwnPropertyDescriptor(frame, "contentWindow");
  const postMessageMock = vi.spyOn(window, "postMessage").mockImplementation(() => undefined);
  Object.defineProperty(frame, "contentWindow", {
    configurable: true,
    value: window,
  });
  return {
    postMessage: postMessageMock,
    restore: () => {
      postMessageMock.mockRestore();
      if (originalDescriptor) {
        Object.defineProperty(frame, "contentWindow", originalDescriptor);
      } else {
        Reflect.deleteProperty(frame, "contentWindow");
      }
    },
  };
}

function dispatchStyleChange(frame: HTMLIFrameElement, style: Record<string, unknown>, targetId = "saved-map"): void {
  if (!frame.contentWindow) throw new Error("missing iframe contentWindow");
  window.dispatchEvent(
    new MessageEvent("message", {
      data: { type: "honua:style-change", style, styleId: maputnikStyleId(targetId) },
      origin: window.location.origin,
      source: frame.contentWindow as MessageEventSource,
    }),
  );
}

function dispatchBridgeError(frame: HTMLIFrameElement, message: string, targetId = "saved-map"): void {
  if (!frame.contentWindow) throw new Error("missing iframe contentWindow");
  window.dispatchEvent(
    new MessageEvent("message", {
      data: { type: "honua:maputnik-error", message, styleId: maputnikStyleId(targetId) },
      origin: window.location.origin,
      source: frame.contentWindow as MessageEventSource,
    }),
  );
}

function maputnikStyleId(targetId: string): string {
  const targetPart = targetId
    .toLowerCase()
    .replace(/[^a-z0-9_-]+/g, "-")
    .replace(/^-+|-+$/g, "");
  return `honua-portal-style-editor-map-style-demo-${targetPart || "saved-map"}`;
}

function cloneStyleWithPaint(
  style: Record<string, unknown>,
  layerId: string,
  paint: Record<string, unknown>,
): Record<string, unknown> {
  const edited = structuredClone(style) as Record<string, unknown>;
  const layerPaint = getLayerPaint(edited, layerId);
  if (!layerPaint) throw new Error(`missing style layer: ${layerId}`);
  Object.assign(layerPaint, paint);
  return edited;
}

function getLayerPaint(style: Record<string, unknown>, layerId: string): Record<string, unknown> | undefined {
  const layers = style["layers"] as Array<{ id: string; paint?: Record<string, unknown> }>;
  const layer = layers.find((entry) => entry.id === layerId);
  return layer?.paint;
}
