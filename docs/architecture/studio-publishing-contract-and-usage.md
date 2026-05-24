# Studio Publishing Contract And Usage

Status: implemented Console fixture for [honua-console#16](https://github.com/honua-io/honua-console/issues/16)

Owner surface: Honua Console Studio, with Catalog and Share route handoff

Related model: [Honua Studio Information Model And Workflows](studio-information-model-and-workflows.md)

## Scope

The Console publish path lets a builder publish a Studio draft or generated preview as a versioned Console content item. The implemented milestone is fixture-backed and intentionally keeps production persistence, RBAC enforcement, validation endpoints, and dashboard/report wire DTOs owned by `honua-server` and `honua-sdk-js` follow-ons.

The fixture contract lives behind `StudioPublishingClient` in `src/studio/publishing/types.ts`. Console uses SDK-owned package shapes where they already exist:

- Maps use `HonuaMapPackage` from `@honua/sdk-js/runtime`.
- Generated apps use `AppPackage` and `ArtifactRef` from `@honua/sdk-js/operator`.
- Share visibility is derived from `HonuaShareRequest` in `@honua/sdk-js/control-plane`.
- Dashboard and report packages are fixture projections until shared SDK/server exports land.

## Supported Publish Targets

| Target | Draft or preview entry | Preview route after publish | Package source |
| --- | --- | --- | --- |
| Map | `/studio/previews/:draftId` or `/studio/drafts/:draftId` | `/maps/:itemId` | SDK `HonuaMapPackage` |
| Dashboard | `/studio/drafts/:draftId` | `/dashboards/:itemId` | Fixture dashboard projection with Vega-Lite specs |
| Report | `/studio/drafts/:draftId` | `/reports/:itemId` | Fixture report projection |
| App | `/studio/previews/:draftId` or `/studio/drafts/:draftId` | `/apps/:itemId/preview` | SDK `AppPackage` projection |

All targets publish through the same review route:

- `/studio/drafts/:draftId/publish`

## Publish Review Input

`StudioPublishingClient.publishDraft` accepts the review state that a builder confirms in Studio:

- `draftId`
- `title`
- `summary`
- `tags`
- `targetAudience`
- `versionNote`
- `share`

`share` carries:

- `visibility`: `private`, `workspace`, `group`, `public-link`, or `public`
- `groupIds`
- `publicLinkEnabled`
- `embedEnabled`
- `embedPolicy`: `disabled`, `same-origin`, or `public`

Private publishes normalize to private visibility, no groups, no public link, and disabled embeds. Non-group publishes clear `groupIds`; group publishes trim empty group entries. When embeds are enabled, public and public-link publishes normalize `embedPolicy` to `public`; other embeddable publishes use `same-origin`.

Group publishes require at least one non-empty `groupIds` entry. The UI validates this before submit, and the fixture client enforces the same rule for non-UI callers.

## Publish Result Contract

Successful publish returns a `PublishedContentItem` with:

- stable identity: `itemId`, `workspaceId`, `type`
- catalog metadata: `title`, `summary`, `tags`, `publicationState: "published"`
- active version metadata: `versionId`, `versionNumber`, `packageRef`, `packageSchemaVersion`, `createdBy`, `createdAt`, `changeNote`, optional `rollbackFromVersionId`
- provenance refs copied from the Studio draft, including prompt, spec, plan, apply job, package artifact refs, source item dependencies, model runs, actor, and publish timestamp
- normalized share/embed settings
- routable Console links

`targetAudience` is part of the review input for the fixture milestone. It is not returned on `PublishedContentItem`; production persistence should either promote it into server-owned item metadata or keep it as publish-review context without changing the route contract.

The canonical route is always the Catalog route:

- `routes.canonical`: `/catalog/:itemId`
- `routes.catalog`: `/catalog/:itemId`
- `routes.preview`: target-specific preview route
- `routes.share`: `/share/:itemId`
- `routes.embed`: `/embed/:itemId`
- `routes.editInStudio`: `/studio/items/:itemId/edit`

Republishing the same fixture draft appends a new immutable version number for the same item id. The current fixture stores published items in browser session storage so route handoffs work within the same browser session.

Type-specific preview routes must match the published item type. For example, a dashboard item is supported at `/dashboards/:itemId`; opening that same item through `/maps/:itemId` renders the Console `unsupported` state instead of pretending a map package binding exists.

## Reopen Contract

`StudioPublishingClient.reopenPublishedItem` returns a `ReopenedStudioArtifact` for `/studio/items/:itemId/edit`.

The edit context includes:

- `draftId`
- `sourceVersionId`
- `promptRef`
- `planRef`
- `packageRef`
- `loadedWithoutGeneration: true`

Reopen loads the active published package revision and does not call generation again. The Studio edit route can then return to publish review for an update.

## Errors And Empty States

Console uses the shared publish problem taxonomy for review, publish, route, and reopen failures:

- `missing`
- `unauthorized`
- `unsupported`
- `invalid`
- `conflict`
- `server`

The fixture currently exercises missing published items, invalid group share requests, unsupported preview route mismatches, and dependency-closure conflicts. A publish is blocked when a blocking warning exists or requested visibility would widen a dependency beyond its required visibility. Visibility is ordered narrowest to widest as private, workspace, group, public-link, public, so group sharing is treated as wider than workspace dependencies. These errors render through the same Console empty-state or inline warning surfaces used by the publish route.

## Telemetry And Smoke Evidence

Studio publish emits browser events on `honua:studio-publish`:

- `publish.review.opened`
- `publish.submitted`
- `publish.succeeded`
- `publish.failed`
- `publish.reopen.completed`

Review, submit, success, and failure events carry the draft and target context when available; success also carries the published item id. Reopen completion carries the published item id and target, and the edit route verifies the package was loaded without a generation call.

Smoke coverage in `tests/smoke/studio-publishing.spec.ts` verifies every supported target from Studio entry to publish review, Catalog canonical route, target preview route, Share route, Embed route, and Edit in Studio reopen without generation. It also covers group visibility validation, dependency-closure conflict rendering, and unsupported preview route mismatches.

## Deferred Server And SDK Follow-ons

- Replace fixture persistence with server-owned content items, versions, publication records, audit, and provenance.
- Enforce RBAC, entitlement, workspace policy, dependency closure, and validation on the server.
- Replace dashboard and report fixture projections with shared SDK/server package contracts.
- Add production rollback semantics for published versions.
- Preserve this Console response shape when wiring real API calls so Catalog, Share, Embed, and Studio reopen routes do not drift.
