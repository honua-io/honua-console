# Catalog administration v4 browser evidence

Captured 13 November 2026 from actual bUnit page renders with the shared v4 flag enabled.
The fixtures use test-only data. bUnit covers actions, read-context boundaries and the legacy flag-off branch; Playwright checks presentation, focus, overflow and axe accessibility at 1280 and 390 pixels.
The browser fixtures render HTML without a running Blazor circuit, so these captures do not prove live server integration.

The original v4 mockups were unavailable in the lane handoff. Direct mockup comparison remains required before promotion.

## Reproduce

Run through the build-host shim:

```bash
remote-run bash -c 'HONUA_CATALOG_PREVIEW_DIR="$PWD/artifacts/catalog-admin/previews" dotnet test tests/Honua.Console.IntegrationTests/Honua.Console.IntegrationTests.csproj --configuration Release --filter "FullyQualifiedName~CatalogAdminPageTests|FullyQualifiedName~CatalogAdminOverviewTests|FullyQualifiedName~CatalogAdminSettingsTests|FullyQualifiedName~CatalogAdminAuthoringTests|FullyQualifiedName~CatalogCategoriesTests|FullyQualifiedName~CatalogListingEditorTests|FullyQualifiedName~CatalogsDiscoveryPageRenderTests"'
```

Then, after `npm ci --prefix e2e`, run on the lane host:

```bash
node e2e/playwright/catalog-admin-presentation.mjs
```

## Screenshots

| Page | 1280 px | 390 px |
| --- | --- | --- |
| catalog-admin-list-v4 | [Desktop](../images/catalog-admin-v4/catalog-admin-list-v4-1280.png) | [Mobile](../images/catalog-admin-v4/catalog-admin-list-v4-390.png) |
| catalog-admin-overview-v4 | [Desktop](../images/catalog-admin-v4/catalog-admin-overview-v4-1280.png) | [Mobile](../images/catalog-admin-v4/catalog-admin-overview-v4-390.png) |
| catalog-endpoint-v4 | [Desktop](../images/catalog-admin-v4/catalog-endpoint-v4-1280.png) | [Mobile](../images/catalog-admin-v4/catalog-endpoint-v4-390.png) |
| catalog-entry-v4 | [Desktop](../images/catalog-admin-v4/catalog-entry-v4-1280.png) | [Mobile](../images/catalog-admin-v4/catalog-entry-v4-390.png) |
