# Vega runtime security upgrade (#337)

The 2026.1 quality contract requires security fixes for shipped Console code, including
Preview and shelved surfaces. Report/dashboard specs may contain user- or model-authored
Vega-Lite, so leaving the vulnerable runtime in the distributed artifact breaks that promise.
This change clears the advisories by upgrading; it does not accept residual advisory risk.

| Runtime | Previous pin | Patched pin | Compatibility |
| --- | --- | --- | --- |
| Vega | 5.33.0 | 6.4.0 | Above the 6.2.0 fix for the high advisory |
| Vega-Lite | 5.23.0 | 6.4.3 | Published peer dependency: Vega `^6.0.0` |
| Vega-Embed | 6.29.0 | 7.1.0 | Published peer dependencies accept Vega and Vega-Lite 6 |

The two moderate advisories were already cleared in #339:
[GHSA-rcw3-wmx7-cphr](https://github.com/vega/vega/security/advisories/GHSA-rcw3-wmx7-cphr)
and [GHSA-963h-3v39-3pqf](https://github.com/vega/vega/security/advisories/GHSA-963h-3v39-3pqf).
The coordinated upgrade also clears
[GHSA-7f2v-3qq3-vvjf](https://github.com/vega/vega/security/advisories/GHSA-7f2v-3qq3-vvjf).

## Reachability assessment

`chart-preview.js` loads UMD bundles into `window.vega`, `window.vegaLite`, and
`window.vegaEmbed`. Embed results and View instances stay in a module-scoped map;
the Console does not set `VEGA_DEBUG`. The upstream proof uses globally exposed Vega
and a View instance with a user-defined Vega spec. The Console's narrower Vega-Lite
input and SVG renderer differ from that proof, but are insufficient grounds for an
accepted-risk decision: dashboard/report panels can carry attacker-influenced specs,
and the Vega library is globally accessible. The earlier issue assessment therefore
treated those paths as reachable. Query, analysis, and Operate charts use fixed C#
spec shapes. Shelving builders does not remove their runtime from the shipped artifact.

The patched parser rejects expression object properties `toString` and `valueOf`,
closing the coercion mechanism. The regression test uses harmless constant-valued
properties and a valid arithmetic control. Both forbidden properties were accepted
by the trunk Vega 5.33.0 bundle and are rejected by the pinned Vega 6.4.0 bundle.
This is a parser regression check, not a claim to have exploited the Console origin.

## Compatibility and provenance evidence

- `node scripts/vendor-assets.mjs --update` re-downloads the npm tarballs, verifies
  their SHA-512 integrity, and regenerates all assets and the SHA-384 file lock.
  Re-running it on this branch reproduces the committed bytes and lock exactly.
- The Console's spec builders now declare Vega-Lite v6. Reopened v5 declarations
  remain accepted. This does not claim compatibility with every possible external spec.
- `e2e/playwright/specs/chart-runtime.spec.ts` exercises the actual vendored bundles
  through `chart-preview.js` in Chromium for both v5 and v6 declarations. Dashboard/
  report bars assert values 12 and 30 and their height ratio. Query/analysis fixtures
  contain two east features and one west feature, assert counts 2 and 1, and exercise
  proxy-row injection and automatic dimension selection.
- Operate fixtures cover latency percentiles, percentage error rate, GP queue depth,
  and alert backlog. They assert each plotted series/value, finite point coordinates,
  and the dashed reconnect rule halfway between two timestamps. Existing C# tests
  cover spec generation; these fixtures verify the resulting chart shapes in the runtime.
- The existing no-external-requests browser test verifies the executed runtime versions
  and same-origin loading. Existing missing-data behavior remains covered separately.

Run the browser evidence with `npm --prefix e2e/playwright run e2e:smoke --
specs/chart-runtime.spec.ts specs/no-external-requests.spec.ts`, alongside `npm test`,
`./scripts/fast-local-check.sh`, the chart-related integration tests, and the formatting check.
No acceptance criterion needs an exact release candidate or is released from #337.

Local verification on the resumed branch (2026-09-05): all 18 Chromium runtime
fixtures passed against the committed assets on a temporary same-origin static host.
Serving the trunk Vega 5.33.0 bundle to the unchanged parser test instead failed at
the `toString` rejection assertion (`accepted`), as intended. The native-core suite
passed 1,382 tests; its three opt-in live-server tests were skipped by their existing
environment guards and are not counted as evidence for this upgrade.
The web-host build also passed with zero warnings and errors, completing
`./scripts/fast-local-check.sh` on the lane's capped .NET SDK.
