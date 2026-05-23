# Honua Studio Port (honua-console#5)

This note records what the Studio port lands in Console, what is intentionally transitional, and what follow-ups remain.

## What landed in honua-console

- React/TypeScript/Vite Console shell (package.json, vite.config.ts, tsconfig.json, index.html, src/main.tsx, src/App.tsx).
- Auth seam: `src/auth/` with fixture + whoami session drivers, `ProtectedRoute`, permissions.
- Shell: `src/shell/` with `AppShell`, `EmptyState`, `Forbidden`, `LoadingShell`, `ErrorBoundary`, `UserMenu`, `NavConfig`. Studio appears in the primary nav alongside Catalog, Operate, and Share (ADR-0001).
- Router: `src/router.tsx` lazy-loads every Studio route via `React.lazy` so the shell, Catalog, and Operate paths do not pay Studio's bundle weight.
- Studio area:
  - `src/studio/proof/` — `StudioProofPage`, `proofFixture`, `proof.css`, `links`, `telemetry`. Renders the prompt -> clarification -> spec/plan -> apply -> preview -> direct edit flow at `/studio/proof`. All six fixture states (`happy`, `clarification`, `unsupported`, `auth-denied`, `oversized`, `apply-failure`) selectable via the `fixture` query param.
  - `src/studio/generated-apps/` — `types`, `lifecycle`, `client` (`FixtureGeneratedAppLifecycleClient` + `HttpGeneratedAppLifecycleClient`), `default-client`, `GeneratedAppLifecycleContext`, `GeneratedAppPreviewPage`, `telemetry`. Mounted at `/studio/apps/:itemId/preview`.
  - `src/studio/charts/ChartSpecView.tsx` — Vega-Lite chart adapter with CSS-bar fallback. The proof fixture's `incidents-by-type` chart now carries a Vega-Lite spec by default.
- Smoke / eval harness: `tests/smoke/app-builder-proof.{spec,config}.ts`, `tests/smoke/generated-apps.spec.ts`, and `fixtures/app-builder/operations-dashboard/*` copied verbatim from `honua-portal`. Ticket id retargeted to `honua-console#5`; chart-spec evidence added to the success-path manifest.
- Vitest unit coverage: `src/studio/proof/proofFixture.test.ts` (fixture normalization + builder-plan shape) and `src/studio/generated-apps/lifecycle.test.ts` (draft -> revision -> publish -> rollback transitions). Run via `npm run test`.

## Source mapping

| honua-portal | honua-console |
|---|---|
| `src/app-builder/AppBuilderProofPage.tsx` | `src/studio/proof/StudioProofPage.tsx` |
| `src/app-builder/proofFixture.ts` | `src/studio/proof/proofFixture.ts` |
| `src/app-builder/proof.css` | `src/studio/proof/proof.css` |
| `src/app-builder/links.ts` | `src/studio/proof/links.ts` |
| `src/app-builder/telemetry.ts` | `src/studio/proof/telemetry.ts` |
| `src/generated-apps/{types,lifecycle,client,default-client,GeneratedAppLifecycleContext,GeneratedAppPreviewPage,telemetry,generated-apps.css}` | `src/studio/generated-apps/...` |
| `tests/smoke/{app-builder-proof.spec,app-builder-proof.config,generated-apps.spec}.ts` | `tests/smoke/...` (verbatim, ticket retargeted) |
| `fixtures/app-builder/operations-dashboard/*` | `fixtures/app-builder/operations-dashboard/*` (verbatim, ticket retargeted; success fixture flags `runtime.chartSpec: vega-lite`) |
| `src/contracts/content-item.ts` | `src/transitional/content-item.ts` (transitional — see below) |
| `src/catalog/CatalogContext.tsx` (subset) | `src/transitional/CatalogContext.tsx` (transitional) |
| `src/catalog/client.ts` (subset: `getItem`) | `src/transitional/catalog-client.ts` (transitional) |
| `src/catalog/default-client.ts` (subset) | `src/transitional/default-catalog-client.ts` (transitional) |

## Reframing rules applied

