# Portal Parity Checklist

Status: filed 2026-05-23.

Owning issue: [honua-console#10 — Freeze and retire honua-portal after Console parity](https://github.com/honua-io/honua-console/issues/10).

Decision source: [ADR-0001: Unified Honua Console Runtime](../adr/0001-unified-honua-console-runtime.md).

Companion artifact: [Portal Retirement Playbook](./PORTAL_RETIREMENT_PLAYBOOK.md).

## Purpose

This checklist is the gate the Portal freeze decision reads. It maps every Portal surface to a Console destination route, owning Console ticket, status, and parity-evidence pointer.

The checklist is the source of truth for the question "is this Portal feature already covered in Console, deferred, or intentionally not ported?". The retirement playbook treats the `Status` column as the freeze trigger: the playbook does not advance to the archive step while any row is `in-progress`.

The Portal surface column mirrors the section headings in [`honua-portal/docs/features/README.md`](https://github.com/honua-io/honua-portal/blob/main/docs/features/README.md). When a Portal feature changes, update that surface row in this checklist rather than restating Portal content here.

## Status Values

- **shipped** — Console route and parity smoke evidence both green. The owning Console ticket may still be open for follow-on work but the porting acceptance is met.
- **in-progress** — Owning Console ticket is open and the surface is in active port. Freeze is blocked until this resolves.
- **deferred** — Intentionally not in Beta scope. The row MUST cite the Console backlog item (existing or `[FILE]`) that owns the deferral.
- **not-ported** — Will not be ported as-is because a Console replacement supersedes the Portal surface. The row MUST state the reason and the superseding Console ticket.

## Parity Matrix

| Portal surface | Console destination route | Owning Console ticket | Status | Parity evidence |
|---|---|---|---|---|
| Portal Shell | `/` Console root, top bar, side nav, auth/session, canonical empty/forbidden/loading/error surfaces | [honua-console#2](https://github.com/honua-io/honua-console/issues/2) scaffold + [honua-console#3](https://github.com/honua-io/honua-console/issues/3) IA/RBAC | in-progress | `honua-portal/docs/runbook/portal-shell-smoke.md`; Console parity smoke (honua-console#9) |
| Content Catalog | `/catalog` browse, search, filters, item detail (`type` ∈ {service, layer, map, scene, app, document, external}) | [honua-console#4](https://github.com/honua-io/honua-console/issues/4) | in-progress | `honua-portal/smoke/catalog.smoke.md`, `honua-portal/smoke/catalog-browser.smoke.md`; Console #9 |
| Map Viewer (incl. Maputnik saved-map style editor) | `/maps/:id` viewer and `/maps/new?from=:itemId` draft hydration | [honua-console#4](https://github.com/honua-io/honua-console/issues/4) | in-progress | `honua-portal/docs/features/MAP_VIEWER_MVP.md`, `honua-portal/smoke/maputnik-style-editor.smoke.md`; Console #9 |
| Saved Web Maps | `/maps/:id` save/load/duplicate/rename/delete; portal-override style separation | [honua-console#4](https://github.com/honua-io/honua-console/issues/4) | in-progress | `honua-portal/smoke/saved-maps.smoke.md`; `honua-portal/docs/contracts/webmap-doc-v1.md`; Console #9 |
| Sharing And Embeds | `/catalog` share dialog + `/embed/maps/:id` parser + tier-aware `patchAccess` + dependency closure | [honua-console#4](https://github.com/honua-io/honua-console/issues/4) | in-progress | `honua-portal/smoke/sharing-and-embed.smoke.md`; Console #9 |
| Generated App Packages | `/studio/apps/:itemId/preview` (canonical path confirmed by Console #5); private publish + rollback | [honua-console#5](https://github.com/honua-io/honua-console/issues/5) | in-progress | `honua-portal/docs/contracts/generated-app-lifecycle-v1.md`; `honua-portal/docs/runbook/generated-app-lifecycle-smoke.md`; Console #9 |
| Open Data Pages | `/share/public` collection + `/share/public/items/:idOrSlug` detail (DCAT-US 3.0, JSON-LD, related items, revision history) | [honua-console#4](https://github.com/honua-io/honua-console/issues/4) | in-progress | `honua-portal/smoke/open-data.smoke.md`; `honua-portal/docs/features/open-data-publishing-quality.md`; Console #9 |
| Collaboration (annotations and comments) | Viewer annotation workspace at `/catalog/maps/:id`; embed read-only with public-moderation seam | [honua-console#4](https://github.com/honua-io/honua-console/issues/4) | in-progress | `honua-portal/smoke/annotations-comments.smoke.md`; `honua-portal/docs/features/ANNOTATIONS_COMMENTS.md`; Console #9 |
| Collaborative Map Editing (MVP contract) | Viewer collaboration panel + presence/cursor/lock seam at `/catalog/maps/:id` | [honua-console#4](https://github.com/honua-io/honua-console/issues/4) (MVP contract only; WebSocket/CRDT GA tracked separately) | in-progress | `honua-portal/docs/features/COLLABORATIVE_MAP_EDITING.md`; Console #9 |
| Admin Handoff (publish → catalog item) | `/operate` transitional surface + portal-equivalent catalog write boundary; `surfaceForSourceService` semantics preserved | [honua-console#6](https://github.com/honua-io/honua-console/issues/6) | in-progress | `honua-portal/smoke/publish-handoff.smoke.md`; `honua-portal/docs/contracts/content-item-v1.md`; Console #9 |
| Migration Compatibility (ArcGIS web map import/export spike) | `/catalog` import path; spike output | [honua-console#4](https://github.com/honua-io/honua-console/issues/4) (spike-level only; broader ArcGIS GA remains a separate Console backlog item) | in-progress | `honua-portal/docs/migration/ARCGIS_WEBMAP_COMPATIBILITY.md`; Console #9 |
| AI App-Builder GTM Proof (`/app-builder/proof`, fixtures #46–#50) | Superseded by Studio production loop on `/studio` | [honua-console#5](https://github.com/honua-io/honua-console/issues/5) | not-ported | Reason: Studio (Console #5) ports the production AI app-builder and generated-app lifecycle. The Portal `/app-builder/proof` route, model-free smoke harness, and operations-dashboard fixtures were scoped to validate the prompt→preview→edit→publish loop ahead of Studio and are obsoleted once Studio lands. `honua-portal/docs/runbook/app-builder-proof.md` and `fixtures/app-builder/operations-dashboard/` remain readable in the archived Portal as design reference but are not re-implemented in Console. |

## Deferred Portal Scope (informational, not parity rows)

These items are called out in Portal's [Deferred Features](https://github.com/honua-io/honua-portal/blob/main/docs/features/README.md#deferred-features) section. They are not parity rows because they were never Beta scope in Portal, but they are recorded here so Console contributors do not assume the Portal archive implies they have shipped.

- **Full dashboard builder** — deferred to a future Console GA dashboard ticket (not yet filed).
- **Broad app builder and template marketplace** — deferred to a future Console GA app-builder ticket (not yet filed). Studio (Console #5) ports the AI app-builder loop, not the full GA template marketplace.
- **Real-time multiplayer presence** — deferred. The Collaborative Map Editing MVP contract row above covers only the fixture seam; real-time transport is a future Console GA ticket.
- **Public issue reporting and civic engagement workflows** — deferred. No Beta or GA Console ticket exists.
- **Metadata harvesting from external catalog networks** — deferred. No Beta or GA Console ticket exists.
- **Mobile/offline editing and field workflows** — deferred. No Beta or GA Console ticket exists.
- **Enterprise publishing approval chains** — deferred. No Beta or GA Console ticket exists.

When any of these is pulled into Beta scope, file a Console backlog item, then promote it to a parity row in the table above with the new owning ticket and a `deferred → in-progress` status update.

## How To Update This Checklist

- A Console ticket cannot move a row to `shipped` until both its acceptance is met AND the Console parity smoke (honua-console#9) for that surface is green on the single deployable artifact (honua-console#8).
- New Portal merges after this checklist is filed are blocked by the freeze banner in `honua-portal/AGENTS.md` (added by the first child ticket in the retirement playbook). If a Portal feature does land before the freeze, add a new row here and assign it to a Console ticket before the freeze gate is opened.
- When a row's `Status` changes, update the parity-evidence pointer to the canonical Console artifact (smoke evidence, route reference) rather than leaving the Portal pointer in place.
- Do not delete rows. A `not-ported` row carries the design rationale that future contributors need to understand why Console did not absorb the surface.
