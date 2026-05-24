/**
 * Mounts the portal map viewer inside a host element supplied by the
 * Maps route. The route renders the static scaffolding (header, sidebar,
 * map shell, table) with `data-mv-*` markers; this function reads those
 * markers, wires the map controller, viewer state, and URL hash sync,
 * and returns a `dispose` handle the route runs from `useEffect` cleanup.
 */

import { loadPortalItem } from "../catalog/portal-item-loader.js";
import { getSampleSourceFeatures } from "../catalog/sample-portal-item.js";
import {
  type CollaborationActor,
  type CollaborationSession,
  type CollaborationSnapshot,
  type FeatureRef,
  createFixtureCollaborationSession,
} from "../collaboration/index.js";
import { type EmbedExtent, type EmbedRouteParams, resolveEffectiveExtent } from "../embed/route.js";
import {
  DEFAULT_MAPUTNIK_EDITOR_URL,
  applyPortalStyleOverride,
  buildPortalViewerItemFromSavedMap,
  listEditableStyleTargets,
  loadFixtureSavedMapForViewer,
  resolveDocStyleOrigin,
  saveFixtureSavedMapDoc,
} from "../saved-maps/index.js";
import type { AnnotationWorkspaceState, SavedMapItem, WebMapDoc } from "../saved-maps/index.js";
import {
  type AnnotationExportFormat,
  type SerializedAnnotationExport,
  serializeAnnotationExport,
} from "./annotation-export.js";
import { createAnnotationPanel } from "./annotation-panel.js";
import {
  type AnnotationActor,
  type AnnotationReadOptions,
  type PortalShapeAnnotation,
  addFeatureCommentThread,
  addMapCommentThread,
  appendAnnotationReply,
  createShapeAnnotation,
  getAnnotationPins,
  getShapeAnnotations,
  parseAnnotationWorkspace,
  setAnnotationPublicComments,
  setAnnotationThreadModeration,
  setAnnotationThreadStatus,
} from "./annotation-state.js";
import {
  type CollaborationFeatureActivity,
  type CollaborationFeatureRef,
  type CollaborationParticipant as CollaborationPanelParticipant,
  type CollaborationSessionSnapshot as CollaborationPanelSnapshot,
  createCollaborationPanel,
} from "./collaboration-panel.js";
import { buildMissingItemMessage, renderItemMissing } from "./error-surface.js";
import { computeFeatureAnchor } from "./feature-anchor.js";
import { type FeatureTableCollaborationLock, createFeatureDetail, createFeatureTable } from "./feature-detail.js";
import { deriveFeatureId, findFeatureById, findFeatureIndexInSource } from "./feature-id.js";
import { type LayerListChangeEvent, createLayerList } from "./layer-list.js";
import {
  type AnnotationShape,
  type CollaborationCursor as MapCollaborationCursor,
  type MapController,
  createMapController,
} from "./map-controller.js";
import { createMetadataPanel } from "./metadata-panel.js";
import { buildPopupViewModel } from "./popup.js";
import type { PortalViewerSdkFeatureLoader } from "./sdk-feature-loader.js";
import type { PortalGeoJsonFeature, PortalViewerLayer, SelectedFeature, ViewerState } from "./types.js";
import { decodeViewerStateFromHash, encodeViewerStateToHash, mergeViewerState } from "./url-state.js";
import {
  buildInitialState,
  deriveLayerOrder,
  findLayer,
  reorderLayer,
  selectFeature,
  setLayerVisibility,
  setView,
} from "./viewer-state.js";

interface MountHosts {
  metadataGrid: HTMLElement;
  layerList: HTMLElement;
  featureDetail: HTMLElement;
  annotationPanel: HTMLElement;
  collaborationPanel?: HTMLElement;
  itemTitle: HTMLElement;
  shareButton: HTMLButtonElement;
  status: HTMLElement;
  mapContainer: HTMLElement;
  tableHead: HTMLElement;
  tableBody: HTMLElement;
  tableLayerLabel: HTMLElement;
  tableRowCount: HTMLElement;
  styleButton?: HTMLButtonElement;
  stylePanel?: HTMLElement;
  styleTargetSelect?: HTMLSelectElement;
  styleOrigin?: HTMLElement;
  styleFrame?: HTMLIFrameElement;
  styleSaveButton?: HTMLButtonElement;
  styleCloseButton?: HTMLButtonElement;
  styleStatus?: HTMLElement;
}

export interface MapViewerHandle {
  ready: Promise<void>;
  dispose: () => void;
}

export interface InitMapViewerOptions {
  savedMapId?: string;
  itemId?: string;
  mode?: "viewer" | "embed";
  actorId?: string;
  actorName?: string;
  canModerateAnnotations?: boolean;
  maputnikEditorUrl?: string;
  embedParams?: EmbedRouteParams | null;
  sdkFeatureLoader?: PortalViewerSdkFeatureLoader;
}

interface SavedMapContext {
  item: SavedMapItem;
  doc: WebMapDoc;
  routeId: string;
}

interface PolygonDraft {
  title: string;
  coordinates: [number, number][];
}

interface FreehandDraft {
  title: string;
  coordinates: [number, number][];
}

type MaputnikBridgeMessage =
  | { type: "honua:maputnik-ready" }
  | { type: "honua:style-change"; style?: Record<string, unknown>; styleId?: string | null }
  | { type: "honua:maputnik-error"; message?: string; styleId?: string | null };

const MAPUTNIK_STYLE_ID_PREFIX = "honua-portal-style-editor";

function getRequiredElement<T extends HTMLElement>(root: ParentNode, selector: string): T {
  const element = root.querySelector<T>(selector);
  if (!element) throw new Error(`Missing required element: ${selector}`);
  return element;
}

function getOptionalElement<T extends HTMLElement>(root: ParentNode, selector: string): T | undefined {
  return root.querySelector<T>(selector) ?? undefined;
}

function readHosts(root: ParentNode): MountHosts {
  return {
    metadataGrid: getRequiredElement(root, "[data-metadata-grid]"),
    layerList: getRequiredElement(root, "[data-layer-list]"),
    featureDetail: getRequiredElement(root, "[data-feature-detail]"),
    annotationPanel: getRequiredElement(root, "[data-annotation-panel]"),
    collaborationPanel: getOptionalElement(root, "[data-collaboration-panel]"),
    itemTitle: getRequiredElement(root, "[data-portal-item-title]"),
    shareButton: getRequiredElement<HTMLButtonElement>(root, "[data-share-url-button]"),
    status: getRequiredElement(root, "[data-map-status]"),
    mapContainer: getRequiredElement(root, "[data-map-container]"),
    tableHead: getRequiredElement(root, "[data-feature-table-head]"),
    tableBody: getRequiredElement(root, "[data-feature-table-body]"),
    tableLayerLabel: getRequiredElement(root, "[data-table-layer-label]"),
    tableRowCount: getRequiredElement(root, "[data-table-row-count]"),
    styleButton: getOptionalElement<HTMLButtonElement>(root, "[data-style-editor-button]"),
    stylePanel: getOptionalElement(root, "[data-style-editor-panel]"),
    styleTargetSelect: getOptionalElement<HTMLSelectElement>(root, "[data-style-target-select]"),
    styleOrigin: getOptionalElement(root, "[data-style-origin]"),
    styleFrame: getOptionalElement<HTMLIFrameElement>(root, "[data-maputnik-frame]"),
    styleSaveButton: getOptionalElement<HTMLButtonElement>(root, "[data-style-save-button]"),
    styleCloseButton: getOptionalElement<HTMLButtonElement>(root, "[data-style-close-button]"),
    styleStatus: getOptionalElement(root, "[data-style-editor-status]"),
  };
}

function setStatus(host: HTMLElement, message: string, state: "loading" | "ready" | "error"): void {
  host.textContent = message;
  host.dataset["state"] = state;
}

