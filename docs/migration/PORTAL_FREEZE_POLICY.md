# `honua-portal` Freeze And Retirement Policy

Status: filed 2026-05-23.

Decision source: [ADR-0001: Unified Honua Console Runtime](../adr/0001-unified-honua-console-runtime.md).
Backlog source: [Honua Console Migration Backlog](../roadmap/HONUA_CONSOLE_MIGRATION_BACKLOG.md).
Cleanup ticket: [`honua-console#10`](https://github.com/honua-io/honua-console/issues/10) (freeze and retire `honua-portal` after Console parity).

## Purpose

This policy resolves an open gap identified during design: the migration commits to retiring `honua-portal` after the parity gate, but does not specify when portal commits stop landing. Without a freeze policy, portal commits during the porting window force re-ports into Console and erode parity.

This policy declares the two freeze gates, the exception path, and the retirement trigger.

## Policy

### Soft freeze: bug-fix-only

The portal enters **soft freeze** when [`honua-console#4`](https://github.com/honua-io/honua-console/issues/4) (Port Catalog, Viewer, Saved Maps, Share, Embed, Open Data) opens.

During soft freeze:

- New features and non-trivial refactors are not landed on `honua-portal`.
- Bug fixes for production-affecting issues (security, data loss, blocking UX regressions) are still landed on `honua-portal`.
- Every bug fix landed on `honua-portal` during soft freeze must also be applied to the corresponding Console area in the same PR or in an immediate follow-up tracked against the relevant child ticket (`#4`, `#5`, `#6`, `#7`).
- A note is added to the PR body identifying which Console child ticket inherits the fix.

### Hard freeze: no commits

The portal enters **hard freeze** when [`honua-console#9`](https://github.com/honua-io/honua-console/issues/9) (cross-surface parity smoke) enters review.

During hard freeze:

- No commits land on `honua-portal` `main` (or its release branches). Open PRs are either closed or rebased onto Console.
- Bug reports are redirected to Console. If a bug is reproducible on the deployed portal but not on Console, that is itself a parity-gate failure and the cross-surface smoke (`#9`) is updated to cover it.
- The portal deployment continues to serve traffic until the deployment cutover lands under [`honua-console#8`](https://github.com/honua-io/honua-console/issues/8) and [`honua-devops#55`](https://github.com/honua-io/honua-devops/issues/55).

### Exception path

Either freeze can be lifted for a specific change with sign-off from:

- The Console epic owner (this ticket: `honua-console-1`).
- The portal area owner whose code is being changed.

The exception is recorded as a comment on `honua-console#10` (retirement ticket) so the freeze ledger stays auditable.

## Retirement Trigger

`honua-portal` is retired (repo archived, deployment removed, DNS/redirects pointed at Console) only when **all** of the following are accepted:

1. [`honua-console#4`](https://github.com/honua-io/honua-console/issues/4) ported: Catalog, Viewer, Saved Maps, Share, Embed, Open Data.
2. [`honua-console#5`](https://github.com/honua-io/honua-console/issues/5) ported: Studio app-builder and generated-app lifecycle (proof loop: prompt -> clarification -> spec/plan -> apply -> preview -> edit -> publish/reopen).
3. [`honua-console#7`](https://github.com/honua-io/honua-console/issues/7) accepted: Console consumes shared Metadata v2, content, package, and RBAC contracts from `honua-sdk-dotnet` (with `honua-sdk-js` still authoritative for embed and generated-app browser bundles).
4. [`honua-console#8`](https://github.com/honua-io/honua-console/issues/8) accepted: single deployable artifact serves `/studio`, `/catalog`, `/operate`, `/share` from one origin.
5. [`honua-console#9`](https://github.com/honua-io/honua-console/issues/9) accepted: cross-surface smoke evidence captured in CI proves publish service -> catalog item -> Studio artifact -> share/embed, including open-data publication and unauthenticated embed rendering (see scope clarification in the migration backlog).
6. The parity gate checklist in [`HONUA_CONSOLE_MIGRATION_BACKLOG.md`](../roadmap/HONUA_CONSOLE_MIGRATION_BACKLOG.md) passes.

The mechanics of repo archival, deployment removal, redirect plan, and removal of any remaining `honua-portal` consumers (e.g., local `file:../honua-portal` references in adjacent JS packages or porting scaffolds) are owned by `honua-console#10`.

## Owner Sign-off Required Before Soft Freeze

Before soft freeze begins (i.e., when `#4` opens), explicit acknowledgement is required from:

- Portal contributors who currently land work on `honua-portal`.
- The Console epic owner.

Without that acknowledgement, soft freeze is announced on the merge of `#4`'s opening PR rather than retroactively enforced. The intent is to avoid surprising portal contributors mid-PR.

## Why Two Gates

A single gate would be either too restrictive (block portal bug fixes until `#9` lands, accepting weeks of production exposure to known bugs) or too lax (allow portal commits up to retirement, accepting porting churn the day before cutover).

Two gates give the migration:

- Production safety during the porting window (bug fixes still land on the live surface during soft freeze).
- A clean cutover window (no portal commits during the parity-smoke review and deployment-artifact landing).

## Where Freeze State Is Tracked

- Active freeze state (none / soft / hard): a single line at the top of [`HONUA_CONSOLE_MIGRATION_BACKLOG.md`](../roadmap/HONUA_CONSOLE_MIGRATION_BACKLOG.md), updated when a gate flips.
- Exception ledger: comments on [`honua-console#10`](https://github.com/honua-io/honua-console/issues/10).
- Per-fix parity follow-ups: linked from the soft-freeze PR back to the relevant Console child ticket.
