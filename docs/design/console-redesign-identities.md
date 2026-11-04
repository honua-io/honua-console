# Console redesign: identities and mutation scope

Checked against `origin/trunk` at `0ea1f168757e12f167d9220ca41022d928c642fc`.
Phases 0–1 change presentation and local chrome state; existing server mutations
and authorization stay with their current clients. This is a preservation table,
not a design for later object routes. See [routes](console-redesign-route-compatibility.md),
[capabilities](console-redesign-capabilities.md) and [acceptance](console-redesign-acceptance.md).

| UI object | Canonical key / backing record | Parent or relationships | Mutation scope and retained boundary | Baseline evidence |
|---|---|---|---|---|
| Environment | Profile `Id` plus configured `ServerBaseUri`; Console-owned profile | Operator account/tenant and selected deployment | Local profile selection does not grant upstream access; authority changes invalidate stale state | `ConsoleEnvironmentProfile`, `AgentWorkspaceBff`, profile pages |
| Connection | `OperateConnectionSummary.Id`; admin connection | Source for tables/layers; a service can expose layers from multiple connections | Connection create/update/test remain existing operations; secret is never a rendered ID | `OperateTransitionModels`, `HonuaServerOperateTransitionDataSource` |
| Dataset | Existing canonical resource ID in `OperateResourceEditPreview`; physical schema/table is a source reference | Resource → publications; linked connection is not universal service ownership | File/remote import can write server data before publication; no new staging or atomic-cancel promise | `ResourcePublicationsTreeModel`, `DataToPublishFlow`, import operations |
| Unpublished table | Connection ID and returned schema/table reference; not automatically a registered metadata resource | Discovered through connection tables | Preserve import/publish result identities; no invented stable Dataset ID or automatic catalog registration | `OperateConnectionDetailPage`, `OperatePublishLayerPage` |
| Layer | `CanonicalResourceId`; admin `LayerId` is only unique within connection | One canonical resource can have exposures in several services | Canonical match wins; integer must resolve to exactly one canonical ID, otherwise choices; service membership not inferred from a display name | `OperateLayerLookup`, `OperateServiceLayerProjection` |
| Protocol exposure | Existing publication/protocol reference for the selected resource and service | Multiple endpoints may expose the same layer | Endpoint row is not an independent per-protocol layer record | `ResourcePublicationsTreeModel`, `PublishProtocolCatalog` |
| Service | `OperateServiceDetail.Name`; admin service settings | Contains layers, possibly from several connections | Go live replaces the service protocol set then separately updates service access; affects siblings; report partial failure; disabled layers stay disabled | `GoLivePublisher`, `GoLiveStep`, `OperateServiceDetailPage` |
| Layer presentation | Existing layer metadata identity | Popup-info / drawing-info belong to layer authoring | Preserve GET/PUT popup-info and drawing-info; choosing a catalog style does not bind it; no generic style-assignment endpoint assumed | `ServerOperateLayerStyleOverrideDataSource`, `DataToPublishFlow` |
| Content map | Catalog `MapId` / content record | Catalog and ShareAccess; saved-map package | Viewer and new-map hydration stay separate from Studio draft/version writes | `MapViewerPage`, `MapDraftPage` |
| Studio map | Studio draft ID, content-item ID and immutable version are distinct | Draft becomes saved version; publication is a separate decision | Keep save, reopen and separate human approval; no change to pinned Studio API or bundle | `StudioPage`, `studio-embed.mjs`, `studio-release.lock.json` |
| Published Studio map | Publication route → active immutable content version | Audience reads current source grants independently | Viewer does not open an editor or use configured admin/agent credentials; republishing may retain audience URL | Web `PublishedMapDocument`, publication reader/BFF |
| Proposal | Source tag + proposal `Id` | Target operation and exact evidence/revision | Inbox aggregates; source owns decisions; proposer cannot approve own work; Home is read-only | `ConsoleApprovalInboxClient`, `ConsoleProposalModel`, `ProposalApprovalPanel` |
| Deploy operation | Server operation ID | Target/environment/version are descriptive fields | Submit/rollback use the existing governed operation client and operator bearer; same version can have multiple attempts; no invented rollback window | `OperateDeployPage`, `OperateDeployOperationsList` |
| Metadata release | GitOps release package ID | Package diff, drift, PR and operation evidence | Not a server semantic version; open-by-ID remains valid even without list support | `OperateReleasesPage`, release client |
| Job / event / alert | Server-issued jobRunId / eventId / alertId respectively | Evidence may relate to a workflow or operation | No interchange of workflowRunId/jobId/eventId; links never synthesize jobs or accept worker-controlled navigation URLs | `OperateObservabilityPage`, `OperateEventsPage`, workspace execution modules |
| Finding | Deterministic server finding ID | Environment, rule, policy | Health breach is not a fabricated finding; proposing a fix retains governed gateway | `HttpConsoleOpsFindingsClient`, `OperateCopilotPage` |
| User / team | Existing server RBAC principal/group identity | Workspace membership and source grants | No new user/team route or frontend-invented permission; retain member/invite operations and server denial | `OperateAccessRolesPage`, `OperateAccessMembersPage` |
| Conversation / task / turn | Workspace, view, task/draft revision, pending-reply and turn IDs | Bound operator/environment and selected task | Changing route must not mutate pending reply/approval/turn target; file selections survive ordinary chrome navigation | `agent-workspace.mjs`, `workspace-executions.mjs`, `AgentWorkspaceBff` |

Source locations: [models](../../src/Honua.Console.Shell/Models/),
[services](../../src/Honua.Console.Shell/Services/),
[pages](../../src/Honua.Console.Shell/Pages/),
[workspace session BFF](../../src/Honua.Console.Web/AgentWorkspaceBff.cs),
[browser workspace](../../src/Honua.Console.Web/wwwroot/js/agent-workspace.mjs).
Names above identify existing projections, not new wire DTOs.

## Phase 1 local drawer contract

One persistent host and one browser controller own the drawer through Blazor
navigation and hydration; adapter commands handle open, close and focus rather
than independent Razor and JS state. Keep all current reply, progress, cancel,
file-input, external-agent handoff, follow-mode and pinned plan-review controls.
Opening or closing the drawer does not reconnect the workspace or clear a turn.

Outside Studio the drawer starts closed. Entering `/studio` or its `StudioPage`
aliases opens it; a manual close holds for that continuous Studio visit. Leaving
Studio closes it; re-entering opens it. Other `/studio/*` tools retain their own
route identity and do not accidentally trigger map-editor initialization.
On mobile it is modal: background inert, focus contained, Escape closes, focus
returns to the opener (or a surviving navigation control), and route changes close
it. On desktop it is nonmodal and main content remains usable without overlap.

Scope drawer preferences and recent links to the validated session's opaque
`principalId` and `environmentId`, with release/source-path authority invalidation
as already used by `agent-workspace.mjs`. Never store bearer/CSRF tokens or raw
operator identity in preference keys. If a validated partition is unavailable,
use ephemeral defaults rather than a shared `honua.shell` record. Clear active
cached results and reject stale async responses when authority changes. Recent
links describe actual visits, not invented server activity. Route selection is
explicitly mapped from current route families; it cannot silently retarget a
pending request or authorize a mutation.
