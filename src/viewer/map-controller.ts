/**
 * MapLibre integration shim for the portal viewer. Owns:
 *
 *   - Style application from a `HonuaStyleSpecification`
 *   - Visibility / opacity / order updates against MapLibre layers
 *   - Click handling that resolves to (portalLayerId, featureId)
 *   - View change events that bubble up to the viewer state machine
 *
 * Honua-typed sources (feature service, OGC features, etc.) flow through
 * the SDK style spec unchanged; native MapLibre source types are passed
 * to MapLibre directly. We intentionally do not duplicate source factory
 * logic that already lives in `@honua/sdk-js/style`.
 */

import maplibregl, {
  type GeoJSONSourceSpecification,
  type LayerSpecification,
  type Map as MapLibreMap,
  type StyleSpecification,
} from "maplibre-gl";
import "maplibre-gl/dist/maplibre-gl.css";
import type { AnnotationPin } from "./annotation-state.js";
import { deriveFeatureId } from "./feature-id.js";
import { type PopupViewModel, buildPopupViewModel, renderPopupHtml } from "./popup.js";
import type { PortalViewerSdkFeatureLoader } from "./sdk-feature-loader.js";
import type {
  PortalGeoJsonFeature,
  PortalGeoJsonGeometry,
  PortalViewerInitialView,
  PortalViewerItem,
  PortalViewerLayer,
} from "./types.js";

export interface MapControllerOptions {
  container: HTMLElement;
  item: PortalViewerItem;
  initialView: PortalViewerInitialView;
  /** Whether to show the built-in zoom/navigation controls. */
  showZoomControls?: boolean;
  /**
   * Resolve a clicked feature's positional index in its source so id-less
   * features share the same id derivation as the table/detail paths.
   * When omitted, the controller falls back to index 0.
   */
  resolveFeatureIndex?: (layer: PortalViewerLayer, feature: PortalGeoJsonFeature) => number;
  /** Load SDK-backed protocol sources and hydrate the matching MapLibre GeoJSON source. */
  loadSdkSource?: PortalViewerSdkFeatureLoader;
  /** Notify viewer chrome that a source now has queryable feature rows. */
  onSourceFeatures?: (sourceId: string, features: readonly PortalGeoJsonFeature[]) => void;
}

export interface FeatureClickEvent {
  layerId: string;
  featureId: string;
  popup: PopupViewModel;
  lngLat: [number, number];
}

export interface ViewChangeEvent {
  center: [number, number];
  zoom: number;
}

export interface MapClickEvent {
  lngLat: [number, number];
}

export interface MapPointerEvent {
  lngLat: [number, number];
}

export interface CollaborationCursor {
  participantId: string;
  name: string;
  color: string;
  lngLat: [number, number];
}

export interface AnnotationPinClickEvent {
  pinId: string;
  threadId: string;
  lngLat: [number, number];
}

export interface FreehandDrawEvent {
  phase: "start" | "move" | "end";
  lngLat: [number, number];
}

export type AnnotationShapeGeometry = Extract<PortalGeoJsonGeometry, { type: "LineString" | "Polygon" }>;

export interface AnnotationShape {
  id: string;
  threadId?: string;
  title?: string;
  status?: AnnotationPin["status"];
  geometry: AnnotationShapeGeometry;
}

export interface MapController {
  ready: Promise<void>;
  setLayerVisibility: (layer: PortalViewerLayer, visible: boolean) => void;
  setLayerOpacity: (layer: PortalViewerLayer, opacity: number) => void;
  applyLayerOrder: (item: PortalViewerItem, layerOrder: readonly string[]) => void;
  setCollaborationCursors: (cursors: readonly CollaborationCursor[]) => void;
  setAnnotationPins: (pins: readonly AnnotationPin[]) => void;
  setAnnotationShapes: (shapes: readonly AnnotationShape[]) => void;
  setFreehandDrawingEnabled: (enabled: boolean) => void;
  flyTo: (center: [number, number], zoom: number) => void;
  showPopup: (event: FeatureClickEvent) => void;
  closePopup: () => void;
  onFeatureClick: (handler: (event: FeatureClickEvent) => void) => void;
  onMapClick: (handler: (event: MapClickEvent) => void) => void;
  onMapPointerMove: (handler: (event: MapPointerEvent) => void) => void;
  onAnnotationPinClick: (handler: (event: AnnotationPinClickEvent) => void) => void;
  onFreehandDraw: (handler: (event: FreehandDrawEvent) => void) => void;
  onViewChange: (handler: (event: ViewChangeEvent) => void) => void;
  onError: (handler: (message: string) => void) => void;
  destroy: () => void;
}

