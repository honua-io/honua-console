# `src/sdk/` — single SDK import boundary

This directory is the **only** place in Console allowed to import from
`@honua/sdk-js`. Feature folders import from `src/sdk/...` instead.

Enforced by ESLint rule `no-restricted-imports` in `eslint.config.js`. The rule
allows `@honua/sdk-js/*` only under `src/sdk/**`.

## Files

- `session.ts` — `SessionClient` facade and identity / capability / entitlement DTOs.
  Synthesizes a bundle from three admin endpoints today; collapses to one
  call when `honua-server#1162` ships the non-admin capability endpoint.
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

## Adding a new SDK surface

1. Add a new file `src/sdk/<area>.ts`.
2. Re-export the SDK types/values you need.
3. Document any pending-binding gaps inline so the eventual SDK publish can be
   tracked.
4. Feature code imports from `~/sdk/<area>` (or relative).

Do **not** add an SDK barrel that re-exports everything from a single file;
keep per-area barrels so feature code stays tree-shake-friendly.
