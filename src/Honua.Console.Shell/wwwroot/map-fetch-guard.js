// MapLibre workers load this file with importScripts. It has no imports, so the map module can load it too.
function guardedAuthorizedFetch(original, input, init) {
    const headerSource = init && init.headers != null
        ? init.headers
        : (typeof Request !== 'undefined' && input instanceof Request ? input.headers : undefined);
    let authorized = false;
    try {
        authorized = new Headers(headerSource).has('Authorization');
    } catch {
        return original(input, init);
    }
    if (!authorized) return original(input, init);
    try {
        return original(new Request(input, { ...(init || {}), redirect: 'error' }));
    } catch {
        return original(input, init);
    }
}

function installGuardedFetch(scope) {
    if (!scope || scope.__honuaAuthorizedFetch || typeof scope.fetch !== 'function') return;
    const original = scope.fetch.bind(scope);
    scope.fetch = (input, init) => guardedAuthorizedFetch(original, input, init);
    scope.__honuaAuthorizedFetch = true;
}

globalThis.guardedAuthorizedFetch = guardedAuthorizedFetch;
globalThis.installGuardedFetch = installGuardedFetch;
installGuardedFetch(globalThis);
