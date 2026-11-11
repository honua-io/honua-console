let browserTransport;

export function initialize(sessionBridge, configuration) {
    browserTransport = createConsoleBrowserTransport({
        ...configuration,
        getSession: () => sessionBridge.invokeMethodAsync('GetSnapshot'),
    });
    globalThis.honuaConsoleApiBase = browserTransport.apiBase;
    globalThis.honuaConsoleAuthorizeResource = url => browserTransport.authorizeResource(url);
}

export function captureConsoleSession() {
    if (!browserTransport) throw new Error('The Console browser transport is unavailable.');
    return browserTransport.capture();
}

function requireSession(session) {
    const expiry = session?.expiresAt == null ? null : Date.parse(session.expiresAt);
    if (!session?.authenticated || !session.principalId
        || (expiry !== null && (!Number.isFinite(expiry) || expiry <= Date.now()))) {
        throw new Error('Sign in to Honua before accessing this API.');
    }
    return session;
}

function resolveRoute(route, api) {
    if (typeof route !== 'string' || !route || /^[/.]/.test(route)
        || /[\\#]/.test(route) || /^[a-z][a-z\d+.-]*:/i.test(route)) {
        throw new Error('A relative Honua API route is required.');
    }
    const routePath = route.split('?')[0];
    if (routePath.split('/').some(segment => /[\\/]/.test(decodeURIComponent(segment)))) {
        throw new Error('An encoded API route separator is not allowed.');
    }
    const target = new URL(route, api);
    if (target.origin !== api.origin || !target.pathname.startsWith(api.pathname)
        || target.username || target.password || target.hash) {
        throw new Error('Console requests must target the configured Honua API.');
    }
    return target.href;
}

function requestOptions(init, session, sameOrigin) {
    const headers = new Headers(init.headers);
    for (const name of ['Authorization', 'Cookie', 'X-API-Key', 'X-CSRF-Token', 'X-Honua-CSRF']) headers.delete(name);
    if (session.accessToken && Date.parse(session.expiresAt) > Date.now()) {
        headers.set('Authorization', `Bearer ${session.accessToken}`);
    } else if (!sameOrigin) {
        throw new Error('Cross-origin Console access requires an operator bearer token.');
    }
    if (sameOrigin && !['GET', 'HEAD', 'OPTIONS'].includes((init.method ?? 'GET').toUpperCase())) {
        if (!session.csrfToken) throw new Error('The API has not issued a CSRF token. Sign in again before making changes.');
        headers.set('X-Honua-CSRF', session.csrfToken);
    }
    return { ...init, headers, credentials: sameOrigin ? 'same-origin' : 'omit', redirect: 'error' };
}

export function createConsoleBrowserTransport({ apiBaseUrl, applicationBaseUrl, getSession, request = fetch }) {
    const api = new URL(apiBaseUrl);
    if (!['http:', 'https:'].includes(api.protocol) || api.username || api.password || api.search || api.hash || !api.pathname.endsWith('/')) {
        throw new Error('The configured Honua API base URL is invalid.');
    }
    const application = new URL(applicationBaseUrl);
    const sameOrigin = api.origin === application.origin;
    let boundSession = null;
    return Object.freeze({
        apiBase: api.href,
        async authorizeResource(url) {
            const target = new URL(url, application);
            if (target.username || target.password || target.hash) {
                throw new Error('A Console resource URL must not contain credentials or a fragment.');
            }
            const onApi = target.origin === api.origin && target.pathname.startsWith(api.pathname);
            if (!onApi) {
                return {
                    url: target.href,
                    credentials: target.origin === application.origin ? 'same-origin' : 'omit',
                    headers: {},
                };
            }
            const session = await getSession();
            const principalId = session?.authenticated ? session.principalId ?? '' : '';
            const revision = session?.revision ?? null;
            if (boundSession && (boundSession.revision !== revision || boundSession.principalId !== principalId)) {
                throw new Error('The Console session changed. Reopen this view before continuing.');
            }
            boundSession = { revision, principalId };
            const expiry = session?.expiresAt == null ? null : Date.parse(session.expiresAt);
            const tokenLive = Boolean(session?.accessToken)
                && (expiry === null || (Number.isFinite(expiry) && expiry > Date.now()));
            if (session?.authenticated && !sameOrigin && !tokenLive) {
                throw new Error('Cross-origin Console access requires an operator bearer token.');
            }
            const headers = {};
            if (session?.authenticated && tokenLive) headers.Authorization = `Bearer ${session.accessToken}`;
            return { url: target.href, credentials: sameOrigin ? 'same-origin' : 'omit', headers };
        },
        async capture() {
            const binding = requireSession(await getSession());
            const getCurrentSession = async () => {
                const current = requireSession(await getSession());
                if (current.revision !== binding.revision || current.principalId !== binding.principalId) {
                    throw new Error('The Console session changed. Reopen this view before continuing.');
                }
                return current;
            };
            return Object.freeze({
                principalId: binding.principalId,
                revision: binding.revision,
                async assertCurrent() { await getCurrentSession(); },
                async fetch(route, init = {}) {
                    const url = resolveRoute(route, api);
                    init.signal?.throwIfAborted();
                    const session = await getCurrentSession();
                    init.signal?.throwIfAborted();
                    const response = await request(url, requestOptions(init, session, sameOrigin));
                    init.signal?.throwIfAborted();
                    await getCurrentSession();
                    return response;
                },
            });
        },
    });
}