export function initMapViewer(root: HTMLElement, options: InitMapViewerOptions = {}): MapViewerHandle {
  const hosts = readHosts(root);
  const isEmbedMode = options.mode === "embed";
  const initialHash = isEmbedMode ? {} : decodeViewerStateFromHash(window.location.hash);
  const initialUrlState = options.itemId ? { ...initialHash, itemId: options.itemId } : initialHash;
  const savedMapLoadResult = options.savedMapId ? loadFixtureSavedMapForViewer(options.savedMapId) : null;
  let savedMapContext: SavedMapContext | null = null;
  const loadResult = savedMapLoadResult
    ? savedMapLoadResult.status === "ok"
      ? { status: "ok" as const, item: savedMapLoadResult.viewerItem }
      : savedMapLoadResult.status === "missing"
        ? { status: "not-found" as const, itemId: savedMapLoadResult.id }
        : { status: "error" as const, itemId: savedMapLoadResult.id, message: savedMapLoadResult.reason }
    : loadPortalItem(initialUrlState.itemId);

  if (loadResult.status !== "ok") {
    renderItemMissing(hosts, buildMissingItemMessage(loadResult));
    return { ready: Promise.resolve(), dispose: () => {} };
  }

  let item = loadResult.item;
  if (savedMapLoadResult?.status === "ok") {
    savedMapContext = {
      item: savedMapLoadResult.item,
      doc: savedMapLoadResult.doc,
      routeId: options.savedMapId ?? savedMapLoadResult.item.id,
    };
  }
  const knownLayerIds = item.layers.map((layer) => layer.id);
  const layerOrderRef: { current: string[] } = {
    current: deriveLayerOrder(knownLayerIds, initialUrlState.visibleLayerIds),
  };

  const stateRef: { current: ViewerState } = {
    current: mergeViewerState(buildInitialState(item), initialUrlState, knownLayerIds),
  };
  const selectedLayerRef: { current: string | undefined } = {
    current: stateRef.current.selected?.layerId ?? item.layers[0]?.id,
  };
  const annotationWorkspaceRef: { current: AnnotationWorkspaceState | null } = { current: null };
  const annotationUnsupportedRef: { current: string | null } = { current: null };
  const selectedAnnotationThreadRef: { current: string | undefined } = { current: undefined };
  const pendingMapAnnotationBodyRef: { current: string | undefined } = { current: undefined };
  const pendingRectangleTitleRef: { current: string | undefined } = { current: undefined };
  const pendingPolygonDraftRef: { current: PolygonDraft | undefined } = { current: undefined };
  const pendingFreehandDraftRef: { current: FreehandDraft | undefined } = { current: undefined };
  const annotationMessageRef: { current: string | undefined } = { current: undefined };
  const collaborationSnapshotRef: { current: CollaborationSnapshot | null } = { current: null };
  const collaborationClaimedFeatureRef: { current: FeatureRef | undefined } = { current: undefined };
  let collaborationSession: CollaborationSession | null = null;
  let unsubscribeCollaboration: (() => void) | undefined;
  let lastCursorPublishAt = 0;

  const metadataPanel = createMetadataPanel(hosts.metadataGrid, hosts.itemTitle);
  const featureDetail = createFeatureDetail(hosts.featureDetail);
  const annotationPanel = createAnnotationPanel(hosts.annotationPanel, {
    onPlaceMapPin: handlePlaceMapPin,
    onPlaceRectangle: handlePlaceRectangle,
    onStartPolygon: handleStartPolygon,
    onFinishPolygon: handleFinishPolygon,
    onCancelPolygon: handleCancelPolygon,
    onStartFreehand: handleStartFreehand,
    onFinishFreehand: handleFinishFreehand,
    onCancelFreehand: handleCancelFreehand,
    onAddFeatureThread: handleAddFeatureThread,
    onAddReply: handleAddAnnotationReply,
    onSetThreadStatus: handleAnnotationThreadStatus,
    onSetThreadModeration: handleAnnotationThreadModeration,
    onSetPublicComments: handleSetPublicComments,
    onSelectThread: handleSelectAnnotationThread,
    onExport: handleExportAnnotations,
  });
  const collaborationPanel =
    hosts.collaborationPanel && !isEmbedMode
      ? createCollaborationPanel(hosts.collaborationPanel, {
          onFollowUser: (userId) => {
            collaborationSession?.follow(userId);
            renderCollaborationPanel();
          },
          onUnfollowUser: () => {
            collaborationSession?.unfollow();
            renderCollaborationPanel();
          },
          onFocusFeature: (feature) => handleCollaborationFocusFeature(feature),
        })
      : null;
  const layerList = createLayerList(hosts.layerList, handleLayerChange);
  const featureTable = createFeatureTable(
    {
      head: hosts.tableHead,
      body: hosts.tableBody,
      layerLabel: hosts.tableLayerLabel,
      rowCount: hosts.tableRowCount,
    },
    (event) => handleSelection({ layerId: event.layerId, featureId: event.featureId }, { fromTable: true }),
  );
  const pendingStyleRef: { current: Record<string, unknown> | null } = { current: null };
  const editorTargetRef: { current: string } = { current: "saved-map" };
  const embedInitialExtent = isEmbedMode
    ? resolveEffectiveExtent({
        query: options.embedParams?.extent ?? null,
        persisted: boundsToEmbedExtent(item.initialView.bounds),
      })
    : null;
  const initialBounds = embedInitialExtent
    ? embedExtentToBounds(embedInitialExtent)
    : initialHash.center
      ? undefined
      : item.initialView.bounds;

  metadataPanel.render(item.metadata);
  hydrateAnnotationWorkspaceFromDoc();
  renderStyleEditorChrome();
  renderLayerList();
  renderAnnotationPanel();
  renderFeatureTable();
  renderFeatureDetail();

  setStatus(hosts.status, "Loading map…", "loading");
  const mapController: MapController = createMapController({
    container: hosts.mapContainer,
    item,
    initialView: {
      center: stateRef.current.center,
      zoom: stateRef.current.zoom,
      bounds: initialBounds,
    },
    showZoomControls: options.embedParams?.zoom ?? true,
    loadSdkSource: options.sdkFeatureLoader,
    onSourceFeatures: () => {
      renderFeatureTable();
      renderFeatureDetail();
    },
    resolveFeatureIndex: (layer, feature) => {
      const features = getSampleSourceFeatures(item, layer.sourceId);
      return findFeatureIndexInSource(features, feature);
    },
  });

  mapController.onError((message) => setStatus(hosts.status, `Map error: ${message}`, "error"));
  mapController.onFeatureClick((event) => {
    handleSelection({ layerId: event.layerId, featureId: event.featureId }, { fromMap: true, popupEvent: event });
  });
  mapController.onMapClick((event) => handleAnnotationMapClick(event.lngLat));
  mapController.onMapPointerMove((event) => handleCollaborationPointerMove(event.lngLat));
  mapController.onAnnotationPinClick((event) => handleSelectAnnotationThread(event.threadId));
  mapController.onFreehandDraw((event) => handleFreehandDraw(event.lngLat));
  mapController.onViewChange((event) => {
    stateRef.current = setView(stateRef.current, event.center, event.zoom);
    writeUrlHash();
  });
  initializeCollaborationSession();
  syncAnnotationPins();
  syncAnnotationShapes();
  syncCollaborationCursors();

  const ready = (async () => {
    try {
      await mapController.ready;
      renderFeatureTable();
      renderFeatureDetail();
      setStatus(hosts.status, `Loaded ${item.layers.length} layer(s) — click a feature to inspect.`, "ready");

      for (const layer of item.layers) {
        mapController.setLayerVisibility(layer, stateRef.current.visibleLayerIds.includes(layer.id));
        mapController.setLayerOpacity(layer, layer.defaultOpacity);
      }
      mapController.applyLayerOrder(item, layerOrderRef.current);

      if (stateRef.current.selected) {
        const layer = findLayer(item, stateRef.current.selected.layerId);
        if (layer) {
          const features = getSampleSourceFeatures(item, layer.sourceId);
          const found = findFeatureById(features, layer.id, stateRef.current.selected.featureId);
          if (found) {
            renderFeatureDetail();
            showPopupForFeature(layer, found.feature, found.index);
          }
        }
      }
    } catch (error) {
      const message = error instanceof Error ? error.message : "Unknown error initializing map";
      setStatus(hosts.status, `Map error: ${message}`, "error");
    }
  })();

  const handleShareClick = async () => {
    const url = `${window.location.origin}${window.location.pathname}${window.location.search}${encodeViewerStateToHash(stateRef.current, { itemId: item.metadata.id })}`;
    const success = await copyToClipboard(url);
    hosts.shareButton.dataset["copied"] = success ? "true" : "false";
    hosts.shareButton.textContent = success ? "Link copied" : "Copy failed";
    window.setTimeout(() => {
      hosts.shareButton.textContent = "Copy view URL";
      hosts.shareButton.removeAttribute("data-copied");
    }, 1800);
  };

  const handleHashChange = () => {
    const overrides = decodeViewerStateFromHash(window.location.hash);
    if (overrides.visibleLayerIds) {
      layerOrderRef.current = deriveLayerOrder(knownLayerIds, overrides.visibleLayerIds);
    }
    const next = mergeViewerState(stateRef.current, overrides, knownLayerIds);
    stateRef.current = next;
    if (overrides.center) mapController.flyTo(next.center, next.zoom);
    syncMapVisibilityAndOrder();
    renderLayerList();
    renderFeatureTable();
    renderFeatureDetail();
    renderAnnotationPanel();
  };

  function initializeCollaborationSession(): void {
    if (!collaborationPanel || !hosts.collaborationPanel) return;
    if (!savedMapContext) {
      hosts.collaborationPanel.innerHTML = `<p class="empty-copy">Open a saved map to collaborate.</p>`;
      return;
    }

    const actor = currentCollaborationActor();
    collaborationSession = createFixtureCollaborationSession(savedMapContext.item.id, actor);
    unsubscribeCollaboration = collaborationSession.subscribe((snapshot) => {
      collaborationSnapshotRef.current = snapshot;
      renderCollaborationPanel();
      syncCollaborationCursors();
      renderFeatureTable();
      followCollaborationTarget(snapshot);
    });
  }

  function renderCollaborationPanel(): void {
    if (!collaborationPanel || !collaborationSnapshotRef.current) return;
    collaborationPanel.render(collaborationPanelSnapshot(collaborationSnapshotRef.current));
  }

  function syncCollaborationCursors(): void {
    const snapshot = collaborationSnapshotRef.current;
    if (!snapshot) {
      mapController.setCollaborationCursors([]);
      return;
    }
    const cursors: MapCollaborationCursor[] = Object.values(snapshot.cursors)
      .filter((cursor) => cursor.participantId !== snapshot.actorId)
      .map((cursor) => {
        const participant = snapshot.participants[cursor.participantId];
        return {
          participantId: cursor.participantId,
          name: participant?.displayName ?? "Collaborator",
          color: participant?.color ?? participantColor(cursor.participantId),
          lngLat: [cursor.mapPoint.lng, cursor.mapPoint.lat],
        };
      });
    mapController.setCollaborationCursors(cursors);
  }

  function handleCollaborationPointerMove(lngLat: [number, number]): void {
    if (!collaborationSession) return;
    const now = Date.now();
    if (now - lastCursorPublishAt < 125) return;
    lastCursorPublishAt = now;
    collaborationSession.publishCursor({ mapPoint: { lng: lngLat[0], lat: lngLat[1] } });
  }

  function updateCollaborationFeatureClaim(selection: SelectedFeature): void {
    if (!collaborationSession) return;
    const previous = collaborationClaimedFeatureRef.current;
    if (previous && !sameCollaborationFeature(previous, selection)) {
      collaborationSession.releaseFeature(previous);
    }
    collaborationSession.selectFeature(selection);
    const claimed = collaborationSession.claimFeature(selection);
    collaborationClaimedFeatureRef.current = claimed ? selection : undefined;
    if (!claimed) {
      const editor = collaborationEditorForFeature(selection);
      if (editor) {
        setStatus(hosts.status, `${editor.displayName} is editing this feature. Your selection is visible.`, "ready");
      }
    }
  }

  function clearCollaborationFeatureClaim(): void {
    if (!collaborationSession) return;
    const previous = collaborationClaimedFeatureRef.current;
    if (previous) collaborationSession.releaseFeature(previous);
    collaborationClaimedFeatureRef.current = undefined;
    collaborationSession.clearFeatureSelection();
  }

  function collaborationEditorForFeature(feature: FeatureRef): { displayName: string } | null {
    const snapshot = collaborationSnapshotRef.current;
    if (!snapshot) return null;
    const lock = snapshot.editLocks[collaborationFeatureKey(feature)];
    if (!lock || lock.participantId === snapshot.actorId) return null;
    const participant = snapshot.participants[lock.participantId];
    return participant ? { displayName: participant.displayName } : null;
  }

  function followCollaborationTarget(snapshot: CollaborationSnapshot): void {
    const targetId = snapshot.followTarget?.participantId;
    if (!targetId) return;
    const cursor = snapshot.cursors[targetId];
    if (!cursor) return;
    mapController.flyTo([cursor.mapPoint.lng, cursor.mapPoint.lat], Math.max(stateRef.current.zoom, 13));
  }

  function handleCollaborationFocusFeature(feature: CollaborationFeatureRef): void {
    handleSelection(
      { layerId: feature.layerId, featureId: feature.featureId },
      { fromTable: true, claimCollaboration: false },
    );
  }

  function renderStyleEditorChrome(): void {
    if (!hosts.styleButton) return;
    const targets = savedMapContext ? listEditableStyleTargets(savedMapContext.doc) : [];
    const editable = options.mode !== "embed" && targets.length > 0 && !!hosts.stylePanel && !!hosts.styleFrame;
    hosts.styleButton.hidden = !editable;
    hosts.styleButton.disabled = !editable;
    if (!editable) {
      if (hosts.stylePanel) hosts.stylePanel.hidden = true;
      return;
    }

    renderStyleTargets(targets);
    renderStyleOrigin();
    setEditorStatus("Maputnik editor ready", "idle");
  }

  function renderStyleTargets(targets = savedMapContext ? listEditableStyleTargets(savedMapContext.doc) : []): void {
    if (!hosts.styleTargetSelect) return;
    hosts.styleTargetSelect.innerHTML = "";
    for (const target of targets) {
      const option = document.createElement("option");
      option.value = target.id;
      option.textContent = target.kind === "saved-map" ? target.label : `Layer: ${target.label}`;
      hosts.styleTargetSelect.appendChild(option);
    }
    if (targets.length === 0) {
      editorTargetRef.current = "";
      hosts.styleTargetSelect.value = "";
      return;
    }
    if (!targets.some((target) => target.id === editorTargetRef.current)) {
      editorTargetRef.current = targets[0].id;
    }
    hosts.styleTargetSelect.value = editorTargetRef.current;
  }

  function renderStyleOrigin(): void {
    if (!savedMapContext || !hosts.styleOrigin) return;
    const targetId = editorTargetRef.current;
    const target = listEditableStyleTargets(savedMapContext.doc).find((entry) => entry.id === targetId);
    const origin = target?.origin ?? resolveDocStyleOrigin(savedMapContext.doc);
    hosts.styleOrigin.textContent = origin === "portal-override" ? "Portal override" : "Admin/server style";
    hosts.styleOrigin.dataset["origin"] = origin;
  }

  function openStyleEditor(): void {
    const targets = savedMapContext ? listEditableStyleTargets(savedMapContext.doc) : [];
    if (!savedMapContext || targets.length === 0 || !hosts.stylePanel || !hosts.styleFrame) return;
    renderStyleTargets(targets);
    hosts.stylePanel.hidden = false;
    hosts.styleFrame.src = options.maputnikEditorUrl ?? DEFAULT_MAPUTNIK_EDITOR_URL;
    pendingStyleRef.current = null;
    setEditorStatus("Opening self-hosted Maputnik editor", "loading");
    postStyleToEditor();
  }

  function closeStyleEditor(): void {
    if (!hosts.stylePanel) return;
    hosts.stylePanel.hidden = true;
    pendingStyleRef.current = null;
    setEditorStatus("Maputnik editor closed", "idle");
  }

  function postStyleToEditor(): void {
    if (!hosts.styleFrame?.contentWindow || !savedMapContext) return;
    hosts.styleFrame.contentWindow.postMessage(
      {
        type: "honua:style-load",
        style: item.style,
        targetId: editorTargetRef.current,
        mapId: savedMapContext.routeId,
      },
      window.location.origin,
    );
  }

  function currentMaputnikStyleId(): string | null {
    if (!savedMapContext) return null;
    return buildMaputnikStyleId(savedMapContext.routeId, editorTargetRef.current);
  }

  function isCurrentMaputnikStyleId(styleId: unknown): boolean {
    const expected = currentMaputnikStyleId();
    return typeof styleId === "string" && !!expected && styleId === expected;
  }

  function saveEditedStyle(): void {
    if (!savedMapContext || !pendingStyleRef.current) {
      setEditorStatus("No style changes to save", "idle");
      return;
    }
    try {
      const nextDoc = applyPortalStyleOverride({
        doc: savedMapContext.doc,
        targetId: editorTargetRef.current,
        style: pendingStyleRef.current,
        sourceStyle: item.style,
      });
      const nextItem = saveFixtureSavedMapDoc(savedMapContext.routeId, nextDoc);
      item = buildPortalViewerItemFromSavedMap(nextItem, nextDoc);
      savedMapContext = { item: nextItem, doc: nextDoc, routeId: savedMapContext.routeId };
      hydrateAnnotationWorkspaceFromDoc();
      syncAnnotationPins();
      syncAnnotationShapes();
      renderAnnotationPanel();
      pendingStyleRef.current = null;
      renderStyleTargets();
      renderStyleOrigin();
      setEditorStatus("Style saved. Reload the saved map to apply it.", "saved");
      setStatus(hosts.status, "Style override saved. Reload the map to apply the edited style.", "ready");
    } catch (error) {
      const message = error instanceof Error ? error.message : "Unknown style save failure";
      setEditorStatus(message, "error");
      setStatus(hosts.status, `Style save failed: ${message}`, "error");
    }
  }

  function setEditorStatus(message: string, state: "idle" | "loading" | "dirty" | "saved" | "error"): void {
    if (!hosts.styleStatus) return;
    hosts.styleStatus.textContent = message;
    hosts.styleStatus.dataset["state"] = state;
  }

  const handleStyleEditorClick = () => {
    openStyleEditor();
  };

  const handleStyleEditorClose = () => {
    closeStyleEditor();
  };

  const handleStyleSaveClick = () => {
    saveEditedStyle();
  };

  const handleStyleTargetChange = () => {
    if (!hosts.styleTargetSelect) return;
    editorTargetRef.current = hosts.styleTargetSelect.value;
    pendingStyleRef.current = null;
    postStyleToEditor();
    renderStyleOrigin();
  };

  const handleMaputnikMessage = (event: MessageEvent) => {
    if (!isMaputnikBridgeMessage(event.data)) return;
    if (event.origin !== window.location.origin) return;
    if (hosts.styleFrame?.contentWindow && event.source !== hosts.styleFrame.contentWindow) return;
    if (event.data.type === "honua:maputnik-ready") {
      postStyleToEditor();
      return;
    }
    if (event.data.type === "honua:maputnik-error") {
      if (event.data.styleId && !isCurrentMaputnikStyleId(event.data.styleId)) return;
      pendingStyleRef.current = null;
      setEditorStatus(event.data.message ?? "Maputnik bridge error", "error");
      return;
    }
    if (event.data.type === "honua:style-change" && event.data.style) {
      if (!isCurrentMaputnikStyleId(event.data.styleId)) return;
      pendingStyleRef.current = event.data.style;
      setEditorStatus("Unsaved style changes", "dirty");
    }
  };

  hosts.shareButton.addEventListener("click", handleShareClick);
  hosts.styleButton?.addEventListener("click", handleStyleEditorClick);
  hosts.styleCloseButton?.addEventListener("click", handleStyleEditorClose);
  hosts.styleSaveButton?.addEventListener("click", handleStyleSaveClick);
  hosts.styleTargetSelect?.addEventListener("change", handleStyleTargetChange);
  window.addEventListener("message", handleMaputnikMessage);
  if (!isEmbedMode) {
    window.addEventListener("hashchange", handleHashChange);
  }

  function handleLayerChange(event: LayerListChangeEvent): void {
    const layer = findLayer(item, event.layerId);
    if (!layer) return;
    switch (event.kind) {
      case "toggle-visibility": {
        stateRef.current = setLayerVisibility(stateRef.current, layerOrderRef.current, layer.id, event.visible);
        mapController.setLayerVisibility(layer, event.visible);
        if (!event.visible && stateRef.current.selected?.layerId === layer.id) {
          stateRef.current = selectFeature(stateRef.current, undefined);
          clearCollaborationFeatureClaim();
          mapController.closePopup();
        }
        renderLayerList();
        if (selectedLayerRef.current === layer.id || event.visible) {
          renderFeatureTable();
        }
        renderFeatureDetail();
        renderAnnotationPanel();
        writeUrlHash();
        return;
      }
      case "set-opacity": {
        mapController.setLayerOpacity(layer, event.opacity);
        return;
      }
      case "reorder": {
        const result = reorderLayer(stateRef.current, layerOrderRef.current, layer.id, event.direction);
        layerOrderRef.current = result.layerOrder;
        stateRef.current = result.state;
        mapController.applyLayerOrder(item, layerOrderRef.current);
        renderLayerList();
        writeUrlHash();
        return;
      }
      case "select-layer": {
        selectedLayerRef.current = layer.id;
        renderLayerList();
        renderFeatureTable();
        renderAnnotationPanel();
        return;
      }
    }
  }

  interface SelectionContext {
    fromTable?: boolean;
    fromMap?: boolean;
    popupEvent?: { lngLat: [number, number] };
    claimCollaboration?: boolean;
  }

  function handleSelection(selection: SelectedFeature, context: SelectionContext): void {
    const layer = findLayer(item, selection.layerId);
    if (!layer) return;

    selectedLayerRef.current = layer.id;
    stateRef.current = selectFeature(stateRef.current, selection);

    const features = getSampleSourceFeatures(item, layer.sourceId);
    const found = findFeatureById(features, layer.id, selection.featureId);
    if (!found) return;

    if (context.claimCollaboration !== false) {
      updateCollaborationFeatureClaim(selection);
    }

    renderLayerList();
    renderFeatureTable();
    renderFeatureDetail();
    renderAnnotationPanel();
    writeUrlHash();

    if (context.fromTable) {
      const center = computeFeatureAnchor(found.feature);
      if (center) {
        mapController.flyTo(center, Math.max(stateRef.current.zoom, 13));
        const popup = buildPopupViewModel(layer, found.feature, found.index);
        mapController.showPopup({
          layerId: layer.id,
          featureId: selection.featureId,
          popup,
          lngLat: center,
        });
      }
    } else if (context.fromMap && context.popupEvent) {
      const popup = buildPopupViewModel(layer, found.feature, found.index);
      mapController.showPopup({
        layerId: layer.id,
        featureId: selection.featureId,
        popup,
        lngLat: context.popupEvent.lngLat,
      });
    }
  }

  function showPopupForFeature(layer: PortalViewerLayer, feature: PortalGeoJsonFeature, index: number): void {
    const center = computeFeatureAnchor(feature);
    if (!center) return;
    const popup = buildPopupViewModel(layer, feature, index);
    mapController.showPopup({
      layerId: layer.id,
      featureId: deriveFeatureId(layer.id, feature, index),
      popup,
      lngLat: center,
    });
  }

  function syncMapVisibilityAndOrder(): void {
    for (const layer of item.layers) {
      mapController.setLayerVisibility(layer, stateRef.current.visibleLayerIds.includes(layer.id));
    }
    mapController.applyLayerOrder(item, layerOrderRef.current);
  }

  function renderLayerList(): void {
    layerList.render({
      item,
      layerOrder: layerOrderRef.current,
      viewer: stateRef.current,
      selectedLayerId: selectedLayerRef.current,
    });
  }

  function renderFeatureTable(): void {
    const layerId = selectedLayerRef.current;
    if (!layerId) {
      featureTable.renderEmpty("Select a layer", "Click a layer in the sidebar to inspect its rows.");
      return;
    }
    const layer = findLayer(item, layerId);
    if (!layer) {
      featureTable.renderEmpty("Unknown layer", "This layer is no longer available in the portal item.");
      return;
    }
    if (!layer.inspectable) {
      featureTable.renderEmpty(layer.name, "This layer does not expose tabular detail in the portal viewer.");
      return;
    }
    if (!stateRef.current.visibleLayerIds.includes(layer.id)) {
      featureTable.renderEmpty(layer.name, "Turn the layer on to inspect its rows.");
      return;
    }
    const features = getSampleSourceFeatures(item, layer.sourceId);
    if (features.length === 0) {
      featureTable.renderEmpty(layer.name, "No features available for this layer.");
      return;
    }
    featureTable.render(layer, features, {
      selectedFeatureId: stateRef.current.selected?.featureId,
      collaborationLocks: collaborationTableLocks(),
    });
  }

  function renderFeatureDetail(): void {
    const selection = stateRef.current.selected;
    if (!selection) {
      featureDetail.renderEmpty();
      return;
    }
    const layer = findLayer(item, selection.layerId);
    if (!layer) {
      featureDetail.renderEmpty("The selected layer is no longer available.");
      return;
    }
    if (!stateRef.current.visibleLayerIds.includes(layer.id)) {
      featureDetail.renderEmpty(`Turn “${layer.name}” on to inspect its features.`);
      return;
    }
    const features = getSampleSourceFeatures(item, layer.sourceId);
    const found = findFeatureById(features, layer.id, selection.featureId);
    if (!found) {
      featureDetail.renderEmpty("The selected feature is no longer available.");
      return;
    }
    featureDetail.render(layer, found.feature, found.index);
  }

  function hydrateAnnotationWorkspaceFromDoc(): void {
    if (!savedMapContext) {
      annotationWorkspaceRef.current = null;
      annotationUnsupportedRef.current = null;
      return;
    }
    const parsed = parseAnnotationWorkspace(savedMapContext.doc.annotations);
    if (parsed.status === "ok") {
      annotationWorkspaceRef.current = parsed.workspace;
      annotationUnsupportedRef.current = null;
      return;
    }
    annotationWorkspaceRef.current = null;
    annotationUnsupportedRef.current = parsed.message;
  }

  function renderAnnotationPanel(): void {
    if (!savedMapContext) {
      annotationPanel.render({
        mode: "unavailable",
        message: "Open a saved map to add annotations.",
      });
      return;
    }
    if (annotationUnsupportedRef.current || !annotationWorkspaceRef.current) {
      annotationPanel.render({
        mode: "unavailable",
        message: annotationUnsupportedRef.current ?? "Annotations are not available for this saved map.",
      });
      return;
    }
    annotationPanel.render({
      workspace: annotationWorkspaceRef.current,
      mode: annotationPanelMode(),
      selectedFeature: stateRef.current.selected,
      selectedThreadId: selectedAnnotationThreadRef.current,
      pendingPlacement: hasPendingAnnotationPlacement(),
      pendingPlacementKind: pendingAnnotationPlacementKind(),
      pendingPlacementCopy: pendingAnnotationPlacementCopy(),
      polygonDraftTitle: pendingPolygonDraftRef.current?.title,
      polygonDraftVertexCount: pendingPolygonDraftRef.current?.coordinates.length,
      freehandDraftTitle: pendingFreehandDraftRef.current?.title,
      freehandDraftPointCount: pendingFreehandDraftRef.current?.coordinates.length,
      canModerate: canModerateAnnotations(),
      canExport: !isEmbedMode,
      message: annotationMessageRef.current,
    });
  }

  function annotationPanelMode(): "edit" | "readonly" | "public-comment" {
    if (!isEmbedMode) return "edit";
    return canAcceptPublicComments() ? "public-comment" : "readonly";
  }

  function canAcceptPublicComments(): boolean {
    if (!savedMapContext || !annotationWorkspaceRef.current) return false;
    if (!annotationWorkspaceRef.current.visibility.publicComments) return false;
    return savedMapContext.item.access.sharing === "public" || savedMapContext.item.access.sharing === "public-link";
  }

  function canModerateAnnotations(): boolean {
    return !isEmbedMode && !!options.canModerateAnnotations;
  }

  function annotationReadOptions(): AnnotationReadOptions {
    return annotationPanelMode() === "public-comment" ? { audience: "public" } : {};
  }

  function hasPendingAnnotationPlacement(): boolean {
    return (
      !!pendingMapAnnotationBodyRef.current ||
      !!pendingRectangleTitleRef.current ||
      !!pendingPolygonDraftRef.current ||
      !!pendingFreehandDraftRef.current
    );
  }

  function pendingAnnotationPlacementKind(): "pin" | "rectangle" | "polygon" | "freehand" | undefined {
    if (pendingFreehandDraftRef.current) return "freehand";
    if (pendingPolygonDraftRef.current) return "polygon";
    if (pendingRectangleTitleRef.current) return "rectangle";
    if (pendingMapAnnotationBodyRef.current) return "pin";
    return undefined;
  }

  function pendingAnnotationPlacementCopy(): string {
    const polygonDraft = pendingPolygonDraftRef.current;
    if (polygonDraft) {
      if (polygonDraft.coordinates.length === 0) return "Click the map to add polygon vertices";
      return `${polygonDraft.coordinates.length} polygon ${
        polygonDraft.coordinates.length === 1 ? "vertex" : "vertices"
      } added`;
    }
    const freehandDraft = pendingFreehandDraftRef.current;
    if (freehandDraft) {
      if (freehandDraft.coordinates.length === 0) return "Drag on the map to draw freehand";
      return `${freehandDraft.coordinates.length} freehand ${
        freehandDraft.coordinates.length === 1 ? "point" : "points"
      } captured`;
    }
    if (pendingRectangleTitleRef.current) return "Click the map to place the rectangle";
    if (canAcceptPublicComments()) return "Click the map to place the public comment";
    return "Click the map to place the pin";
  }

  function polygonDraftMessage(vertexCount: number): string {
    if (vertexCount < 3) {
      return `${vertexCount} polygon ${vertexCount === 1 ? "vertex" : "vertices"} added. Add ${
        3 - vertexCount
      } more before finishing.`;
    }
    return `${vertexCount} polygon vertices added. Finish polygon when the boundary is ready.`;
  }

  function freehandDraftMessage(pointCount: number): string {
    if (pointCount < 2) return "1 freehand point captured. Keep dragging before finishing.";
    return `${pointCount} freehand points captured. Finish freehand when the stroke is ready.`;
  }

  function handlePlaceMapPin(body: string): void {
    if (isEmbedMode && !canAcceptPublicComments()) return;
    const normalized = body.trim();
    if (!normalized) {
      annotationMessageRef.current = "Write a comment before placing a pin.";
      renderAnnotationPanel();
      return;
    }
    pendingMapAnnotationBodyRef.current = normalized;
    pendingRectangleTitleRef.current = undefined;
    pendingPolygonDraftRef.current = undefined;
    pendingFreehandDraftRef.current = undefined;
    mapController.setFreehandDrawingEnabled(false);
    annotationMessageRef.current = canAcceptPublicComments()
      ? "Click the map to place the public comment."
      : "Click the map to place the pin.";
    syncAnnotationShapes();
    renderAnnotationPanel();
  }

  function handlePlaceRectangle(title: string): void {
    if (isEmbedMode) return;
    pendingRectangleTitleRef.current = title.trim() || "Review rectangle";
    pendingMapAnnotationBodyRef.current = undefined;
    pendingPolygonDraftRef.current = undefined;
    pendingFreehandDraftRef.current = undefined;
    mapController.setFreehandDrawingEnabled(false);
    annotationMessageRef.current = "Click the map to place the rectangle.";
    syncAnnotationShapes();
    renderAnnotationPanel();
  }

  function handleStartPolygon(title: string): void {
    if (isEmbedMode) return;
    pendingMapAnnotationBodyRef.current = undefined;
    pendingRectangleTitleRef.current = undefined;
    pendingFreehandDraftRef.current = undefined;
    mapController.setFreehandDrawingEnabled(false);
    pendingPolygonDraftRef.current = {
      title: title.trim() || "Review polygon",
      coordinates: [],
    };
    annotationMessageRef.current = "Click the map to add polygon vertices.";
    syncAnnotationShapes();
    renderAnnotationPanel();
  }

  function handleStartFreehand(title: string): void {
    if (isEmbedMode) return;
    pendingMapAnnotationBodyRef.current = undefined;
    pendingRectangleTitleRef.current = undefined;
    pendingPolygonDraftRef.current = undefined;
    pendingFreehandDraftRef.current = {
      title: title.trim() || "Review freehand",
      coordinates: [],
    };
    mapController.setFreehandDrawingEnabled(true);
    annotationMessageRef.current = "Drag on the map to draw freehand.";
    syncAnnotationShapes();
    renderAnnotationPanel();
  }

  function handleFinishPolygon(): void {
    const draft = pendingPolygonDraftRef.current;
    if (isEmbedMode || !annotationWorkspaceRef.current || !draft) return;
    if (draft.coordinates.length < 3) {
      annotationMessageRef.current = "Add at least 3 polygon vertices before finishing.";
      renderAnnotationPanel();
      return;
    }
    try {
      const next = createShapeAnnotation(annotationWorkspaceRef.current, {
        title: draft.title,
        shape: "polygon",
        coordinates: draft.coordinates,
        actor: currentAnnotationActor(),
        style: {
          strokeColor: "#15803d",
          fillColor: "#86efac",
          strokeWidth: 2,
          fillOpacity: 0.2,
        },
      });
      pendingPolygonDraftRef.current = undefined;
      mapController.setFreehandDrawingEnabled(false);
      persistAnnotationWorkspace(next, "Polygon annotation saved.");
    } catch (error) {
      annotationMessageRef.current = error instanceof Error ? error.message : "Polygon annotation save failed.";
      renderAnnotationPanel();
    }
  }

  function handleCancelPolygon(): void {
    if (isEmbedMode || !pendingPolygonDraftRef.current) return;
    pendingPolygonDraftRef.current = undefined;
    mapController.setFreehandDrawingEnabled(false);
    annotationMessageRef.current = "Polygon drawing canceled.";
    syncAnnotationShapes();
    renderAnnotationPanel();
  }

  function handleFinishFreehand(): void {
    const draft = pendingFreehandDraftRef.current;
    if (isEmbedMode || !annotationWorkspaceRef.current || !draft) return;
    if (uniqueCoordinateCount(draft.coordinates) < 2) {
      annotationMessageRef.current = "Draw at least 2 freehand points before finishing.";
      renderAnnotationPanel();
      return;
    }
    try {
      const next = createShapeAnnotation(annotationWorkspaceRef.current, {
        title: draft.title,
        shape: "freehand",
        coordinates: draft.coordinates,
        actor: currentAnnotationActor(),
        style: {
          strokeColor: "#9333ea",
          strokeWidth: 3,
        },
      });
      pendingFreehandDraftRef.current = undefined;
      mapController.setFreehandDrawingEnabled(false);
      persistAnnotationWorkspace(next, "Freehand annotation saved.");
    } catch (error) {
      annotationMessageRef.current = error instanceof Error ? error.message : "Freehand annotation save failed.";
      renderAnnotationPanel();
    }
  }

  function handleCancelFreehand(): void {
    if (isEmbedMode || !pendingFreehandDraftRef.current) return;
    pendingFreehandDraftRef.current = undefined;
    mapController.setFreehandDrawingEnabled(false);
    annotationMessageRef.current = "Freehand drawing canceled.";
    syncAnnotationShapes();
    renderAnnotationPanel();
  }

  function handleFreehandDraw(lngLat: [number, number]): void {
    const draft = pendingFreehandDraftRef.current;
    if (isEmbedMode || !draft) return;
    appendFreehandCoordinate(draft, lngLat);
    annotationMessageRef.current = freehandDraftMessage(draft.coordinates.length);
    syncAnnotationShapes();
    renderAnnotationPanel();
  }

  function handleAnnotationMapClick(lngLat: [number, number]): void {
    const freehandDraft = pendingFreehandDraftRef.current;
    if (freehandDraft && !isEmbedMode) {
      appendFreehandCoordinate(freehandDraft, lngLat);
      annotationMessageRef.current = freehandDraftMessage(freehandDraft.coordinates.length);
      syncAnnotationShapes();
      renderAnnotationPanel();
      return;
    }

    const polygonDraft = pendingPolygonDraftRef.current;
    if (polygonDraft && !isEmbedMode) {
      polygonDraft.coordinates = [...polygonDraft.coordinates, lngLat];
      annotationMessageRef.current = polygonDraftMessage(polygonDraft.coordinates.length);
      syncAnnotationShapes();
      renderAnnotationPanel();
      return;
    }

    const rectangleTitle = pendingRectangleTitleRef.current;
    if (rectangleTitle && !isEmbedMode && annotationWorkspaceRef.current) {
      try {
        const next = createShapeAnnotation(annotationWorkspaceRef.current, {
          title: rectangleTitle,
          shape: "rectangle",
          coordinates: rectangleAround(lngLat),
          actor: currentAnnotationActor(),
          style: {
            strokeColor: "#2563eb",
            fillColor: "#93c5fd",
            strokeWidth: 2,
            fillOpacity: 0.24,
          },
        });
        pendingRectangleTitleRef.current = undefined;
        persistAnnotationWorkspace(next, "Rectangle annotation saved.");
      } catch (error) {
        annotationMessageRef.current = error instanceof Error ? error.message : "Rectangle annotation save failed.";
        renderAnnotationPanel();
      }
      return;
    }

    const body = pendingMapAnnotationBodyRef.current;
    const isPublicSubmission = isEmbedMode && canAcceptPublicComments();
    if (!body || (isEmbedMode && !isPublicSubmission) || !annotationWorkspaceRef.current) return;
    try {
      const next = addMapCommentThread(annotationWorkspaceRef.current, {
        body,
        lngLat,
        actor: currentAnnotationActor(),
        moderationState: isPublicSubmission ? "pending" : "approved",
        submittedBy: isPublicSubmission ? "guest" : "member",
      });
      pendingMapAnnotationBodyRef.current = undefined;
      persistAnnotationWorkspace(
        next,
        isPublicSubmission ? "Public comment submitted for approval." : "Annotation saved.",
      );
    } catch (error) {
      annotationMessageRef.current = error instanceof Error ? error.message : "Annotation save failed.";
      renderAnnotationPanel();
    }
  }

  function handleAddFeatureThread(body: string): void {
    if (isEmbedMode || !annotationWorkspaceRef.current) return;
    const selection = stateRef.current.selected;
    if (!selection) {
      annotationMessageRef.current = "Select a feature before adding a feature comment.";
      renderAnnotationPanel();
      return;
    }
    try {
      const layer = findLayer(item, selection.layerId);
      const featureContext = layer ? selectedFeatureContext(layer, selection.featureId) : null;
      const next = addFeatureCommentThread(annotationWorkspaceRef.current, {
        body,
        selected: selection,
        actor: currentAnnotationActor(),
        ...(featureContext?.label ? { label: featureContext.label } : {}),
        ...(featureContext?.lngLat ? { lngLat: featureContext.lngLat } : {}),
      });
      selectedAnnotationThreadRef.current = getNewestAnnotationThreadId(next);
      persistAnnotationWorkspace(next, "Feature comment saved.");
    } catch (error) {
      annotationMessageRef.current = error instanceof Error ? error.message : "Feature comment save failed.";
      renderAnnotationPanel();
    }
  }

  function handleAddAnnotationReply(threadId: string, body: string): void {
    if (isEmbedMode || !annotationWorkspaceRef.current) return;
    try {
      const next = appendAnnotationReply(annotationWorkspaceRef.current, {
        threadId,
        body,
        actor: currentAnnotationActor(),
      });
      selectedAnnotationThreadRef.current = threadId;
      persistAnnotationWorkspace(next, "Reply saved.");
    } catch (error) {
      annotationMessageRef.current = error instanceof Error ? error.message : "Reply save failed.";
      renderAnnotationPanel();
    }
  }

  function handleAnnotationThreadStatus(threadId: string, status: "open" | "resolved"): void {
    if (isEmbedMode || !annotationWorkspaceRef.current) return;
    try {
      const next = setAnnotationThreadStatus(annotationWorkspaceRef.current, {
        threadId,
        status,
        actor: currentAnnotationActor(),
      });
      selectedAnnotationThreadRef.current = threadId;
      persistAnnotationWorkspace(next, status === "open" ? "Thread reopened." : "Thread resolved.");
    } catch (error) {
      annotationMessageRef.current = error instanceof Error ? error.message : "Thread update failed.";
      renderAnnotationPanel();
    }
  }

  function handleAnnotationThreadModeration(threadId: string, state: "approved" | "pending" | "hidden"): void {
    if (isEmbedMode || !annotationWorkspaceRef.current || !canModerateAnnotations()) return;
    try {
      const next = setAnnotationThreadModeration(annotationWorkspaceRef.current, {
        threadId,
        state,
        actor: currentAnnotationActor(),
      });
      selectedAnnotationThreadRef.current = threadId;
      const message =
        state === "approved" ? "Comment approved." : state === "hidden" ? "Comment hidden." : "Comment marked pending.";
      persistAnnotationWorkspace(next, message);
    } catch (error) {
      annotationMessageRef.current = error instanceof Error ? error.message : "Comment moderation failed.";
      renderAnnotationPanel();
    }
  }

  function handleSetPublicComments(enabled: boolean): void {
    if (isEmbedMode || !annotationWorkspaceRef.current || !canModerateAnnotations()) return;
    const next = setAnnotationPublicComments(annotationWorkspaceRef.current, {
      enabled,
      actor: currentAnnotationActor(),
    });
    persistAnnotationWorkspace(next, enabled ? "Public comments enabled." : "Public comments disabled.");
  }

  function handleSelectAnnotationThread(threadId: string): void {
    selectedAnnotationThreadRef.current = threadId;
    const pin = annotationWorkspaceRef.current
      ? getAnnotationPins(annotationWorkspaceRef.current).find((entry) => entry.threadId === threadId)
      : undefined;
    if (pin) mapController.flyTo(pin.lngLat, Math.max(stateRef.current.zoom, 14));
    renderAnnotationPanel();
  }

  function handleExportAnnotations(format: AnnotationExportFormat): void {
    if (isEmbedMode || !savedMapContext || !annotationWorkspaceRef.current) return;
    try {
      const exported = serializeAnnotationExport(
        annotationWorkspaceRef.current,
        {
          mapId: savedMapContext.routeId,
          mapTitle: savedMapContext.item.title,
          webMapVersion: savedMapContext.doc.version,
        },
        format,
      );
      downloadAnnotationExport(exported);
      annotationMessageRef.current = "Annotation export downloaded.";
      renderAnnotationPanel();
      setStatus(hosts.status, "Annotation export downloaded.", "ready");
    } catch (error) {
      annotationMessageRef.current = error instanceof Error ? error.message : "Annotation export failed.";
      renderAnnotationPanel();
    }
  }

  function persistAnnotationWorkspace(workspace: AnnotationWorkspaceState, message: string): void {
    if (!savedMapContext || (isEmbedMode && !canAcceptPublicComments())) return;
    const nextDoc: WebMapDoc = {
      ...savedMapContext.doc,
      annotations: workspace,
    };
    const nextItem = saveFixtureSavedMapDoc(savedMapContext.routeId, nextDoc);
    savedMapContext = { item: nextItem, doc: nextDoc, routeId: savedMapContext.routeId };
    annotationWorkspaceRef.current = workspace;
    annotationUnsupportedRef.current = null;
    annotationMessageRef.current = message;
    syncAnnotationPins();
    syncAnnotationShapes();
    renderAnnotationPanel();
    setStatus(hosts.status, message, "ready");
  }

  function syncAnnotationPins(): void {
    mapController.setAnnotationPins(
      annotationWorkspaceRef.current ? getAnnotationPins(annotationWorkspaceRef.current, annotationReadOptions()) : [],
    );
  }

  function syncAnnotationShapes(): void {
    const persistedShapes = annotationWorkspaceRef.current
      ? getShapeAnnotations(annotationWorkspaceRef.current).map(shapeToMapShape)
      : [];
    const draftShapes = [
      polygonDraftToMapShape(pendingPolygonDraftRef.current),
      freehandDraftToMapShape(pendingFreehandDraftRef.current),
    ].filter((shape): shape is AnnotationShape => !!shape);
    mapController.setAnnotationShapes([...persistedShapes, ...draftShapes]);
  }

  function currentAnnotationActor(): AnnotationActor {
    if (!options.actorId && canAcceptPublicComments()) {
      return { id: "guest", name: "Guest visitor" };
    }
    return {
      id: options.actorId ?? "anonymous",
      ...(options.actorName ? { name: options.actorName } : {}),
    };
  }

  function currentCollaborationActor(): CollaborationActor {
    const actorId = options.actorId ?? "anonymous";
    return {
      id: actorId,
      displayName: options.actorName ?? "Portal user",
      color: participantColor(actorId),
      role: "edit",
    };
  }

  function collaborationPanelSnapshot(snapshot: CollaborationSnapshot): CollaborationPanelSnapshot {
    const participants = Object.values(snapshot.participants).sort((a, b) =>
      a.displayName.localeCompare(b.displayName),
    );
    const currentParticipant =
      snapshot.participants[snapshot.actorId] ??
      participants.find((participant) => participant.id === snapshot.actorId);
    const currentUser = currentParticipant
      ? collaborationPanelParticipant(currentParticipant, snapshot)
      : {
          id: snapshot.actorId,
          name: "Portal user",
          role: "editor" as const,
          status: "active" as const,
          color: participantColor(snapshot.actorId),
        };

    return {
      currentUser,
      collaborators: participants
        .filter((participant) => participant.id !== snapshot.actorId)
        .map((participant) => collaborationPanelParticipant(participant, snapshot)),
      ...(snapshot.followTarget ? { followingUserId: snapshot.followTarget.participantId } : {}),
      featureActivities: collaborationFeatureActivities(snapshot),
    };
  }

  function collaborationPanelParticipant(
    participant: CollaborationSnapshot["participants"][string],
    snapshot: CollaborationSnapshot,
  ): CollaborationPanelParticipant {
    const selection = Object.values(snapshot.selections).find((entry) => entry.participantId === participant.id);
    const lock = Object.values(snapshot.editLocks).find((entry) => entry.participantId === participant.id);
    const cursor = snapshot.cursors[participant.id];
    return {
      id: participant.id,
      name: participant.displayName,
      role:
        participant.id === savedMapContext?.item.owner.id ? "owner" : participant.role === "edit" ? "editor" : "viewer",
      status: participant.status === "away" ? "idle" : participant.status,
      color: participant.color ?? participantColor(participant.id),
      ...(cursor
        ? {
            cursor: {
              x: cursor.screenPoint?.x ?? 0,
              y: cursor.screenPoint?.y ?? 0,
              label: "Cursor on map",
            },
          }
        : {}),
      ...(lock ? { editing: collaborationPanelFeature(lock.feature) } : {}),
      ...(selection ? { selecting: collaborationPanelFeature(selection.feature) } : {}),
    };
  }

  function collaborationFeatureActivities(snapshot: CollaborationSnapshot): CollaborationFeatureActivity[] {
    const byFeature = new Map<string, CollaborationFeatureActivity>();
    for (const lock of Object.values(snapshot.editLocks)) {
      const activity = getCollaborationActivity(byFeature, lock.feature);
      activity.editingBy = lock.participantId;
    }
    for (const selection of Object.values(snapshot.selections)) {
      const activity = getCollaborationActivity(byFeature, selection.feature);
      activity.selectedBy = [...(activity.selectedBy ?? []), selection.participantId];
    }
    return [...byFeature.values()];
  }

  function getCollaborationActivity(
    byFeature: Map<string, CollaborationFeatureActivity>,
    feature: FeatureRef,
  ): CollaborationFeatureActivity {
    const key = collaborationFeatureKey(feature);
    const existing = byFeature.get(key);
    if (existing) return existing;
    const activity = collaborationPanelFeature(feature);
    byFeature.set(key, activity);
    return activity;
  }

  function collaborationPanelFeature(feature: FeatureRef): CollaborationFeatureRef {
    const layer = findLayer(item, feature.layerId);
    const context = layer ? selectedFeatureContext(layer, feature.featureId) : null;
    return {
      layerId: feature.layerId,
      featureId: feature.featureId,
      label: context?.label ?? feature.featureId,
    };
  }

  function collaborationTableLocks(): FeatureTableCollaborationLock[] {
    const snapshot = collaborationSnapshotRef.current;
    if (!snapshot) return [];
    const locks: FeatureTableCollaborationLock[] = [];
    const seen = new Set<string>();
    for (const lock of Object.values(snapshot.editLocks)) {
      if (lock.participantId === snapshot.actorId) continue;
      const participant = snapshot.participants[lock.participantId];
      if (!participant) continue;
      locks.push({
        layerId: lock.feature.layerId,
        featureId: lock.feature.featureId,
        participantId: participant.id,
        participantName: participant.displayName,
        color: participant.color ?? participantColor(participant.id),
        status: "editing",
      });
      seen.add(collaborationFeatureKey(lock.feature));
    }
    for (const selection of Object.values(snapshot.selections)) {
      if (selection.participantId === snapshot.actorId || seen.has(collaborationFeatureKey(selection.feature)))
        continue;
      const participant = snapshot.participants[selection.participantId];
      if (!participant) continue;
      locks.push({
        layerId: selection.feature.layerId,
        featureId: selection.feature.featureId,
        participantId: participant.id,
        participantName: participant.displayName,
        color: participant.color ?? participantColor(participant.id),
        status: "selecting",
      });
    }
    return locks;
  }

  function selectedFeatureContext(
    layer: PortalViewerLayer,
    featureId: string,
  ): { label: string; lngLat?: [number, number] } | null {
    const features = getSampleSourceFeatures(item, layer.sourceId);
    const found = findFeatureById(features, layer.id, featureId);
    if (!found) return null;
    const lngLat = computeFeatureAnchor(found.feature);
    return {
      label: buildPopupViewModel(layer, found.feature, found.index).title,
      ...(lngLat ? { lngLat } : {}),
    };
  }

  function getNewestAnnotationThreadId(workspace: AnnotationWorkspaceState): string | undefined {
    const newest = workspace.commentThreads.at(-1);
    return typeof newest?.["id"] === "string" ? newest["id"] : undefined;
  }

  function writeUrlHash(): void {
    if (isEmbedMode) return;
    const next = encodeViewerStateToHash(stateRef.current, { itemId: item.metadata.id });
    if (next === window.location.hash) return;
    window.history.replaceState(null, "", `${window.location.pathname}${window.location.search}${next}`);
  }

  if (!isEmbedMode) {
    // The mergeViewerState above already trimmed unknown layers; surface that
    // truth back to the URL on first load so a tweaked link is preserved
    // exactly as the user copied it.
    writeUrlHash();
  }

  return {
    ready,
    dispose: () => {
      hosts.shareButton.removeEventListener("click", handleShareClick);
      hosts.styleButton?.removeEventListener("click", handleStyleEditorClick);
      hosts.styleCloseButton?.removeEventListener("click", handleStyleEditorClose);
      hosts.styleSaveButton?.removeEventListener("click", handleStyleSaveClick);
      hosts.styleTargetSelect?.removeEventListener("change", handleStyleTargetChange);
      window.removeEventListener("message", handleMaputnikMessage);
      if (!isEmbedMode) {
        window.removeEventListener("hashchange", handleHashChange);
      }
      clearCollaborationFeatureClaim();
      unsubscribeCollaboration?.();
      collaborationPanel?.destroy();
      collaborationSession?.dispose();
      mapController.destroy();
    },
  };
}

