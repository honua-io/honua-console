const API = '/api/studio/viewer';
const PARTITION = 'X-Honua-Viewer-Partition';

export function publishedMapPath(route) {
    const key = route.replace(/^\//, '');
    if (key.length > 1024 || key.split('/').some(part => !part || part === '.' || part === '..' || /[\\%\p{Cc}]/u.test(part)))
        throw new Error('This publication has an unsupported route.');
    return `/maps/published/${key.split('/').map(encodeURIComponent).join('/')}`;
}

/** Current-reader descriptors supply data routes; identifiers never become guessed service names. */
export function audienceCatalog(catalog, origin) {
    if (!Array.isArray(catalog) || catalog.length > 1000)
        throw new Error('Current-viewer source metadata is unavailable.');
    const ids = new Set();
    return catalog.map(source => {
        if (typeof source?.id !== 'string' || ids.has(source.id)
            || source.protocol !== 'geoservices-feature-service' || !Number.isSafeInteger(source.locator?.layerId)
            || source.locator.layerId < 0 || typeof source.locator.url !== 'string')
            throw new Error('The published map has incompatible source metadata.');
        ids.add(source.id);
        const target = new URL(source.locator.url, origin);
        const match = target.pathname.match(/\/rest\/services\/((?:[A-Za-z0-9_-][A-Za-z0-9_.-]*\/)+FeatureServer)$/);
        if (!/^https?:$/.test(target.protocol) || target.username || target.password || target.search || target.hash || !match
            || match[1].split('/').some(part => part === '.' || part === '..'))
            throw new Error('The published source has an unsupported data route.');
        return { ...source, locator: { ...source.locator, url: `${origin}${API}/data/rest/services/${match[1]}` } };
    });
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

/** Owns a viewer instance only. No editor, agent session, lifecycle writer or browser bearer. */
export function createPublishedMapViewer({route, container, widgetContainer, status, title, origin = globalThis.location.origin,
    fetchImpl = globalThis.fetch, loadRenderer = () => import('/_studio/console.js')}) {
    const key = publishedMapPath(route).slice('/maps/published/'.length);
    let generation = 0;
    let lifetime;
    let canvas;
    let disposed = false;
    const message = text => { status.textContent = text; };
    const clear = () => { lifetime?.abort(); canvas?.dispose(); canvas = undefined; };
    async function reload() {
        if (disposed) return;
        clear();
        const current = ++generation;
        const controller = new AbortController();
        lifetime = controller;
        message('Loading map…');
        const isCurrent = () => !disposed && current === generation && !controller.signal.aborted;
        try {
            const response = await fetchImpl(`${origin}${API}/published/${key}`, {
                method: 'GET', credentials: 'same-origin', redirect: 'error', cache: 'no-store', signal: controller.signal,
            });
            if (!response.ok) throw responseError(response);
            const partition = response.headers.get(PARTITION);
            if (!/^[0-9a-f]{64}$/.test(partition ?? '')) throw new Error('The viewer connection is unavailable.');
            const payload = await response.json();
            const artifact = payload.success === true ? payload.data : undefined;
            if (artifact?.family !== 'map' || artifact.envelope?.family !== 'map' || !artifact.envelope.body)
                throw new Error('This publication requires a different viewer.');
            const catalog = audienceCatalog(artifact.sourceCatalog, origin);
            const reads = new Set(catalog.flatMap(source => {
                const path = new URL(source.locator.url).pathname + '/' + source.locator.layerId;
                return [path, path + '/query'];
            }));
            const styleRoot = `${API}/styles/`;
            const transport = Object.freeze({
                fetchRequest: async (input, init = {}) => {
                    if (!isCurrent()) throw new Error('The map view changed.');
                    if (input instanceof Request) throw new Error('The viewer requires a bounded data URL.');
                    const target = new URL(String(input), origin);
                    const styleId = target.pathname.startsWith(styleRoot) ? target.pathname.slice(styleRoot.length) : '';
                    const styleRead = styleId.length > 0 && styleId.length <= 768 && !styleId.includes('/');
                    if (target.origin !== origin || (!reads.has(target.pathname) && !styleRead)
                        || (styleRead && target.search) || (target.search && !target.pathname.endsWith('/query'))
                        || target.hash || (init.method ?? 'GET').toUpperCase() !== 'GET')
                        throw new Error('The map requested an unsupported data route.');
                    const result = await fetchImpl(target.href, { method: 'GET',
                        headers: {Accept: styleRead ? 'application/vnd.mapbox.style+json' : 'application/json', [PARTITION]: partition},
                        credentials: 'same-origin', redirect: 'error', cache: 'no-store',
                        signal: init.signal ? AbortSignal.any([controller.signal, init.signal]) : controller.signal,
                    });
                    if (!isCurrent()) throw new Error('The map view changed.');
                    if (result.status === 409) throw responseError(result);
                    if (result.ok && result.headers.get(PARTITION) !== partition) throw new Error('The viewer connection changed. Reload the map.');
                    return result;
                },
            });
            const runtime = await loadRenderer();
            if (!isCurrent()) return;
            if (typeof runtime.createReadOnlyCompositionView !== 'function' || typeof runtime.AudienceStyleProvider !== 'function')
                throw new Error('The map viewer runtime is unavailable.');
            const styleProvider = new runtime.AudienceStyleProvider({
                styleBaseUrl: `${origin}${API}/styles`, documentUrl: `${origin}${publishedMapPath(route)}`,
                catalog, getTransport: () => isCurrent() ? transport : undefined, signal: controller.signal,
            });
            const rendered = runtime.createReadOnlyCompositionView({container, widgetContainer,
                body: artifact.envelope.body,
                projection: {catalog, baseUrl: `${origin}${API}/data`},
                getAudienceTransport: () => isCurrent() ? transport : undefined,
                sourceAuthBaseUrl: `${origin}${API}/data`, styleProvider,
                onChange: view => {
                    if (isCurrent()) message(canvasMessage(view));
                },
            });
            if (!isCurrent()) { rendered.dispose(); return; }
            canvas = rendered;
            await rendered.settled();
            if (!isCurrent()) return;
            title.textContent = artifact.envelope.title || artifact.envelope.body.title || 'Published map';
            message(canvasMessage(canvas));
        } catch (error) {
            if (isCurrent()) { canvas?.dispose(); canvas = undefined; message(error instanceof Error ? error.message : 'The map is unavailable.'); }
        }
    }
    return Object.freeze({reload, dispose() { disposed = true; generation++; clear(); }});
}

if (typeof document !== 'undefined') {
    const host = document.querySelector('[data-published-map]');
    if (host) {
        const viewer = createPublishedMapViewer({route: host.dataset.publishedRoute,
            container: host.querySelector('[data-map-canvas]'), widgetContainer: host.querySelector('[data-map-widgets]'),
            status: host.querySelector('[data-map-status]'),
            title: host.querySelector('[data-map-title]')});
        host.querySelector('[data-map-reload]').addEventListener('click', () => viewer.reload());
        globalThis.addEventListener('pagehide', () => viewer.dispose(), {once: true});
        void viewer.reload();
    }
}
