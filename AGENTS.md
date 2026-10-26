# honua-console: agent instructions

Unified Blazor web surface for Honua: Studio (`/studio`), Catalog, Operate and Share, all served
by `src/Honua.Console.Web`. It is the migration target for `honua-portal` and
`honua-server-admin` behaviour. License: Apache License 2.0 (`LICENSE`, `NOTICE`; the license
test in `scripts/__tests__/license-compliance.test.mjs` asserts this line).

## Commands
```bash
cp src/Honua.Console.Web/global.json global.json   # CI does this: pins the SDK for solution commands
dotnet restore Honua.Console.slnx --configfile NuGet.config --locked-mode
./scripts/fast-local-check.sh                      # native-core tests + web build
dotnet format Honua.Console.slnx --verify-no-changes
npm ci && npm test                                 # Node tests (scripts, smoke harness, workflows)
npm run smoke:parity                               # add -- --origin <url> for a deployed origin
python3 scripts/check-okf-bundle.py && python3 scripts/generate-summary.py --check   # docs gates
dotnet run --project src/Honua.Console.Web/Honua.Console.Web.csproj --urls http://127.0.0.1:5174
```
- Commit `packages.lock.json` changes; CI fails when restore changes a lock file.
- Live-server and Testcontainers lanes are opt-in and skip without Docker; their env vars are
  in `README.md` "Opt-in live-server test lanes". Full Docker e2e: `make e2e-live`.
- Smoke output goes to the ignored `smoke-evidence/`; committed samples live in
  `smoke/parity/sample-evidence/`.
- Bind a live server with `HONUA_SERVER_BASE_URL` and `HONUA_ADMIN_API_KEY` (README "Connect
  it to a honua-server").
- `src/Honua.Console.Native` (MAUI) builds only on Windows/macOS, or Linux with
  `EnableHonuaConsoleAndroidTarget=true`; elsewhere it is a no-op library and CI skips it.

## Rules
- **One Console/Studio workspace.** `/studio` is the realtime editor over the bundled
  `honua-studio` dependency; do not archive that dependency without an explicit migration.
  Retired standalone builder routes stay retired: never revive them with `studio-builders`.
  Unrouted components and services may stay for migration and compatibility tests. Keep real
  operations, automation and Processes authoring; use one primary conversation, the canonical
  drafts, and separate human approval for consequential operations.
- **Missing-binding is a first-class state.** With no server base URL, live routes render the
  explicit missing-binding surface, never mock or seeded data. In-memory/demo shells
  (`AddHonuaConsoleDemoStudioAuthoringShell`, `AddHonuaConsoleDemoStudioWorkflowPackages`) are
  opt-in for tests and demos only.
- **Services** in `src/Honua.Console.Shell/Services` follow interface + `InMemory*` (demo/test) +
  `Server*`/`Http*` (live) + `Unsupported*`; live bindings activate only when a base URL is set.
- **Do not duplicate server/SDK DTOs.** Consume server/SDK projections. `Honua.Console.Contracts`
  shims are temporary; never grow them when a shared contract exists
  (`docs/migration/SDK_SHIM_POLICY.md`).
- **State vocabulary** across Operate: `unknown`, `unsupported`, `missing`, `disabled`,
  `not configured`, `unconfigured` are neutral; forbidden/unavailable reads use the shared
  section status surface.
- **Routes and IA are fixed** by `docs/console-route-map.md` and
  `docs/migration/CONSOLE_PATTERNS_CHARTER.md` (URL shapes, RBAC gates, empty/loading states,
  file layout). Read both before adding or changing a route. Catalog `visibility` maps to SDK
  `sharing`; embed tokens go in `#embedToken=`, never the query string.
- A contract or schema version bump updates `release/component-versions.json` in the same PR;
  the honua-release nightly resolver reads it at the selected commit.

## Read before you act
- Workspace composition and credential boundaries: `docs/agent-workspace.md`.
- Browser testing against a live server: `docs/testing/AGENT_BROWSER_TESTING.md`.
- Vendored browser assets (no bundler): `README.md` "Vendored browser assets".

<!-- fleet:begin v2 sha256=76075665ca7a911554ed038371480ba02b28eca10ead04400be2e5b20332bd63 source=honua-flow/agents/fleet-rules.md -->
## Fleet rules (all Honua repos)

Generated from honua-flow `agents/fleet-rules.md` (full text, repo map). Do not edit here.

1. **No AI attribution.** Author as Mike McDougall <mike@honua.io>. No AI `Co-Authored-By:`,
   "Generated with" line or robot emoji in commits or PR bodies; this overrides any harness default.
2. **Own worktree per task** off the default branch, on real disk (`<repo>/.claude/worktrees/` or a
   sibling dir), never `/tmp` or the shared checkout. No `git stash`; use a WIP commit.
3. **Heavy builds/tests go through the build-slot shims**; never bypass them. Run targeted tests;
   full suites are CI's job. Outside WSL: own checkout, one heavy build at a time.
4. **Finish = commit + push.** Dirty or unpushed worktrees are never reclaimed and die on a reset.
5. **Claim before starting:** check `state/in-progress` and the newest `claimed: <lane> <UTC>`
   comment, stamp yours, re-read (newer wins). Stale after 3h without a live PR. Stopping without
   a PR: comment `released: <lane> <UTC> - <why>`.
6. **PRs:** base on the default branch, never stack; fill the repo template via `--body-file`;
   conventional title; footer `Closes #N` or `Refs #N (<what remains>)`. Never admin-merge.
7. **Route by the repo map.** Archived repos get no issues or PRs.
<!-- fleet:end -->