function collaborationFeatureKey(feature: FeatureRef): string {
  return `${feature.layerId}:${feature.featureId}`;
}

function sameCollaborationFeature(a: FeatureRef, b: FeatureRef): boolean {
  return a.layerId === b.layerId && a.featureId === b.featureId;
}

function participantColor(seed: string): string {
  const colors = ["#4ec9b0", "#f3b562", "#7aa7ff", "#ff9a9a", "#b694ff"];
  let hash = 0;
  for (const char of seed) hash = (hash + char.charCodeAt(0)) % colors.length;
  return colors[hash];
}

function rectangleAround(center: [number, number]): [number, number][] {
  const [lng, lat] = center;
  const lngDelta = 0.004;
  const latDelta = 0.0025;
  return [
    [lng - lngDelta, lat + latDelta],
    [lng + lngDelta, lat + latDelta],
    [lng + lngDelta, lat - latDelta],
    [lng - lngDelta, lat - latDelta],
  ];
}

function shapeToMapShape(annotation: PortalShapeAnnotation): AnnotationShape {
  return {
    id: annotation.id,
    title: annotation.title,
    status: annotation.status,
    geometry:
      annotation.shape === "freehand"
        ? {
            type: "LineString",
            coordinates: annotation.geometry.coordinates,
          }
        : {
            type: "Polygon",
            coordinates: [annotation.geometry.coordinates],
          },
  };
}

