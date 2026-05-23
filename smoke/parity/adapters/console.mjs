// Console adapter: assembles the same-origin URLs Console exposes for each
// surface and verifies same-origin properties (no cross-origin embeds, no
// off-origin Studio previews, etc.). When the porting tickets (#4, #5, #7)
// merge to trunk, replace the URL builders here with imports from the
// shared route map (honua-console#3) — the smoke evidence URLs should not
// drift from what Console actually renders.

export const CONSOLE_ROUTES = Object.freeze({
  catalogItem: (id) => `/catalog/${id}`,
  viewerNewFrom: (sourceId) => `/maps/new?from=${sourceId}`,
  viewerMap: (mapId) => `/maps/${mapId}`,
  studioDraftForMap: (mapId) => `/studio/drafts?source=saved-map&id=${mapId}`,
  generatedAppDetail: (appId) => `/catalog/${appId}`,
  share: (appId) => `/share/items/${appId}`,
  embed: (appId, token) => `/embed/items/${appId}?token=${token}`,
});

export function buildConsoleUrls({ originUrl, items }) {
  if (!originUrl) throw new Error("buildConsoleUrls requires originUrl");
  return {
    catalog: `${originUrl}${CONSOLE_ROUTES.catalogItem(items.serviceItemId)}`,
    viewerHydration: `${originUrl}${CONSOLE_ROUTES.viewerNewFrom(items.serviceItemId)}`,
    viewer: `${originUrl}${CONSOLE_ROUTES.viewerMap(items.savedMapId)}`,
    studio: `${originUrl}${CONSOLE_ROUTES.studioDraftForMap(items.savedMapId)}`,
    generatedApp: `${originUrl}${CONSOLE_ROUTES.generatedAppDetail(items.generatedAppId)}`,
    share: `${originUrl}${CONSOLE_ROUTES.share(items.generatedAppId)}`,
    embed: `${originUrl}${CONSOLE_ROUTES.embed(items.generatedAppId, items.embedToken)}`,
  };
}

export function assertSameOrigin(originUrl, urls) {
  const expectedOrigin = new URL(originUrl).origin;
  for (const [label, value] of Object.entries(urls)) {
    const actualOrigin = new URL(value).origin;
    if (actualOrigin !== expectedOrigin) {
      throw new Error(
        `Same-origin invariant broken: ${label} resolves to ${actualOrigin}, expected ${expectedOrigin}. ` +
          `Console parity requires every surface (catalog, viewer, studio, share, embed) to live on the same origin as the single deployable artifact.`,
      );
    }
  }
}
