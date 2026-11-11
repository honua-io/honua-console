let browserTransport;

export function initialize(sessionBridge, configuration) {
    browserTransport = createConsoleBrowserTransport({
        ...configuration,
        getSession: () => sessionBridge.invokeMethodAsync('GetSnapshot'),
    });
    globalThis.honuaConsoleApiBase = browserTransport.apiBase;
    globalThis.honuaConsoleCaptureResourceAuthorization = () => browserTransport.captureResourceAuthorization();
    globalThis.honuaConsoleSubscribeSession = (listener) => browserTransport.subscribeSession(listener);
}

export function publishSession(snapshot) {
    browserTransport?.publishSession(snapshot);
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
    let current = null;
    let expiryTimer = null;
    const sessionListeners = new Set();
    const armExpiry = (snapshot) => {
        if (expiryTimer) clearTimeout(expiryTimer);
        expiryTimer = null;
        if (!snapshot?.authenticated || snapshot.expiresAt == null) return;
        const expiry = Date.parse(snapshot.expiresAt);
        if (!Number.isFinite(expiry)) return;
        expiryTimer = setTimeout(() => {
            expiryTimer = null;
            if (!current || current.revision !== snapshot.revision || !current.authenticated) return;
            note({ ...current, authenticated: false, principalId: '', accessToken: null });
        }, Math.max(0, expiry - Date.now()));
        expiryTimer.unref?.();
    };
    const note = (next) => {
        current = next;
        armExpiry(next);
        for (const listener of [...sessionListeners]) {
            try {
                listener(current);
            } catch {
                // One preview failing to close must not keep the others on the old session.
            }
        }
    };
    const identity = (session) => {
        const expiry = session?.expiresAt == null ? null : Date.parse(session.expiresAt);
        const live = Boolean(session?.authenticated)
            && (expiry === null || (Number.isFinite(expiry) && expiry > Date.now()));
        return {
            revision: session?.revision ?? null,
            principalId: live ? session.principalId ?? '' : '',
            authenticated: live,
            accessToken: live ? session.accessToken ?? null : null,
            expiresAt: session?.expiresAt ?? null,
        };
    };
    const decide = (url, session) => {
        const target = new URL(url, application);
        if (target.username || target.password || target.hash) {
            throw new Error('A Console resource URL must not contain credentials or a fragment.');
        }
        const rest = target.origin === api.origin && target.pathname.startsWith(api.pathname)
            ? target.pathname.slice(api.pathname.length)
            : null;
        const apiResource = rest !== null && /^(?:api|tiles|rest|scenes|ogc)(?:\/|$)/.test(rest);
        if (!apiResource) {
            return {
                url: target.href,
                api: false,
                credentials: target.origin === application.origin ? 'same-origin' : 'omit',
                headers: {},
            };
        }
        const expiry = session.expiresAt == null ? null : Date.parse(session.expiresAt);
        const tokenLive = Boolean(session.accessToken)
            && (expiry === null || (Number.isFinite(expiry) && expiry > Date.now()));
        if (session.authenticated && !sameOrigin && !tokenLive) {
            throw new Error('Cross-origin Console access requires an operator bearer token.');
        }
        const headers = {};
        if (session.authenticated && tokenLive) headers.Authorization = `Bearer ${session.accessToken}`;
        return { url: target.href, api: true, credentials: sameOrigin ? 'same-origin' : 'omit', headers };
    };
    return Object.freeze({
        apiBase: api.href,
        publishSession(snapshot) { note(identity(snapshot)); },
        subscribeSession(listener) {
            sessionListeners.add(listener);
            return () => sessionListeners.delete(listener);
        },
        async captureResourceAuthorization() {
            const captured = identity(await getSession());
            const published = current?.revision ?? -1;
            const seen = captured.revision ?? -1;
            if (current === null || published < seen) note(captured);
            else if (published === seen && current.authenticated && !captured.authenticated) note(captured);
            const authorize = (url) => {
                if (!authorize.current()) {
                    throw new Error('The Console session changed. Reopen this view before continuing.');
                }
                return decide(url, captured);
            };
            const captureLive = () => {
                if (!captured.authenticated || captured.expiresAt == null) return true;
                const expiry = Date.parse(captured.expiresAt);
                return Number.isFinite(expiry) && expiry > Date.now();
            };
            authorize.current = () => Boolean(current)
                && current.revision === captured.revision
                && current.principalId === captured.principalId
                && current.authenticated === captured.authenticated
                && captureLive();
            return authorize;
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