function polygonDraftToMapShape(draft: PolygonDraft | undefined): AnnotationShape | null {
  if (!draft || draft.coordinates.length < 2) return null;
  return {
    id: "__polygon-draft",
    title: draft.title,
    status: "open",
    geometry: {
      type: "LineString",
      coordinates: draft.coordinates,
    },
  };
}

function freehandDraftToMapShape(draft: FreehandDraft | undefined): AnnotationShape | null {
  if (!draft || draft.coordinates.length < 2) return null;
  return {
    id: "__freehand-draft",
    title: draft.title,
    status: "open",
    geometry: {
      type: "LineString",
      coordinates: draft.coordinates,
    },
  };
}

function appendFreehandCoordinate(draft: FreehandDraft, lngLat: [number, number]): void {
  const coordinate = normalizeDraftCoordinate(lngLat);
  const previous = draft.coordinates.at(-1);
  if (previous && previous[0] === coordinate[0] && previous[1] === coordinate[1]) return;
  draft.coordinates = [...draft.coordinates, coordinate];
}

function uniqueCoordinateCount(coordinates: readonly [number, number][]): number {
  return new Set(coordinates.map((coordinate) => coordinate.join(","))).size;
}

function normalizeDraftCoordinate([lng, lat]: [number, number]): [number, number] {
  return [roundDraftCoordinate(lng), roundDraftCoordinate(lat)];
}

