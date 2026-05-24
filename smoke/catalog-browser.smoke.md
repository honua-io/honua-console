## Smoke Evidence — Catalog Browser And Item Detail Pages (`honua-console#4`)

This note records what the catalog browser and item detail surfaces are exercised against in CI today. Update it whenever a behavior change touches the catalog, viewer, share, embed, or open-data flows. The Console port preserves the Portal-side row matrix verbatim — every row keeps mapping AC → test file with the same evidence rows. Source repo history is `honua-portal#12`; ongoing ownership is `honua-console#4`.

### Flows Covered

| Flow | Coverage | Evidence file |
| --- | --- | --- |
| Catalog list (single round-trip) | `CatalogPage` mounts and renders every fixture as a `<CatalogCard>` from one `listItems` call; `getItem` is never called for cards. | `src/catalog/__tests__/CatalogPage.test.tsx` |
| Find by title / tag / type | Search input narrows the grid to "Permit Finder" for `q=permit`. The Service type facet narrows the grid to service items only. The `basemap` tag facet narrows the grid to a single basemap card. | `src/catalog/__tests__/CatalogPage.test.tsx` |
| Sort matrix | Default `modified-desc`, `title-asc`, and `relevance` (q-driven) all sort the fixture list as expected. | `src/catalog/__tests__/sort.test.ts` |
| URL search params | Round-trip of `q`, `type`, `tag`, `owner`, `visibility`, `sort`, `cursor` between URL and state; unknown enum values are dropped silently; the `visibility` URL param maps to the wire `sharing` request param. | `src/catalog/__tests__/searchParams.test.ts` |
| Pagination cursor | Wraps the fixture client to a page size of 3, then "Load more" appends the next page's items into the grid using the `nextCursor`. | `src/catalog/__tests__/CatalogPage.test.tsx` |
| Empty / unauthorized / missing / error surfaces | Catalog "no matching items" surface shown for `q` with no matches; catalog client error surfaces; unauthorized fixture maps to "you don't have access" surface; missing id maps to "Item not found"; both are distinct from each other. | `src/catalog/__tests__/CatalogPage.test.tsx`, `src/catalog/__tests__/ItemDetailPage.test.tsx` |
| Item detail — section coverage | Service detail renders Description, Extent, Metadata, Endpoints, Capabilities, and Dependencies sections in one `getItem` call. License URL, attribution, and source kind all rendered. | `src/catalog/__tests__/ItemDetailPage.test.tsx` |
| Item detail — empty cells | The `state-elevation-mosaic` external-url fixture has no extent, no SPDX, no native CRS — empty cells render `—` consistently rather than "undefined". | `src/catalog/__tests__/ItemDetailPage.test.tsx` |
| Open-in-map gating matrix | `getOpenAction` returns `open-in-map` for service+layer+map, `unsupported` for scenes (Beta has no scene viewer) and publisher-asserted unsupported services (`extensions["honua-portal-viewer"].supported = false`), and `open-external` for app, document, and external-url. Tested against every fixture and against altered service/layer/map permutations. | `src/catalog/__tests__/openability.test.ts` |
| Card-vs-detail openability parity | `CatalogCard` consumes the same gate as the detail page via `ContentItemSummary.viewerSupport` (projected from `extensions["honua-portal-viewer"]` by `summarize()`). The legacy WMS service shows the unsupported pill on the card without a per-item detail fetch. Scenes and openable items render the right pill on the card too. | `src/catalog/__tests__/CatalogCard.test.tsx` |
| Dependency closure openability | `defaultUnsupportedPredicate` reuses `getOpenAction(item).kind === "unsupported"` so a scene dependency without a publisher override is still categorized as unsupported in share/embed/open-data review. | `src/catalog/__tests__/dependencies.test.ts` |
| Stale Load more guard | A pending Load more is dropped when filters change before it resolves; the post-filter grid is not contaminated with the stale page. The Load more button re-enables for the post-filter list — the stuck-disabled state from the orphaned request is reset on every base reload. | `src/catalog/__tests__/CatalogPage.test.tsx` |
| Card-vs-detail openability for query-only services | A service summary with `capabilities: ["query"]` and no `viewerSupport` override renders as `unsupported` on the card (no endpoints to verify on the wire) while the same item with full endpoints renders as `open-in-map` on the detail page. Prevents the inverse drift where a card promises an Open in map action that the detail would disable. | `src/catalog/__tests__/openability.test.ts` |
| Render-capable summary stays openable | A service summary with `render`/`tiles`/`pbf` capability remains `open-in-map` even though its summary projection has no endpoints — those capabilities are sufficient on their own. | `src/catalog/__tests__/openability.test.ts` |
| Antimeridian extent preview | An extent with `west > east` (e.g. `[170, -10, -170, 10]`) renders as two SVG rectangles (`[west, 180]` and `[-180, east]`) rather than collapsing to a sliver near 180°E. The `data-antimeridian` attribute is set to `"true"` for assertion. | `src/catalog/__tests__/ExtentPreview.test.tsx` |
| Dependency closure total cap | The `limit` parameter caps the SUM of `nodes` + `missing` + `unauthorized` + `unsupported`. A root with many missing branches over the cap stops at the cap and reports `truncated: true`, so non-viewable branches cannot bypass the bound by hiding in failure arrays. | `src/catalog/__tests__/dependencies.test.ts` |
| Publish handoff carries viewer override | The handoff schema accepts optional `extensions`; a fixture with `honua-portal-viewer.supported=false` validates and round-trips into a `ContentItem` whose `summarize()` projection sets `viewerSupport.supported=false`. | `src/catalog/__tests__/publish-handoff.test.ts` |
| Detail action wiring | Detail page primary action renders as a `<Link>` to `/maps/...` for openable items, an external `<a target="_blank" rel="noopener">` for external items, and a disabled `<button>` with the unsupported reason for unsupported items. | `src/catalog/__tests__/ItemDetailPage.test.tsx` |
| Catalog → detail navigation | Clicking a card title link navigates to `/catalog/:idOrSlug` and the detail page renders. | `src/catalog/__tests__/ItemDetailPage.test.tsx` |
| Dependency closure on demand | Closure walker is not called on detail mount; "Show full dependency closure" triggers exactly one `getDependencies` call and renders a categorized summary. | `src/catalog/__tests__/ItemDetailPage.test.tsx` |
| Public open-data item pages | Anonymous `/public` lists public open-data services, layers, and documents; `/public/items/:idOrSlug` renders metadata, preview extent, download/API rows, API examples, and JSON-LD while private/non-open-data items get a generic not-found surface. | `src/open-data/OpenDataItemPage.test.tsx`, `tests/smoke/shell.spec.ts` |

