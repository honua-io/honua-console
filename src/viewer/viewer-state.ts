/**
 * Pure viewer state operations. The DOM, MapLibre, and URL hash watchers
 * all consume `ViewerState` and reply with one of the operations defined
 * here. Keeping these pure lets the state changes be unit tested without
 * a browser, and gives the URL state writer a single source of truth to
 * subscribe to.
 */

import type { PortalViewerItem, PortalViewerLayer, SelectedFeature, ViewerState } from "./types.js";

export function buildInitialState(item: PortalViewerItem): ViewerState {
  return {
    center: [...item.initialView.center] as [number, number],
    zoom: item.initialView.zoom,
    visibleLayerIds: item.layers.filter((layer) => layer.defaultVisible).map((layer) => layer.id),
    selected: undefined,
  };
}

export function deriveLayerOrder(
  defaultLayerOrder: readonly string[],
  restoredVisibleLayerIds?: readonly string[],
): string[] {
  const defaultOrder = [...defaultLayerOrder];
  if (!restoredVisibleLayerIds) return defaultOrder;

  const knownLayerIds = new Set(defaultOrder);
  const seenLayerIds = new Set<string>();
  const restoredOrder: string[] = [];

  for (const layerId of restoredVisibleLayerIds) {
    if (!knownLayerIds.has(layerId) || seenLayerIds.has(layerId)) continue;
    restoredOrder.push(layerId);
    seenLayerIds.add(layerId);
  }

  if (restoredOrder.length === 0) return defaultOrder;
  return [...restoredOrder, ...defaultOrder.filter((layerId) => !seenLayerIds.has(layerId))];
}

export function setView(state: ViewerState, center: [number, number], zoom: number): ViewerState {
  if (state.center[0] === center[0] && state.center[1] === center[1] && state.zoom === zoom) {
    return state;
  }
  return { ...state, center: [center[0], center[1]], zoom };
}

export function setLayerVisibility(
  state: ViewerState,
  layerOrder: readonly string[],
  layerId: string,
  visible: boolean,
): ViewerState {
  const isCurrentlyVisible = state.visibleLayerIds.includes(layerId);
  if (isCurrentlyVisible === visible) return state;

  const visibleSet = new Set(state.visibleLayerIds);
  if (visible) {
    visibleSet.add(layerId);
  } else {
    visibleSet.delete(layerId);
  }

  return {
    ...state,
    visibleLayerIds: layerOrder.filter((id) => visibleSet.has(id)),
    selected: state.selected?.layerId === layerId && !visible ? undefined : state.selected,
  };
}

export function reorderLayer(
  state: ViewerState,
  layerOrder: string[],
  layerId: string,
  direction: "up" | "down",
): { state: ViewerState; layerOrder: string[] } {
  const index = layerOrder.indexOf(layerId);
  if (index === -1) return { state, layerOrder };
  const swapWith = direction === "up" ? index + 1 : index - 1;
  if (swapWith < 0 || swapWith >= layerOrder.length) return { state, layerOrder };

  const nextOrder = [...layerOrder];
  [nextOrder[index], nextOrder[swapWith]] = [nextOrder[swapWith], nextOrder[index]];

  const visibleSet = new Set(state.visibleLayerIds);
  return {
    state: { ...state, visibleLayerIds: nextOrder.filter((id) => visibleSet.has(id)) },
    layerOrder: nextOrder,
  };
}

export function selectFeature(state: ViewerState, selection: SelectedFeature | undefined): ViewerState {
  if (!selection) {
    if (!state.selected) return state;
    return { ...state, selected: undefined };
  }
  if (
    state.selected &&
    state.selected.layerId === selection.layerId &&
    state.selected.featureId === selection.featureId
  ) {
    return state;
  }
  return { ...state, selected: selection };
}

export function findLayer(item: PortalViewerItem, layerId: string): PortalViewerLayer | undefined {
  return item.layers.find((layer) => layer.id === layerId);
}

export function isLayerVisible(state: ViewerState, layerId: string): boolean {
  return state.visibleLayerIds.includes(layerId);
}