- Route base: `/app-builder/proof` -> `/studio/proof`. `/apps/:itemId/preview` -> `/studio/apps/:itemId/preview`.
- Copy: "Portal" -> "Honua Console" / "Honua Studio". "portal source" -> "catalog source". Back-links default to Console home until Catalog is ported (#4).
- Telemetry namespace: `proof.*` -> `studio.proof.*`. `generated-app.*` -> `studio.generated-app.*`.
- Window event name: kept as `honua:app-builder-proof` so the ported smoke harness can match without translation. Telemetry detail still carries the new `studio.proof.*` event name on the `detail.name` field.
- Storage namespace: `honua.portal.app-builder-proof.*` -> `honua.console.studio.proof.*`.
- Preview URL builder: `portal.honua.example/apps/...` -> `console.honua.example/studio/apps/...`.
- `self` link format: `Honua:Portal:v1` -> `Honua:Console:v1` (Console value added alongside the legacy one in `src/transitional/conforms-to.ts`).

## Usage

- `/studio/proof` accepts a `?fixture=` query param to select any of `happy`, `clarification`, `unsupported`, `auth-denied`, `oversized`, `apply-failure`. Default is `happy`.
- `/studio/apps/:itemId/preview` reads a stored `AppPackage` + manifest from the lifecycle store; it does not re-invoke generation. With the default fixture client the seeded item id is `01J7APPS00000000000000`.
- Lifecycle client selection is controlled by `VITE_GENERATED_APP_LIFECYCLE_CLIENT`:
  - `auto` (default): fixture client when `VITE_SESSION_DRIVER=fixture`, HTTP client otherwise.
  - `fixture`: force the in-memory `FixtureGeneratedAppLifecycleClient` seeded from `fixtures/catalog/proof-source-map.json`.
  - `http`: force `HttpGeneratedAppLifecycleClient` against `VITE_CONSOLE_API_BASE_URL` (defaults to `/api/v1/console`).

## Transitional shims (retire on cleanup tickets)

The Studio port consumes shared SDK and server contracts via `@honua/sdk-js/operator`, `@honua/sdk-js/exploration`, `@honua/sdk-js/runtime`, `@honua/sdk-js/contract`. The browser-safe catalog/content-item/lifecycle contract is not yet published by `@honua/sdk-js` (tracked by honua-sdk-js#225). Until that lands:

- `src/transitional/content-item.ts` mirrors `content-item/v1`. Retire and re-export from `@honua/sdk-js` once honua-sdk-js#225 ships.
- `src/transitional/{catalog-client,CatalogContext,default-catalog-client}.ts` provide the minimum `getItem` surface Studio needs. Retires when honua-console#4 lands the full Catalog port.
- `src/transitional/conforms-to.ts` mirrors the service-format enum (adds `Honua:Console:v1`).

The HTTP transport `HttpGeneratedAppLifecycleClient` is a thin wrapper around `fetch` rather than the Portal-internal `portalFetch`. It will retire once honua-server#1162 publishes the Console content/lifecycle APIs.

## What is intentionally out of scope

- Catalog browsing, item detail, dependency walker — honua-console#4.
- Saved Maps and Embed surfaces — honua-console#4.
- Server-side publish/version contracts — honua-server#1162 / honua-sdk-js#225.
- Legacy Admin coordination (hiding the duplicate app-builder route from legacy Admin's normal navigation) — honua-console#6.

## Acceptance gates this port satisfies

- AC1 (Studio proof flow runs at `/studio/proof` driven by `OperatorWorkspace` + `@honua/sdk-js/exploration`; all six fixture states selectable). — covered by `src/studio/proof/StudioProofPage.tsx` + `proofFixture.ts` + `routes/StudioProof.tsx`.
- AC2 (model-free smoke/eval harness). — covered by `tests/smoke/app-builder-proof.{spec,config}.ts` + `fixtures/app-builder/operations-dashboard/*`. `liveModelCalls === 0` and `refineApp === 0` are asserted; `PROOF_STEPS` and `FAILURE_FIXTURES` selectors preserved.
- AC3 (`publish`/`reopen` creates Console content item with lifecycle extension `honua-generated-app-lifecycle/v1`). — covered by `src/studio/generated-apps/lifecycle.ts` (`materializeGeneratedAppDraft`, `addGeneratedAppRevision`, `publishGeneratedAppItem`) and the fixture client at `default-client.ts`. `/studio/apps/:itemId/preview` loads stored `AppPackage` + manifest + plan + buildSpec refs without re-invoking generation (`getPreview` consumes stored revisions only).
- AC4 (no duplicate app-builder surface in legacy Admin's normal nav). — Pending honua-console#6 coordination ticket; this Console-side port does not introduce a new duplicate.
- AC5 (Vega-Lite chart adapter for generated dashboards). — covered by `src/studio/charts/ChartSpecView.tsx`. The success fixture flags `runtime.chartSpec: "vega-lite"` and the smoke harness asserts the branch.
- AC6 (smoke evidence under `artifacts/smoke/app-builder-proof/`; consistent error/empty/forbidden surfaces). — `tests/smoke/app-builder-proof.spec.ts` writes to that directory; `EmptyState` / `Forbidden` shell components are used for missing items, unauthorized access, unsupported packages, and missing/unsupported bindings. The manifest schema gains a `runtime.chartSpec` field.

## Performance posture

- All Studio routes are lazy-loaded via `React.lazy` so the Console shell / Home / future Catalog / Operate paths do not pay Studio bundle weight.
- The Studio proof page resolves its catalog source via a single `getItem(itemId)` call; reopen reads `AppPackage` + manifest references from the lifecycle store without re-invoking generation.

## Follow-ups

- Retire `src/transitional/*` when honua-sdk-js#225 lands.
- Wire honua-console#6 (legacy Admin hide) to flip AC4 green.
- Replace the deterministic `vega-lite` fixture spec with real Studio-authored chart specs as later Studio chart tickets ship.
