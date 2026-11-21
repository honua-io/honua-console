// Minimal MapLibre GL interop for the shared MapPreview component.
//
// MapLibre GL is VENDORED (honua-console#333): it is a committed, version-pinned asset served from
// this same origin, never fetched from a CDN at page load. This module runs in the privileged
// Console origin (Blazor session + admin-keyed BFF proxy), so third-party code fetched at runtime
// would execute with full session and proxy access; it also meant an air-gapped or egress-restricted
// deployment got a broken map preview, and the CSP had to admit a script origin nothing else needed.
// The pinned version and the update procedure live in scripts/vendored-assets.json — see
// wwwroot/vendor/maplibre-gl/README.md. `npm test` fails if the committed bytes ever drift.
//
// The library is still loaded LAZILY, only when a style URL is provided: MapLibre is ~800 KB and
// most Console pages never mount a map. When it is unavailable (no style bound, or a non-browser
// host such as the bUnit render harness), every entry point fails gracefully — the .NET component
// keeps its inline SVG schematic placeholder and never throws. This preserves the no-mock /
// missing-binding contract: a live map appears only when a real style source is bound.

import './map-fetch-guard.js';

// Resolved against document.baseURI rather than a root-absolute path so the Console still works
// when hosted under a path prefix, and against the DOCUMENT rather than import.meta.url because
// this module is imported from a blob: URL in some hosts (see e2e map-preview-interop.spec.ts),
// where relative resolution would have nothing to resolve against.
const VENDOR_BASE = '_content/Honua.Console.Shell/vendor/maplibre-gl/';
const maplibreAsset = (file) =>
    (typeof document !== 'undefined' ? new URL(`${VENDOR_BASE}${file}`, document.baseURI).href : file);

// No Subresource Integrity here, deliberately: SRI exists to constrain bytes served by an origin you
// do not control, and these bytes are this origin's own static assets. Their integrity is pinned
// where it is actually verifiable — sha384 digests in scripts/vendored-assets.lock.json, checked at
// test time against the committed files.

const instances = new Map();
// A weak token also covers containers whose map has not been constructed yet.
const initializations = new WeakMap();
let maplibrePromise = null;

function ensureMapLibreStyles() {
    if (document.querySelector('link[data-honua-maplibre]')) return;
    const link = document.createElement('link');
    link.rel = 'stylesheet';
    link.href = maplibreAsset('maplibre-gl.css');
    link.setAttribute('data-honua-maplibre', '');
    link.onerror = () => link.remove();
    document.head.appendChild(link);
}

function loadMapLibre() {
    if (typeof window === 'undefined' || typeof document === 'undefined') {
        return Promise.resolve(null);
    }
    try {
        ensureMapLibreStyles();
    } catch {
        return Promise.resolve(null);
    }
    if (window.maplibregl) {
        return Promise.resolve(window.maplibregl);
    }
    if (maplibrePromise) {
        return maplibrePromise;
    }
    maplibrePromise = new Promise((resolve) => {
        try {
            const script = document.createElement('script');
            script.src = maplibreAsset('maplibre-gl.js');
            script.async = true;
            script.onload = () => resolve(window.maplibregl ?? null);
            script.onerror = () => resolve(null);
            document.head.appendChild(script);
        } catch {
            resolve(null);
        }
    }).then(runtime => {
        if (!runtime) maplibrePromise = null;
        return runtime;
    });
    return maplibrePromise;
}

// Walks a GeoJSON geometry's coordinates, expanding [minLng, minLat, maxLng, maxLat] in place.
function expandBounds(b, coords) {
    if (typeof coords[0] === 'number') {
        const [lng, lat] = coords;
        if (Number.isFinite(lng) && Number.isFinite(lat)) {
            if (lng < b[0]) b[0] = lng;
            if (lat < b[1]) b[1] = lat;
            if (lng > b[2]) b[2] = lng;
            if (lat > b[3]) b[3] = lat;
        }
        return;
    }
    for (const c of coords) {
        expandBounds(b, c);
    }
}

