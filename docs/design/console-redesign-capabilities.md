# Console redesign: phase 0–1 endpoints, capabilities and owners

Source baseline: `origin/trunk` at `0ea1f168757e12f167d9220ca41022d928c642fc`.
The [route table](console-redesign-route-compatibility.md) lists every advertised
entry. This matrix groups their existing transports; it does not assert that a
particular deployment supports every endpoint. Keep missing-binding, forbidden,
unsupported and unavailable distinct. No row authorizes server or Studio work.
See [identity scope](console-redesign-identities.md) and
[journey acceptance](console-redesign-acceptance.md).

| Surface / capability | Existing read or transport | Availability / credential boundary | Data owner → Console implementation owner | Phase 0–1 contract |
|---|---|---|---|---|
| Home connection / setup / environment switch | `IConsoleEnvironmentProfileStore`; `/setup/local`, `/environments*` | Local profile and authenticated account state; profile count alone is not proof of a working binding | Console → profile store/pages, `ConsoleHomePage` | Preserve first-run; connected layout requires active binding; stale profile shows recovery |
| Home approvals / inbox | `GET /api/v1/admin/proposals`; source aggregator | `IConsoleApprovalInboxClient` returns per-source result, partial reads and actual pending counts | Server / optional DevOps source → `ConsoleApprovalInboxClient`, `ServerConsoleProposalSource` | Promote existing inbox; partial source cannot become “nothing needs you”; Home does not decide |
| Home health / Health | `GET /api/v1/admin/observability/ops-health` | `IOpsHealthDataSource` / `IConsoleOpsHealthClient` result; existing active-account transport | Server → `OpsHealthDataSource`, `HttpConsoleOpsHealthClient`, `OpsSummaryStrip` | Reuse snapshot and actual breach count; source timestamp/freshness; no invented healthy state |
| Home findings / Findings | `GET /api/v1/admin/observability/findings` | Existing findings read result | Server → `HttpConsoleOpsFindingsClient`, `OpsSummaryStrip` | Link real findings; health metrics do not synthesize findings |
| Home recent links | Actual successful route visits, local UI state | Partition by validated operator/environment; no credential persistence | Console → shell controller | Empty for new partition; bounded list of supported links; no fabricated operations history |
| Data / Connections / Services / Layers / Settings | Existing admin connection/service/settings reads through `IOperateTransitionDataSource` | Configured server and existing per-surface result; reuse shared admin pipeline | Server → `HonuaServerOperateTransitionDataSource` and existing pages | Retain working targets and current permissions; presentation adds no HTTP stack |
| Data preview | Console map proxy bound to selected publication/environment | Current operator/server source grants | Server → `LayerPreviewPane`, `LayerMapPreview` | Preserve served-feature rendering; do not replace with generic schematic preview |
| Add data / publish | Existing import, publishing and service-configuration operations | Human identity, current task/source and API result | Server → `DataToPublishFlow`, import pages, `GoLivePublisher` | No new publishing UX or endpoint; preserve service-wide side effects and durable receipts |
| Studio / Processes | `/api/agent-workspace/session`, `/api/studio/{**path}`, `/api/processes/{**path}` | `Studio:EmbedJsStudio`; active profile/bearer, antiforgery for mutations, pinned transport | Server and pinned Studio → Web BFFs, `studio-embed.mjs`, Processes embed | Existing release remains v0.1.1; chrome adapter only; no Studio change or lock update |
| Conversation | `/api/agent-workspace/{**path}` and current GA/process adapters | Validated session partition; exact request/task binding; no shared-key human approval | Server → `AgentWorkspaceBff`, `agent-workspace.mjs` | One session/host lifetime; pending work survives ordinary navigation without retargeting |
| Catalog / Share / content-map viewer | `/api/v1/console/content` and current content/share/package clients | Console `persistent-catalog` maps to server `console.content.persistent`; product mode and read grants still apply | Server → `ManifestBackedConsoleCapabilityManifest`, `CatalogAvailabilityGate`, catalog/share/viewer pages | Separate capability-gated surface; unavailable content cannot become foundational Home/map inventory |
| Published Studio viewer | `/api/v1/studio/published/{route}` via audience reader and source BFF | Current audience identity; anonymous means unauthenticated upstream; never admin/agent fallback | Server / Studio renderer → `PublishedMapDocument` and Web audience transport | Keep independent public viewer and source authorization |
| Activity / events / alerts / jobs / GP / Metrics | Existing `IConsoleOperateObservabilityClient`, job detail and metrics clients | Per-section API read results; actual server-issued IDs | Server → observability/GP/metrics pages | Retain detail links, polling and evidence; no new global Jobs or dashboard route |
| Inbox decisions / Deploy | Proposal approve/reject and deploy operation read/submit/rollback APIs | Existing human bearer, approval grants and separate approver checks | Server → proposal panel, deploy clients/pages | No Home mutation; immutable operation target; preserve preflight, readback, rejection and retry |
| Metadata releases | Existing GitOps release package / operation clients | Open by package ID; no server list-all assumption; promotion additionally gates on `gitops.release-manifest` | Server → `OperateReleasesPage`, deploy panels | Do not relabel metadata package as server version or invent rollback guarantees |
| Access / members | Current Console RBAC metadata/action clients | Server read grants and mutation facets; valid viewer denial is not expired login | Server → role/member pages and existing client | Existing destinations; no new invite/team APIs |
| Support | Existing support client | Explicit unsupported-binding state if no support service | Support → `SupportPage` | Keep help reachable without pretending a binding exists |
| Capabilities | `ICapabilityRegistryClient` through current server registry | Only supported AND available descriptors, intersected with local policy; failed refresh revokes prior availability | Server → `ManifestBackedConsoleCapabilityManifest` | Fail closed on environment switch; no frontend invented capability grants |
| Browser sign-in | Existing `/auth/login`, `/auth/signin` and configured callback | Safe return URL; HTML negotiation for browsers, JSON preserved for API clients | Console auth → `ConsoleAuthentication`, `SignInRequiredDocument` | Existing HTML fix retained; no duplicate auth flow |

Evidence roots: [services](../../src/Honua.Console.Shell/Services/),
[capability mappings](../../src/Honua.Console.Shell/Services/IConsoleCapabilityManifest.cs),
[Home](../../src/Honua.Console.Shell/Pages/ConsoleHomePage.razor),
[summary strip](../../src/Honua.Console.Shell/Components/OpsSummaryStrip.razor),
[workspace BFF](../../src/Honua.Console.Web/AgentWorkspaceBff.cs),
[Studio lock](../../scripts/studio-release.lock.json).

## Explicit limits

The current inbox aggregates source-tagged proposals, not an invented deploy
list. `DevOpsConsoleProposalSource` can refuse a missing bridge and the aggregator
can return partial results. Home must show that annotation and may not claim a
complete work queue when a supplementary source cannot be read.

Connected Home promotes the current approvals, health, findings and working
entry links, then adds partitioned recent visits. Omit mockup-only serving counts,
upgrade availability, invitations, charts and jobs totals unless an existing
projection supplies the exact fact and its time window. Search, global context,
new object routes, catalog auto-registration, style assignment and server changes
are outside phases 0–1. If an implementation row requires a new server endpoint,
projection, grant or Studio API, record `NEEDS-MAIN` and stop the lane rather than
substituting seeded data or changing another repository.