export function createMapController(options: MapControllerOptions): MapController {
  const featureClickHandlers: Array<(event: FeatureClickEvent) => void> = [];
  const mapClickHandlers: Array<(event: MapClickEvent) => void> = [];
  const mapPointerMoveHandlers: Array<(event: MapPointerEvent) => void> = [];
  const annotationPinClickHandlers: Array<(event: AnnotationPinClickEvent) => void> = [];
  const freehandDrawHandlers: Array<(event: FreehandDrawEvent) => void> = [];
  const viewChangeHandlers: Array<(event: ViewChangeEvent) => void> = [];
  const errorHandlers: Array<(message: string) => void> = [];
  let annotationPins: readonly AnnotationPin[] = [];
  let annotationShapes: readonly AnnotationShape[] = [];
  let collaborationCursors: readonly CollaborationCursor[] = [];
  let annotationPinLayerReady = false;
  let annotationShapeLayerReady = false;
  let collaborationCursorLayerReady = false;
  let freehandDrawingEnabled = false;
  let freehandDrawingActive = false;

  const map = new maplibregl.Map({
    container: options.container,
    style: options.item.style as unknown as StyleSpecification,
    center: options.initialView.center,
    zoom: options.initialView.zoom,
    attributionControl: { compact: true },
  });

  if (options.showZoomControls !== false) {
    map.addControl(new maplibregl.NavigationControl({ visualizePitch: false }), "top-right");
  }
  map.addControl(new maplibregl.ScaleControl({ unit: "metric" }), "bottom-left");

  let activePopup: maplibregl.Popup | null = null;

  const ready = new Promise<void>((resolve, reject) => {
    let isLoaded = false;
    let isSettled = false;

    const resolveReady = () => {
      isLoaded = true;
      isSettled = true;
      resolve();
    };

    const rejectReady = (error: unknown) => {
      if (isSettled) return;
      isSettled = true;
      reject(error);
    };

    let didHandleLoad = false;
    const onStyleReady = () => {
      if (didHandleLoad) return;
      didHandleLoad = true;
      void handleLoad();
    };

    const handleLoad = async () => {
      try {
        if (options.initialView.bounds) {
          const [w, s, e, n] = options.initialView.bounds;
          map.fitBounds(
            [
              [w, s],
              [e, n],
            ],
            { padding: 40, duration: 0, maxZoom: 14 },
          );
        }

        await hydrateSdkSources(map, options);
        ensureAnnotationShapeLayers(map, annotationShapes);
        annotationShapeLayerReady = true;
        ensureAnnotationPinLayer(map, annotationPins);
        annotationPinLayerReady = true;
        ensureCollaborationCursorLayers(map, collaborationCursors);
        collaborationCursorLayerReady = true;
        map.on("click", ANNOTATION_LAYER_ID, (event) => {
          const feature = event.features?.[0];
          const properties = feature?.properties as Record<string, unknown> | undefined;
          const pinId = properties?.["pinId"];
          const threadId = properties?.["threadId"];
          if (typeof pinId !== "string" || typeof threadId !== "string") return;
          for (const handler of annotationPinClickHandlers) {
            handler({ pinId, threadId, lngLat: [event.lngLat.lng, event.lngLat.lat] });
          }
        });

        for (const layer of options.item.layers) {
          applyVisibility(map, layer, layer.defaultVisible);
          applyOpacity(map, layer, layer.defaultOpacity);

          const interactiveId = layer.interactiveLayerId ?? layer.renderLayerIds[0];
          if (interactiveId) {
            map.on("mouseenter", interactiveId, () => {
              map.getCanvas().style.cursor = "pointer";
            });
            map.on("mouseleave", interactiveId, () => {
              map.getCanvas().style.cursor = "";
            });
            map.on("click", interactiveId, (event) => {
              const features = event.features ?? [];
              if (features.length === 0) return;
              const feature = features[0];
              const portalFeature: PortalGeoJsonFeature = {
                type: "Feature",
                id: feature.id,
                properties: (feature.properties as Record<string, unknown>) ?? {},
                geometry: feature.geometry as never,
              };
              const sourceIndex = options.resolveFeatureIndex?.(layer, portalFeature) ?? 0;
              const featureId = deriveFeatureId(layer.id, portalFeature, sourceIndex);
              const popupModel = buildPopupViewModel(layer, portalFeature, sourceIndex);
              const lngLat: [number, number] = [event.lngLat.lng, event.lngLat.lat];
              const clickEvent: FeatureClickEvent = {
                layerId: layer.id,
                featureId,
                popup: popupModel,
                lngLat,
              };
              for (const handler of featureClickHandlers) handler(clickEvent);
            });
          }
        }

        map.on("moveend", () => {
          const center = map.getCenter();
          const event: ViewChangeEvent = {
            center: [center.lng, center.lat],
            zoom: map.getZoom(),
          };
          for (const handler of viewChangeHandlers) handler(event);
        });

        map.on("click", (event) => {
          for (const handler of mapClickHandlers) {
            handler({ lngLat: [event.lngLat.lng, event.lngLat.lat] });
          }
        });

        map.on("mousedown", (event) => {
          if (!freehandDrawingEnabled) return;
          freehandDrawingActive = true;
          setDragPanEnabled(map, false);
          emitFreehandDraw("start", [event.lngLat.lng, event.lngLat.lat]);
        });

        map.on("mousemove", (event) => {
          const lngLat: [number, number] = [event.lngLat.lng, event.lngLat.lat];
          for (const handler of mapPointerMoveHandlers) {
            handler({ lngLat });
          }
          if (!freehandDrawingEnabled || !freehandDrawingActive) return;
          emitFreehandDraw("move", lngLat);
        });

        map.on("mouseup", (event) => {
          if (!freehandDrawingEnabled || !freehandDrawingActive) return;
          freehandDrawingActive = false;
          emitFreehandDraw("end", [event.lngLat.lng, event.lngLat.lat]);
          setDragPanEnabled(map, true);
        });

        resolveReady();
      } catch (error) {
        rejectReady(error);
      }
    };

    const onError = (event: { error?: { message?: string } }) => {
      const message = event.error?.message ?? "Unknown map error";
      for (const handler of errorHandlers) handler(message);
      if (!isLoaded) {
        rejectReady(new Error(message));
      }
    };

    map.on("style.load", onStyleReady);
    map.on("load", onStyleReady);
    map.on("error", onError);
  });

  function setLayerVisibility(layer: PortalViewerLayer, visible: boolean): void {
    applyVisibility(map, layer, visible);
  }

  function setLayerOpacity(layer: PortalViewerLayer, opacity: number): void {
    applyOpacity(map, layer, opacity);
  }

  function applyLayerOrder(item: PortalViewerItem, layerOrder: readonly string[]): void {
    // MapLibre paint order is insertion order; "before" of undefined puts the
    // moved layer on top. Apply in render order so the topmost group ends up last.
    for (const layerId of layerOrder) {
      const portalLayer = item.layers.find((entry) => entry.id === layerId);
      if (!portalLayer) continue;
      for (const renderLayerId of portalLayer.renderLayerIds) {
        if (map.getLayer(renderLayerId)) {
          map.moveLayer(renderLayerId);
        }
      }
    }
  }

  function setAnnotationPins(pins: readonly AnnotationPin[]): void {
    annotationPins = [...pins];
    if (!annotationPinLayerReady) return;
    updateAnnotationPinSource(map, annotationPins);
  }

  function setCollaborationCursors(cursors: readonly CollaborationCursor[]): void {
    collaborationCursors = [...cursors];
    if (!collaborationCursorLayerReady) return;
    updateCollaborationCursorSource(map, collaborationCursors);
    syncCollaborationCursorMarkers(map, collaborationCursors);
  }

  function setAnnotationShapes(shapes: readonly AnnotationShape[]): void {
    annotationShapes = [...shapes];
    if (!annotationShapeLayerReady) return;
    updateAnnotationShapeSource(map, annotationShapes);
  }

  function setFreehandDrawingEnabled(enabled: boolean): void {
    freehandDrawingEnabled = enabled;
    if (!enabled) {
      freehandDrawingActive = false;
      setDragPanEnabled(map, true);
    }
  }

  function flyTo(center: [number, number], zoom: number): void {
    map.flyTo({ center, zoom, duration: 0 });
  }

  function showPopup(event: FeatureClickEvent): void {
    if (activePopup) activePopup.remove();
    activePopup = new maplibregl.Popup({ closeOnClick: true, maxWidth: "300px" })
      .setLngLat(event.lngLat)
      .setHTML(renderPopupHtml(event.popup))
      .addTo(map);
  }

  function closePopup(): void {
    if (activePopup) {
      activePopup.remove();
      activePopup = null;
    }
  }

  return {
    ready,
    setLayerVisibility,
    setLayerOpacity,
    applyLayerOrder,
    setCollaborationCursors,
    setAnnotationPins,
    setAnnotationShapes,
    setFreehandDrawingEnabled,
    flyTo,
    showPopup,
    closePopup,
    onFeatureClick: (handler) => featureClickHandlers.push(handler),
    onMapClick: (handler) => mapClickHandlers.push(handler),
    onMapPointerMove: (handler) => mapPointerMoveHandlers.push(handler),
    onAnnotationPinClick: (handler) => annotationPinClickHandlers.push(handler),
    onFreehandDraw: (handler) => freehandDrawHandlers.push(handler),
    onViewChange: (handler) => viewChangeHandlers.push(handler),
    onError: (handler) => errorHandlers.push(handler),
    destroy: () => {
      closePopup();
      clearCollaborationCursorMarkers(map);
      map.remove();
    },
  };

  function emitFreehandDraw(phase: FreehandDrawEvent["phase"], lngLat: [number, number]): void {
    for (const handler of freehandDrawHandlers) handler({ phase, lngLat });
  }
}

