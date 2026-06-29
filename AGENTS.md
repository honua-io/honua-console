# AGENTS.md — Honua Console

## Overview

Honua Console is the unified web surface for Honua, bringing four product areas into one
Blazor deployment runtime:

- **Studio**: AI-assisted spatial query, analysis, map, dashboard, report, form, app, and workflow authoring/publishing.
- **Catalog**: data, layers, services, saved maps/dashboards/reports/forms/workflows, generated apps, metadata, provenance.
- **Operate**: publishing, jobs, service config, identity, connectors, deployment health, observability, licensing, admin.
- **Share**: public links, embeds, open-data pages, exports, external publishing.

`/studio`, `/catalog`, `/operate`, and `/share` all resolve from the same Blazor Web host
(`src/Honua.Console.Web`). This repo is the migration target for porting `honua-portal` and
`honua-server-admin` behavior; many feature paths bind to a live honua-server and otherwise
render an explicit missing-binding state (see Conventions).

## Tech Stack

- **.NET 10** (`net10.0`) — Blazor (Razor Components, interactive server render mode), C# with
  `Nullable` and `ImplicitUsings` enabled.
- **Node.js >= 20** — smoke/parity harness and build-metadata stamping (ESM, `node --test`).
- Optional **.NET MAUI Blazor Hybrid** native host (`src/Honua.Console.Native`); only builds on
  Windows/macOS (or Linux with `EnableHonuaConsoleAndroidTarget=true` + Android SDK).
- Solution file: `Honua.Console.slnx` (XML `.slnx` format). Key package refs:
  `Microsoft.AspNetCore.Components.Web` 10.0.3, `Grpc.Net.Client` 2.76.0.
- License: Apache License 2.0. See `LICENSE` and `NOTICE`.

## Setup

- Install the .NET 10 SDK and Node.js 20+.
- Restore .NET: `dotnet restore Honua.Console.slnx`
- Restore Node (lockfile-strict): `npm ci`
- Optional env bindings for live honua-server features:
  - `HONUA_SERVER_BASE_URL` (or config `Honua:Server:BaseUrl`) — absolute HTTP(S) URL.
  - `HONUA_ADMIN_API_KEY` (or `Honua:Server:AdminApiKey`) — sent to admin endpoints as `X-API-Key`.
- Docker is required only for the opt-in Testcontainers integration tests.

## Commands

Build / run / test commands are copied from the manifests, CI, and `scripts/`.

- **Run the browser Console host:**
  `dotnet run --project src/Honua.Console.Web/Honua.Console.Web.csproj --urls http://127.0.0.1:5174`
- **Build the web host:** `dotnet build src/Honua.Console.Web/Honua.Console.Web.csproj`
- **Publish artifact (consumed by honua-devops):**
  `dotnet publish src/Honua.Console.Web/Honua.Console.Web.csproj -c Release -o artifacts/honua-console-web`
- **Fast local check (host-independent tests + web build):** `./scripts/fast-local-check.sh`
- **.NET unit tests:** `dotnet test tests/Honua.Console.Native.Core.Tests/Honua.Console.Native.Core.Tests.csproj`
- **Lint / format check:** `dotnet format Honua.Console.slnx --verify-no-changes` (CI gate; run
  `dotnet format Honua.Console.slnx` to apply).
- **Node tests (smoke + metadata):** `npm test`
- **Parity smoke:** `npm run smoke:parity` (add `-- --origin https://console.staging.honua.example`
  for deployed origins); harness self-tests: `npm run smoke:parity:test`.
- **Workflow contract smoke:** `npm run smoke:workflow`
- **Build metadata stamp:** `npm run build:metadata` (uses `HONUA_CONSOLE_ARTIFACT_DIR`,
  `HONUA_CONSOLE_COMMIT_SHA`, `HONUA_CONSOLE_REF`).
- **Opt-in live integration tests** (need Docker + honua-server checkout/image): set the relevant
  env vars (`HONUA_CONSOLE_RUN_LIVE_SERVER_TESTS`, `HONUA_SERVER_PROJECT`,
  `HONUA_CONSOLE_OPERATE_SERVER_IMAGE`/`_CONTEXT`) and use `dotnet test --filter <Name>`. See README
  "Local Usage" / "Operate Observability Usage". These skip without Docker.

## Architecture

Five source projects (see `Honua.Console.slnx`):

- **`Honua.Console.Shell`** (Razor class lib) — shared routes (`ConsoleRoutes.razor`), layout,
  pages (`Pages/`), models, and service interfaces/implementations (`Services/`, DI in
  `DependencyInjection/HonuaConsoleShellServiceCollectionExtensions.cs`). This is where most UI lives.
- **`Honua.Console.Contracts`** — temporary SDK shim boundary for Console-side contracts (env-trust
  shapes, validate wire contracts, package/observability/workflow contracts) until shared .NET SDK
  projections replace them.
- **`Honua.Console.Web`** (`Sdk.Web`) — default browser host. `Program.cs` wires
  `AddRazorComponents().AddInteractiveServerComponents()` + `AddHonuaConsoleShell(...)`, serves
  `/version.json`, and mounts the shell assembly. Independently buildable/deployable without MAUI.
- **`Honua.Console.Native.Core`** — testable native host services: persisted environment profiles,
  account-token sessions, certificate references, HTTP/gRPC connection creation with pinned server
  fingerprints, the server-bound trust gate, and the telemetry streaming proof.
