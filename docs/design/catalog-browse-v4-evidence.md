# Catalog browse v4 browser evidence

Captured 13 November 2026 from actual bUnit page renders with the shared v4 flag enabled.
The fixtures use test-only data. bUnit covers actions, read-context boundaries and the legacy flag-off branch; Playwright checks presentation, focus, overflow and axe accessibility at 1280 and 390 pixels.
The browser fixtures render HTML without a running Blazor circuit, so these captures do not prove live server integration.

The original v4 mockups were unavailable in the lane handoff. Direct mockup comparison remains required before promotion.

## Reproduce

Run through the build-host shim:

```bash
HONUA_CATALOG_PREVIEW_DIR="$PWD/artifacts/catalog-browse/previews" dotnet test tests/Honua.Console.IntegrationTests/Honua.Console.IntegrationTests.csproj --configuration Release --filter "FullyQualifiedName~CatalogBrowseV4Tests|FullyQualifiedName~CatalogsDiscoveryPageRenderTests"
```

Then, after `npm ci --prefix e2e`, run on the lane host:

```bash
node e2e/playwright/catalog-browse-presentation.mjs
```

## Screenshots

| Page | 1280 px | 390 px |
| --- | --- | --- |
| catalog-browse-v4 | [Desktop](../images/catalog-browse-v4/catalog-browse-v4-1280.png) | [Mobile](../images/catalog-browse-v4/catalog-browse-v4-390.png) |
| catalog-item-overview-v4 | [Desktop](../images/catalog-browse-v4/catalog-item-overview-v4-1280.png) | [Mobile](../images/catalog-browse-v4/catalog-item-overview-v4-390.png) |
| catalog-item-permissions-v4 | [Desktop](../images/catalog-browse-v4/catalog-item-permissions-v4-1280.png) | [Mobile](../images/catalog-browse-v4/catalog-item-permissions-v4-390.png) |
| catalog-discovery-v4 | [Desktop](../images/catalog-browse-v4/catalog-discovery-v4-1280.png) | [Mobile](../images/catalog-browse-v4/catalog-discovery-v4-390.png) |