const ANNOTATION_SOURCE_ID = "honua-annotation-pins";
const ANNOTATION_LAYER_ID = "honua-annotation-pins-circle";
const ANNOTATION_SHAPE_SOURCE_ID = "honua-annotation-shapes";
const ANNOTATION_SHAPE_FILL_LAYER_ID = "honua-annotation-shapes-fill";
const ANNOTATION_SHAPE_OUTLINE_LAYER_ID = "honua-annotation-shapes-outline";
const ANNOTATION_SHAPE_LINE_LAYER_ID = "honua-annotation-shapes-line";
const COLLABORATION_CURSOR_SOURCE_ID = "honua-collaboration-cursors";
const COLLABORATION_CURSOR_DOT_LAYER_ID = "honua-collaboration-cursors-dot";
const collaborationCursorMarkersByMap = new WeakMap<MapLibreMap, maplibregl.Marker[]>();

function setDragPanEnabled(map: MapLibreMap, enabled: boolean): void {
  const dragPan = map.dragPan as { enable?: () => void; disable?: () => void } | undefined;
  if (enabled) {
    dragPan?.enable?.();
  } else {
    dragPan?.disable?.();
  }
}

interface AnnotationPinFeatureCollection {
  type: "FeatureCollection";
  features: AnnotationPinFeature[];
}

