/**
 * Pure mapping between a viewer's in-memory state and a durable `WebMapDoc`.
 *
 * Functions here are the canonical contract between #13 (viewer) and the
 * saved-map storage path. They are pure — no I/O, no globals — so they are
 * safe to call from server-side render, client-side hydration, or test code.
 */

import {
  type BaseMap,
  type InitialState,
  type OperationalLayer,
  type ViewerBaseMapState,
  type ViewerExtentState,
  type ViewerLayerState,
  type ViewerState,
  WEBMAP_DOC_VERSION,
  type WebMapDoc,
} from "./types.js";

const PORTAL_AUTHORING_APP = "honua-portal";

function stripUndefined<T extends object>(obj: T): T {
  const out: Record<string, unknown> = {};
  for (const [k, v] of Object.entries(obj as Record<string, unknown>)) {
    if (v !== undefined) out[k] = v;
  }
  return out as T;
}

function viewerLayerToOperationalLayer(layer: ViewerLayerState): OperationalLayer {
  return stripUndefined({
    id: layer.id,
    title: layer.title,
    layerType: layer.layerType,
    sourceRef: { ...layer.sourceRef },
    styleRef: layer.styleRef ? cloneStyleRef(layer.styleRef) : null,
    visibility: layer.visibility,
    opacity: layer.opacity,
    popupInfo: layer.popupInfo ? structuredCloneSafe(layer.popupInfo) : null,
    minScale: layer.minScale ?? null,
    maxScale: layer.maxScale ?? null,
  });
}

function viewerBaseMapToBaseMap(state: ViewerBaseMapState): BaseMap {
  return {
    title: state.title,
    baseMapLayers: state.baseMapLayers.map((l) => ({
      ...l,
      sourceRef: l.sourceRef ? { ...l.sourceRef } : null,
    })),
  };
}

function viewerExtentToInitialState(extent: ViewerExtentState): InitialState {
  return {
    viewpoint: {
      extent: {
        xmin: extent.xmin,
        ymin: extent.ymin,
        xmax: extent.xmax,
        ymax: extent.ymax,
      },
      rotation: extent.rotation ?? 0,
    },
  };
}

function cloneStyleRef(ref: NonNullable<ViewerLayerState["styleRef"]>) {
  return {
    ...(ref.itemId ? { itemId: ref.itemId } : {}),
    ...(ref.inline ? { inline: structuredCloneSafe(ref.inline) } : {}),
    ...(ref.origin ? { origin: ref.origin } : {}),
  };
}

function structuredCloneSafe<T>(value: T): T {
  if (typeof structuredClone === "function") return structuredClone(value);
  return JSON.parse(JSON.stringify(value)) as T;
}

/** Serialize the viewer's in-memory state into a durable WebMapDoc. */
export function viewerStateToWebMapDoc(state: ViewerState): WebMapDoc {
  return stripUndefined({
    version: WEBMAP_DOC_VERSION,
    authoringApp: PORTAL_AUTHORING_APP,
    operationalLayers: state.operationalLayers.map(viewerLayerToOperationalLayer),
    baseMap: viewerBaseMapToBaseMap(state.baseMap),
    initialState: viewerExtentToInitialState(state.extent),
    annotations: state.annotations ? structuredCloneSafe(state.annotations) : undefined,
  });
}

/**
 * Hydrate a viewer state from a durable WebMapDoc.
 *
 * Pure function — does NOT resolve `sourceRef`/`styleRef` against a catalog.
 * The viewer (#13) is responsible for fetching layer/style content; this
 * serializer guarantees the references are well-formed and ordered.
 */
export function webMapDocToViewerState(doc: WebMapDoc): ViewerState {
  return {
    operationalLayers: doc.operationalLayers.map((l) => ({
      id: l.id,
      title: l.title,
      layerType: l.layerType,
      sourceRef: { ...l.sourceRef },
      styleRef: l.styleRef ? cloneStyleRef(l.styleRef) : null,
      visibility: l.visibility,
      opacity: l.opacity,
      popupInfo: l.popupInfo ? structuredCloneSafe(l.popupInfo) : null,
      minScale: l.minScale ?? null,
      maxScale: l.maxScale ?? null,
    })),
    baseMap: {
      title: doc.baseMap.title,
      baseMapLayers: doc.baseMap.baseMapLayers.map((l) => ({
        ...l,
        sourceRef: l.sourceRef ? { ...l.sourceRef } : null,
      })),
    },
    extent: {
      xmin: doc.initialState.viewpoint.extent.xmin,
      ymin: doc.initialState.viewpoint.extent.ymin,
      xmax: doc.initialState.viewpoint.extent.xmax,
      ymax: doc.initialState.viewpoint.extent.ymax,
      rotation: doc.initialState.viewpoint.rotation ?? 0,
    },
    ...(doc.annotations ? { annotations: structuredCloneSafe(doc.annotations) } : {}),
  };
}

/**
 * Deep-clone a WebMapDoc and regenerate operational-layer ids. Used by
 * duplicate flows so the copy's layer ids do not collide with the original
 * if both are viewed in the same session.
 */
export function cloneWebMapDoc(doc: WebMapDoc, options: { layerIdFactory?: () => string } = {}): WebMapDoc {
  const factory = options.layerIdFactory ?? defaultLayerIdFactory();
  const cloned = structuredCloneSafe(doc);
  cloned.operationalLayers = cloned.operationalLayers.map((layer) => ({
    ...layer,
    id: factory(),
  }));
  return cloned;
}

function defaultLayerIdFactory(): () => string {
  let counter = 0;
  return () => {
    counter += 1;
    const suffix = Math.random().toString(36).slice(2, 8);
    return `layer-${Date.now().toString(36)}-${counter}-${suffix}`;
  };
}
