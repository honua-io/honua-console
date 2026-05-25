# First-Release Strategy And Depth Cut-Line

Status: planning draft.

Decision source: [ADR-0001: Unified Honua Console Runtime](../adr/0001-unified-honua-console-runtime.md).

Companion artifacts:

- [Honua Console Migration Backlog](HONUA_CONSOLE_MIGRATION_BACKLOG.md)
- [Console UI Implementation Backlog](CONSOLE_UI_IMPLEMENTATION_BACKLOG.md)
- [Console Backend Capability Backlog](CONSOLE_BACKEND_CAPABILITY_BACKLOG.md)
- [Design Artifact Work Breakdown Matrix](DESIGN_ARTIFACT_WORK_BREAKDOWN.md)

## Purpose

The other backlogs answer *what the platform is* and *in what order it can be built*. This document answers *what shape of go-to-market we are*, *where the first release draws its line*, and *what cost discipline makes that buildable with agentic tooling*.

It replaces an earlier "MVP wedge" framing. A narrow feature wedge is the wrong frame for this market (see below); the real levers are **depth**, the **AI breadth mechanism**, and a **non-negotiable operate floor**.

## Strategic Frame: Consolidation, Not A Wedge

Every GIS org has the same job: deliver maps, apps, analysis, ETL, GP, forms, dashboards, and publishing — and operate it reliably. GIS is a horizontal capability inside the org, not a point need. ArcGIS won by being the broad platform.

Consequence: a narrow tool that does only one or two of those is not a beachhead — it is a **second system running alongside ArcGIS**, which *adds* operational surface. That directly contradicts the "easy to operate" thesis. Therefore:

- **Breadth is mandatory.** First release must cover the whole authoring surface to be credible as a replacement rather than an addition.
- **We consolidate the server, operations, and authoring layer**, not land-and-expand. The differentiator — one artifact, easy to operate — only delivers value when Honua absorbs the breadth itself, rather than being one more system to run.

### What We Replace vs. What We Keep

We do **not** offer a path completely off the Esri ecosystem, and that is deliberate — it collapses switching cost.

- **Replace:** the ArcGIS Server / Portal / Enterprise *operational* sprawl and the "million toolboxes" authoring complexity. The felt promise is *a glimpse of what life looks like without a million toolboxes* — express intent, do not hunt through tool catalogs.
- **Keep / interoperate:** Honua speaks **Esri REST** as a northbound interface, so existing Esri clients — including ArcGIS Pro — keep working against Honua with no forced client migration.
- **The distinction that matters:** *server-side breadth* must be ours (otherwise the org runs two servers — the "second system" failure), while the *client-side protocol* stays Esri-compatible (so users keep the tools they already know). Esri REST interop is not a second system; it is one broad server speaking the incumbent's protocol.

## Backend Readiness (honua-server, verified 2026-05-24)

This plan builds on a mature backend, not greenfield. `honua-server` is an OGC-CITE-certified (952/952) multi-protocol feature server that already speaks the GeoServices REST surface (incl. GPServer), OGC WMS/WFS/WCS/WMTS, STAC, OGC API, OData, and MVT, and already does FileGDB import and live Esri REST migration. Most heavy substrate is built; the open work is a tight wrapper layer plus Console UI.

- **Built (CLOSED):** metadata v2 + RBAC (`#1162`), Studio package lifecycle (`#1180`), geoprocessing/ETL execution substrate (`#360/#361/#681/#721/#724`), GitOps engine (`#351/#515/#992`, `#1163/#1164`), Operate observability + jobs (`#1168/#1170`), realtime + geofence (`#1169/#393/#339`), native mTLS (`#1171`).
- **True open critical path (Studio-facing wrappers over built capability):** validation/preview (`#1181`), query/analysis content + artifacts (`#1182`), publication registry (`#1183`), form package (`#1184`), GP/ETL package + node registry (`#1185`), capability manifest (`#1186`), release-operation lifecycle API (`#1165`). These wrap existing engines — not new engines.
- **Genuinely open depth:** temporal (`#1166`), disconnected sync (`#1167`), versioned editing (`#371`).
- **Known enterprise gap:** **audit is not fully baked** (`#504/#507` open). Observability event query (`#1168`) is built, but immutable audit trail / SIEM is enterprise-tier and capability-gated — *not* a first-release guarantee. Do not claim "every governed op has immutable audit evidence" until it lands.

