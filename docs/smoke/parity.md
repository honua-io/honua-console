# Console Parity Smoke (`honua-console#9`)

Decision source: [ADR-0001: Unified Honua Console Runtime](../adr/0001-unified-honua-console-runtime.md).

The Console parity smoke is one automated command that exercises the
cross-surface chain Honua Console must own end-to-end before
[`honua-portal`](https://github.com/honua-io/honua-portal) can be frozen:

> publish → catalog → Studio → share/embed

It is the acceptance gate called out in the migration backlog
([HONUA_CONSOLE_MIGRATION_BACKLOG.md](../roadmap/HONUA_CONSOLE_MIGRATION_BACKLOG.md))
under "Pass the cross-surface smoke."

## Running the smoke

```sh
# Run against the local single deployable artifact metadata.
npm run smoke:parity

# Run against a deployed origin (the staging or preview URL devops promotes).
npm run smoke:parity -- --origin https://console.staging.honua.example

# Run the smoke's own unit tests (taxonomy, contract registry, scenario,
# evidence emitter). Used by CI to gate the harness itself.
npm run smoke:parity:test

# Run the focused Studio workflow smoke added for honua-console#40.
npm run smoke:workflow
```

Runner options:

- `--origin <url>` (or `-o <url>`) — origin to verify. Non-loopback
  origins must serve `<origin>/version.json`; loopback origins
  (`127.0.0.1`, `localhost`, `[::1]`, or `0.0.0.0`) use the local/offline
  artifact path. The runner normalizes this value with `new URL(url).origin`,
  so a trailing slash, path, query, or fragment is stripped before fetching
  `version.json` or assembling evidence URLs.
- `--output <path>` — write evidence somewhere other than
  `smoke-evidence/console-parity.json`.
- `--quiet` — write evidence without printing the text summary.
- `--help` (or `-h`) — print the runner usage line.

Exit codes:

- `0` — scenario completed (`result: ok`).
- `1` — one step failed (`result: failed`); see the triage line on stdout
  and the JSON evidence file written under `smoke-evidence/` for details.
- `2` — runner setup error (e.g., bad CLI arguments or an invalid
  `--origin` URL).

For deployed origins, the runner fetches and validates
`<origin>/version.json` and fails the `devops/build-artifact` step if the
origin is unreachable or serves invalid metadata. The evidence stores the
normalized origin, and every Console URL in `urls` is assembled from that
same normalized origin so trailing-slash inputs cannot produce doubled
route slashes. Local/offline runs (default `127.0.0.1`, `localhost`,
`[::1]`, `0.0.0.0`, or no origin) read
`artifacts/honua-console-web/version.json` when a local publish artifact has
been stamped by `scripts/write-build-metadata.mjs`. When that local file is
absent - for example, on a checkout that has not run `dotnet publish` -
the runner falls back to the committed fixture under
[`smoke/parity/fixtures/version.json`](../../smoke/parity/fixtures/version.json).
The evidence JSON records `buildArtifact.source` as `"origin"`, `"artifact"`,
or `"fixture"` so promotion tooling can distinguish deployed-origin
evidence from a local placeholder run. Release-promotion jobs should gate
on `"origin"` evidence once the preview pipeline is wired; `"fixture"` is
only harness/protocol evidence.

## Owning-layer taxonomy

Every scenario step declares the layer that owns the contract it
exercises. A failure prints a triage line that names the layer and its
owning repo so the smoke does not waste an oncall round-trip:

| Owning layer    | Repo                  | Owns                                                                                  |
| --------------- | --------------------- | ------------------------------------------------------------------------------------- |
| `devops`        | `honua-devops`        | Single deployable artifact, version.json, same-origin SPA fallback, proxy routing.    |
| `server`        | `honua-server`        | Content/metadata APIs, publish-handoff upsert, share/access, embed-token minting.     |
| `sdk`           | `honua-sdk-js`        | Browser-safe projections (ContentItemSummary, BuilderPlan, AppPackage, lifecycle).    |
| `console`       | `honua-console`       | Console UI surfaces, route map, same-origin invariants, saved-map and Studio drafts.  |
| `legacy-admin`  | `honua-server-admin`  | Transitional operator publish path until Console Operate ports fully replace it.      |

The taxonomy is closed: a typo in a scenario step or adapter throws at
load time so the smoke surface cannot silently widen
(see [`smoke/parity/owning-layers.mjs`](../../smoke/parity/owning-layers.mjs)).

## Scenario steps

The chain is sequenced so each step depends on the previous one. A
failure short-circuits and the remaining steps are recorded as
`skipped` (not silently dropped) in evidence.

| Order | Step id                          | Layer          | What it verifies                                                                                |
| ----- | -------------------------------- | -------------- | ----------------------------------------------------------------------------------------------- |
| 1     | `devops/build-artifact`          | `devops`       | The deployed or local `version.json` declares the four areas and the legacy block.              |
| 2     | `legacy-admin/operator-publish`  | `legacy-admin` | Publish-handoff event from the transitional admin surface.                                      |
| 3     | `server/catalog-upsert`          | `server`       | Server upserts the event and returns a stable content item id.                                  |
| 4     | `sdk/catalog-projection`         | `sdk`          | SDK projects the row into a browser-safe summary the viewer accepts.                            |
| 5     | `console/catalog-list`           | `console`      | Catalog browse lists the new item.                                                              |
| 6     | `console/viewer-open`            | `console`      | Same-origin `/maps/new?from=<id>` hydration URL is built.                                       |
| 7     | `console/saved-map-save`         | `console`      | Saved map references the published service via `webmap-doc/v1`.                                 |
| 8     | `console/studio-draft`           | `console`      | Studio accepts the source-scoped draft route, records prompt clarification, and exposes a route-compatible package snapshot. |
| 9     | `sdk/app-package-build`          | `sdk`          | SDK builds the BuilderPlan and AppPackage from the draft.                                       |
| 10    | `server/generated-app-publish`   | `server`       | Server records the generated app as a content item with provenance back to the source saved map or catalog item. |
| 11    | `console/share-publish`          | `console`      | Share dialog promotes the saved map for embed and the generated app for catalog publication.    |
| 12    | `server/embed-token-mint`        | `server`       | Server mints a same-origin embed token descriptor.                                              |
| 13    | `console/embed-render`           | `console`      | Console assembles the same-origin embed URL using the minted token.                             |

## Contract versions captured in evidence

| Contract                    | Version  | Owning layer    | Source repo (today)            |
| --------------------------- | -------- | --------------- | ------------------------------ |
| `build-artifact`            | `v1`     | `devops`        | `honua-console`                |
| `publish-handoff`           | `v1`     | `legacy-admin`  | `honua-portal` (transitional)  |
| `content-item`              | `v1.1.0` | `server`        | `honua-portal` (transitional)  |
| `webmap-doc`                | `v1`     | `console`       | `honua-portal` (transitional)  |
| `studio-authoring-shell`    | `v1`     | `console`       | `honua-console`                |
| `share-access`              | `v1`     | `server`        | `honua-portal` (transitional)  |
| `embed-token`               | `v1`     | `server`        | `honua-portal` (transitional)  |
| `generated-app-lifecycle`   | `v1`     | `sdk`           | `honua-portal` (transitional)  |

The registry lives in
[`smoke/parity/contracts.mjs`](../../smoke/parity/contracts.mjs). When a
contract version changes in its source repo, bump the `version` field
there in the same PR so the smoke evidence stays truthful.

## Studio Workflow Smoke (`honua-console#40`)

The workflow editor has a focused smoke alongside the broader parity
chain. It exercises the route and contract path introduced for
`workflow.package` authoring:

> Studio workflow draft -> dry-run -> version save -> publish -> Operate monitor

Run it with:

```sh
npm run smoke:workflow
npm run smoke:workflow -- --origin https://console.staging.honua.example
```

By default it writes `smoke-evidence/studio-workflow.json`. The same
`--origin`, `--output`, `--quiet`, and `--help` options are supported.

Workflow smoke steps:

| Order | Step id                         | Layer     | What it verifies |
| ----- | ------------------------------- | --------- | ---------------- |
| 1     | `devops/build-artifact`         | `devops`  | The same Console artifact is serving the Studio workflow route. |
| 2     | `console/studio-workflow-draft` | `console` | The editor can materialize a `workflow.package` draft with sources, transforms, sinks, parameters, schedule, worker profile, retry policy, failure edges, output schemas, and publication intent. |
| 3     | `server/workflow-dry-run`       | `server`  | The server-owned dry-run response includes sample rows, logs, artifacts, and output schemas. |
| 4     | `server/workflow-version-save`  | `server`  | The package is saved as a versioned workflow content item. |
| 5     | `server/workflow-publish`       | `server`  | The publication uses a saved content version, queues a job-runner job, and exposes an invocation endpoint when parameter validation passes. |
| 6     | `console/operate-job-monitor`   | `console` | Dry-run and publish jobs deep-link to same-origin Operate job and event evidence. |

The workflow smoke adds these contract names to evidence:

| Contract                 | Version | Owning layer | Source repo |
| ------------------------ | ------- | ------------ | ----------- |
| `workflow-package`       | `v1`    | `server`     | `honua-server` |
| `workflow-dry-run`       | `v1`    | `server`     | `honua-server` |
| `workflow-publication`   | `v1`    | `server`     | `honua-server` |

Response-contract notes worth keeping in sync with the registry:

- `build-artifact/v1` requires `name`, `version`, `commit`,
  `shortCommit`, `ref`, `builtAt`, `legacy.portal`, `legacy.admin`, and
  an `areas[]` list containing `studio`, `catalog`, `share`, and
  `operate`.
- `studio-authoring-shell/v1` evidence records the Console-owned package
  shell projection before SDK app package construction: ambiguous prompt
  clarification, the route-compatible inspectable package snapshot,
  inspector section names, and lifecycle labels for Draft, Preview,
  Saved version, and Published.
- `share-access/v1` patch responses contain `sharing`, `embeddable`,
  `groupIds` for group-tier shares, and `publicLinkToken` for
  public-link shares. They do not echo `openData`; that field is owned by
  `content-item.access`.
- `embed-token/v1` owns the transitive dependency closure descriptor and
  the Console embed URL must carry the minted bearer in
  `#embedToken=<token>`, not in the query string. Smoke evidence and log
  summaries store only the token hash/redacted fragment.
- Console route compatibility pins public-link browser reads separately
  from embed reads: `/catalog/:id?token=<token>` and
  `/maps/:mapId?token=<token>` use the query token emitted by Portal
  share links. Tokenless public reads use `/catalog/:id` and
  `/maps/:mapId`; public embeddable maps may also render at
  `/embed/maps/:mapId` without a token. Token-authorized embeds use
  `/embed/maps/:mapId#embedToken=<token>` so the iframe bearer stays in
  the fragment and query-string bearer tokens are rejected. Embed controls
  preserve Portal snippet spellings such as `chrome=none`, `legend=off`,
  and `zoom=off`; invalid WGS84 extents fall back to the saved map extent.
- `workflow-package/v1` covers the authored draft graph and versioned
  package snapshot. Smoke evidence must include source/transform/sink
  coverage, failure edges, parameters, schedule, worker profile, retry
  policy, output schemas, and publication intent.
- `workflow-dry-run/v1` and `workflow-publication/v1` responses must
  carry Operate evidence URLs so a builder can move from Studio into job
  and event evidence without leaving the same Console origin.

The `Version` column is the exact string the registry emits into
evidence. Some contracts intentionally report only the major family
(for example, `publish-handoff` reports `v1`) while the "Wire shapes"
section below documents the more precise schema revision the smoke
materializes (for example, `publish-handoff/v1.1.0`). Readers triaging
an evidence file should treat the table as the evidence contract and the
wire-shape entry as the precise payload revision asserted by the smoke.

### Wire shapes the smoke actually exercises

The smoke does not just report contract versions — it materializes the
canonical wire shapes for each step so a future port of an adapter to a
real HTTP transport cannot silently accept a drifted payload:

- **`workflow-package/v1`** — The workflow smoke materializes a draft
  with `packageType="workflow.package"` and
  `schemaVersion="workflow.package/v1"`. The draft includes graph nodes
  with `source`, `transform`, and `sink` categories; success and failure
  edges; invocation parameters using the allowed
  `string|date|number|boolean|geometry` parameter family; a cron schedule;
  a geospatial worker profile; retry/failure routing; publication intent;
  and named output schemas. Failure edges and output schemas are required
  before publish evidence can pass.
- **`workflow-dry-run/v1`** — The dry-run response asserted by the smoke
  contains `jobId`, `kind="workflow_dry_run"`, `status`, `sampleRows`,
  logs, artifacts, and output schema names. Console must surface these
  through `/operate/jobs/{jobId}` and `/operate/events?jobId={jobId}`.
- **`workflow-publication/v1`** — Publication evidence contains
  `publicationId`, `contentItemId`, `versionId`, `mode`, `status`,
  `jobId`, optional same-origin invocation endpoint, and per-parameter
  validation. For `process-endpoint` mode, the invocation endpoint ends
  in `/invoke` only when parameter validation passes; invalid endpoint
  parameter contracts block publication instead of queuing a job.
- **`publish-handoff/v1.1.0`** — The fixture at
  [`smoke/parity/fixtures/publish-handoff.json`](../../smoke/parity/fixtures/publish-handoff.json)
  matches every top-level required field in
  [`publish-handoff-v1.json`](https://github.com/honua-io/honua-portal/blob/main/schemas/publish-handoff-v1.json):
  `type, title, summary, owner, extent, nativeCrs, license, attribution,
  source, target, endpoints, preview, capabilities, dependencies, access`.
  `source.kind` is restricted to the enum (`import|publish|admin-job|external`),
  `target.type` is asserted to match the item `type`, and every populated
  ServiceLink carries the v1.1 keys
  (`accessURL/format/mediaType/describedBy/describedByType/conformsTo`).
  The `access` object is validated against the canonical
  `content-item/v1.1.0` `Access` schema: `sharing` must be one of
  `private|org|group|public-link|public`, `embeddable` and `openData`
  must be booleans, and the `openData=true ⇒ sharing="public"`
  conditional is enforced (negative cases covered in
  `__tests__/contract-shapes.test.mjs`).
- **`content-item/v1.1.0`** — `server.publishService` produces a full
  `ContentItem`. Upsert identity keys on **`(source.kind, source.sourceId)`**
  per the schema's idempotency description, so the same `sourceId`
  republished under a different `source.kind` mints a distinct item
  (regression test pins this). Generated item, saved-map, revision, and
  dependency ids use the canonical Crockford ULID alphabet and match
  `/^[0-9A-HJKMNP-TV-Z]{26}$/`. The SDK projection
  (`sdk.summarizeContentItem`) emits the canonical `ContentItemSummary`
  (`id, slug, type, title, summary, owner, tags, extent, preview,
  modified, capabilities, formats, sharing, openData, viewerSupport`).
  `formats` is derived from the non-`self` endpoint slots so catalog
  cards render format pills without a per-item detail fetch.
  `viewerSupport` is the projection of `extensions["honua-portal-viewer"]`
  and is **`null` when the publisher has not asserted an override** (the
  canonical contract — the previous type-default fallback inside
  `summarize()` was a contract drift and has been moved to
  `resolveViewerOpenability` on the viewer layer).
- **`generated-app-lifecycle/v1`** — Published generated-app items carry
  `target = { type: "app", url, framework: "honua" }` and a
  `saved-map` (or `catalog-item`) dependency back to the source the
  generator was run against. `source.kind` is `manual` and the active
  revision records source provenance with the matching role. The
  upstream SDK projections are structurally faithful to
  `@honua/sdk-js`: `BuilderPlan` carries `{ id, intentId, kind:"builder",
  steps[] }` with each `PlanStep` typed `{ id, kind, label, inputs?,
  outputs? }`; `AppPackage` carries `{ id, version, assets[] }` plus a
  `manifestArtifact` (and its `manifest_artifact` snake_case alias) with
  the `honua_generated_app_manifest.v1` format and an
  `operations-dashboard.v1` profile layout whose widget kinds belong to
  the `HonuaGeneratedAppWidgetKind` union.
- **`studio-authoring-shell/v1`** — The Console-owned Studio step records
  the first package-shell proof path before the SDK app package is built:
  an ambiguous prompt routes to structured clarification, the draft
  remains inspectable as an `app.package`, the inspector exposes
  assumptions, data bindings, warnings, validation, and provenance, and
  the lifecycle evidence names the distinct Draft, Preview, Saved version,
  and Published states. The current route is same-origin compatibility
  evidence for `/studio?source=map&itemId=<id>`; source hydration and
  publication persistence remain server/SDK follow-up work, so the package
  snapshot records `sourceHydrated: false` and uses the generic mock
  binding rather than the saved-map id. This is a stable mock projection
  until the server package lifecycle API and SDK package helpers are wired
  into Console.
- **`share-access/v1`** — `patchAccess` returns
  `{ sharing, embeddable, groupIds?, publicLinkToken? }` only; `groupIds`
  is emitted for `sharing="group"` and `publicLinkToken` is emitted for
  `sharing="public-link"`. `openData` lives on `content-item.access` and
  is not echoed in the share-access response.
  The patch validates `sharing` against the canonical
  `private|org|group|public-link|public` enum (returns `kind:"invalid-tier"`
  on a non-enum string), preserves `content-item.access.openData` (the
  invariant `openData=true ⇒ sharing="public"` runs one way: public sharing
  does NOT auto-enable openData), and refuses to narrow an open-data item
  below `sharing="public"` (returns `kind:"open-data-locked"`).
  Widening also evaluates the transitive dependency closure and returns
  `kind:"closureBlocked"` when any resolved dependency is narrower than
  the proposed tier, when a dependency is missing, or when traversal is
  truncated. The generated-app smoke path therefore promotes the source
  service and saved map to `org`, marks the saved map embeddable for
  `/embed/maps/:mapId`, then promotes the generated app to `org`.
- **`content-item/v1.1.0` re-publish ownership** — Server `publishService`
  upserts on `(source.kind, source.sourceId)` and, on re-publish, preserves
  the portal-owned fields `access`, `preview`, `dependencies`, `extensions`,
  `endpoints.self`, and `timestamps.created`. Handoff updates flow through
  for `title`, `summary`, `description`, `tags`, `owner`, `extent`,
  `license`, `attribution`, `target`, the non-self `endpoints[*]`, and
  `capabilities`. This matches the canonical portal mapping: legacy admin
  cannot clobber Console-managed access state or edited previews on a
  metadata refresh.
- **`embed-token/v1`** — The Console embed URL carries the bearer as
  `#embedToken=<token>` in the URL fragment so it never reaches access
  logs. A query-string token is rejected by
  `assertEmbedTokenInFragment` and attributed to the `console` layer. The
  runner keeps the raw token only in memory while assembling the route;
  JSON evidence and text summaries write `embedTokenHash` plus a redacted
  embed URL fragment. Minting snapshots the transitive dependency closure
  at token time; for the saved-map embed path the closure evidence includes
  the underlying service item.
- **`webmap-doc/v1`** — `saveMap` produces a document with
  `version: "honua-webmap/v1"` plus `operationalLayers[]`, `baseMap`,
  and `initialState.viewpoint.extent`. The adapter also records the saved
  map as a shareable dependency-graph node whose closure points back to
  the source service item.

Contract-shape parity is enforced by
[`smoke/parity/__tests__/contract-shapes.test.mjs`](../../smoke/parity/__tests__/contract-shapes.test.mjs).

## Evidence format

The runner writes a JSON evidence file (default
`smoke-evidence/console-parity.json`; override with `--output`). A
committed sample lives at
[`smoke/parity/sample-evidence/console-parity.json`](../../smoke/parity/sample-evidence/console-parity.json).

Top-level fields:

- `scenario` — Stable scenario id (`console-parity-publish-to-embed`).
- `ranAt` — ISO-8601 timestamp.
- `originUrl` — The normalized origin the smoke ran against
  (`new URL(<origin>).origin`).
- `repoRoot` — Local repository root used by the runner. The committed
  sample sanitizes this path.
- `buildArtifact` — Deployed `<origin>/version.json`, local published
  artifact metadata, or local fixture snapshot. Includes `source:
  "origin"|"artifact"|"fixture"` so promotion tooling can distinguish a
  deployed-origin verification from a placeholder run.
- `contractVersions[]` — All contracts exercised by the scenario, with
  owning layer and source repo.
- `items` — Stable ids generated during the run
  (`serviceItemId`, `savedMapId`, `generatedAppId`, `shareTier`,
  `embedTokenHash`). The raw embed bearer is never written to evidence or
  log output.
- `urls` — Same-origin URLs for catalog, viewer hydration, viewer, Studio
  draft, generated app detail, share, and embed surfaces, assembled from
  the normalized origin.
- `steps[]` — Per-step status (`ok` / `failed` / `skipped`),
  `owningLayer`, `owningLayerLabel`, `description`, `durationMs`, the
  step's declared evidence payload, and `error` (`null` unless that step
  failed).
- `result` — `ok` or `failed`.
- `failure` — When `result === "failed"`, the step id, owning layer,
  owning repo, and the error message. Used as the CI triage line.

The current harness emits JSON evidence only. Browser screenshots or
trace attachments should be added when the fixture/in-memory adapters are
replaced by real browser and HTTP transports.

## Wiring to the real surfaces

The smoke ships with fixture/in-memory adapters today so the scenario can
run green before all dependencies merge. When the porting tickets land,
replace the remaining in-memory adapter implementations with their real
counterparts — the scenario steps and the evidence format stay the same.

| Adapter                                          | Replace with                                                                 |
| ------------------------------------------------ | ---------------------------------------------------------------------------- |
| [`adapters/devops.mjs`](../../smoke/parity/adapters/devops.mjs)   | Already fetches `<origin>/version.json` for deployed origins; devops `#55` / `#56` should provide the real file and CI origin. |
| [`adapters/admin.mjs`](../../smoke/parity/adapters/admin.mjs)     | Real publish trigger against `honua-server-admin` (or Console Operate when `#6` ports it). |
| [`adapters/server.mjs`](../../smoke/parity/adapters/server.mjs)   | HTTP calls to `honua-server` (Console metadata v2 baseline, `#1162`).        |
| [`adapters/sdk.mjs`](../../smoke/parity/adapters/sdk.mjs)         | Imports from `@honua/sdk-js` (`honua-sdk-js#225` / `#226`). `resolveViewerOpenability` (the type-default openability gate the SDK summary intentionally does not project) is a transitional home here; it moves into the Console viewer module under `honua-console#4` so the canonical `ContentItemSummary.viewerSupport: null` contract is preserved. |
| [`adapters/console.mjs`](../../smoke/parity/adapters/console.mjs) | Imports from the Console route map module (`honua-console#3` / `#4` / `#5`). |

Each adapter is self-contained and stateless across calls so the swap
can happen one layer at a time without changing the scenario.

## CI hook-up

`honua-devops` owns the CI pipeline (see
[`honua-devops#56`](https://github.com/honua-io/honua-devops/issues/56)).
The Console parity smoke is the gate the release-promotion job should
run after the Blazor artifact is published and previewed, before promotion:

```yaml
# Sketch — owned by honua-devops, included here so a reviewer can see
# how Console expects the parity smoke to land in CI.
- name: Build Console artifact
  run: |
    dotnet publish src/Honua.Console.Web/Honua.Console.Web.csproj -c Release -o artifacts/honua-console-web
    HONUA_CONSOLE_ARTIFACT_DIR=artifacts/honua-console-web node scripts/write-build-metadata.mjs

- name: Console parity smoke
  run: npm run smoke:parity -- --origin "$PREVIEW_ORIGIN"

- name: Upload parity evidence
  if: always()
  uses: actions/upload-artifact@v4
  with:
    name: console-parity-evidence
    path: smoke-evidence/console-parity.json
```

The evidence file is the artifact attached to release notes alongside
the Console version and legacy `portal` / `admin` deployment status from
`version.json`.
