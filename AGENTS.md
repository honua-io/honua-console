# honua-console: agent instructions

Unified Blazor web surface for Honua: Studio (`/studio`), Catalog, Operate and Share, all served
by `src/Honua.Console.Web`. It is the migration target for `honua-portal` and
`honua-server-admin` behaviour. License: Apache License 2.0 (`LICENSE`, `NOTICE`; the license
test in `scripts/__tests__/license-compliance.test.mjs` asserts this line).

## Commands
```bash
cp src/Honua.Console.Web/global.json global.json   # CI does this: pins the SDK for solution commands
dotnet restore Honua.Console.slnx --configfile NuGet.config --locked-mode
./scripts/fast-local-check.sh                      # Shell tests + web build
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

## Rules
- **One Console/Studio workspace.** `/studio` is the realtime editor over the bundled
  `honua-studio` dependency; do not archive that dependency without an explicit migration.
  Retired builder routes stay retired: `/studio/workflows/new`, `/studio/query`, `/studio/analysis`,
  `/studio/form/ai`, `/studio/map`, `/studio/report/ai`, `/studio/app`, `/studio/dashboard`.
  Keep the routed manual `/studio/form` and `/studio/report` tools.
  The bundle is never committed: bump `scripts/studio-release.lock.json` and run
  `node scripts/fetch-studio-release.mjs` (`docs/agent-workspace.md` "Studio release pin").
  Delete unrouted components and services; tests cover routed behaviour, not dead code. Keep real
  operations, automation and Processes authoring; use one primary conversation, the canonical
  drafts, and separate human approval for consequential operations.
- **Missing-binding is a first-class state.** With no server base URL, live routes render the
  explicit missing-binding surface, never mock or seeded data. In-memory/demo registrations
  (`AddHonuaConsoleDemoStudioWorkflowPackages` and the other `AddHonuaConsoleDemo*` helpers) are
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

## Vocabulary review

Review new labels, accessibility names, help text, and errors against the [canonical Honua vocabulary](https://github.com/honua-io/honua-server/blob/115011ce760443e93ec70e4bd5123d7b9fa08414/docs/concepts/domain/README.md) and its [shared retired-terms policy](https://github.com/honua-io/honua-server/blob/115011ce760443e93ec70e4bd5123d7b9fa08414/scripts/ci/retired-terms.v1.json). Block retired object names in new UI. Review contextual exceptions using that same policy; preserve protocol wire names and actual agent collaboration workspace identifiers. Model and SDK contract renames follow the coordinated server migration.
`scripts/__tests__/retired-vocabulary.test.mjs` (run by `npm test`) fails on retired Console wording and type names in `src/`; add an exception there only for a source-format reader or a wire literal, with its reason.

## Read before you act
- Workspace composition and credential boundaries: `docs/agent-workspace.md`.
- Browser testing against a live server: `docs/testing/AGENT_BROWSER_TESTING.md`.
- Vendored browser assets (no bundler): `README.md` "Vendored browser assets".

## Review guidelines
Reviews (people, Codex, Claude) judge the lines a PR adds or changes by Robert C. Martin's *Clean Code*
and cite smell codes ([rubric](https://github.com/honua-io/.github/blob/main/clean-code/RUBRIC.md)). The
`Clean code` PR check enforces what a tool can decide; `quality-exception: <reason>` in the PR body is logged.

BLOCKING:
- G5 duplication: search first; reuse or consolidate, never copy a handler, parser or validator.
- C1 history, ticket/PR refs or reviewer notes in code; C2-C4 obsolete, redundant or rambling comments;
  C5 commented-out code. Comment only what the code cannot say.
- XML doc comments (`///`) in application code; published SDKs keep at most a one-line `<summary>`
  on public members.
- N1 names that hide intent; F1 more than 3 parameters; F2 output arguments; F3 boolean flag arguments.
- Over budget with no reason in the PR: functions ~40 lines or 2 nesting levels; classes, files ~400 lines.

WARNING: the rubric's other smells (N2-N7, G6-G36, T1-T9), e.g. G25 magic numbers or G12
single-implementation interfaces.

<!-- fleet:begin v3 sha256=4faeba041ae102508d105847af58b407641cbfdf1ae506488819a2ccdbe73e29 -->
## Fleet rules (all Honua repos)

Generated from the org fleet-rules source; do not edit this block. Start with org skill `honua-system-map`.

1. Author commits as the repository owner's configured git identity. No AI attribution in commits or PR bodies.
2. Use a separate worktree per task, off the default branch, on durable disk. Include current trunk before opening a PR. No shared-checkout edits or git stash; preserve work with commits.
3. Run .NET builds and tests only through the approved remote build-host shims. Never fall back to a local build. See org skill `honua-build-host`.
4. Commit and push completed work; keep existing branches, commits and worktrees until their retention rules permit cleanup.
5. Claim work before starting, reread the issue and current lane rulings, and release the claim after completion. See org skill `honua-dev-loop`.
6. Use a PR slot and bundle at least three issues, a plan row, or a permitted single-work reason. Current caps and exceptions come from current lane rulings.
7. Target the default branch with the repository's PR template and conventional title. Never force-push or use an admin merge.
8. Run pre-PR checks, obtain one independent review of the final head, and resolve every review thread. Merge on green required checks; arm auto-merge where enforced checks support it. See org skill `honua-pr-flow`.
9. Read failed required-check logs and fix in one push. For red trunk, fix forward or revert within the repository's current ruling deadline; see org skills `honua-red-pr` and `honua-trunk-red`.
10. Never commit secrets, deployment identifiers, private repository names or infrastructure details. Security findings belong in the private tracker. Archived repos get no issues or PRs; public instructions name org skills only.
<!-- fleet:end -->
