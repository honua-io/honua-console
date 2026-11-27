// Minimal Vega-Lite interop for the shared ChartPreview component.
//
// Vega is VENDORED (honua-console#334, the same treatment MapLibre got in #333): vega, vega-lite,
// and vega-embed are committed, version-pinned assets served from this same origin, never fetched
// from a CDN at page load. This module runs in the privileged Console origin (Blazor session +
// admin-keyed BFF proxy), so third-party code fetched at runtime would execute with full session and
// proxy access; it also meant an air-gapped or egress-restricted deployment got a broken chart
// preview. The pinned versions and the update procedure live in scripts/vendored-assets.json — see
// wwwroot/vendor/vega/README.md. `npm test` fails if the committed bytes ever drift.
//
// The libraries are still loaded LAZILY, only when a chart spec is supplied: the three bundles are
// ~820 KB and most Console pages never mount a chart. When the runtime is unavailable (no spec, or a
// non-browser host such as the bUnit render harness) every entry point fails gracefully — the .NET
// component keeps its inline SVG schematic placeholder and never throws. This preserves the no-mock /
// missing-binding contract: a live chart appears only when a real Vega-Lite spec is bound.
//
// Data is REAL: when a featuresUrl is supplied the module fetches the bound layer's actual
// rows from the supplied feature query and injects
// them as the spec's inline data. The chart never fabricates sample data; with no rows and
// no inline data in the spec the embed is skipped and the placeholder stays.

// vega-embed's standalone build does NOT bundle vega + vega-lite; they are peer deps that must be
// present first, so the three are loaded in order (vega → vega-lite → vega-embed).
//
// Resolved against document.baseURI rather than a root-absolute path so the Console still works when
// hosted under a path prefix, and against the DOCUMENT rather than import.meta.url because this
// module is imported from a blob: URL in some hosts (see the e2e interop specs), where relative
// resolution would have nothing to resolve against.
const VENDOR_BASE = '_content/Honua.Console.Shell/vendor/';
const vendorAsset = (path) =>
    (typeof document !== 'undefined' ? new URL(`${VENDOR_BASE}${path}`, document.baseURI).href : path);

const VEGA_JS = 'vega/vega.min.js';
const VEGA_LITE_JS = 'vega-lite/vega-lite.min.js';
const VEGA_EMBED_JS = 'vega-embed/vega-embed.min.js';

// No Subresource Integrity here, deliberately: SRI exists to constrain bytes served by an origin you
// do not control, and these bytes are this origin's own static assets. Their integrity is pinned
// where it is actually verifiable — sha384 digests in scripts/vendored-assets.lock.json, checked at
// test time against the committed files. (Those digests are byte-identical to the SRI hashes this
// module used to pin for the CDN URLs: the vendored bytes are the bytes that were being served.)
//
// vega-embed carries its own CSS inside the bundle and injects it as a <style> element, so unlike
// MapLibre there is no stylesheet to link.

const instances = new Map();
const initializations = new WeakMap();
// A chart draws only the rows the Console supplies. A stored or model-authored spec may not fetch
// URLs (data, lookups, images) or turn marks into links, so every loader entry point refuses.
// Expressions run through vega-embed's bundled AST interpreter (`ast: true`), never compiled code.
// An `href` encoding therefore renders without links (Vega logs a console error per mark); clicks do nothing.
const refuseExternalResource = () => Promise.reject(new Error('Chart specs cannot load external resources.'));
export const NO_NETWORK_LOADER = Object.freeze({
    load: refuseExternalResource,
    sanitize: refuseExternalResource,
    http: refuseExternalResource,
    file: refuseExternalResource,
});

let vegaEmbedPromise = null;

