# Same-origin scene and chart assets (#334)

Release promise: the shipped privileged Console origin must retain secure
packaging and function without downloading executable dependencies at page load.
Experimental 3D remains subject to that security obligation under the 2026.1
quality contract. This change completes the existing must-fix-before-cut item.

Cesium 1.119.0 is packaged at build/publish time from its SHA-256-pinned npm
archive. The extracted-tree lock covers 377 files, including workers, assets,
widgets and the upstream Apache-2.0 license. `scripts/vendored-assets.lock.json`
records the archive and tree identities alongside the committed Vega packages.
`node scripts/vendor-assets.mjs --update` refreshes both locks and the assets.
The browser loads these assets from the Console origin; the CSP has no jsdelivr
allowance. Scene tiles use the authenticated same-origin BFF, with no Ion basemap.

The browser regression in `e2e/playwright/specs/no-external-requests.spec.ts`
mounts both real rendering runtimes. The chart uses categories a/b and amounts
28/55 and asserts those exact rendered mark labels. The scene uses a hand-built
3D Tiles point cloud containing three RGB points, expressed in metres relative
to ECEF (6378137, 0, 0). It must fetch the binary tile and load all three points;
an empty tileset or a schematic cannot satisfy the assertion. Both tests record
all page requests and require zero off-origin requests.

## Local evidence

- The documented asset update command completed and reproduced Cesium's tree
  SHA-256 `c12dc6c18055a163876be210107bdb1f4e449e07340f81960b82bb9074c50259`.
- `npm test` passed all 13 test files.
- Removing `packages.cesium` from the common lock made the vendored-assets test
  fail (exit 1); the original lock was restored. This challenges the new pin
  requirement with the previous implementation's missing entry.
- Chromium preflight against the source static assets passed both populated
  scene/chart tests (2/2), including the three-point tile, exact chart values,
  and zero off-origin requests. The PR records published-artifact validation.
- Removing the point-cloud content from the browser fixture made the scene test
  fail with `pointsLength: 0` versus the required `3` (exit 1), even though Cesium
  mounted successfully and created its canvas. The populated fixture was restored.
- The fetch hook runs in the Shell before Razor's static-asset inventory and
  explicitly adds files created after project evaluation, covering first builds.
  The Playwright CI job moves the prefetched tree aside before its first publish;
  published-tree verification and real rendering then enforce this build path.

No acceptance criterion requires an unavailable release candidate. The local
published artifact proves packaging and runtime behavior; exact-candidate
qualification remains a separate release receipt.
