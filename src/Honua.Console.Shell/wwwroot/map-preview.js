// Minimal MapLibre GL interop for the shared MapPreview component.
//
// The Console has no bundled MapLibre dependency yet, so this module loads MapLibre GL
// lazily from a CDN ONLY when a style URL is provided. When MapLibre is unavailable
// (offline, no style bound, or a non-browser host such as the bUnit render harness),
// every entry point fails gracefully: the .NET component keeps its inline SVG schematic
// placeholder and never throws. This preserves the no-mock / missing-binding contract —
// a live map appears only when a real style source is bound.

const MAPLIBRE_JS = 'https://unpkg.com/maplibre-gl@4.7.1/dist/maplibre-gl.js';
const MAPLIBRE_CSS = 'https://unpkg.com/maplibre-gl@4.7.1/dist/maplibre-gl.css';
// Subresource Integrity: this module runs in the privileged Console origin (Blazor session +
// admin-keyed BFF proxy), so a tampered CDN asset would execute with full access. Pin the exact
// bytes for the versioned URLs above; a mismatched/altered asset fails to load instead of running.
const MAPLIBRE_JS_SRI = 'sha384-SYKAG6cglRMN0RVvhNeBY0r3FYKNOJtznwA0v7B5Vp9tr31xAHsZC0DqkQ/pZDmj';
const MAPLIBRE_CSS_SRI = 'sha384-MinO0mNliZ3vwppuPOUnGa+iq619pfMhLVUXfC4LHwSCvF9H+6P/KO4Q7qBOYV5V';

const instances = new Map();
let maplibrePromise = null;

function loadMapLibre() {
    if (typeof window === 'undefined' || typeof document === 'undefined') {
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
            if (!document.querySelector('link[data-honua-maplibre]')) {
                const link = document.createElement('link');
                link.rel = 'stylesheet';
                link.href = MAPLIBRE_CSS;
                link.integrity = MAPLIBRE_CSS_SRI;
                link.crossOrigin = 'anonymous';
                link.setAttribute('data-honua-maplibre', '');
                document.head.appendChild(link);
            }
            const script = document.createElement('script');
            script.src = MAPLIBRE_JS;
            script.integrity = MAPLIBRE_JS_SRI;
            script.crossOrigin = 'anonymous';
            script.async = true;
            script.onload = () => resolve(window.maplibregl ?? null);
            script.onerror = () => resolve(null);
            document.head.appendChild(script);
        } catch {
            resolve(null);
        }
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

// Once the map first goes idle, frame the camera on the layer's actual rendered features.
// Uses real geometry queried from the live map (never a synthetic extent); if no features are
// present (empty layer, or tiles unreachable) the initial world view is left untouched. Runs once.
function fitToFeaturesOnce(map) {
    let done = false;
    const onIdle = () => {
        if (done) {
            return;
        }
        done = true;
        map.off('idle', onIdle);
        try {
            // Only the style's own feature layers — exclude the injected OSM raster basemap.
            const featureLayerIds = ((map.getStyle().layers) || [])
                .filter(l => l.id !== 'osm-basemap' && l.type !== 'raster' && l.type !== 'background')
                .map(l => l.id);
            if (featureLayerIds.length === 0) {
                return;
            }
            const features = map.queryRenderedFeatures({ layers: featureLayerIds });
            if (!features || features.length === 0) {
                return;
            }
            const b = [Infinity, Infinity, -Infinity, -Infinity];
            for (const f of features) {
                if (f.geometry && f.geometry.coordinates) {
                    expandBounds(b, f.geometry.coordinates);
                }
            }
            if (b[0] <= b[2] && b[1] <= b[3] && Number.isFinite(b[0])) {
                map.fitBounds([[b[0], b[1]], [b[2], b[3]]], { padding: 40, maxZoom: 16, duration: 0 });
                // Make this fitted view the home position for the Recenter control.
                map._homeCenter = map.getCenter().toArray();
                map._homeZoom = map.getZoom();
            }
        } catch {
            /* auto-fit is best-effort; never break the render */
        }
    };
    map.on('idle', onIdle);
}

// Attempts to mount a live MapLibre map into the given container.
// Returns true when a real map was bound; false when the component should keep its placeholder.
export async function init(container, options) {
    if (!container || !options || !options.styleUrl) {
        return false;
    }
    const maplibregl = await loadMapLibre();
    if (!maplibregl) {
        return false;
    }
    try {
        const map = new maplibregl.Map({
            container,
            style: options.styleUrl,
            center: options.center ?? [0, 0],
            zoom: options.zoom ?? 1,
            attributionControl: true,
        });
        if (options.showScale) {
            map.addControl(new maplibregl.ScaleControl({ unit: 'metric' }), 'bottom-right');
        }
        map.addControl(new maplibregl.NavigationControl({ showCompass: true }), 'top-left');
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
            // Frame the camera on the layer's data. Preferred source is the server-provided extent
            // ([minLng, minLat, maxLng, maxLat] in EPSG:4326, from the layer's cached metadata extent),
            // which is reliable even at a whole-world start where the vector tiles are empty. When no
            // extent is supplied, fall back to fitting the actual rendered features once the map idles.
            // Both use real geometry only — never a synthetic extent.
            const b = options.bounds;
            if (Array.isArray(b) && b.length === 4 && b.every(n => Number.isFinite(n))) {
                try {
                    map.fitBounds([[b[0], b[1]], [b[2], b[3]]], { padding: 40, maxZoom: 16, duration: 0 });
                    map._homeCenter = map.getCenter().toArray();
                    map._homeZoom = map.getZoom();
                } catch {
                    fitToFeaturesOnce(map);
                }
            } else {
                fitToFeaturesOnce(map);
            }
        });
        // Remember the initial view so the custom Recenter control can restore it.
        map._homeCenter = options.center ?? [0, 0];
        map._homeZoom = options.zoom ?? 1;
        instances.set(container, map);
        return true;
    } catch {
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

// Recenter returns the live map to the initial center/zoom it was mounted with.
export function recenter(container, center, zoom) {
    const map = instances.get(container);
    if (map) {
        try {
            map.easeTo({ center: center ?? [0, 0], zoom: zoom ?? 1 });
            return true;
        } catch {
            return false;
        }
    }
    return false;
}

export function dispose(container) {
    const map = instances.get(container);
    if (map) {
        try {
            map.remove();
        } catch {
            /* ignore */
        }
        instances.delete(container);
    }
}