### How To Run Locally

```bash
npm install
npm test
npm run typecheck
```

Last green run on this branch: 107 vitest assertions across 11 files; `tsc --noEmit` clean.

### Flows Deferred Out Of This Ticket

These remain bounded into other tickets and are not exercised here:

- **MapLibre-backed extent preview.** Catalog ships an SVG bbox overlay only; full basemap rendering rides with the viewer port.
- **Markdown rendering of `description`.** Plain-text paragraph rendering only; markdown moves to a later quality-baseline ticket.
- **DCAT-US 3.0 / `data.json` export from detail.** Public pages render API/download affordances and Schema.org JSON-LD but do not generate DCAT/data.json.
- **Viewer route shape.** `getOpenAction` stages `/maps/new?from=<itemId>` for service/layer and `/maps/<webmapJsonRef>` for map; Console serves both `/maps/...` (legacy) and `/catalog/maps/...` (Console IA) so emitters and tests do not need a synchronized swap.
- **SDK swap.** Active clients are `FixtureCatalogClient`, `FixtureSavedMapClient`, `FixtureShareClient`. Replacement of these with server-backed clients is a follow-up to `honua-sdk-js#225` and `honua-server#1162` — tracked outside `honua-console#4`.

### Empty / Error Surface Coverage

Every non-success outcome routes through the shared `<EmptyState kind={"empty" | "unauthorized" | "unsupported" | "missing" | "error" | "loading"} />`:

- `empty` — catalog query produces zero items.
- `unauthorized` — the unauthorized fixture (`01HXY3ZK7N1J2Q9V8M0FQ2PWAN`) returns `CatalogError("unauthorized")`; the detail page renders the unauthorized surface, distinct from `missing`.
- `missing` — an unknown id returns `CatalogError("missing")`; rendered as "Item not found", not as "you don't have access".
- `unsupported` — publisher-asserted unsupported service (`01HXY3ZK7N1J2Q9V8M0FQ2PWAM`) and scenes both render with a disabled primary action and a visible reason string.
- `error` — generic catalog client failure renders the error surface.