interface AnnotationPinFeature {
  type: "Feature";
  id: string;
  properties: {
    pinId: string;
    threadId: string;
    title: string;
    status: AnnotationPin["status"];
    anchorKind: AnnotationPin["anchorKind"];
  };
  geometry: { type: "Point"; coordinates: [number, number] };
}

function ensureAnnotationPinLayer(map: MapLibreMap, pins: readonly AnnotationPin[]): void {
  if (!map.getSource(ANNOTATION_SOURCE_ID)) {
    map.addSource(ANNOTATION_SOURCE_ID, {
      type: "geojson",
      data: annotationPinsToFeatureCollection(pins),
    } satisfies GeoJSONSourceSpecification);
  } else {
    updateAnnotationPinSource(map, pins);
  }

  if (!map.getLayer(ANNOTATION_LAYER_ID)) {
    map.addLayer({
      id: ANNOTATION_LAYER_ID,
      type: "circle",
      source: ANNOTATION_SOURCE_ID,
      paint: {
        "circle-radius": ["case", ["==", ["get", "status"], "resolved"], 5, 8],
        "circle-color": ["case", ["==", ["get", "anchorKind"], "feature"], "#7aa7ff", "#f3b562"],
        "circle-opacity": ["case", ["==", ["get", "status"], "resolved"], 0.55, 0.95],
        "circle-stroke-color": "#0e1822",
        "circle-stroke-width": 2,
      },
    } satisfies LayerSpecification);
  }

  map.on("mouseenter", ANNOTATION_LAYER_ID, () => {
    map.getCanvas().style.cursor = "pointer";
  });
  map.on("mouseleave", ANNOTATION_LAYER_ID, () => {
    map.getCanvas().style.cursor = "";
  });
}

