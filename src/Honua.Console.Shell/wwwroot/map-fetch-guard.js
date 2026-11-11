// MapLibre workers load this file with importScripts. It has no imports, so the map module can load it too.
function guardedAuthorizedFetch(original, input, init) {
    let effective;
    try {
        effective = new Request(input, init);
    } catch {
        return original(input, init);
    }
    if (!new Headers(effective.headers).has('Authorization')) return original(input, init);
    return original(new Request(effective, { redirect: 'error', signal: effective.signal }));
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
