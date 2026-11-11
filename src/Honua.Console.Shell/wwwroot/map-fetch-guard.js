// MapLibre's worker loads this with importScripts, which cannot import the map module.
(function (scope) {
    if (scope.__honuaAuthorizedFetch || typeof scope.fetch !== 'function') return;
    const original = scope.fetch.bind(scope);
    scope.fetch = function (input, init) {
        return guardedAuthorizedFetch(original, input, init);
    };
    scope.__honuaAuthorizedFetch = true;
})(globalThis);

function guardedAuthorizedFetch(original, input, init) {
    const request = typeof Request !== 'undefined' && input instanceof Request ? input : null;
    const headers = new Headers(request ? request.headers : init?.headers);
    if (!headers.has('Authorization')) return original(input, init);
    if (request) return original(new Request(request, { redirect: 'error' }));
    return original(input, { ...(init ?? {}), redirect: 'error' });
}