// A usable camera frame is a finite WGS84 box. Projected metres and the whole-world box are
// not a layer frame; an all-zero box is an unbound extent, not a point at the Gulf of Guinea.
function geographicBounds(value) {
    if (!Array.isArray(value) || value.length !== 4 || !value.every(n => Number.isFinite(n))) {
        return null;
    }
    const [west, south, east, north] = value;
    if (west < -180 || east > 180 || south < -90 || north > 90 || east < west || north < south) {
        return null;
    }
    if (west === 0 && south === 0 && east === 0 && north === 0) {
        return null;
    }
    if ((east - west) >= 350 && (north - south) >= 170) {
        return null;
    }
    return [west, south, east, north];
}

// Padding must stay inside the canvas. A 390px preview is short enough that a fixed 40px
// pad makes fitBounds throw, which used to abandon the frame.
function paddingFor(map) {
    const canvas = typeof map.getCanvas === 'function' ? map.getCanvas() : null;
    // No canvas probe (unit doubles) means a normal preview. A reported 0 is an unlaid-out pane.
    const width = canvas ? Number(canvas.clientWidth ?? canvas.width ?? 0) : 800;
    const height = canvas ? Number(canvas.clientHeight ?? canvas.height ?? 0) : 600;
    if (!Number.isFinite(width) || !Number.isFinite(height) || width < 16 || height < 16) {
        return null;
    }
    return Math.max(0, Math.min(40, Math.floor(width / 2) - 8, Math.floor(height / 2) - 8));
}

function rememberFittedHome(map) {
    map._homeCenter = map.getCenter().toArray();
    map._homeZoom = map.getZoom();
}

function fitGeographic(map, bounds) {
    const padding = paddingFor(map);
    if (padding === null) {
        return false;
    }
    map.fitBounds([[bounds[0], bounds[1]], [bounds[2], bounds[3]]], { padding, maxZoom: 16, duration: 0 });
    rememberFittedHome(map);
    return true;
}

function renderedFeatureBounds(map) {
    const layers = (map.getStyle().layers) || [];
    const featureLayers = layers.filter(l => l.id !== 'osm-basemap' && l.type !== 'raster' && l.type !== 'background');
    if (featureLayers.length === 0) {
        return null;
    }
    const rendered = typeof map.queryRenderedFeatures === 'function'
        ? map.queryRenderedFeatures({ layers: featureLayers.map(l => l.id) }) || []
        : [];
    const sourced = typeof map.querySourceFeatures === 'function'
        ? featureLayers.flatMap(layer => {
            if (!layer.source) return [];
            try {
                return map.querySourceFeatures(layer.source, { sourceLayer: layer['source-layer'] }) || [];
            } catch {
                return [];
            }
        })
        : [];
    const b = [Infinity, Infinity, -Infinity, -Infinity];
    for (const feature of [...rendered, ...sourced]) {
        if (feature.geometry && feature.geometry.coordinates) {
            expandBounds(b, feature.geometry.coordinates);
        }
    }
    return geographicBounds(b);
}

function declaredSourceBounds(map) {
    const sources = map.getStyle()?.sources || {};
    const acc = [Infinity, Infinity, -Infinity, -Infinity];
    let any = false;
    for (const [id, declared] of Object.entries(sources)) {
        if (!declared || declared.type === 'raster' || declared.type === 'image' || id === 'osm-basemap') {
            continue;
        }
        const live = typeof map.getSource === 'function' ? map.getSource(id) : null;
        let raw = null;
        if (live?.bounds && typeof live.bounds.getWest === 'function') {
            raw = [live.bounds.getWest(), live.bounds.getSouth(), live.bounds.getEast(), live.bounds.getNorth()];
        } else if (Array.isArray(live?.bounds)) {
            raw = live.bounds;
        } else if (Array.isArray(declared.bounds)) {
            raw = declared.bounds;
        }
        const geo = geographicBounds(raw);
        if (!geo) continue;
        any = true;
        acc[0] = Math.min(acc[0], geo[0]);
        acc[1] = Math.min(acc[1], geo[1]);
        acc[2] = Math.max(acc[2], geo[2]);
        acc[3] = Math.max(acc[3], geo[3]);
    }
    return any ? acc : null;
}

