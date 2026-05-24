# `src/sdk/` — single SDK import boundary

This directory is the **only** place in Console allowed to import from
`@honua/sdk-js`. Feature folders import from `src/sdk/...` instead.

Enforced by ESLint rule `no-restricted-imports` in `eslint.config.js`. The rule
allows `@honua/sdk-js/*` only under `src/sdk/**` for application code. Tests may
import SDK fixtures directly, but the protocol DTO redeclaration guard still
applies there.

## Stable SDK subpaths used now

- `@honua/sdk-js/honua`
- `@honua/sdk-js/control-plane`
- `@honua/sdk-js/runtime`
- `@honua/sdk-js/generated-app`
- `@honua/sdk-js/operator/workspace`
- `@honua/sdk-js/collaboration`

Content item, saved-map, dashboard package, report package, app package, open
data, and embed projections are pending upstream publishes and are tracked in
`docs/server-sdk-gap-log.md`.

## Files

- `session.ts` — `SessionClient` facade and identity / capability / entitlement DTOs.
  Synthesizes a bundle from three admin endpoints today; collapses to one
  call when `honua-server#1162` ships the non-admin capability endpoint. Its
  public bootstrap result is `{ status, fellBackEndpoints }`, where 401 on the
  session endpoint becomes `anonymous` and 401/403 on secondary bundle endpoints
  are reported through `fellBackEndpoints`. The transitional permissions adapter
  reads server `PermissionGrantResponse` grants (`service`, `layer`,
  `operation`) from the user-id based endpoint and bridges wildcard grants to
  the current Console route gate labels. Entitlements are read from the flat
  active entitlement list returned by `/admin/license/entitlements`.
- `content.ts` — content item / metadata v2 / provenance projections.
  Currently exports `MetadataV2Pending` markers; `honua-sdk-js#225` will
  publish the real `ContentItem`, `SavedMapItem`, `ContentOwner`,
  `ContentAccess`, and provenance types.
- `packages.ts` — re-exports map-package clients and locators from
  `@honua/sdk-js/control-plane` + `/runtime`. App/dashboard/report
  package clients added as `#225` lands them.
- `sharing.ts` — re-exports the sharing client and share request/response
  DTOs from `/control-plane`.
- `runtime.ts` — re-exports map runtime + package loader functions.
- `generated-app.ts` — re-exports generated-app manifest runtime + projectors.
- `operator.ts` — re-exports operator workspace, provenance, and approval types.
- `collaboration.ts` — re-exports saved-map collaboration client and session.

## Loader response contract

Feature hooks adapt SDK calls into `LoadSurface<T>` from
`src/surfaces/LoadSurface.ts`:

- `ok`: SDK value is available.
- `missing`: the item or route target returned 404.
- `unauthorized`: the request returned 401/403 or a server-authored gate failed.
- `unsupported`: the service or package binding is not supported; keep the
  SDK reason/code when present.
- `pending-binding`: an upstream server or SDK contract has not shipped yet;
  include `waitingFor` with the tracked dependency.

Render non-`ok` states with `ResourceState` or `ResourceStateFor`; do not throw
SDK errors into React render paths.

## Adding a new SDK surface

1. Add a new file `src/sdk/<area>.ts`.
2. Re-export the SDK types/values you need.
3. Document any pending-binding gaps inline so the eventual SDK publish can be
   tracked.
4. Feature code imports from the per-area SDK file by relative path.

Do **not** add an SDK barrel that re-exports everything from a single file;
keep per-area barrels so feature code stays tree-shake-friendly.
