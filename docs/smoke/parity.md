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
# Run against the local single deployable artifact (dist/version.json).
npm run smoke:parity

# Run against a deployed origin (the staging or preview URL devops promotes).
npm run smoke:parity -- --origin https://console.staging.honua.example

# Run the smoke's own unit tests (taxonomy, contract registry, scenario,
# evidence emitter). Used by CI to gate the harness itself.
npm run smoke:parity:test
```

Exit codes:

- `0` — scenario completed (`result: ok`).
- `1` — one step failed (`result: failed`); see the triage line on stdout
  and the JSON evidence file written under `smoke-evidence/` for details.
- `2` — runner setup error (e.g., bad CLI arguments).

The runner reads `dist/version.json` (produced by
[`honua-console#8`](https://github.com/honua-io/honua-console/issues/8)
and consumed by the devops promotion pipeline). When that file is absent
— for example, on trunk before the scaffolding lands, or on a checkout
that has not run `npm run build` — the runner falls back to the
committed fixture under [`smoke/parity/fixtures/dist-version.json`](../../smoke/parity/fixtures/dist-version.json).
The evidence JSON records `buildArtifact.source` as `"dist"` or
`"fixture"` so a CI reviewer can tell the two apart.

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
| 1     | `devops/build-artifact`          | `devops`       | `dist/version.json` declares the four areas and the legacy block.                               |
| 2     | `legacy-admin/operator-publish`  | `legacy-admin` | Publish-handoff event from the transitional admin surface.                                      |
| 3     | `server/catalog-upsert`          | `server`       | Server upserts the event and returns a stable content item id.                                  |
| 4     | `sdk/catalog-projection`         | `sdk`          | SDK projects the row into a browser-safe summary the viewer accepts.                            |
| 5     | `console/catalog-list`           | `console`      | Catalog browse lists the new item.                                                              |
| 6     | `console/viewer-open`            | `console`      | Same-origin `/maps/new?from=<id>` hydration URL is built.                                       |
| 7     | `console/saved-map-save`         | `console`      | Saved map references the published service via `webmap-doc/v1`.                                 |
| 8     | `console/studio-draft`           | `console`      | Studio draft route is same-origin with the artifact.                                            |
| 9     | `sdk/app-package-build`          | `sdk`          | SDK builds the BuilderPlan and AppPackage from the draft.                                       |
| 10    | `server/generated-app-publish`   | `server`       | Server records the generated app as a content item with provenance back to the source service.  |
| 11    | `console/share-publish`          | `console`      | Share dialog promotes the generated app to org-tier and marks it embeddable.                    |
| 12    | `server/embed-token-mint`        | `server`       | Server mints a same-origin embed token descriptor.                                              |
| 13    | `console/embed-render`           | `console`      | Console assembles the same-origin embed URL using the minted token.                             |

## Contract versions captured in evidence

| Contract                    | Version  | Owning layer    | Source repo (today)            |
| --------------------------- | -------- | --------------- | ------------------------------ |
| `build-artifact`            | `v1`     | `devops`        | `honua-console`                |
| `publish-handoff`           | `v1`     | `legacy-admin`  | `honua-portal` (transitional)  |
| `content-item`              | `v1.1.0` | `server`        | `honua-portal` (transitional)  |
| `webmap-doc`                | `v1`     | `console`       | `honua-portal` (transitional)  |
| `share-access`              | `v1`     | `server`        | `honua-portal` (transitional)  |
| `embed-token`               | `v1`     | `server`        | `honua-portal` (transitional)  |
| `generated-app-lifecycle`   | `v1`     | `sdk`           | `honua-portal` (transitional)  |

The registry lives in
[`smoke/parity/contracts.mjs`](../../smoke/parity/contracts.mjs). When a
contract version changes in its source repo, bump the `version` field
there in the same PR so the smoke evidence stays truthful.

## Evidence format

The runner writes a JSON evidence file (default
`smoke-evidence/console-parity.json`; override with `--output`). A
committed sample lives at
[`smoke/parity/sample-evidence/console-parity.json`](../../smoke/parity/sample-evidence/console-parity.json).

Top-level fields:

- `scenario` — Stable scenario id (`console-parity-publish-to-embed`).
- `ranAt` — ISO-8601 timestamp.
- `originUrl` — The origin the smoke ran against.
- `buildArtifact` — `dist/version.json` snapshot (or `null` if neither
  `dist/version.json` nor the fixture loaded). Includes `source:
  "dist"|"fixture"` so promotion tooling can distinguish a real build
  verification from a placeholder run.
- `contractVersions[]` — All contracts exercised by the scenario, with
  owning layer and source repo.
- `items` — Stable ids generated during the run
  (`serviceItemId`, `savedMapId`, `generatedAppId`, `shareTier`,
  `embedToken`).
- `urls` — Same-origin URLs for catalog, viewer hydration, viewer, Studio
  draft, generated app detail, share, and embed surfaces.
- `steps[]` — Per-step status (`ok` / `failed` / `skipped`),
  `owningLayer`, `owningLayerLabel`, `durationMs`, and the step's
  declared evidence payload.
- `result` — `ok` or `failed`.
- `failure` — When `result === "failed"`, the step id, owning layer,
  owning repo, and the error message. Used as the CI triage line.

## Wiring to the real surfaces

The smoke ships with fixture adapters today so the scenario can run
green before its dependencies merge. When the porting tickets land,
replace the in-memory adapter implementations with their real
counterparts — the scenario steps and the evidence format stay the
same.

| Adapter                                          | Replace with                                                                 |
| ------------------------------------------------ | ---------------------------------------------------------------------------- |
| [`adapters/devops.mjs`](../../smoke/parity/adapters/devops.mjs)   | `fetch('/version.json')` against the deployed origin (devops `#55` / `#56`). |
| [`adapters/admin.mjs`](../../smoke/parity/adapters/admin.mjs)     | Real publish trigger against `honua-server-admin` (or Console Operate when `#6` ports it). |
| [`adapters/server.mjs`](../../smoke/parity/adapters/server.mjs)   | HTTP calls to `honua-server` (Console metadata v2 baseline, `#1162`).        |
| [`adapters/sdk.mjs`](../../smoke/parity/adapters/sdk.mjs)         | Imports from `@honua/sdk-js` (`honua-sdk-js#225` / `#226`).                  |
| [`adapters/console.mjs`](../../smoke/parity/adapters/console.mjs) | Imports from the Console route map module (`honua-console#3` / `#4` / `#5`). |

Each adapter is self-contained and stateless across calls so the swap
can happen one layer at a time without changing the scenario.

## CI hook-up

`honua-devops` owns the CI pipeline (see
[`honua-devops#56`](https://github.com/honua-io/honua-devops/issues/56)).
The Console parity smoke is the gate the release-promotion job should
run after `npm run build` and before promotion:

```yaml
# Sketch — owned by honua-devops, included here so a reviewer can see
# how Console expects the parity smoke to land in CI.
- name: Build Console artifact
  run: npm run build

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
