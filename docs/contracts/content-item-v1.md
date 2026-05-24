# Content Item, Catalog, Share, Embed, And Open Data Contracts

Status: active for `honua-console#4`.

This note documents the Console-side contract used by the ported Catalog, map
viewer, saved maps, Share, Embed, and public open-data surfaces. The JSON
Schemas remain authoritative; this file describes how Console routes and
fixture clients consume them.

## Canonical Schemas

| Schema | Runtime use |
| --- | --- |
| `schemas/content-item-v1.json` | Full `ContentItem`, `ContentItemSummary`, access, endpoints, dependencies, source history, and service metadata. |
| `schemas/catalog-api-v1.json` | Catalog list/detail/dependency/error envelopes. |
| `schemas/webmap-doc-v1.json` | Durable saved-map document, layer state, basemap, initial viewpoint, and annotation workspace. |
| `schemas/share-access-v1.json` | Sharing tier, group IDs, public-link token slot, and `embeddable` flag. |
| `schemas/embed-token-v1.json` | Opaque embed-token descriptor returned by the future server verify endpoint. |
| `schemas/publish-handoff-v1.json` | Admin/publish handoff into catalog content items. |

`src/contracts/content-item.ts` is the only transitional TypeScript mirror for
the content-item schema. Saved maps and publish handoff import or alias those
types rather than re-declaring DTOs. Schema parity is pinned by the catalog,
saved-maps, publish-handoff, and share schema tests until `honua-sdk-js#225`
exports generated browser-safe types.

## Saved Maps And Viewer

Saved maps are catalog `ContentItem` records with `type="map"` and a
`target.webmapJsonRef` that points at a `webmap-doc/v1` document. The fixture
client supports create, read, list, metadata patch, content replace,
duplicate, soft delete, and thumbnail upload against the same shape the future
SDK client will expose.

Saved-map metadata writes use the shared content-item limits:

- title: required, max 280 characters.
- summary: max 280 characters; blank or null normalizes to `No summary provided.`.
- tags: non-empty, unique, max 64 characters each.

Saved-map `ContentItem.id` values and dependency ids must be 26-character
Crockford base32 ULIDs. `endpoints.self.accessURL` and thumbnail URLs must be
absolute `http(s)` URLs to satisfy `content-item/v1`; the stable route path
inside those URLs remains `/maps/:id`. The route helper `mapUrl()` and share
helpers still emit legacy relative `/maps/...` paths for in-app navigation and
clipboard output. Console IA aliases are available at the router level.

Viewer routes accept both Console IA and legacy Portal paths:

- `/catalog/maps` and `/maps` list saved maps.
- `/catalog/maps/:mapId` and `/maps/:mapId` open a saved map.
- `/catalog/maps/new?from=:itemId` and `/maps/new?from=:itemId` open a catalog service/layer source in the viewer.

For catalog `type="map"` items, open actions route by the catalog item id.
`target.webmapJsonRef` stays a WebMap document URL owned by the saved-map
loader and must not be encoded into `/maps/:mapId`.

WebMap extents are normalized to WGS84 before catalog publication and viewer
initial-view hydration. Extent-level `spatialReference` wins over document-level
`WebMapDoc.spatialReference`; 4326 passes through; 3857/102100 is reprojected;
unknown or out-of-range extents fall back instead of treating metre coordinates
as lon/lat.

## Publish Handoff

Publish handoff projects an admin event into a catalog `ContentItem` with
`type="service"`. The upsert layer owns the catalog item id and must provide a
schema-valid ULID. `target.serviceUrl` and every non-tile `ServiceLink.accessURL`
must be absolute `http(s)` URLs; relative paths and non-http schemes are
rejected before the fixture client indexes or mutates a record.

Re-publish and metadata-update events preserve item id, created timestamp,
`endpoints.self`, access, preview, dependencies, and source history. Invalid
service URLs on update leave the stored item unchanged.

## Share And Embed

`ShareAccess` has five ordered tiers: `private`, `org`, `group`, `public-link`,
and `public`. The `embeddable` flag is independent of tier: public items are
not iframe-renderable unless the owner explicitly allows embeds.

Copied share URLs are centralized in `buildShareUrl()`:

- tiers narrower than `public-link` are not URL-shareable.
- public open-data service/layer/document items emit `/public/items/:slug`.
- public-link service/layer/document items emit `/public/items/:slug?token=...`.
- public/public-link maps emit `/maps/:id`, with `?token=...` for public-link.
- other public/public-link items emit `/catalog/:slug`, with `?token=...` for public-link.

Embed snippets target the legacy iframe-compatible route:

```text
/embed/maps/:id?chrome=minimal|none|full&legend=on|off&zoom=on|off&extent=W,S,E,N#embedToken=...
```

Embed tokens live in the URL fragment as `#embedToken=...` so they do not reach
access logs or referer headers. The embed route resolves auth before mounting
MapLibre.

## Public Open Data

Anonymous open-data routes are available at both Console IA and legacy paths:

- `/share/public` and `/public`.
- `/share/public/items/:idOrSlug` and `/public/items/:idOrSlug`.

The public collection lists only `ContentItemSummary` rows where
`sharing === "public"`, `openData === true`, and `type` is `service`, `layer`,
or `document`. Item pages render when the item is public open data or when a
public-link item is requested with the matching `?token=` value.

## Deferred Contract Work

- Swap fixture clients and token redemption for `honua-sdk-js#225` /
  `honua-server#1162`.
- Canonicalize emitted URLs to Console IA paths while retaining legacy aliases.
- Add DCAT-US 3.0 / `data.json` generation and validation.
- Document deployment-owned headers/metadata for embed routes, including
  `noindex,nofollow` and frame policy.
