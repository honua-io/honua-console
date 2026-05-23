# Portal Retirement Playbook

Status: filed 2026-05-23.

Owning issue: [honua-console#10 — Freeze and retire honua-portal after Console parity](https://github.com/honua-io/honua-console/issues/10).

Decision source: [ADR-0001: Unified Honua Console Runtime](../adr/0001-unified-honua-console-runtime.md).

Companion artifact: [Portal Parity Checklist](./PORTAL_PARITY_CHECKLIST.md).

## Purpose

This playbook makes the Portal retirement decision mechanical. It defines the freeze trigger, the per-repo sequence, the retention decision, and the announcement path, so that once parity is reached the freeze can be executed without rebuilding context.

This playbook does NOT itself flip any switches. Each step below is performed by a bounded single-repo child ticket. The playbook lists those tickets so they can be filed after this design lands.

## Freeze Trigger

The freeze is gated on two conditions, both of which must be true:

1. **Console parity smoke green on the single deployable artifact.** [honua-console#9](https://github.com/honua-io/honua-console/issues/9) (publish service → catalog → Studio artifact → share/embed) must be green in CI or release promotion on the single deployable artifact produced by [honua-console#8](https://github.com/honua-io/honua-console/issues/8). Smoke evidence must be captured in the same promotion record so the freeze decision is reproducible.
2. **Every row in the parity checklist is resolved.** No row in [PORTAL_PARITY_CHECKLIST.md](./PORTAL_PARITY_CHECKLIST.md) may be `in-progress`. `shipped`, `deferred`, and `not-ported` are all acceptable terminal states.

If either condition is not met, the freeze does not advance. There is no human override at this step — open Portal work either ships in Console (`shipped`), is deferred with an owning Console ticket (`deferred`), or is explicitly retired (`not-ported`).

## Sequence

Steps run chronologically, one repo per step. Each step is a separate child ticket so ownership stays single-repo and verification stays inside the repo where the change lands.

1. **`honua-portal`: Add freeze banner and contributor redirect.** Edit `README.md` and `AGENTS.md` with the freeze notice. The banner names the freeze date, points contributors at `honua-console`, and references this playbook + parity checklist. New issues filed during the wind-down are closed with a "moved to honua-console" macro that links the corresponding parity-checklist row.
2. **`honua-portal`: Archive open backlog.** Open issues and PRs are closed-as-not-planned with the macro from step 1. Any open issue that has a parity-checklist destination is closed with a pointer to that row; any open issue that does not has a one-line reason in the close comment.
3. **`honua-devops`: Remove Portal deployment target.** Delete the Portal-specific build/preview/promote steps in CI and release. Update `docs/strategy/portfolio-60-day-plan.md` and any pipeline references. After this step the single deployable artifact from honua-console#8 is the only web surface in the release pipeline.
4. **`honua-server-admin`: Redirect Operate work to Console.** Update `README.md` and `AGENTS.md` to point Operate workflow filings at `honua-console` (per the transitional Operate surface in honua-console#6). This is the verbal handoff; the legacy Admin runtime continues to serve operator workflows until Operate is fully ported by a later Console ticket.
5. **`honua-portal`: Archive repository read-only.** Flip the GitHub repo to archived. Verify no open PRs or branches are blocked at the time of flip.

Each step's child ticket carries its own acceptance evidence (banner screenshot, archived-repo URL, removed pipeline line, etc.). The playbook treats step 5 as the formal retirement; the parity checklist's last `in-progress`-to-terminal transition is the formal trigger.

## Retention Decision

**Default: archive read-only.**

Rationale:

- The parity checklist references `honua-portal/docs/features/README.md`, `honua-portal/docs/strategy/MIGRATION_MATRIX.md`, the `honua-portal/docs/contracts/*` files (`content-item-v1.md`, `webmap-doc-v1.md`, `generated-app-lifecycle-v1.md`, `app-builder-proof-v1.md`), and the `honua-portal/smoke/*.md` evidence files from this playbook's history columns. Preserving Portal's git history and the renderable docs is cheaper than vendoring those files into Console.
- Inbound links from `honua-server-admin`, `honua-sdk-js`, and prior issues remain resolvable. Deletion would break them.
- The freeze banner (step 1) is applied before archive (step 5), so contributors browsing the archived repo see the redirect at the top of the README before they can follow a stale link.

"Retain as active" is explicitly rejected — it is the failure mode this entire ticket exists to prevent. "Delete after N months" is rejected for the inbound-link reason above; a future cleanup ticket can revisit this if archived Portal turns out to attract spam or create confusion.

### Contract files retention

Portal's `docs/contracts/*` files are kept as read-only references in the archived repo (the default retention decision above). Console does not vendor them. SDK-side contract ownership (e.g. migrating `content-item-v1.md` shape definitions into `honua-sdk-js`) is out of scope for this ticket and would be a separate `honua-sdk-js` design if it becomes necessary.

### Smoke evidence supersession

Portal's `smoke/*.md` files (`catalog.smoke.md`, `catalog-browser.smoke.md`, `saved-maps.smoke.md`, `sharing-and-embed.smoke.md`, `open-data.smoke.md`, `publish-handoff.smoke.md`, `annotations-comments.smoke.md`, `maputnik-style-editor.smoke.md`) stop receiving updates at archive time. The Console parity smoke (honua-console#9) is the new canonical record. Parity-checklist rows that still cite Portal smoke files at freeze time MUST be updated to cite the Console parity-smoke evidence before their row moves to `shipped`.

## Announcement

Single freeze-date note in:

- The org-level roadmap entry that already references the Console migration.
- `honua-portal/README.md` (added by step 1).
- `honua-console/README.md` (a one-line "Portal frozen as of {date}; see [PORTAL_RETIREMENT_PLAYBOOK.md](docs/migration/PORTAL_RETIREMENT_PLAYBOOK.md)" line, added as part of step 1's coordination).

No separate Slack or email channel is required for Beta. If Portal is referenced by an external partner integration that we know about at freeze time, the freeze coordinator adds that partner to the announcement list as a one-off.

## Bounded Single-Repo Child Tickets To File

These are not filed in this ticket. They are recorded so a human can file them after the design and playbook are accepted. Each is single-repo, single-scope, and verifiable inside the owning repo.

| Repo | Proposed title | Scope | Gate |
|---|---|---|---|
| `honua-portal` | Add freeze banner and contributor redirect for Console migration | Edit `README.md` and `AGENTS.md` with freeze notice + Console pointer; close open issues/PRs with the "moved to honua-console" macro pointing at the parity-checklist row | After honua-console#9 green |
| `honua-portal` | Archive repository read-only after Console parity | Flip GitHub repo to archived; verify no open PRs/branches blocked at flip time | After the banner ticket above + final smoke evidence |
| `honua-devops` | Remove `honua-portal` deployment target from single-artifact pipeline | Delete Portal-specific build/preview/promote steps; update `docs/strategy/portfolio-60-day-plan.md` and pipeline references; ensure single deployable artifact from honua-console#8 is the only web surface | After honua-console#8 ships and honua-console#9 green |
| `honua-server-admin` | Update README/AGENTS to direct Operate work to `honua-console` | Replace existing references with pointers to the Console Operate surface (per honua-console#6) | After honua-console#6 lands |

If the human filing these decides to fold the `honua-server-admin` ticket into honua-console#6's scope rather than file it separately, that is acceptable — it changes the ownership boundary but not the work.

## Risks And Mitigations

- **Freeze-too-early.** A Console ticket reports `shipped` but the corresponding smoke is not actually green. Mitigation: the freeze trigger requires both checklist resolution AND honua-console#9 green on the deployable artifact. The smoke gate catches the case where a checklist row is incorrectly marked.
- **Late-landing Portal merge.** A feature merges into Portal after the checklist is filed but before the banner is added. Mitigation: the banner ticket (step 1) is the first child filed; once the banner is in `AGENTS.md`, the binding-pattern enforcement that already lives there directs new feature work to Console.
- **Archive vs delete.** Archived Portal stays visible to contributors. Mitigation: banner is applied before archive (step 1 before step 5), so the redirect is the first thing a contributor sees.
- **Coordination drift.** Four child tickets across three repos depend on honua-console#8 and #9. If those slip, the freeze date slips. Tradeoff accepted — collapsing into one mega-ticket would violate the single-repo constraint and obscure ownership.
- **Operate handoff overlap.** The `honua-server-admin` README child ticket overlaps honua-console#6 scope. Mitigation: the child ticket is explicitly gated AFTER honua-console#6 lands, so it edits whatever README state Console #6 leaves behind.