function roundDraftCoordinate(value: number): number {
  return Math.round(value * 100_000) / 100_000;
}

function boundsToEmbedExtent(bounds: [number, number, number, number] | undefined): EmbedExtent | null {
  if (!bounds) return null;
  const [west, south, east, north] = bounds;
  return { west, south, east, north };
}

function embedExtentToBounds(extent: EmbedExtent): [number, number, number, number] {
  return [extent.west, extent.south, extent.east, extent.north];
}

function isMaputnikBridgeMessage(value: unknown): value is MaputnikBridgeMessage {
  if (!value || typeof value !== "object") return false;
  const type = (value as { type?: unknown }).type;
  return type === "honua:maputnik-ready" || type === "honua:style-change" || type === "honua:maputnik-error";
}

function buildMaputnikStyleId(mapId: string, targetId: string): string {
  const mapPart = slugMaputnikStyleIdPart(mapId) || "map";
  const targetPart = slugMaputnikStyleIdPart(targetId) || "saved-map";
  return `${MAPUTNIK_STYLE_ID_PREFIX}-${mapPart}-${targetPart}`.slice(0, 120);
}

function slugMaputnikStyleIdPart(value: unknown): string {
  return String(value ?? "")
    .toLowerCase()
    .replace(/[^a-z0-9_-]+/g, "-")
    .replace(/^-+|-+$/g, "");
}

async function copyToClipboard(value: string): Promise<boolean> {
  try {
    if (navigator.clipboard && window.isSecureContext) {
      await navigator.clipboard.writeText(value);
      return true;
    }
  } catch {
    // Fall through to legacy path.
  }
  try {
    const textarea = document.createElement("textarea");
    textarea.value = value;
    textarea.setAttribute("readonly", "");
    textarea.style.position = "absolute";
    textarea.style.left = "-9999px";
    document.body.appendChild(textarea);
    textarea.select();
    const ok = document.execCommand("copy");
    document.body.removeChild(textarea);
    return ok;
  } catch {
    return false;
  }
}

function downloadAnnotationExport(exported: SerializedAnnotationExport): void {
  const blob = new Blob([exported.text], { type: exported.mediaType });
  const url = URL.createObjectURL(blob);
  const link = document.createElement("a");
  link.href = url;
  link.download = exported.filename;
  link.rel = "noopener";
  document.body.appendChild(link);
  link.click();
  link.remove();
  URL.revokeObjectURL(url);
}
