// Minimal CesiumJS interop for the shared SceneViewer (3D Tiles) component.
//
// Cesium is served from THIS origin, never a CDN (honua-console#334). Its
// Build/Cesium tree is ~69 MB across Workers/, Assets/, ThirdParty/, and
// Widgets/, resolved dynamically through window.CESIUM_BASE_URL, so committing
// it would put that weight in every clone and CI checkout forever. Instead it is
// fetched at deploy/build time by scripts/fetch-cesium.mjs into this Razor class
// library's wwwroot/vendor/cesium tree (gitignored), which publishes under the
// standard _content/Honua.Console.Shell static-asset prefix.
//
// When those bytes are absent — a checkout that never ran the fetch, or a
// deployment that deliberately ships without 3D — loading fails and every entry
// point degrades exactly as it always has: the .NET component keeps its inline
// SVG schematic placeholder and never throws. 3D is therefore a capability that
// lights up when its assets are present, which is also what makes an air-gapped
// deployment work instead of hanging on an unreachable CDN.
//
// Same-origin assets need no Subresource Integrity: SRI defends against a
// third-party CDN serving tampered bytes, and there is no third party left in
// this path.

const CESIUM_BASE_URL = '/_content/Honua.Console.Shell/vendor/cesium/';
const CESIUM_JS = `${CESIUM_BASE_URL}Cesium.js`;
const CESIUM_CSS = `${CESIUM_BASE_URL}Widgets/widgets.css`;

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

    let viewer;
    try {
        const safeTilesetUrl = sameOriginTilesetUrl(tilesetUrl);
        viewer = new Cesium.Viewer(element, {
            // Cesium's implicit default is an Ion-backed base layer. The Console
            // scene surface renders only the supplied candidate-owned tileset.
            baseLayer: false,
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

        const tileset = await Cesium.Cesium3DTileset.fromUrl(safeTilesetUrl);
        viewer.scene.primitives.add(tileset);
        await viewer.zoomTo(tileset);

        instances.set(element, { viewer, tilesetUrl: safeTilesetUrl });
        return true;
    } catch {
        try {
            if (viewer && !viewer.isDestroyed()) viewer.destroy();
        } catch {
            // Ignore teardown failures while reporting the initialization failure.
        }
        return false;
    }
}

export function inspect(element) {
    const instance = instances.get(element);
    if (!instance) {
        return null;
    }
    return {
        imageryLayerCount: instance.viewer.imageryLayers.length,
        tilesetUrl: instance.tilesetUrl,
    };
}

export function dispose(element) {
    const instance = instances.get(element);
    if (instance) {
        try {
            instance.viewer.destroy();
        } catch {
            // Ignore teardown failures.
        }
        instances.delete(element);
    }
}

function sameOriginTilesetUrl(value) {
    const source = new URL(value, window.location.href);
    if (source.username || source.password || source.hash) {
        throw new Error('tileset URL must not contain credentials or a fragment');
    }

    // A server-bound Console commonly has a different origin from honua-server.
    // Derive the upstream from the active environment inside the same-origin BFF;
    // never widen browser CSP or forward an operator credential cross-origin.
    if (source.pathname.startsWith('/scenes/')) {
        return new URL(`/scene-proxy${source.pathname}${source.search}`, window.location.origin).href;
    }
    if (source.origin !== window.location.origin || !source.pathname.startsWith('/scene-proxy/scenes/')) {
        throw new Error('tileset URL must be a Honua scene route');
    }
    return source.href;
}
