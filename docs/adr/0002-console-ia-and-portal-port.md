# ADR-0002: Console IA, Fixture Posture, And Portal-Path Compatibility

## Status

Accepted

## Date

2026-05-23

## Context

[ADR-0001](0001-unified-honua-console-runtime.md) committed Honua to one product surface and one deployment runtime under `honua-console`. Implementation work begins with [honua-console#4](https://github.com/honua-io/honua-console/issues/4): port Catalog browse + item detail, the map viewer, saved maps, sharing + embed, and the public open-data pages from `honua-portal` into Console.

Two decisions emerged during the port that are worth recording so subsequent Console work (`#5`, `#6`, `#7`, `#9`) can rely on the same posture instead of re-deriving it.

## Decisions

### 1. Console IA route groups

Routes are grouped under top-level area paths instead of inheriting Portal's flat `/maps`, `/embed/maps`, `/public` taxonomy:

| Console path | Group | Surface |
|---|---|---|
| `/` | Home | landing |
| `/catalog` | Catalog | `CatalogPage` |
| `/catalog/:idOrSlug` | Catalog | `ItemDetailPage` |
| `/catalog/maps` | Catalog | saved-maps list |
| `/catalog/maps/:mapId` | Catalog | `MapViewerSurface` mode=viewer |
| `/share/public` | Share | `OpenDataCollectionPage` (anonymous) |
| `/share/public/items/:idOrSlug` | Share | `OpenDataItemPage` (anonymous) |
| `/share/embed/maps/:mapId` | Share | `MapViewerSurface` mode=embed (no shell, token via fragment) |
| `/data`, `/groups` | Catalog | placeholders |
| `/auth/signin`, `/auth/signed-out`, `/auth/callback` | (system) | anonymous |

This matches the design brief for `#4` and lets the upcoming Studio (`#5`), Operate (`#6`), and Share-only flows slot into the same `Catalog | Studio | Operate | Share` mental model without re-architecting later.

### 2. Legacy Portal URLs stay valid as compatibility aliases

Embed snippets already in the wild target Portal's `/embed/maps/:id` (with the embed token in the URL fragment) and customers may have linked `/maps/:id` and `/public/items/:idOrSlug`. The Console router serves the legacy paths through the same components as the new Console-IA paths. The URL fragment (embed token) and query string (chrome / legend / zoom / extent) survive untouched because the same component handles both routes.

Helpers that emit URLs (`mapUrl`, the saved-map fixture renderer, share snippet builder, open-data `publicItemPath`, the `openInMap` action) continue to emit the legacy `/maps/...`, `/embed/maps/...`, `/public/...` URL space. Nav-level user-visible links (`NavConfig.NAV_ITEMS`, the AppShell `Maps` and `Public` items) point at the new Console-IA paths. Canonicalising URL emission to Console-IA paths is a follow-up cleanup ticket; it does not block `#4`.

### 3. Fixture posture and contracts

The Console port inherits Portal's fixture-backed clients:

- `FixtureCatalogClient` — `listItems`, `getItem`, `getDependencies`, `patchAccess`.
- `FixtureSavedMapClient` — full saved-map CRUD against `webmap-doc/v1`.
- `FixtureShareClient` — `patchAccess`, `mintEmbedToken`, `redeemEmbedToken`, `listMyGroups`.

JSON Schemas under `schemas/` (`content-item-v1`, `webmap-doc-v1`, `share-access-v1`, `embed-token-v1`, `catalog-api-v1`, `publish-handoff-v1`) are the canonical contracts. TS mirrors in `src/contracts/` and `src/saved-maps/types.ts` are explicitly transitional and pinned by schema-parity tests until [honua-sdk-js#225](https://github.com/honua-io/honua-sdk-js/issues/225) exports generated types. Console never forks the wire contract.

The fixture-to-SDK swap is mechanical (replace the client constructor; drop the `Fixture*Client` import) and is tracked as a follow-up Console ticket. It is **not** on `#4`.

### 4. Class-name taxonomy

Portal's `hp-*` CSS class prefix was renamed to `hc-*` in one pass across `src/**/*.css`, `src/**/*.tsx`, and route CSS. The smoke specs, JSON-LD publisher name (`Honua Console`), AppShell brand text, sign-in heading, and HTML `<title>` were updated to "Honua Console" in the same pass.

The transitional wire identifiers `extensions["honua-portal-viewer"]`, `Honua:Portal:v1` (a `SERVICE_FORMATS` token), the saved-map `authoringApp: "honua-portal"`, and `MAPUTNIK_STYLE_ID_PREFIX = "honua-portal-style-editor"` are **not** renamed — they are wire-level contract identifiers shared with already-published items and admin handoffs.

## Consequences

- Console's Catalog and Share groups gain a stable IA shape that Studio (`#5`), Operate (`#6`), and the cross-surface smoke (`#9`) can build on without re-debating route taxonomy.
- Customer embed snippets and shared public-link URLs continue to work without server-side rewrites; Console's client router absorbs the compat redirects.
- The next Console-side cleanup ticket can canonicalise URL emission to Console-IA paths (currently the saved-map `accessURL`, share snippet, and `openInMap` action still emit Portal-flavored paths) without touching wire contracts.
- The SDK swap is a known, bounded follow-up. Until it lands, Console's catalog/saved-map/share writes are fixture-only and do not reach the server.