// The first idle often fires before vector tiles arrive, and polygon tiles are empty at the
// starting world zoom. Keep framing until a real extent, loaded source bounds, or features
// appear, or until the preview has had long enough to load. An empty layer stays at the
// initial camera.
function frameLayer(map, explicitBounds) {
    const started = Date.now();
    let done = false;
    const finish = () => {
        if (done) return;
        done = true;
        map.off('idle', onFrame);
        map.off('sourcedata', onFrame);
    };
    const onFrame = () => {
        if (done) return;
        try {
            const chosen = geographicBounds(explicitBounds) || declaredSourceBounds(map) || renderedFeatureBounds(map);
            if (chosen && fitGeographic(map, chosen)) {
                finish();
                return;
            }
        } catch {
            /* a short canvas or an unloaded style retries on the next idle */
        }
        if (Date.now() - started > 12000) finish();
    };
    map.on('idle', onFrame);
    map.on('sourcedata', onFrame);
    onFrame();
}

export function mapRequestParameters(authorize, url) {
    const approved = authorize(url);
    return { url: approved.url, credentials: approved.credentials, headers: approved.headers };
}

export async function requireMapWorkerGuard(maplibregl) {
    if (typeof maplibregl?.prewarm !== 'function' || typeof maplibregl?.importScriptInWorkers !== 'function') {
        throw new Error('Map workers cannot be guarded');
    }
    maplibregl.prewarm();
    await maplibregl.importScriptInWorkers(new URL('./map-fetch-guard.js', import.meta.url).href);
}

export function bindMapSession(map, authorize, subscribe) {
    if (!map || typeof authorize?.current !== 'function' || typeof subscribe !== 'function') return () => {};
    let stop = () => {};
    const listener = () => {
        if (authorize.current()) return;
        stop();
        try {
            map.remove();
        } catch {
            // The map is already gone; the session change still stands.
        }
    };
    stop = subscribe(listener) || (() => {});
    return () => stop();
}

const sessionReleases = new Map();

// Attempts to mount a live MapLibre map into the given container.
// Returns the mounted resources, or null when initialization was superseded.
async function mountAuthorizedMap(maplibregl, { container, options, isCurrent }) {
    const capture = globalThis.honuaConsoleCaptureResourceAuthorization;
    const authorize = typeof capture === 'function' ? await capture() : null;
    if (!isCurrent()) return null;
    const mapOptions = {
        container,
        style: options.styleUrl,
        center: options.center ?? [0, 0],
        zoom: options.zoom ?? 1,
        attributionControl: true,
    };
    if (authorize) {
        mapOptions.transformRequest = (url) => mapRequestParameters(authorize, url);
        await requireMapWorkerGuard(maplibregl);
    }
    if (!isCurrent()) return null;
    const map = new maplibregl.Map(mapOptions);
    return { map, releaseSession: bindMapSession(map, authorize, globalThis.honuaConsoleSubscribeSession) };
}

