# Server / SDK Gap Log

Status: filed 2026-05-23 (honua-console#7).

This is the explicit list of upstream contracts Console is waiting on. Each row
is the source of truth for one `<ResourceState kind="pending-binding" />`
rendered in Console. No Console-local DTOs were introduced to fill these gaps.

When a row is resolved, the wiring update is mechanical:

1. Add or update the re-export in `src/sdk/<area>.ts`.
2. Replace the corresponding `pending-binding` LoadSurface in the matching
   feature folder with a real loader.
3. Remove the row here.

## Open Gaps

| Surface | Console hook | Waits on | Notes |
| --- | --- | --- | --- |
| Catalog content-item list/detail/search/create/update | `useContentItemList` (`src/features/catalog/useContentItemList.ts`) | [honua-sdk-js#225](https://github.com/honua-io/honua-sdk-js/issues/225) | Once `@honua/sdk-js` publishes `ContentItem`, `ContentOwner`, `ContentAccess`, and metadata v2 projections, expose them from `src/sdk/content.ts`. Replace `MetadataV2Pending` markers there. |
| Dashboard package client | (no hook yet — render through `DASHBOARD_PACKAGE_PENDING`) | [honua-sdk-js#225](https://github.com/honua-io/honua-sdk-js/issues/225) | Add `HonuaDashboardPackagesClient` re-export to `src/sdk/packages.ts` when published; mirror `usePackageList` for dashboards. |
| Report package client | (no hook yet — render through `REPORT_PACKAGE_PENDING`) | [honua-sdk-js#225](https://github.com/honua-io/honua-sdk-js/issues/225) | Same wiring pattern as dashboard packages. |
| App package client | (covered by `previewGeneratedApp` for the proof slice) | [honua-sdk-js#225](https://github.com/honua-io/honua-sdk-js/issues/225) and [honua-sdk-js#226](https://github.com/honua-io/honua-sdk-js/issues/226) | Generated-app preview already wires through `src/sdk/generated-app.ts`. App package list/CRUD waits on #225. |
| Non-admin capability bundle endpoint | `SessionClient.bootstrap` (`src/sdk/session.ts`) | [honua-server#1162](https://github.com/honua-io/honua-server/issues/1162) | Today Console fans out three admin endpoints (`auth/session`, `users/{id}/effective-permissions`, `license/entitlements`) and maps 401 to an `anonymous` bundle so non-admin users still render. When the unified endpoint lands, replace the fan-out in `SessionClient.bootstrap`; consumers stay put. |
| Saved-map list/detail projection | `usePackageList` (`src/features/catalog/usePackageList.ts`) covers map packages; saved-map item projection still pending | [honua-sdk-js#225](https://github.com/honua-io/honua-sdk-js/issues/225) | The `SavedMapItem` shape used by Portal isn't published yet. Once exposed, add re-exports to `src/sdk/content.ts` and wire a `useSavedMapList`. |
| Provenance read API | `useProvenance` (`src/features/operate/useProvenance.ts`) accepts a loader callback | [honua-server#1162](https://github.com/honua-io/honua-server/issues/1162) | The hook uses the SDK-owned `ProvenanceRecord` type today; the caller currently passes `undefined`, so the surface renders `pending-binding`. When the server provenance read endpoint lands, pass an `async` loader that yields `ReadonlyArray<ProvenanceRecord>`. |
| Sharing list view | `SharePage` (`src/pages/SharePage.tsx`) | [honua-sdk-js#225](https://github.com/honua-io/honua-sdk-js/issues/225) | `useShareMutate` is wired today. The list view waits on the saved-map projection so we can render a "who has access" list. |
| Open data + embed loaders | (not yet defined) | [honua-sdk-js#225](https://github.com/honua-io/honua-sdk-js/issues/225) | Will follow the saved-map projection once it lands. |

## Resolved Gaps

(none yet — first wiring lands with this ticket)

## Hoist Candidates

- `LoadSurface<T>` is duplicated in `honua-portal/src/saved-maps/types.ts` and
  Console (`src/surfaces/LoadSurface.ts`). Hoist into `@honua/sdk-js/contract`
  once the shape is stable (design Q3); both repos switch to the SDK type
  without API change.
- Console smoke event `{ surface, sdkSubpath, status, durationMs }` mirrors
  Portal's app-builder telemetry shape closely. A shared smoke bus belongs in
  the SDK or `honua-devops`; for now both repos co-exist on the same window
  custom event name.
