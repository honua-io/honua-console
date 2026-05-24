/**
 * Browser fixture saved-map renderer.
 *
 * The durable server endpoints are still tracked outside this portal ticket,
 * so the implementation pass needs a bounded, reloadable fixture path for
 * `/maps/:id` and `/embed/maps/:id`. The fixture stores only the WebMapDoc in
 * localStorage; all service/source projection still comes from the existing
 * viewer fixture so the MapLibre source definitions stay identical across
 * viewer, editor preview, share, and embed.
 */

import { buildSamplePortalItem } from "../catalog/sample-portal-item.js";
import type { PortalViewerInitialView, PortalViewerItem, PortalViewerLayer } from "../viewer/types.js";
import { resolveSavedMapStyle } from "./style-overrides.js";
import { type SavedMapItem, WEBMAP_DOC_VERSION, type WebMapDoc } from "./types.js";

export const STYLE_EDITOR_DEMO_MAP_ID = "map-style-demo";
export const STYLE_EDITOR_DEMO_CONTENT_ITEM_ID = "01HXY3ZK7N1J2Q9V8M0FQ2PWAR";

const DEMO_SOURCE_LAYER_ID = "01HXY3ZK7N1J2Q9V8M0FQ2PWAC";
const DEMO_STYLE_DOCUMENT_ID = "01HXY3ZK7N1J2Q9V8M0FQ2PWAH";
const DEMO_BASEMAP_SERVICE_ID = "01HXY3ZK7N1J2Q9V8M0FQ2PWAE";
const DEMO_CONSOLE_ORIGIN = "https://console.honua.example";

interface StoredSavedMap {
  doc: WebMapDoc;
  modified: string;
}

interface SavedMapViewerRecord {
  item: SavedMapItem;
  doc: WebMapDoc;
  viewerItem: PortalViewerItem;
}

export type SavedMapViewerLoadResult =
  | ({ status: "ok" } & SavedMapViewerRecord)
  | { status: "missing"; id: string }
  | { status: "unsupported"; id: string; reason: string };

const STORAGE_PREFIX = "honua.portal.saved-map.";
const DEMO_CREATED = "2026-05-08T00:00:00.000Z";

export function loadFixtureSavedMapForViewer(
  id: string,
  storage: Storage | null = browserStorage(),
): SavedMapViewerLoadResult {
  if (!isDemoSavedMapId(id)) return { status: "missing", id };

  const stored = readStoredSavedMap(id, storage);
  const doc = stored?.doc ?? buildDemoWebMapDoc();
  if (doc.version !== WEBMAP_DOC_VERSION) {
    return { status: "unsupported", id, reason: `Unsupported WebMapDoc version: ${doc.version}` };
  }

  const modified = stored?.modified ?? DEMO_CREATED;
  const item = buildDemoSavedMapItem(doc, modified);
  return {
    status: "ok",
    item,
    doc,
    viewerItem: buildPortalViewerItemFromSavedMap(item, doc),
  };
}

export function saveFixtureSavedMapDoc(
  id: string,
  doc: WebMapDoc,
  storage: Storage | null = browserStorage(),
  now: () => Date = () => new Date(),
): SavedMapItem {
  if (!isDemoSavedMapId(id)) {
    throw new Error(`Saved-map fixture cannot write unknown id: ${id}`);
  }
  const modified = now().toISOString();
  writeStoredSavedMap(id, { doc, modified }, storage);
  return buildDemoSavedMapItem(doc, modified);
}

export function buildPortalViewerItemFromSavedMap(item: SavedMapItem, doc: WebMapDoc): PortalViewerItem {
  const base = buildSamplePortalItem();
  const layerById = new Map(base.layers.map((layer) => [layer.id, layer]));
  const operationalById = new Map(doc.operationalLayers.map((layer) => [layer.id, layer]));
  const orderedLayers: PortalViewerLayer[] = [];

  for (const opLayer of doc.operationalLayers) {
    const baseLayer = layerById.get(opLayer.id);
    if (!baseLayer) continue;
    orderedLayers.push({
      ...baseLayer,
      name: opLayer.title,
      defaultVisible: opLayer.visibility,
      defaultOpacity: opLayer.opacity,
      popup: opLayer.popupInfo ? (opLayer.popupInfo as unknown as PortalViewerLayer["popup"]) : baseLayer.popup,
    });
  }

  for (const baseLayer of base.layers) {
    if (!operationalById.has(baseLayer.id)) orderedLayers.push(baseLayer);
  }

  return {
    ...base,
    metadata: {
      ...base.metadata,
      id: item.id,
      title: item.title,
      summary: item.summary,
      description: item.description,
      owner: item.owner.name,
      modified: item.timestamps.modified,
      itemUrl: item.endpoints.self.accessURL,
    },
    style: resolveSavedMapStyle(doc, base.style),
    layers: orderedLayers,
    initialView: initialViewFromDoc(doc) ?? base.initialView,
  };
}

