// Console routes are app paths ("/inbox"). The static bundle is served under a base such as
// /console/, so a root-absolute href would leave the Console. Resolve every app path against the
// document base; the legacy host (base "/") keeps the same URL.
export function consoleHref(path, base = globalThis.document?.baseURI) {
    if (typeof path !== 'string' || !path.startsWith('/') || path.startsWith('//') || path.includes('\\'))
        throw new Error('A Console app path is required.');
    if (!base) return path;
    const root = new URL(base);
    const target = new URL(path.slice(1), new URL('./', root));
    if (target.origin !== root.origin) throw new Error('A Console app path is required.');
    return `${target.pathname}${target.search}${target.hash}`;
}