function configureMapPreview(maplibregl, map, options) {
    if (options.showScale) {
        map.addControl(new maplibregl.ScaleControl({ unit: 'metric' }), 'bottom-right');
    }
    // The Console's own zoom and recenter buttons sit over the top-left corner, so a native
    // NavigationControl there would be hidden and unclickable. Rotation is off, so no compass is needed.
    map.dragRotate?.disable();
    map.touchZoomRotate?.disableRotation();
    map.keyboard?.disableRotation();
    map.touchPitch?.disable();
    // The server layer style is feature-only (fill/outline) with no basemap, so the map renders
    // on a blank background. Inject a free, no-API-key OpenStreetMap raster basemap UNDER the
    // feature layers when the loaded style carries no raster basemap of its own, so the data has
    // geographic context. (OSM tiles require network; if unreachable the features still render.)
    map.on('load', () => {
        try {
            const style = map.getStyle();
            const hasRaster = style && style.sources
                && Object.values(style.sources).some(s => s && s.type === 'raster');
            if (!hasRaster && !map.getSource('osm-basemap')) {
                map.addSource('osm-basemap', {
                    type: 'raster',
                    tiles: ['https://tile.openstreetmap.org/{z}/{x}/{y}.png'],
                    tileSize: 256,
                    attribution: '© OpenStreetMap contributors',
                });
                const layers = (map.getStyle().layers) || [];
                const firstLayerId = layers.length > 0 ? layers[0].id : undefined;
                map.addLayer({ id: 'osm-basemap', type: 'raster', source: 'osm-basemap' }, firstLayerId);
            }
        } catch {
            /* basemap is best-effort; never break the feature render */
        }
        // Frame on the server extent when it is a real WGS84 box. Otherwise wait for TileJSON
        // source bounds or for features that are not in the first world-zoom tile.
        frameLayer(map, options.bounds);
    });
    // Remember the initial view so the custom Recenter control can restore it.
    map._homeCenter = options.center ?? [0, 0];
    map._homeZoom = options.zoom ?? 1;
}

export async function init(container, options) {
    if (!container || !options || !options.styleUrl) {
        return false;
    }
    dispose(container);
    const token = {};
    initializations.set(container, token);
    const isCurrent = () => initializations.get(container) === token;
    const maplibregl = await loadMapLibre();
    if (!maplibregl || !isCurrent()) {
        return false;
    }
    let releaseSession = () => {};
    let map;
    try {
        const mounted = await mountAuthorizedMap(maplibregl, { container, options, isCurrent });
        if (!mounted) return false;
        map = mounted.map;
        releaseSession = mounted.releaseSession;
        if (!isCurrent()) {
            releaseSession();
            try { map.remove(); } catch { /* teardown is best-effort */ }
            return false;
        }
        configureMapPreview(maplibregl, map, options);
        sessionReleases.set(map, releaseSession);
        instances.set(container, map);
        return true;
    } catch {
        releaseSession();
        try { map?.remove(); } catch { /* teardown is best-effort */ }
        return false;
    }
}

export function setBasemap(container, styleUrl) {
    const map = instances.get(container);
    if (map && styleUrl) {
        try {
            map.setStyle(styleUrl);
            return true;
        } catch {
            return false;
        }
    }
    return false;
}

// Zoom / recenter controls for the schematic-overlay chrome. These only do something when a live
// MapLibre map is bound for the container; otherwise they no-op (the static schematic has no camera).
// Returns true when the live map handled the gesture, false when there was no live map to drive.
export function zoomIn(container) {
    const map = instances.get(container);
    if (map) {
        try {
            map.zoomIn();
            return true;
        } catch {
            return false;
        }
    }
    return false;
}

export function zoomOut(container) {
    const map = instances.get(container);
    if (map) {
        try {
            map.zoomOut();
            return true;
        } catch {
            return false;
        }
    }
    return false;
}

// Recenter restores the fitted layer view, or the initial camera when no extent was available.
export function recenter(container, center, zoom) {
    const map = instances.get(container);
    if (map) {
        try {
            map.easeTo({ center: map._homeCenter ?? center ?? [0, 0], zoom: map._homeZoom ?? zoom ?? 1, bearing: 0, pitch: 0 });
            return true;
        } catch {
            return false;
        }
    }
    return false;
}

export function dispose(container) {
    initializations.delete(container);
    const map = instances.get(container);
    if (map) {
        sessionReleases.get(map)?.();
        sessionReleases.delete(map);
        try {
            map.remove();
        } catch {
            /* ignore */
        }
        instances.delete(container);
    }
}
