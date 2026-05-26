# Console #9 real-server gate (`honua-console#59`)

Decision source: [Console Patterns Charter §11](../migration/CONSOLE_PATTERNS_CHARTER.md) (real-server integration, no standing mocks).

The [parity smoke](./parity.md) runs the cross-surface chain (publish → catalog
→ Studio → share/embed) against **in-memory contract-shape adapters**. That run
proves wire shapes and choreography cheaply, but it does not prove Console works
against the real system. Per Charter §11 and `honua-console#59`, the
[`honua-console#9`](https://github.com/honua-io/honua-console/issues/9) gate is
satisfied **only** by a run against a real `honua-server`.

This document is the contract for that real-server run and the gate that scores
it. The gate checker exists and is wired today; the real-server **runner** is
blocked upstream (see [Status](#status)).

## What the gate checks

[`smoke/parity/check-gate.mjs`](../../smoke/parity/check-gate.mjs) reads a parity
evidence file and classifies it:

- **satisfied** — `result: "ok"`, `sourceHydrated: true`, and a `server` block
  carrying `{ image, commit, seedProfile }`. Exit 0.
- **pending** — mock-only evidence (`sourceHydrated: false`, `server: null`).
  Never satisfies the gate. With `--pending-ok` it prints a `::warning::` and
  exits 0 (CI stays green while the gate is honestly unmet); without the flag it
  exits 1.
- **failed** — evidence that *claims* a real-server run but is incomplete
  (missing provenance) or red (`result !== "ok"`). Exit 1, regardless of
  `--pending-ok` — a forged/partial real-server claim is a hard failure.

```sh
npm run smoke:gate -- smoke-evidence/console-parity.json              # strict
npm run smoke:gate -- --pending-ok smoke-evidence/console-parity.json # CI today
```

CI runs the second form. When the real-server runner lands and writes
`sourceHydrated: true` evidence, drop `--pending-ok` so the gate becomes
blocking.

## Evidence the real-server run must emit

The runner reuses the existing [evidence format](./parity.md#evidence-format)
and sets the two fields the gate keys on:

```jsonc
{
  "scenario": "console-parity-publish-to-embed",
  "sourceHydrated": true,
  "server": {
    "image": "ghcr.io/honua-io/honua-server@sha256:…", // booted image
    "commit": "<honua-server short commit>",
    "seedProfile": "console-e2e"                         // applied seed fixture
  },
  "result": "ok"
}
```

`sourceHydrated`/`server` are threaded through `runParitySmoke({ sourceHydrated,
serverProvenance })` ([`run.mjs`](../../smoke/parity/run.mjs)) and emitted by
[`evidence.mjs`](../../smoke/parity/evidence.mjs). The in-memory path leaves them
`false`/`null`.

## Boot + seed contract (target design)

Modeled on `honua-server`'s own integration harness
(`tests/dotnet/Honua.TestKit/PostgresFixture.cs` +
`WebAppFixture.cs`) and the `sdk-integration-testing-against-honua-server` work,
so Console does not invent a parallel bootstrap:

1. **Boot** a real `honua-server` + PostgreSQL/PostGIS via Testcontainers (the
   published `honua-server` container image from `deploy-platform-images` /
   `nightly-container-build`) or docker compose. **Skip gracefully when Docker
   is unavailable.**
2. **Seed** a known fixture sufficient for the full chain — a publishable
   service, plus the org/group identities used by share tiers and the
   open-data publication.
3. **Authenticate** with the server's dev-auth bypass
   (`HONUA_DEV_AUTH=true` + `HONUA_DEV_AUTH_ALLOW_BYPASS=true`) or a seeded API
   key.
4. **Drive** the chain over HTTP through `honua-sdk-dotnet` (or a thin
   `HttpClient` behind `Honua.Console.Contracts` until the SDK projection lands),
   asserting each surface renders from live data, including open-data
   publication and unauthenticated embed rendering.
5. **Emit** evidence with `sourceHydrated: true` and the `server` provenance
   block above.

### Chain steps → real endpoints

| Chain step | Real surface it must call |
| --- | --- |
| publish (operator) | publish-handoff / service publish ingest |
| catalog item + list | content-item read/list (Console metadata v2) |
| viewer + saved map | saved-map / webmap document read+write |
| Studio artifact | generated-app publish + package lifecycle |
| share | share-access tier patch |
| embed | embed-token mint + unauthenticated embed render |
| open data | open-data publication + anonymous read |

## Status

**BLOCKED — gate + policy landed, real-server runner cannot be built in
`honua-console` alone yet.**

Verified against the upstream repos (2026-05-24):

- `honua-server` has the Testcontainers/seed harness
  (`tests/dotnet/Honua.TestKit/`) and publishes a container image. The
  metadata v2 graph landed (coordinated via
  [`honua-server#1162`](https://github.com/honua-io/honua-server/issues/1162)),
  **but the Console-facing HTTP surfaces the chain calls were not found in the
  published API** — share-access patch, embed-token mint, open-data
  publication, and webmap read/write. Any still-open server wrappers in that
  cluster must close before the chain can be driven.
- `honua-sdk-dotnet` exists but **does not project** the content/share/embed/
  map-package/publish-handoff clients. Gated on
  [`honua-sdk-dotnet#166`](https://github.com/honua-io/honua-sdk-dotnet/issues/166)
  (and `#169`); `Honua.Console.Contracts/SdkShims.cs` records these as
  not-yet-projected.
- `honua-console` wires **zero** real clients today (`AddHonuaConsoleShell`
  registers only `InMemory*` services); there is no HTTP transport to bind. The
  "swap `InMemory*` → real client" integration lane is itself unbuilt.

Per Charter §11, mocking these open contracts is not allowed, so the runner
stays blocked rather than shipping a fake.

### Bounded child tickets to unblock

1. **honua-sdk-dotnet#166 / #169** — project typed .NET clients for the chain
   contracts (content/catalog detail, share-access, embed-token, map-package,
   publish-handoff). This is the closest-in blocker.
2. **honua-server** — confirm/land the Console-facing chain HTTP endpoints
   (share-access, embed-token, open-data publication, webmap read/write) and a
   consumable container image + seed profile for external Testcontainers boot
   (coordinated via honua-server#1162; close any remaining wrappers in that
   cluster).
3. **honua-console — integration lane** (the missing per-mock specs): register
   the real `honua-sdk-dotnet` client (or a thin `HttpClient` behind
   `Honua.Console.Contracts`) in the shell DI, replacing the `InMemory*`
   services for server-owned data and retiring the matching shim.
4. **honua-console — real-server runner** (this slice's successor): add the
   xUnit Testcontainers run that boots the image, seeds the fixture, drives the
   chain through the client from (3), emits `sourceHydrated: true` evidence, and
   flips CI's gate step off `--pending-ok`.
