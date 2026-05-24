# Saved Web Maps — Smoke Evidence (`honua-console#4`)

This document records the smoke evidence for the saved-maps slice ported into
Honua Console. It maps each acceptance criterion to concrete tests and
contract artifacts in this repo. The tests are run by `npm test` and validate
the path `save → reopen → duplicate → edit-copy independence` against
`FixtureSavedMapClient`, plus schema parity with `content-item/v1` and
`webmap-doc/v1`. Source-repo history is `honua-portal#14`; ongoing ownership
is `honua-console#4`.

## Run

```sh
npm install
npm run typecheck
npm test
```

## Coverage map

| AC | Description | Evidence |
|---|---|---|
| 1 | A user can create a saved map from a published layer. | `src/saved-maps/__tests__/client.test.ts` → "FixtureSavedMapClient.create (AC1: save from a published layer)" |
| 2 | Reopening the map restores state. | `src/saved-maps/__tests__/client.test.ts` → "FixtureSavedMapClient.get + getWebMap (AC2: reopen restores state)" + `src/saved-maps/__tests__/serializer.test.ts` round-trip |
| 3 | Duplicating creates a separate editable copy without mutating the original. | `src/saved-maps/__tests__/client.test.ts` → "FixtureSavedMapClient.duplicate (AC3: duplicate is a true copy)" — includes a byte-equal check after editing the duplicate, plus a regression test that the duplicate's `preview.thumbnail` URL is re-keyed to the duplicate's id and survives source deletion |
| Persisted state | Layer order, visibility, opacity, style references, popup config, basemap, extent. | `src/saved-maps/__tests__/serializer.test.ts` |
| Thumbnail | Capture is best-effort, never blocks save, and enforces the 200 KB encoded-blob cap. | `src/saved-maps/__tests__/thumbnail.test.ts` + `src/saved-maps/__tests__/actions.test.ts` "save proceeds even when thumbnail capture fails" |
| Stable URL | `/maps/{id}` canonical, ephemeral view-state in query string. | `src/saved-maps/__tests__/url.test.ts` |
| Empty/error surfaces | Missing item → `null`; soft-deleted item → `null`; non-owner mutation → `SavedMapForbiddenError`. | `src/saved-maps/__tests__/client.test.ts` |
| Read permission | Owner reads always; org-shared maps readable by any authenticated actor; public-link/public readable anonymously; private maps unreadable + non-listable + non-duplicable by non-owner. | `src/saved-maps/__tests__/client.test.ts` → "FixtureSavedMapClient read permission enforcement" |
| Catalog extent CRS | WebMap viewpoint extent is normalized to WGS84 lon/lat on the catalog item; 3857/102100 reprojected; unknown WKID → `extent: null`. | `src/saved-maps/__tests__/client.test.ts` → "FixtureSavedMapClient extent CRS normalization" |
| Metadata validation | content-item/v1 invariants on title (1–280), summary (≤ 280), tags (unique non-empty ≤ 64), schema-valid ULID ids, and absolute http(s) metadata URLs. | `src/saved-maps/__tests__/client.test.ts` → "FixtureSavedMapClient metadata validation" |
| Schema parity | Emitted ContentItem and WebMapDoc validate against the JSON Schemas with Ajv pattern/format enforcement; `type='map'` requires the map target shape. | `src/saved-maps/__tests__/schema-parity.test.ts` |

## Cross-repo coordination

The Console slice lands behind `FixtureSavedMapClient`. Production wiring
requires the following bounded child tickets, recorded but not implemented
here:

- **honua-server#1162** — Saved-map endpoints with owner-scoped sharing
  (`POST /api/v1/console/maps`, `GET /{id}`, `GET /{id}/webmap`,
  `PUT /{id}/webmap`, `PATCH /{id}`, `DELETE /{id}` soft delete,
  `POST /{id}/thumbnail`) and the owner-scoped permission middleware.
  Server-side duplicate optimization
  (`POST /api/v1/console/maps?from={id}`) is included.
- **honua-sdk-js#225** — `HonuaPortalCatalog.savedMaps` browser-safe
  surface plus generated types from the JSON Schemas in this repo.
- **honua-console** (follow-up to `honua-sdk-js#225`) — Replace
  `FixtureSavedMapClient` with the SDK surface. Not on `honua-console#4`.
