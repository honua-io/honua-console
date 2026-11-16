import { loadStudioBundle } from './studio-bundle.mjs';
import { consoleHref } from './console-links.mjs';

// Retired Console-host reader routes, kept only for the legacy regression host document.
const LEGACY_API = '/api/studio/viewer';
const PARTITION = 'X-Honua-Viewer-Partition';
const COMPOSITION_FAMILIES = new Set(['map', 'dashboard', 'app']);

export function publishedMapPath(route) {
    const key = route.replace(/^\//, '');
    if (key.length > 1024 || key.split('/').some(part => !part || part === '.' || part === '..' || /[\\%\p{Cc}]/u.test(part)))
        throw new Error('This release has an unsupported route.');
    return `/maps/published/${key.split('/').map(encodeURIComponent).join('/')}`;
}

/** Current-reader descriptors supply data routes; identifiers never become guessed service names. */
export function audienceCatalog(catalog, dataBase) {
    if (!Array.isArray(catalog) || catalog.length > 1000)
        throw new Error('Current-viewer source metadata is unavailable.');
    const ids = new Set();
    return catalog.map(source => {
        if (typeof source?.id !== 'string' || ids.has(source.id)
            || source.protocol !== 'geoservices-feature-service' || !Number.isSafeInteger(source.locator?.layerId)
            || source.locator.layerId < 0 || typeof source.locator.url !== 'string')
            throw new Error('The published map has incompatible source metadata.');
        ids.add(source.id);
        const target = new URL(source.locator.url, dataBase);
        const match = target.pathname.match(/\/rest\/services\/((?:[A-Za-z0-9_-][A-Za-z0-9_.-]*\/)+FeatureServer)$/);
        if (!/^https?:$/.test(target.protocol) || target.username || target.password || target.search || target.hash || !match
            || match[1].split('/').some(part => part === '.' || part === '..'))
            throw new Error('The published source has an unsupported data route.');
        return { ...source, locator: { ...source.locator, url: `${dataBase}/rest/services/${match[1]}` } };
    });
}

/**
 * Native reader: the configured Honua API with the viewer's own Console session (cookie on the same
 * origin, operator bearer across origins) or no credential at all. The server enforces visibility,
 * so a public publication opens anonymously. A session change ends the view.
 */
export function nativeViewerConnection({ apiBase = globalThis.honuaConsoleApiBase,
    authorize = () => globalThis.honuaConsoleCaptureResourceAuthorization?.(), fetchImpl = globalThis.fetch,
    subscribe = listener => globalThis.honuaConsoleSubscribeSession?.(listener) } = {}) {
    return async (key, signal) => {
        if (typeof apiBase !== 'string' || !apiBase.endsWith('/')) throw new Error('The viewer connection is unavailable.');
        const api = new URL(apiBase);
        const authorization = await authorize();
        if (typeof authorization !== 'function' || typeof authorization.current !== 'function')
            throw new Error('The viewer connection is unavailable.');
        const send = (url, accept, requestSignal) => {
            const decision = authorization(url);
            if (decision?.api !== true) throw new Error('The map requested an unsupported data route.');
            return fetchImpl(decision.url, { method: 'GET', headers: { ...decision.headers, Accept: accept },
                credentials: decision.credentials, redirect: 'error', cache: 'no-store', signal: requestSignal });
        };
        const response = await send(new URL(`api/v1/studio/published/${key}?includeSources=true`, api).href,
            'application/json', signal);
        return Object.freeze({ response, dataBase: api.href.slice(0, -1), styleBase: `${api.href}ogc/styles`,
            current: () => authorization.current(), fetch: send,
            // Ends a rendered view once the Console session it was read with changes.
            watch: listener => {
                const stop = subscribe(() => { if (!authorization.current()) listener(); });
                return typeof stop === 'function' ? stop : () => {};
            } });
    };
}

/** Retired Console-host reader (legacy regression host only): same-origin cookie plus a view partition. */
export function legacyViewerConnection({ origin = globalThis.location.origin, fetchImpl = globalThis.fetch } = {}) {
    return async (key, signal) => {
        const response = await fetchImpl(`${origin}${LEGACY_API}/published/${key}`, {
            method: 'GET', credentials: 'same-origin', redirect: 'error', cache: 'no-store', signal,
        });
        const partition = response.headers.get(PARTITION);
        if (response.ok && !/^[0-9a-f]{64}$/.test(partition ?? '')) throw new Error('The viewer connection is unavailable.');
        return Object.freeze({ response, dataBase: `${origin}${LEGACY_API}/data`, styleBase: `${origin}${LEGACY_API}/styles`,
            current: () => true,
            fetch: async (url, accept, requestSignal) => {
                const result = await fetchImpl(url, { method: 'GET', headers: { Accept: accept, [PARTITION]: partition },
                    credentials: 'same-origin', redirect: 'error', cache: 'no-store', signal: requestSignal });
                if (result.status === 409) throw responseError(result);
                if (result.ok && result.headers.get(PARTITION) !== partition) throw new Error('The viewer connection changed. Reload the map.');
                return result;
            } });
    };
}

function responseError(response) {
    return new Error(({401: 'Sign in to view this map, then reload it.',
        403: 'Your account cannot view this map or its data.',
        404: 'This published map is no longer available.',
        409: 'Your account or environment changed. Reload the map.'})[response.status]
        ?? 'The map service is unavailable. Try reloading the map.');
}

function canvasMessage(view) {
    if (view.status === 'unavailable') return view.statusDetail || 'The map is unavailable.';
    if (view.statusDetail) return `Some map content is unavailable. ${view.statusDetail}`;
    return view.status === 'ready' ? 'Map ready.' : 'Loading map…';
}

const VIEW_CHANGED = 'The map view changed. Reload the map.';
const SESSION_CHANGED = 'Your Console session changed. Reload the map.';
const DEFAULT_TITLE = 'Published map';

function requireCurrent(isCurrent) {
    if (!isCurrent()) throw new Error(VIEW_CHANGED);
}

function readComposition(payload) {
    const artifact = payload.success === true ? payload.data : undefined;
    const body = artifact?.envelope?.body;
    if (!COMPOSITION_FAMILIES.has(artifact?.family) || artifact.envelope?.family !== artifact.family
        || !body || typeof body !== 'object' || Array.isArray(body) || !Array.isArray(body.layers))
        throw new Error('This release requires a different viewer.');
    return artifact;
}

function isBoundedRead(target, init, {dataOrigin, reads, styleRoot}) {
    const styleId = target.pathname.startsWith(styleRoot) ? target.pathname.slice(styleRoot.length) : '';
    const styleRead = styleId.length > 0 && styleId.length <= 768 && !styleId.includes('/');
    const allowed = target.origin === dataOrigin && (reads.has(target.pathname) || styleRead)
        && !(styleRead && target.search) && !(target.search && !target.pathname.endsWith('/query') && target.search !== '?f=json')
        && !target.hash && (init.method ?? 'GET').toUpperCase() === 'GET';
    return allowed ? {styleRead} : undefined;
}

/** GET-only reads of the publication's own layers and single-segment styles, on the connection's origin. */
function boundedAudienceTransport({connection, catalog, signal, isCurrent}) {
    const bounds = {
        dataOrigin: new URL(connection.dataBase).origin,
        reads: new Set(catalog.flatMap(source => {
            const path = new URL(source.locator.url).pathname + '/' + source.locator.layerId;
            return [path, path + '/query'];
        })),
        styleRoot: `${new URL(connection.styleBase).pathname}/`,
    };
    return Object.freeze({
        fetchRequest: async (input, init = {}) => {
            requireCurrent(isCurrent);
            if (input instanceof Request) throw new Error('The viewer requires a bounded data URL.');
            const target = new URL(String(input), connection.dataBase);
            const read = isBoundedRead(target, init, bounds);
            if (!read) throw new Error('The map requested an unsupported data route.');
            const result = await connection.fetch(target.href,
                read.styleRead ? 'application/vnd.mapbox.style+json' : 'application/json',
                init.signal ? AbortSignal.any([signal, init.signal]) : signal);
            requireCurrent(isCurrent);
            return result;
        },
    });
}

function renderComposition({runtime, artifact, catalog, connection, transport, documentUrl, signal, isCurrent,
    container, widgetContainer, onChange}) {
    if (typeof runtime.createReadOnlyCompositionView !== 'function' || typeof runtime.AudienceStyleProvider !== 'function')
        throw new Error('The map viewer runtime is unavailable.');
    const getTransport = () => isCurrent() ? transport : undefined;
    const styleProvider = new runtime.AudienceStyleProvider({
        styleBaseUrl: connection.styleBase, documentUrl, catalog, getTransport, signal,
    });
    return runtime.createReadOnlyCompositionView({container, widgetContainer,
        body: artifact.envelope.body,
        projection: {catalog, baseUrl: connection.dataBase},
        getAudienceTransport: getTransport,
        sourceAuthBaseUrl: connection.dataBase, styleProvider, onChange,
    });
}

/** Owns a viewer instance only. No editor, agent session, lifecycle writer or ambient credential. */
export function createPublishedMapViewer({route, container, widgetContainer, status, title, connect = nativeViewerConnection(),
    documentUrl, loadRenderer = () => loadStudioBundle()}) {
    const key = publishedMapPath(route).slice('/maps/published/'.length);
    const viewerDocument = documentUrl ?? new URL(consoleHref(publishedMapPath(route)), globalThis.location.origin).href;
    let generation = 0;
    let lifetime;
    let canvas;
    let disposed = false;
    let stopWatch;
    const message = text => { status.textContent = text; };
    const updateRenderCount = view => {
        container.dataset.renderedFeatureCount = view?.status === 'ready'
            && Number.isSafeInteger(view.renderedFeatureCount) && view.renderedFeatureCount >= 0
            ? String(view.renderedFeatureCount) : '0';
    };
    const dropCanvas = () => {
        stopWatch?.(); stopWatch = undefined;
        canvas?.dispose(); canvas = undefined; updateRenderCount(undefined);
    };
    const clear = () => {
        lifetime?.abort(); dropCanvas();
        if (title.textContent) title.textContent = DEFAULT_TITLE;
    };
    async function show({connection, current, signal, isCurrent}) {
        stopWatch = connection.watch?.(() => {
            if (!disposed && current === generation) { clear(); message(SESSION_CHANGED); }
        });
        const response = connection.response;
        if (!response.ok) throw responseError(response);
        const payload = await response.json();
        requireCurrent(isCurrent);
        const artifact = readComposition(payload);
        const catalog = audienceCatalog(artifact.sourceCatalog, connection.dataBase);
        const transport = boundedAudienceTransport({connection, catalog, signal, isCurrent});
        const runtime = await loadRenderer();
        requireCurrent(isCurrent);
        const rendered = renderComposition({runtime, artifact, catalog, connection, transport, documentUrl: viewerDocument,
            signal, isCurrent, container, widgetContainer,
            onChange: view => { if (isCurrent()) { updateRenderCount(view); message(canvasMessage(view)); } }});
        if (!isCurrent()) { rendered.dispose(); throw new Error(VIEW_CHANGED); }
        canvas = rendered;
        await rendered.settled();
        requireCurrent(isCurrent);
        title.textContent = artifact.envelope.title || artifact.envelope.body.title || DEFAULT_TITLE;
        updateRenderCount(canvas);
        message(canvasMessage(canvas));
    }
    async function reload() {
        if (disposed) return;
        clear();
        const current = ++generation;
        const controller = new AbortController();
        lifetime = controller;
        message('Loading map…');
        let connection;
        const isCurrent = () => !disposed && current === generation && !controller.signal.aborted
            && (connection === undefined || connection.current());
        try {
            connection = await connect(key, controller.signal);
            requireCurrent(() => !disposed && current === generation);
            await show({connection, current, signal: controller.signal, isCurrent});
        } catch (error) {
            if (disposed || current !== generation) return;
            dropCanvas();
            message(connection && !connection.current() ? SESSION_CHANGED
                : error instanceof Error ? error.message : 'The map is unavailable.');
        }
    }
    return Object.freeze({reload, dispose() { disposed = true; generation++; clear(); }});
}

/** Mounts the viewer into a [data-published-map] host; the Console page disposes it on navigation. */
export function mountPublishedMap(host, {connect = nativeViewerConnection(), documentUrl, loadRenderer} = {}) {
    const viewer = createPublishedMapViewer({route: host.dataset.publishedRoute,
        container: host.querySelector('[data-map-canvas]'), widgetContainer: host.querySelector('[data-map-widgets]'),
        status: host.querySelector('[data-map-status]'), title: host.querySelector('[data-map-title]'),
        connect, documentUrl, ...(loadRenderer ? {loadRenderer} : {})});
    const reloadButton = host.querySelector('[data-map-reload]');
    const reload = () => viewer.reload();
    reloadButton?.addEventListener('click', reload);
    void viewer.reload();
    return Object.freeze({dispose() { reloadButton?.removeEventListener('click', reload); viewer.dispose(); }});
}

// The legacy regression host renders a standalone document; the Console bundle mounts through its page.
if (typeof document !== 'undefined') {
    const host = document.querySelector('[data-published-map][data-viewer-connection="legacy"]');
    if (host) {
        const mounted = mountPublishedMap(host, {connect: legacyViewerConnection(),
            documentUrl: `${globalThis.location.origin}${publishedMapPath(host.dataset.publishedRoute)}`});
        globalThis.addEventListener('pagehide', () => mounted.dispose(), {once: true});
    }
}