function updateAnnotationPinSource(map: MapLibreMap, pins: readonly AnnotationPin[]): void {
  const source = map.getSource(ANNOTATION_SOURCE_ID) as
    | { setData?: (data: AnnotationPinFeatureCollection) => void }
    | undefined;
  source?.setData?.(annotationPinsToFeatureCollection(pins));
}

function annotationPinsToFeatureCollection(pins: readonly AnnotationPin[]): AnnotationPinFeatureCollection {
  return {
    type: "FeatureCollection",
    features: pins.map((pin) => ({
      type: "Feature",
      id: pin.id,
      properties: {
        pinId: pin.id,
        threadId: pin.threadId,
        title: pin.title,
        status: pin.status,
        anchorKind: pin.anchorKind,
      },
      geometry: { type: "Point", coordinates: pin.lngLat },
    })),
  };
}

interface CollaborationCursorFeatureCollection {
  type: "FeatureCollection";
  features: CollaborationCursorFeature[];
}

interface CollaborationCursorFeature {
  type: "Feature";
  id: string;
  properties: {
    participantId: string;
    name: string;
    color: string;
  };
  geometry: { type: "Point"; coordinates: [number, number] };
}

function ensureCollaborationCursorLayers(map: MapLibreMap, cursors: readonly CollaborationCursor[]): void {
  if (!map.getSource(COLLABORATION_CURSOR_SOURCE_ID)) {
    map.addSource(COLLABORATION_CURSOR_SOURCE_ID, {
      type: "geojson",
      data: collaborationCursorsToFeatureCollection(cursors),
    } satisfies GeoJSONSourceSpecification);
  } else {
    updateCollaborationCursorSource(map, cursors);
  }

  if (!map.getLayer(COLLABORATION_CURSOR_DOT_LAYER_ID)) {
    map.addLayer({
      id: COLLABORATION_CURSOR_DOT_LAYER_ID,
      type: "circle",
      source: COLLABORATION_CURSOR_SOURCE_ID,
      paint: {
        "circle-radius": 7,
        "circle-color": ["coalesce", ["get", "color"], "#4ec9b0"],
        "circle-opacity": 0.95,
        "circle-stroke-color": "#0e1822",
        "circle-stroke-width": 2,
      },
    } satisfies LayerSpecification);
  }
  syncCollaborationCursorMarkers(map, cursors);
}

function updateCollaborationCursorSource(map: MapLibreMap, cursors: readonly CollaborationCursor[]): void {
  const source = map.getSource(COLLABORATION_CURSOR_SOURCE_ID) as
    | { setData?: (data: CollaborationCursorFeatureCollection) => void }
    | undefined;
  source?.setData?.(collaborationCursorsToFeatureCollection(cursors));
}

function syncCollaborationCursorMarkers(map: MapLibreMap, cursors: readonly CollaborationCursor[]): void {
  clearCollaborationCursorMarkers(map);
  const markers = cursors.map((cursor) =>
    new maplibregl.Marker({
      element: renderCollaborationCursorMarker(cursor),
      anchor: "bottom-left",
    })
      .setLngLat(cursor.lngLat)
      .addTo(map),
  );
  collaborationCursorMarkersByMap.set(map, markers);
}