Implication: the operate floor and breadth substrate are largely *done server-side*. First-release effort concentrates in (a) the open wrapper cluster above and (b) Console UI. Several "gated exotic" rows below are already server-built, so enabling them later is a Console-UI cost, not a backend build.

## The Cut-Line Is Depth, Not Breadth

Because breadth is mandatory, the first-release line is drawn on **how deep each capability goes**, not on which capabilities exist.

- Target depth: *sufficient to be the system of record for a new project or dataset*, end to end on Honua alone.
- Do **not** target ArcGIS Pro's accreted tool-count depth. We do not out-depth twenty years on day one.
- Be **sufficient across the whole surface**, **deep on one or two visible spikes** (maps + spatial analysis), and **thin-but-present** on the rest — leaning on AI + the capability registry rather than hand-built editors.

| Authoring family | First-release depth | Deferred depth (pull trigger) |
| --- | --- | --- |
| `map.package` | **Spike** — layers, style, popups, legend, basemap, extent, publish/embed | Advanced cartography, exhaustive renderer parity |
| `query.package` | **Spike** — NL → SQL/filter/spatial predicate, preview, save, reuse | Exotic join/CRS edge cases |
| `analysis.package` | **Spike (sufficient)** — common spatial methods, params, job, artifact, rerun | Full GP tool breadth, custom models |
| `dashboard.package` / `report.package` | **Sufficient** — Vega-Lite common charts, map/table panels, filters | Exotic visualization, deep narrative tooling |
| `form.package` | **Sufficient** — fields, domains, validation, basic offline | Complex offline/sync depth (gated, see floor) |
| `app.package` | **Sufficient** — assemble published items into pages/nav | Rich custom app logic |
| `workflow.package` (GP/ETL) | **Thin-but-present** — chain/schedule registry-covered ops via AI | Full node breadth, custom script/model tools |

## The Breadth Mechanism: AI Over A Shared Package Model + Registry

A small team can credibly cover ArcGIS breadth only because authoring runs through **one** generate → validate → preview → publish pipeline over a shared package model and a **capability registry**, instead of eight bespoke editors.

- This is the disruption mechanism *and* the answer to development cost: build one pipeline and one registry, not N editors.
- The capability registry is therefore load-bearing, not a "later, for breadth" item. For first release it need only cover the spike + sufficient ops above — not Pro's full catalog.
- De-risked by readiness: the execution substrate already exists (GPServer + geoprocessing/ETL engine, `honua-server#360/#681/#721/#724` closed). The registry *catalogs and exposes* built capability for NL planning; it does not build new engines.
- Felt promise: because intent resolves against the registry, the user expresses what they want instead of hunting a million toolboxes — the experiential core of the pitch.
- "Do not fork schemas across Console / MCP / QGIS / SDK" (per [Backend Capability Backlog](CONSOLE_BACKEND_CAPABILITY_BACKLOG.md)) is what keeps each new capability incremental rather than a new silo.

## The Operate Floor: GitOps As Invisible Plumbing

The one place we cannot go shallow. Absorbing the ArcGIS Server/operations sprawl (vs. adding to it) requires an operate layer a non-operator can actually run. The core of that floor is **GitOps used as plumbing the operator never sees**.

- **One-time bootstrap only:** the operator points Honua at a git repo. After that, every environment/metadata/service/layer/style/publish change is safe, reversible, and audited — with **zero Git exposure**. No branches, no PRs, no YAML in the operator's path.
- GitOps relocates operational complexity from *every operator on every change* into a reviewed, repeatable, reversible **process**. The process is the operator.
- The console surfaces only semantic intent: semantic resource diff, compatibility preflight (blast radius), review/approve, apply, **rollback** — per `honua-console#22` / UI-044. The git repo is the durable audit substrate beneath, not a workflow surface.
- **AI authors the proposal; a human approves; failure auto-rolls-back.** This is the real "operations for people who aren't operators" loop, and it is one governance pipeline applied to *all* change types — not bespoke "are you sure?" UX in eight places. Another one-pipeline-not-N win.

Non-negotiable operate-floor scope for first release:

