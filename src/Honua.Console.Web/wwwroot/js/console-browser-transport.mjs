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

function configuredApi(apiBaseUrl) {
    const api = new URL(apiBaseUrl);
    if (!['http:', 'https:'].includes(api.protocol) || api.username || api.password || api.search || api.hash || !api.pathname.endsWith('/')) {
        throw new Error('The configured Honua API base URL is invalid.');
    }
    return api;
}

function sessionIdentity(session) {
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
}

function tokenLive(session) {
    const expiry = session.expiresAt == null ? null : Date.parse(session.expiresAt);
    return Boolean(session.accessToken) && (expiry === null || (Number.isFinite(expiry) && expiry > Date.now()));
}

function resourceDecision(url, session, boundaries) {
    const target = new URL(url, boundaries.application);
    if (target.username || target.password || target.hash) {
        throw new Error('A Console resource URL must not contain credentials or a fragment.');
    }
    const api = boundaries.api;
    const rest = target.origin === api.origin && target.pathname.startsWith(api.pathname)
        ? target.pathname.slice(api.pathname.length)
        : null;
    const apiResource = rest !== null && /^(?:api|tiles|rest|scenes|ogc)(?:\/|$)/.test(rest);
    if (!apiResource) {
        const sameApplication = target.origin === boundaries.application.origin;
        return { url: target.href, api: false, credentials: sameApplication ? 'same-origin' : 'omit', headers: {} };
    }
    if (session.authenticated && !boundaries.sameOrigin && !tokenLive(session)) {
        throw new Error('Cross-origin Console access requires an operator bearer token.');
    }
    const headers = {};
    if (session.authenticated && tokenLive(session)) headers.Authorization = `Bearer ${session.accessToken}`;
    return { url: target.href, api: true, credentials: boundaries.sameOrigin ? 'same-origin' : 'omit', headers };
}

function publishListeners(listeners, current) {
    for (const listener of [...listeners]) {
        try {
            listener(current);
        } catch {
            // One preview failing to close must not keep the others on the old session.
        }
    }
}

function armExpiry(timer, readCurrent, note) {
    return (snapshot) => {
        if (timer.id) clearTimeout(timer.id);
        timer.id = null;
        if (!snapshot?.authenticated || snapshot.expiresAt == null) return;
        const expiry = Date.parse(snapshot.expiresAt);
        if (!Number.isFinite(expiry)) return;
        timer.id = setTimeout(() => {
            timer.id = null;
            const current = readCurrent();
            if (!current || current.revision !== snapshot.revision || !current.authenticated) return;
            note({ ...current, authenticated: false, principalId: '', accessToken: null });
        }, Math.max(0, expiry - Date.now()));
        timer.id.unref?.();
    };
}

function captureStillCurrent(current, captured) {
    if (!current || current.revision !== captured.revision || current.principalId !== captured.principalId
        || current.authenticated !== captured.authenticated) {
        return false;
    }
    if (!captured.authenticated || captured.expiresAt == null) return true;
    const expiry = Date.parse(captured.expiresAt);
    return Number.isFinite(expiry) && expiry > Date.now();
}

async function captureResourceAuthorization(state) {
    const captured = sessionIdentity(await state.getSession());
    const published = state.current?.revision ?? -1;
    const seen = captured.revision ?? -1;
    if (state.current === null || published < seen) state.note(captured);
    else if (published === seen && state.current.authenticated && !captured.authenticated) state.note(captured);
    const authorize = (url) => {
        if (!authorize.current()) {
            throw new Error('The Console session changed. Reopen this view before continuing.');
        }
        return resourceDecision(url, captured, state);
    };
    authorize.current = () => captureStillCurrent(state.current, captured);
    return authorize;
}

async function openCapture(state, request) {
    const binding = requireSession(await state.getSession());
    const getCurrentSession = async () => {
        const current = requireSession(await state.getSession());
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
            const url = resolveRoute(route, state.api);
            init.signal?.throwIfAborted();
            const session = await getCurrentSession();
            init.signal?.throwIfAborted();
            const response = await request(url, requestOptions(init, session, state.sameOrigin));
            init.signal?.throwIfAborted();
            await getCurrentSession();
            return response;
        },
    });
}

export function createConsoleBrowserTransport({ apiBaseUrl, applicationBaseUrl, getSession, request = fetch }) {
    const api = configuredApi(apiBaseUrl);
    const application = new URL(applicationBaseUrl);
    const state = {
        api,
        application,
        sameOrigin: api.origin === application.origin,
        getSession,
        current: null,
        note: null,
        arm: null,
    };
    const listeners = new Set();
    state.note = (next) => {
        state.current = next;
        state.arm(next);
        publishListeners(listeners, next);
    };
    state.arm = armExpiry({ id: null }, () => state.current, state.note);
    return Object.freeze({
        apiBase: api.href,
        publishSession(snapshot) { state.note(sessionIdentity(snapshot)); },
        subscribeSession(listener) {
            listeners.add(listener);
            return () => listeners.delete(listener);
        },
        captureResourceAuthorization: () => captureResourceAuthorization(state),
        capture: () => openCapture(state, request),
    });
}