function clearCollaborationCursorMarkers(map: MapLibreMap): void {
  for (const marker of collaborationCursorMarkersByMap.get(map) ?? []) marker.remove();
  collaborationCursorMarkersByMap.delete(map);
}

function renderCollaborationCursorMarker(cursor: CollaborationCursor): HTMLElement {
  const marker = document.createElement("div");
  marker.className = "collaboration-map-cursor";
  marker.dataset["collaborationCursor"] = cursor.participantId;
  marker.style.color = cursor.color;

  const pointer = document.createElement("span");
  pointer.className = "collaboration-map-cursor__pointer";

  const label = document.createElement("span");
  label.className = "collaboration-map-cursor__label";
  label.textContent = cursor.name;

  marker.append(pointer, label);
  return marker;
}

function collaborationCursorsToFeatureCollection(
  cursors: readonly CollaborationCursor[],
): CollaborationCursorFeatureCollection {
  return {
    type: "FeatureCollection",
    features: cursors.map((cursor) => ({
      type: "Feature",
      id: cursor.participantId,
      properties: {
        participantId: cursor.participantId,
        name: cursor.name,
        color: cursor.color,
      },
      geometry: { type: "Point", coordinates: cursor.lngLat },
    })),
  };
}

interface AnnotationShapeFeatureCollection {
  type: "FeatureCollection";
  features: AnnotationShapeFeature[];
}

interface AnnotationShapeFeature {
  type: "Feature";
  id: string;
  properties: {
    shapeId: string;
    threadId?: string;
    title?: string;
    status?: AnnotationPin["status"];
    geometryType: AnnotationShapeGeometry["type"];
  };
  geometry: AnnotationShapeGeometry;
}

function ensureAnnotationShapeLayers(map: MapLibreMap, shapes: readonly AnnotationShape[]): void {
  if (!map.getSource(ANNOTATION_SHAPE_SOURCE_ID)) {
    map.addSource(ANNOTATION_SHAPE_SOURCE_ID, {
      type: "geojson",
      data: annotationShapesToFeatureCollection(shapes),
    } satisfies GeoJSONSourceSpecification);
  } else {
    updateAnnotationShapeSource(map, shapes);
  }

  if (!map.getLayer(ANNOTATION_SHAPE_FILL_LAYER_ID)) {
    map.addLayer({
      id: ANNOTATION_SHAPE_FILL_LAYER_ID,
      type: "fill",
      source: ANNOTATION_SHAPE_SOURCE_ID,
      filter: ["==", ["geometry-type"], "Polygon"],
      paint: {
        "fill-color": "#3b82f6",
        "fill-opacity": ["case", ["==", ["get", "status"], "resolved"], 0.12, 0.22],
      },
    } satisfies LayerSpecification);
  }

  if (!map.getLayer(ANNOTATION_SHAPE_OUTLINE_LAYER_ID)) {
    map.addLayer({
      id: ANNOTATION_SHAPE_OUTLINE_LAYER_ID,
      type: "line",
      source: ANNOTATION_SHAPE_SOURCE_ID,
      filter: ["==", ["geometry-type"], "Polygon"],
      paint: {
        "line-color": "#1d4ed8",
        "line-opacity": ["case", ["==", ["get", "status"], "resolved"], 0.5, 0.9],
        "line-width": 2,
      },
    } satisfies LayerSpecification);
  }

  if (!map.getLayer(ANNOTATION_SHAPE_LINE_LAYER_ID)) {
    map.addLayer({
      id: ANNOTATION_SHAPE_LINE_LAYER_ID,
      type: "line",
      source: ANNOTATION_SHAPE_SOURCE_ID,
      filter: ["==", ["geometry-type"], "LineString"],
      paint: {
        "line-color": "#1d4ed8",
        "line-opacity": ["case", ["==", ["get", "status"], "resolved"], 0.5, 0.9],
        "line-width": 3,
      },
    } satisfies LayerSpecification);
  }
}

