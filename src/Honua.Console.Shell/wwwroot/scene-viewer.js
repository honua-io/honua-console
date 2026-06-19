// Minimal CesiumJS interop for the shared SceneViewer (3D Tiles) component.
//
// The Console has no bundled Cesium dependency, so this module loads CesiumJS
// lazily from a CDN ONLY when a tileset URL is provided. When Cesium is
// unavailable (offline, blocked CDN, no tileset bound, or a non-browser host
// such as the bUnit render harness), every entry point fails gracefully: the
// .NET component keeps its inline SVG schematic placeholder and never throws.
// This preserves the no-mock / missing-binding contract — a live 3D scene
// appears only when a real tileset is bound.

const CESIUM_JS = 'https://cdn.jsdelivr.net/npm/cesium@1.119.0/Build/Cesium/Cesium.js';
const CESIUM_CSS = 'https://cdn.jsdelivr.net/npm/cesium@1.119.0/Build/Cesium/Widgets/widgets.css';
const CESIUM_BASE_URL = 'https://cdn.jsdelivr.net/npm/cesium@1.119.0/Build/Cesium/';

const instances = new Map();
let cesiumPromise = null;

function loadCesium() {
    if (typeof window === 'undefined' || typeof document === 'undefined') {
        return Promise.resolve(null);
    }
    if (window.Cesium) {
        return Promise.resolve(window.Cesium);
    }
    if (cesiumPromise) {
        return cesiumPromise;
    }

    window.CESIUM_BASE_URL = CESIUM_BASE_URL;

    cesiumPromise = new Promise((resolve) => {
        try {
            if (!document.querySelector(`link[data-cesium-css]`)) {
                const link = document.createElement('link');
                link.rel = 'stylesheet';
                link.href = CESIUM_CSS;
                link.setAttribute('data-cesium-css', 'true');
                document.head.appendChild(link);
            }

            const script = document.createElement('script');
            script.src = CESIUM_JS;
            script.async = true;
            script.onload = () => resolve(window.Cesium ?? null);
            script.onerror = () => resolve(null);
            document.head.appendChild(script);
        } catch {
            resolve(null);
        }
    });

    return cesiumPromise;
}

export async function init(element, tilesetUrl) {
    if (!element || !tilesetUrl) {
        return false;
    }

    const Cesium = await loadCesium();
    if (!Cesium) {
        return false;
    }

    try {
        const viewer = new Cesium.Viewer(element, {
            baseLayerPicker: false,
            geocoder: false,
            homeButton: false,
            sceneModePicker: false,
            navigationHelpButton: false,
            animation: false,
            timeline: false,
            fullscreenButton: false,
            infoBox: false,
        });

        const tileset = await Cesium.Cesium3DTileset.fromUrl(tilesetUrl);
        viewer.scene.primitives.add(tileset);
        await viewer.zoomTo(tileset);

        instances.set(element, viewer);
        return true;
    } catch {
        return false;
    }
}

export function dispose(element) {
    const viewer = instances.get(element);
    if (viewer) {
        try {
            viewer.destroy();
        } catch {
            // Ignore teardown failures.
        }
        instances.delete(element);
    }
}