function loadScript(path) {
    return new Promise((resolve) => {
        try {
            const script = document.createElement('script');
            script.src = vendorAsset(path);
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
    })().then(runtime => {
        if (!runtime) vegaEmbedPromise = null;
        return runtime;
    });
    return vegaEmbedPromise;
}

// Fetches the bound layer's real rows from the console features proxy and flattens the Esri
// feature JSON ({features:[{attributes:{...}}]}) to a plain array of attribute records that
// Vega-Lite can plot. Returns [] on any failure so the caller falls back to the spec's own
// inline data (if any) — never fabricated rows.
export async function fetchRows(featuresUrl, authorize = null) {
    if (!featuresUrl) {
        return [];
    }
    try {
        const approved = authorize
            ? authorize(featuresUrl)
            : { url: featuresUrl, credentials: 'same-origin', headers: {} };
        const headers = new Headers(approved.headers);
        headers.set('Accept', 'application/json');
        const response = await fetch(approved.url, {
            headers,
            credentials: approved.credentials,
            redirect: 'error',
        });
        if (!response.ok) {
            return [];
        }
        const body = await response.json();
        authorize?.(approved.url);
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
    dispose(container);
    const token = {};
    initializations.set(container, token);
    const isCurrent = () => initializations.get(container) === token;
    const spec = parseSpec(options.spec);
    if (!spec) {
        return false;
    }

    try {
        const capture = globalThis.honuaConsoleCaptureResourceAuthorization;
        const authorize = typeof capture === 'function' ? await capture() : null;
        if (!isCurrent()) return false;
        const rows = await fetchRows(options.featuresUrl, authorize);
        if (!isCurrent() || !prepareSpec(spec, rows)) return false;
        const vegaEmbed = await loadVegaEmbed();
        if (!vegaEmbed || !isCurrent()) return false;
        if (authorize && options.featuresUrl) authorize(options.featuresUrl);
        return await mountChart(container, { spec, vegaEmbed, isCurrent });
    } catch {
        return false;
    }
}

function stripDataBlock(data) {
    if (data && typeof data === 'object' && !Array.isArray(data)) {
        delete data.url;
    }
}

function stripTransformDataUrls(transforms) {
    if (!Array.isArray(transforms)) return;
    for (const transform of transforms) {
        stripDataBlock(transform?.from?.data);
    }
}

const SPEC_ARRAY_KEYS = ['layer', 'concat', 'hconcat', 'vconcat'];
const SPEC_CHILD_KEYS = ['spec', 'facet', 'repeat'];

function childSpecs(node) {
    const children = [];
    for (const key of SPEC_CHILD_KEYS) {
        if (node[key]) children.push(node[key]);
    }
    for (const key of SPEC_ARRAY_KEYS) {
        if (Array.isArray(node[key])) children.push(...node[key]);
    }
    return children;
}

export function stripDataUrls(root) {
    const seen = new WeakSet();
    const stack = [root];
    while (stack.length > 0) {
        const node = stack.pop();
        if (!node || typeof node !== 'object' || Array.isArray(node) || seen.has(node)) continue;
        seen.add(node);
        stripDataBlock(node.data);
        stripTransformDataUrls(node.transform);
        stack.push(...childSpecs(node));
    }
    return root;
}

function prepareSpec(spec, rows) {
    // Stored embed options override our options unless removed before embedding.
    if (spec.usermeta && typeof spec.usermeta === 'object') {
        delete spec.usermeta.embedOptions;
    }
    // Defense in depth alongside NO_NETWORK_LOADER: strip any data/lookup URLs anywhere in the spec.
    stripDataUrls(spec);
    if (rows.length > 0) {
        spec.data = { values: rows };
    } else if (!spec.data || !Array.isArray(spec.data.values) || spec.data.values.length === 0) {
        // Only inline rows can be drawn; a data URL would load nothing, so show the fallback instead.
        return false;
    }

    if (rows.length > 0 && spec.encoding?.x?.field === '__auto__') {
        const keys = Object.keys(rows[0]);
        const dimension = keys.find(k => typeof rows[0][k] === 'string') ?? keys[0];
        if (!dimension) return false;
        spec.encoding.x.field = dimension;
        if (spec.encoding.x.title === '__auto__') spec.encoding.x.title = dimension;
    }
    spec.width = spec.width ?? 'container';
    spec.height = spec.height ?? 'container';
    spec.autosize = spec.autosize ?? { type: 'fit', contains: 'padding' };
    return true;
}

// Vega parses inline CSV/TSV with d3-dsv, which compiles each row object with new Function: the
// Console policy has no 'unsafe-eval', so those specs would fail. These readers parse the same
// delimited text without compiling code: a field is quoted only when it starts with a quote (RFC 4180),
// a blank line is a row of empty fields and a trailing line break adds no row, as in d3-dsv.
export function delimitedRows(text, delimiter) {
    if (typeof delimiter !== 'string' || delimiter.length !== 1) throw new Error('invalid delimiter');
    const separator = delimiter.replace(/[\\\]^-]/g, '\\$&');
    const field = new RegExp(`"((?:[^"]|"")*)"([^\\r\\n${separator}]*)|([^\\r\\n${separator}]*)`, 'y');
    const rows = [];
    let row = [];
    let position = 0;
    while (position <= text.length) {
        field.lastIndex = position;
        const match = field.exec(text);
        row.push(match[1] === undefined ? match[3] : match[1].replaceAll('""', '"') + match[2]);
        position = field.lastIndex;
        if (text[position] === delimiter) {
            position += 1;
            continue;
        }
        rows.push(row);
        row = [];
        position += text.startsWith('\r\n', position) ? 2 : 1;
    }
    const last = rows.at(-1);
    if (last?.length === 1 && last[0] === '') rows.pop();
    return rows;
}

export function delimitedObjects(text, format) {
    const rows = delimitedRows(String(text), format.delimiter);
    const header = Array.isArray(format.header) ? format.header.map(String) : rows.shift() ?? [];
    return rows.map(cells => Object.fromEntries(header.map((name, index) => [name, cells[index] ?? ''])));
}

export function useCodeFreeDelimitedReaders(vega) {
    if (typeof vega?.formats !== 'function') return;
    // Like Vega's own readers: csv and tsv fix their delimiter; dsv requires format.delimiter.
    const reader = (delimiter) => Object.assign(
        (data, format = {}) => delimitedObjects(data, { ...format, delimiter: delimiter ?? format.delimiter }),
        { responseType: 'text' });
    vega.formats('csv', reader(','));
    vega.formats('tsv', reader('\t'));
    vega.formats('dsv', reader(undefined));
}

async function mountChart(container, { spec, vegaEmbed, isCurrent }) {
    useCodeFreeDelimitedReaders(window.vega);
    const mount = document.createElement('div');
    mount.style.width = '100%';
    mount.style.height = '100%';
    container.appendChild(mount);
    try {
        const result = await vegaEmbed(mount, spec, { actions: false, renderer: 'svg', loader: NO_NETWORK_LOADER, ast: true });
        if (!isCurrent()) {
            finalize(result);
            return false;
        }
        instances.set(container, result);
        return true;
    } catch {
        if (isCurrent()) dispose(container);
        return false;
    }
}

function finalize(result) {
    try {
        result?.finalize?.();
    } catch {
        /* ignore teardown failures */
    }
}

export function dispose(container) {
    initializations.delete(container);
    finalize(instances.get(container));
    instances.delete(container);
    try {
        container?.replaceChildren();
    } catch {
        /* ignore teardown failures */
    }
}
