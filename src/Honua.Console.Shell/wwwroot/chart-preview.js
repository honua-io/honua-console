// Minimal Vega-Lite interop for the shared ChartPreview component.
//
// Mirrors map-preview.js: the Console bundles no Vega dependency, so this module loads
// vega + vega-lite + vega-embed lazily from a CDN ONLY when a chart spec is supplied. When
// the Vega runtime is unavailable (offline, blocked CDN, or a non-browser host such as the
// bUnit render harness) every entry point fails gracefully — the .NET component keeps its
// inline SVG schematic placeholder and never throws. This preserves the no-mock /
// missing-binding contract: a live chart appears only when a real Vega-Lite spec is bound.
//
// Data is REAL: when a featuresUrl is supplied the module fetches the bound layer's actual
// rows (via the console /map-proxy/features proxy → server FeatureServer query) and injects
// them as the spec's inline data. The chart never fabricates sample data; with no rows and
// no inline data in the spec the embed is skipped and the placeholder stays.

// vega-embed's standalone build does NOT bundle vega + vega-lite; they are peer deps that must be
// present first. Load all three from the CDN in order (vega → vega-lite → vega-embed).
const VEGA_JS = 'https://cdn.jsdelivr.net/npm/vega@5.30.0/build/vega.min.js';
const VEGA_LITE_JS = 'https://cdn.jsdelivr.net/npm/vega-lite@5.21.0/build/vega-lite.min.js';
const VEGA_EMBED_JS = 'https://cdn.jsdelivr.net/npm/vega-embed@6.26.0/build/vega-embed.min.js';

const instances = new Map();
let vegaEmbedPromise = null;

function loadScript(src) {
    return new Promise((resolve) => {
        try {
            const script = document.createElement('script');
            script.src = src;
            script.async = false; // preserve execution order across the three peer scripts
            script.onload = () => resolve(true);
            script.onerror = () => resolve(false);
            document.head.appendChild(script);
        } catch {
            resolve(false);
        }
    });
}

function loadVegaEmbed() {
    if (typeof window === 'undefined' || typeof document === 'undefined') {
        return Promise.resolve(null);
    }
    if (window.vegaEmbed && window.vega && window.vegaLite) {
        return Promise.resolve(window.vegaEmbed);
    }
    if (vegaEmbedPromise) {
        return vegaEmbedPromise;
    }
    vegaEmbedPromise = (async () => {
        // Sequential: vega-lite needs vega; vega-embed needs both.
        if (!window.vega && !(await loadScript(VEGA_JS))) {
            return null;
        }
        if (!window.vegaLite && !(await loadScript(VEGA_LITE_JS))) {
            return null;
        }
        if (!window.vegaEmbed && !(await loadScript(VEGA_EMBED_JS))) {
            return null;
        }
        return window.vegaEmbed ?? null;
    })();
    return vegaEmbedPromise;
}

// Fetches the bound layer's real rows from the console features proxy and flattens the Esri
// feature JSON ({features:[{attributes:{...}}]}) to a plain array of attribute records that
// Vega-Lite can plot. Returns [] on any failure so the caller falls back to the spec's own
// inline data (if any) — never fabricated rows.
async function fetchRows(featuresUrl) {
    if (!featuresUrl) {
        return [];
    }
    try {
        const response = await fetch(featuresUrl, { headers: { Accept: 'application/json' } });
        if (!response.ok) {
            return [];
        }
        const body = await response.json();
        const features = Array.isArray(body?.features) ? body.features : [];
        return features
            .map(f => (f && typeof f.attributes === 'object' && f.attributes !== null ? f.attributes : null))
            .filter(Boolean);
    } catch {
        return [];
    }
}

// Returns a Vega-Lite spec object from the supplied JSON string, or null when it is missing or
// unparseable (the component keeps its placeholder rather than embedding a broken spec).
function parseSpec(specJson) {
    if (!specJson || typeof specJson !== 'string') {
        return null;
    }
    try {
        const spec = JSON.parse(specJson);
        return spec && typeof spec === 'object' ? spec : null;
    } catch {
        return null;
    }
}

// Mounts a live Vega-Lite chart into the container. Returns true when a real chart was bound;
// false when the component should keep its placeholder (no spec, no data, or runtime absent).
export async function init(container, options) {
    if (!container || !options) {
        return false;
    }
    const spec = parseSpec(options.spec);
    if (!spec) {
        return false;
    }

    // Bind the bound layer's real rows. If the proxy returns nothing, honour any inline data the
    // server already put in the spec; if neither exists there is nothing real to chart — bail.
    const rows = await fetchRows(options.featuresUrl);
    if (rows.length > 0) {
        spec.data = { values: rows };
    } else if (!spec.data || (Array.isArray(spec.data.values) && spec.data.values.length === 0)) {
        return false;
    }

    // Let the chart fill the stage; the schematic uses a fixed aspect so width/height autosizing here
    // keeps the live chart visually consistent with the placeholder it replaces.
    spec.width = spec.width ?? 'container';
    spec.height = spec.height ?? 'container';
    if (!spec.autosize) {
        spec.autosize = { type: 'fit', contains: 'padding' };
    }

    const vegaEmbed = await loadVegaEmbed();
    if (!vegaEmbed) {
        return false;
    }
    try {
        const result = await vegaEmbed(container, spec, {
            actions: false,
            renderer: 'svg',
        });
        instances.set(container, result);
        return true;
    } catch {
        return false;
    }
}

export function dispose(container) {
    const result = instances.get(container);
    if (result) {
        try {
            result.finalize?.();
        } catch {
            /* ignore teardown failures */
        }
        instances.delete(container);
    }
    try {
        if (container) {
            container.replaceChildren();
        }
    } catch {
        /* ignore */
    }
}