| Floor capability | Owning work | Status |
| --- | --- | --- |
| Authoritative data, RBAC | `honua-server#1162` | server built |
| Connect / import existing data (incl. live Esri REST migration, FileGDB) | server import APIs; UI-030, UI-033 | server built; Console UI open |
| Publishing reliability (publication, slot, catalog registration, rollback) | `honua-server#1183`; UI-034, UI-035 | server wrapper + Console UI open |
| GitOps change-safety plumbing, single instance | engine `honua-server#351/#515/#992`, `#1163/#1164`; lifecycle API `#1165`; `honua-console#22` / UI-044 | engine built; `#1165` + Console UI open |
| Minimal Operate: jobs + events for what was published/changed | `honua-server#1168`, `#1170`; UI-040, UI-041, UI-043 | server built; Console UI open |
| One deployable artifact + preview/release pipeline | `honua-devops#55`, `#56` | devops |
| Esri REST northbound interop (sufficient, not exhaustive parity) | GeoServices REST surface (CITE-certified); Esri catalog endpoint (UI-036) | server built (certified) |
| Per-action audit evidence | `honua-server#504`, `#507` | **partial — enterprise gap** |

## Capability-Gated Exotic Depth

Designed broad in the contracts (cheap), not built out until a customer pulls. Absent from the capability manifest → renders as unsupported, lights up later with no re-architecture.

| Gated depth | Owning issue(s) | Pull trigger |
| --- | --- | --- |
| Cross-environment promotion (dev→staging→prod fleet) | `honua-devops#57`, `#58` | A genuine multi-environment account |
| Exhaustive GP/ETL node breadth, custom script/model tools | `honua-server#1185`, UI-027 | A pipeline-authoring account; do not chase Pro breadth pre-revenue |
| Temporal "git over data" | `honua-server#1166`, UI-045 | A data-steward account needing as-of/diff/rollback |
| Disconnected sync conflict review | `honua-server#1167`, UI-046 | A disconnected field-data account |
| Realtime / geofence alerting | `honua-server#1169`, `#393`, `#339`, UI-042 | A monitoring/IoT use case |
| Native MAUI host + mTLS | `honua-server#1171`, UI-050 | An org requiring client-cert trust |
| Full SIEM / investigations / AI DevOps advisory | `honua-devops#59`, UI-047 | After Operate has real event volume to summarize |

Note: several of these are *already implemented server-side* — realtime/geofence (`#1169/#393/#339`), mTLS (`#1171`), and the GP/ETL execution engine under `#1185`. Gating them is a Console-UI scope choice, cheap to enable later — not a backend build. Temporal (`#1166`), sync (`#1167`), versioned editing (`#371`), and cross-environment promotion depth are the genuinely open backend items.

## AI Development-Cost Posture

Dominant dev-AI costs are **rework** (regenerating fan-out when a shared contract changes) and **building unpulled depth**. Controls, by leverage:

1. **Hold the depth cut-line.** The cheapest token is the one not spent on deferred depth.
2. **Build against the stable contracts; freeze only the open ones before fan-out.** `#1162` (metadata/RBAC) and `#1180` (package lifecycle) are already shipped — build against them, do not reinvent. The freeze discipline applies to the *open* wrapper cluster (`#1181`–`#1186`, `#1165`): lock the package envelope, capability-manifest shape, content/version/publication records, and capability-registry entry schema before Console + SDK fan out. Changing these late regenerates everything downstream — the dominant rework cost.
3. **Mock-first, but mock == final shape.** Checked-in mocks let agents build UI in parallel; the payoff is real only when the mock matches the frozen contract.
4. **Eval + CI gates to stop churn loops.** A golden NL → `query.package`/`map.package`/`analysis.package` set, plus the `honua-console#9` parity smoke. Silent agent churn is pure burn.
5. **Build reusable primitives once** (Razor library UI-002, shared SDK projections).
6. **One governance pipeline, not N** (package authoring; GitOps change-safety) — the same architectural leverage that makes breadth and easy-ops affordable.

## First-Release Exit Criteria

Ship when a real org can run a real *new* project end to end on Honua alone, from one deployable artifact and one origin:

- Connect or import existing data (including migrating an ArcGIS remote service) without leaving the console.
- Author across the whole surface — maps and query at spike depth, analysis/dashboard/report/form/app/workflow at least sufficient — as inspectable, governed packages with validation and provenance shown before publish.
- Publish and share/embed through the unified runtime, with content/version/publication/job/event records linked by stable IDs (immutable audit/SIEM is enterprise-tier — see Backend Readiness).
- Make a production change safely: propose → preflight → approve → apply → roll back, **without ever touching Git**, after a one-time repo bootstrap.
- Existing Esri REST clients (including ArcGIS Pro) can consume and publish to Honua services through the Esri-compatible interface — no forced client migration.
- System-of-record reliability holds: server-authored RBAC/entitlement gates every route and action.
- Gated exotic depth renders as unsupported via the capability manifest, with no dead UI.
