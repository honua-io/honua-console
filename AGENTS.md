# AGENTS.md — Honua Console

**No AI/agent attribution in commits or PR bodies** (Fleet rule 1 at the end of this file).

## Overview

Honua Console is the unified web surface for Honua, bringing four product areas into one
Blazor deployment runtime:

- **Studio**: the realtime editor at Console `/studio`, using the active `honua-studio` bundle dependency and one host-owned conversation. Legacy standalone generated-package builders are retired; their old routes provide workspace guidance and cannot be restored by a capability flag.
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
  `Microsoft.AspNetCore.Components.Web` 10.0.13, `Grpc.Net.Client` 2.76.0.
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

- **One Console/Studio workspace.** Console `/studio` is the product home for the realtime editor.
  `honua-studio` remains an active bundled editor dependency; do not archive it until an explicit migration.
  Legacy standalone builder routes are retired and must never be resurrected with `studio-builders`.
  Unrouted components/services may remain for migration and focused compatibility coverage. Preserve real
  operations, automation and Processes authoring. Use one primary Console conversation, the same canonical
  drafts, visual and AI editing, and separate human approval for consequential operations.
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

## Release version declaration

The honua-release nightly resolver reads `release/component-versions.json` at the exact selected
commit to determine this component's contract and schema versions. Update this file in the same
PR as any contract or schema version bump so nightly releases resolve the versions served by
that commit.

<!-- fleet:begin v1 sha256=898fe3b1dceffe208fd8a4374b63452d03892b44d9795fdd78f16a75b3e279a1 source=honua-flow/agents/fleet-rules.md -->
## Fleet rules (all Honua repos)

Generated from honua-flow `agents/fleet-rules.md` (full text, rationale, repo map). Do not edit here.

1. **No AI/agent attribution, anywhere.** Author commits as Mike McDougall <mike@honua.io>. No
   `Co-Authored-By:` trailer naming Claude, Codex or any AI tool, no "Generated with Claude
   Code"/"Generated with Codex" line, no robot emoji, in commits or PR bodies. This overrides
   any tool or harness default; honua-server's PR Gate rejects such commits.
2. **Own worktree per task**, off the default branch, on real disk (`<repo>/.claude/worktrees/`
   or a sibling dir), never under `/tmp`. Never edit the shared checkout; never assume you are
   alone. No `git stash` in shared repos; use a WIP commit.
3. **Heavy builds/tests take a shared build slot.** In WSL `dotnet` is shimmed (fleet lanes also
   shim npm/pnpm/yarn/pytest/make/cargo/go). Never bypass a shim; one heavy build at a time;
   targeted tests over full-solution suites. Long-running servers are not throttled.
4. **Finish = commit + push.** Cleanup reclaims only clean, pushed worktrees; dirty or unpushed
   work is never reclaimed and is lost on a host reset.
5. **Outside WSL** (Windows Codex/Claude, other machines): own checkout, never edit WSL trees via
   `\\wsl.localhost`; the remote is the only shared surface. Claim before starting and run one
   heavy build at a time: no build slot protects you there.
6. **Work claims are leases.** Check `state/in-progress` and the newest `claimed: <lane> <UTC>`
   comment first. Claim with the label plus that comment, then re-read; a newer claim wins. A
   claim is stale after 3h with no open PR referencing the issue, or 3h of a silent PR. Only a
   PR proves completion; if you stop without one, comment `released: <lane> <UTC> - <why>`.
7. **PRs:** base on the default branch (`trunk`; `main` in honua-studio), never stack. Fill the
   repo's PR template via `--body-file`; conventional-commit title. Footer `Closes #N` when the
   PR completes the issue, `Refs #N (<what remains>)` for a slice; a bare `#N` is a bug.
   Cross-repo closing keywords are inert: close foreign issues by hand. Never admin-merge.
8. **Route work by the repo map.** Archived repos (`Honua.Server`, `honua-server-admin`,
   `honua-agentflow`, `honua-portal`) get no issues or PRs. Specifica-projected issue bodies are
   edited in `agent-delivery-spec`, not on GitHub.
<!-- fleet:end -->