function updateAnnotationShapeSource(map: MapLibreMap, shapes: readonly AnnotationShape[]): void {
  const source = map.getSource(ANNOTATION_SHAPE_SOURCE_ID) as
    | { setData?: (data: AnnotationShapeFeatureCollection) => void }
    | undefined;
  source?.setData?.(annotationShapesToFeatureCollection(shapes));
}

function annotationShapesToFeatureCollection(shapes: readonly AnnotationShape[]): AnnotationShapeFeatureCollection {
  return {
    type: "FeatureCollection",
    features: shapes.map((shape) => {
      const properties: AnnotationShapeFeature["properties"] = {
        shapeId: shape.id,
        geometryType: shape.geometry.type,
      };
      if (shape.threadId) properties.threadId = shape.threadId;
      if (shape.title) properties.title = shape.title;
      if (shape.status) properties.status = shape.status;
      return {
        type: "Feature",
        id: shape.id,
        properties,
        geometry: shape.geometry,
      };
    }),
  };
}

function applyVisibility(map: MapLibreMap, layer: PortalViewerLayer, visible: boolean): void {
  for (const renderLayerId of layer.renderLayerIds) {
    if (!map.getLayer(renderLayerId)) continue;
    map.setLayoutProperty(renderLayerId, "visibility", visible ? "visible" : "none");
  }
}

function applyOpacity(map: MapLibreMap, layer: PortalViewerLayer, opacity: number): void {
  const clamped = Math.max(0, Math.min(1, opacity));
  for (const renderLayerId of layer.renderLayerIds) {
    const mapLayer = map.getLayer(renderLayerId);
    if (!mapLayer) continue;
    const opacityProperty = OPACITY_PROPERTY_BY_TYPE[mapLayer.type as keyof typeof OPACITY_PROPERTY_BY_TYPE];
    if (!opacityProperty) continue;
    map.setPaintProperty(renderLayerId, opacityProperty, clamped);
  }
}

const OPACITY_PROPERTY_BY_TYPE = {
  fill: "fill-opacity",
  line: "line-opacity",
  circle: "circle-opacity",
  symbol: "icon-opacity",
  raster: "raster-opacity",
  "fill-extrusion": "fill-extrusion-opacity",
  background: "background-opacity",
  heatmap: "heatmap-opacity",
} as const;

interface PortalGeoJsonFeatureCollection {
  type: "FeatureCollection";
  features: readonly PortalGeoJsonFeature[];
}

async function hydrateSdkSources(map: MapLibreMap, options: MapControllerOptions): Promise<void> {
  if (!options.loadSdkSource) return;

  const sources = new Map<string, PortalViewerLayer["sdkSource"]>();
  for (const layer of options.item.layers) {
    if (layer.sdkSource) sources.set(layer.sdkSource.sourceId, layer.sdkSource);
  }

  for (const sdkSource of sources.values()) {
    if (!sdkSource) continue;
    const features = await options.loadSdkSource(sdkSource);
    const featureCollection: PortalGeoJsonFeatureCollection = {
      type: "FeatureCollection",
      features,
    };
    setGeoJsonSourceData(map, sdkSource.sourceId, featureCollection);
    updateItemSourceData(options.item, sdkSource.sourceId, featureCollection);
    options.onSourceFeatures?.(sdkSource.sourceId, features);
  }
}

function setGeoJsonSourceData(
  map: MapLibreMap,
  sourceId: string,
  featureCollection: PortalGeoJsonFeatureCollection,
): void {
  const source = map.getSource(sourceId) as { setData?: (data: PortalGeoJsonFeatureCollection) => void } | undefined;
  if (typeof source?.setData !== "function") {
    throw new Error(`Map source "${sourceId}" is not ready for SDK feature data`);
  }
  source.setData(featureCollection);
}

function updateItemSourceData(
  item: PortalViewerItem,
  sourceId: string,
  featureCollection: PortalGeoJsonFeatureCollection,
): void {
  const source = item.style.sources[sourceId];
  if (!source || typeof source !== "object" || (source as { type?: string }).type !== "geojson") return;
  (source as { data?: PortalGeoJsonFeatureCollection }).data = featureCollection;
}
