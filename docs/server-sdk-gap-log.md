# Server / SDK Gap Log

Status: filed 2026-05-23 (honua-console#7).

This is the explicit list of upstream contracts Console is waiting on. Each row
is the source of truth for one `<ResourceState kind="pending-binding" />`
rendered in Console. No Console-local DTOs were introduced to fill these gaps.

## Implemented Contract Baseline

The current Console shell has the shared response and guard contract in place:

- All SDK-backed loaders return `LoadSurface<T>` from
  `src/surfaces/LoadSurface.ts`.
- Non-`ok` loader states render through `ResourceState`:
  `missing`, `unauthorized`, `unsupported`, and `pending-binding`.
- `pending-binding` must include a `waitingFor` list with upstream issue or
  contract names; this file tracks those values.
- `unsupported` preserves a human-readable `reason` and optional SDK `code`.
- `adaptControlPlaneResult` maps supported control-plane results to `ok`, 404
  to `missing`, and other unsupported SDK responses to `unsupported`.
- `adaptSdkThrown` maps thrown 401/403 errors to `unauthorized`, 404 to
  `missing`, and all other errors to `unsupported`.
- `RequireCapability` gates routes and actions from the server-authored
  capability and entitlement bundle only. Console does not define a local role
  matrix.
- Smoke events use the `honua:console-smoke` window event with detail
  `{ surface, sdkSubpath, status, durationMs, at, detail? }`.

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
| Non-admin capability bundle endpoint | `SessionClient.bootstrap` (`src/sdk/session.ts`) | [honua-server#1162](https://github.com/honua-io/honua-server/issues/1162) | Today Console fans out three admin endpoints (`auth/session`, `users/{id}/effective-permissions`, `license/entitlements`) against the configured Honua server origin. Permissions are requested with a user-id claim, parsed from server `PermissionGrantResponse` grants (`service`, `layer`, `operation`), and wildcard grants bridge to the current Console gate labels until the unified endpoint publishes first-class capabilities. Entitlements parse the flat active entitlement list from `/license/entitlements`. A 401 from the session endpoint becomes `anonymous`; 401s from permissions or entitlements are recorded in `fellBackEndpoints` while the authenticated session keeps rendering with the available bundle. When the unified endpoint lands, replace the fan-out in `SessionClient.bootstrap`; consumers stay put. |
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
  the SDK or `honua-devops`; for now Console emits `honua:console-smoke` so
  Portal-style listeners can subscribe without binding directly to feature
  hooks.
