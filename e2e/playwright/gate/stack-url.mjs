// Accept the stack origin or its Console root, preserving the shipping path base.
export function gateConsoleUrl(input) {
  if (!input?.trim()) throw new Error('HONUA_GATE_STACK_URL is required.');
  const url = new URL(input.trim());
  if (!['http:', 'https:'].includes(url.protocol) || url.username || url.password ||
      url.search || url.hash || !['/', '/console', '/console/'].includes(url.pathname)) {
    throw new Error('HONUA_GATE_STACK_URL must be an HTTP(S) stack origin or /console/ root without credentials, query or fragment.');
  }
  return new URL('/console/', url).href;
}
