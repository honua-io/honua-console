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
                link.setAttribute('data-honua-maplibre', '');
                document.head.appendChild(link);
            }
            const script = document.createElement('script');
            script.src = MAPLIBRE_JS;
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

export function zoomIn(container) {
    const map = instances.get(container);
    if (map) {
        try { map.zoomIn(); } catch { /* ignore */ }
    }
}

export function zoomOut(container) {
    const map = instances.get(container);
    if (map) {
        try { map.zoomOut(); } catch { /* ignore */ }
    }
}

export function recenter(container) {
    const map = instances.get(container);
    if (map) {
        try { map.easeTo({ center: map._homeCenter ?? [0, 0], zoom: map._homeZoom ?? 1 }); } catch { /* ignore */ }
    }
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