export function buildDemoWebMapDoc(): WebMapDoc {
  return {
    version: WEBMAP_DOC_VERSION,
    authoringApp: "honua-portal",
    authoringAppVersion: "fixture-style-editor",
    operationalLayers: [
      {
        id: "districts",
        title: "Districts",
        layerType: "geojson",
        sourceRef: { itemId: DEMO_SOURCE_LAYER_ID, subLayerId: "districts-source" },
        styleRef: { itemId: DEMO_STYLE_DOCUMENT_ID, origin: "admin-layer-style" },
        visibility: true,
        opacity: 0.32,
        popupInfo: { title: "{NAME}" },
        minScale: null,
        maxScale: null,
      },
      {
        id: "field-stations",
        title: "Field stations",
        layerType: "geojson",
        sourceRef: { itemId: DEMO_SOURCE_LAYER_ID, subLayerId: "field-stations-source" },
        styleRef: { itemId: DEMO_STYLE_DOCUMENT_ID, origin: "admin-layer-style" },
        visibility: true,
        opacity: 1,
        popupInfo: { title: "{NAME}" },
        minScale: null,
        maxScale: null,
      },
    ],
    baseMap: {
      title: "MapLibre demo tiles",
      baseMapLayers: [
        {
          id: "demotiles-basemap",
          title: "MapLibre demo tiles",
          layerType: "honua-raster-tile",
          sourceRef: { itemId: DEMO_BASEMAP_SERVICE_ID },
          visibility: true,
          opacity: 1,
        },
      ],
    },
    initialState: {
      viewpoint: {
        extent: {
          xmin: -157.91,
          ymin: 21.26,
          xmax: -157.77,
          ymax: 21.35,
          spatialReference: { wkid: 4326 },
        },
        rotation: 0,
      },
    },
    spatialReference: { wkid: 4326 },
  };
}

function buildDemoSavedMapItem(doc: WebMapDoc, modified: string): SavedMapItem {
  return {
    id: STYLE_EDITOR_DEMO_CONTENT_ITEM_ID,
    slug: "honolulu-style-demo",
    type: "map",
    title: "Honolulu style demo",
    summary: "Saved map fixture for portal style editing.",
    description: "Saved map fixture for portal style editing.",
    tags: ["sample", "style-editor"],
    owner: { id: "u-member", name: "Mira Chen", kind: "user" },
    timestamps: { created: DEMO_CREATED, modified, published: null, refreshed: null },
    extent: { bbox: [-157.91, 21.26, -157.77, 21.35], crs: "EPSG:4326" },
    nativeCrs: null,
    license: {
      spdx: "CC-BY-4.0",
      name: "Creative Commons Attribution 4.0",
      url: "https://spdx.org/licenses/CC-BY-4.0.html",
    },
    attribution: "Honua sample data; MapLibre demo tiles",
    source: {
      kind: "manual",
      sourceId: DEMO_SOURCE_LAYER_ID,
      jobId: null,
      publishedBy: "u-member",
      history: [{ at: DEMO_CREATED, kind: "manual", actor: "u-member" }],
    },
    target: {
      type: "map",
      webmapJsonRef: `/api/v1/portal/maps/${STYLE_EDITOR_DEMO_CONTENT_ITEM_ID}/webmap`,
      operationalLayerCount: doc.operationalLayers.length,
    },
    endpoints: {
      self: {
        accessURL: new URL(`/maps/${STYLE_EDITOR_DEMO_CONTENT_ITEM_ID}`, DEMO_CONSOLE_ORIGIN).toString(),
        format: "Honua:Portal:v1",
        mediaType: "text/html",
        describedBy: null,
        describedByType: null,
        conformsTo: ["https://schemas.honua.io/content-item/v1"],
      },
      geoservices: null,
      ogcFeatures: null,
      stac: null,
      tiles: null,
    },
    preview: { thumbnail: null, image: null },
    capabilities: ["render"],
    dependencies: [
      { id: DEMO_SOURCE_LAYER_ID, type: "layer", role: "operationalLayer" },
      { id: DEMO_STYLE_DOCUMENT_ID, type: "document", role: "style" },
      { id: DEMO_BASEMAP_SERVICE_ID, type: "service", role: "baseMap" },
    ],
    access: { sharing: "public-link", embeddable: true, openData: false },
    extensions: {
      "honua:styleEditing": {
        effectiveOrigin: doc.operationalLayers.some((layer) => layer.styleRef?.origin === "portal-override")
          ? "portal-override"
          : "admin-layer-style",
      },
    },
  };
}

function isDemoSavedMapId(id: string): boolean {
  return id === STYLE_EDITOR_DEMO_MAP_ID || id === STYLE_EDITOR_DEMO_CONTENT_ITEM_ID;
}

function initialViewFromDoc(doc: WebMapDoc): PortalViewerInitialView | null {
  const extent = doc.initialState.viewpoint.extent;
  const bounds: [number, number, number, number] = [extent.xmin, extent.ymin, extent.xmax, extent.ymax];
  return {
    bounds,
    center: [(extent.xmin + extent.xmax) / 2, (extent.ymin + extent.ymax) / 2],
    zoom: 11.5,
  };
}

function readStoredSavedMap(id: string, storage: Storage | null): StoredSavedMap | null {
  if (!storage) return null;
  for (const key of storageKeys(id)) {
    try {
      const raw = storage.getItem(key);
      if (!raw) continue;
      const parsed = JSON.parse(raw) as StoredSavedMap;
      if (parsed?.doc?.version === WEBMAP_DOC_VERSION && parsed.modified) return parsed;
    } catch {
      return null;
    }
  }
  return null;
}

function writeStoredSavedMap(id: string, record: StoredSavedMap, storage: Storage | null): void {
  if (!storage) return;
  storage.setItem(storageKey(canonicalStorageId(id)), JSON.stringify(record));
}

function storageKey(id: string): string {
  return `${STORAGE_PREFIX}${id}`;
}

function storageKeys(id: string): string[] {
  const canonical = canonicalStorageId(id);
  if (canonical === STYLE_EDITOR_DEMO_MAP_ID) return [storageKey(canonical)];
  return [storageKey(canonical), storageKey(STYLE_EDITOR_DEMO_MAP_ID)];
}

function canonicalStorageId(id: string): string {
  return id === STYLE_EDITOR_DEMO_MAP_ID ? STYLE_EDITOR_DEMO_CONTENT_ITEM_ID : id;
}

function browserStorage(): Storage | null {
  try {
    return typeof window === "undefined" ? null : window.localStorage;
  } catch {
    return null;
  }
}