- **`Honua.Console.Native`** (`Sdk.Razor`/MAUI) — optional MAUI Blazor Hybrid desktop host rendering
  the shell in a `BlazorWebView`. Conditionally a no-op Library on unsupported hosts.

Service layer pattern (in `Shell/Services`): each capability has an interface plus `InMemory*`
(demo/test), `Server*`/`Http*` (live honua-server), and `Unsupported*` implementations. Live
bindings activate only when a server base URL is configured.

## Directory Layout

- `src/` — the five projects above.
- `tests/Honua.Console.Native.Core.Tests` — host-independent unit tests (route boundaries, profiles,
  connections, trust gate, streaming proof) + opt-in Testcontainers Operate/live-server coverage.
- `tests/Honua.Console.IntegrationTests` — opt-in Testcontainers mTLS/trust + server-bound form
  builder suite; skips without Docker.
- `smoke/parity/` — Node ESM parity/workflow smoke harness, adapters, scenarios, fixtures,
  sample-evidence, and `__tests__/`.
- `scripts/` — `fast-local-check.sh`, `integration-trust-check.sh`, `write-build-metadata.mjs`,
  and `__tests__/`.
- `config/console-areas.json` — the four area slugs (studio, catalog, operate, share).
- `docs/` — extensive architecture/ADR/migration/route-map docs. `docs/console-route-map.md` is the
  IA source of truth; `docs/migration/CONSOLE_PATTERNS_CHARTER.md` holds binding patterns.
- `.github/workflows/ci.yml` — the CI pipeline.
- `Honua.Console.slnx`, `package.json`, `README.md`, `.gitignore`.

## Conventions & Gotchas

- **Missing-binding is a first-class state.** When no honua-server base URL is configured, live
  routes (Operate transition, Studio `/studio/form`, workflow editor, etc.) render an explicit
  missing-binding surface — never mock/seeded data. In-memory/demo shells are test/demo-only and
  opt-in (e.g. `AddHonuaConsoleDemoStudioAuthoringShell`, `AddHonuaConsoleDemoStudioWorkflowPackages`).
- **Do not duplicate server/SDK DTOs.** Consume server/SDK projections for validate, preview,
  publish, and run responses. `Honua.Console.Contracts` shims are temporary; do not expand them when
  a shared contract exists.
- **State vocabulary** is preserved across Operate surfaces: `unknown`, `unsupported`, `missing`,
  `disabled`, `not configured`, `unconfigured` are neutral; forbidden/unavailable reads use the
  shared section status surface.
- **Route/IA is fixed.** Follow `docs/console-route-map.md` and the Console Patterns Charter for URL
  shapes, RBAC gates, empty/loading states, and file layout. Query-key conventions matter (e.g.
  Catalog maps `visibility` -> SDK `sharing`; embed tokens go in `#embedToken=`, never query string).
- **CI gates** (`.github/workflows/ci.yml`): `dotnet format --verify-no-changes`, native-core tests,
  web build, `npm ci`, `npm test`, publish, metadata stamp, and `npm run smoke:parity`. Run
  `dotnet format` and `./scripts/fast-local-check.sh` before pushing.
- **MAUI native host** does not build on plain Linux; it degrades to a Library no-op. The CI runner
  is `ubuntu-latest` and does not build the MAUI app.
- **Build output:** ignored dirs are `bin/ obj/ node_modules/ artifacts/ coverage/ smoke-evidence/`.
  Smoke evidence is written to `smoke-evidence/`; committed samples live in
  `smoke/parity/sample-evidence/`.

## Shared dev-environment rules (multi-agent WSL)

This machine runs many agents concurrently (**Codex + Claude**, often via agentflow with multiple tabs/agents). To prevent host lockups and lost work, every agent MUST follow these:

1. **Heavy builds/tests are throttled by a shared lock.** `dotnet` and `npm` are PATH-shimmed, so their build/test/publish/pack and ci/install/test/run-build/run-test subcommands automatically run under a global semaphore (default 1 concurrent, `HONUA_BUILD_SLOTS`). For other heavy tools, call the wrapper explicitly: `with-build-lock pytest ...`, `with-build-lock cargo build`, `with-build-lock make build`. The lock is shared across ALL of this user's processes (every Codex/Claude tab, agentflow children). Do not bypass it for compiles or test suites. Long-running servers (`dotnet run`, `npm run dev`) are intentionally NOT locked — never wrap those.

2. **Commit and push when you finish a task** so your worktree can be reclaimed. An hourly job (`honua-clean`) removes a worktree ONLY when it is clean AND fully pushed (merged, remote-gone, or idle >=2d). Dirty or unpushed worktrees are NEVER touched — but uncommitted/unpushed work blocks reclamation and is at risk if the instance is reset. Build artifacts (bin/obj and untracked node_modules) are reclaimed automatically and safely.

3. **Commit hygiene — no agent attribution.** Author every commit as the repo owner only (git identity: Mike McDougall <mike@honua.io>). Do **NOT** add any agent/tool attribution to commits: no `Co-Authored-By: Claude ...`, no `Co-Authored-By: Codex ...` (or other bot co-authors), and no "Generated with Claude Code" / "Generated with Codex" / "🤖" lines in the message or PR body. Write a plain, descriptive commit message and stop.
