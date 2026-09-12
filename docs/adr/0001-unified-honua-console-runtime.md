# ADR-0001: Unified Honua Console Runtime

## Status

Accepted, amended 2026-05-23

## Date

2026-05-23

## Context

Honua has three related web surfaces:

- Admin/operator workflows for publishing, runtime configuration, identity, connectors, observability, and service operations.
- Studio workflows for AI-assisted query, analysis, map, dashboard, report, form, app, and workflow authoring and publishing.
- Portal/catalog/share workflows for finding, composing, publishing, embedding, and sharing spatial content.

Earlier planning separated `honua-portal` from `honua-server-admin` to prevent end-user content workflows from becoming another operator screen. That boundary was useful, but it now creates a different risk: Honua can appear fragmented before the user has even created a map.

That risk conflicts with a core Honua premise: spatial software should not inherit the deployment complexity and fragility associated with ArcGIS Enterprise-style stacks. A user should not need to reason about separate products, deployment artifacts, auth systems, UI conventions, or metadata models just to publish a service and turn it into a map, dashboard, report, form, app, or workflow.

Current implementation also has a framework split:

- `honua-portal` uses React/Vite and contains the recent Studio/app-builder work.
- `honua-server-admin` uses Blazor WebAssembly/MudBlazor and contains existing operator workflows.

Admin is expected to be redesigned around a new metadata contract and UI mockups. That makes this the right time to converge the product shell and deployment model instead of polishing two separate web products.

## Decision

Honua will ship a single top-level web product surface and deployment runtime: **Honua Console**.

Honua Console contains workflow areas, not separately deployed products:

- **Studio**: AI-assisted spatial query, analysis, map, dashboard, report, form, app, and workflow authoring and publishing.
- **Catalog**: data, layers, services, saved maps, dashboards, reports, forms, workflows, generated apps, metadata, and provenance.
- **Operate**: publishing, jobs, service configuration, identity, connectors, deployment health, observability, licensing, and runtime administration.
- **Share**: public links, embeds, open-data pages, exports, and external publishing flows.

Honua Studio must live in this same surface. It is the primary creation workspace inside Honua Console, not a detached portal app.

The product boundary remains role/workflow-based:

- End-user and builder workflows belong in Studio/Catalog/Share.
- Operator and control-plane workflows belong in Operate.

The deployment boundary changes:

- One URL/origin.
- One login/session model.
- One RBAC and entitlement model.
- One metadata/content model.
- One audit/provenance trail.
- One deployment artifact or one coordinated deployment unit.
- One upgrade path.

## UI Framework Direction

Net-new and redesigned Console surfaces should converge on a .NET-first UI architecture:

- **Blazor Web App** is the default web Console shell.
- Shared Razor components should live in a reusable component library that can be hosted by both the web Console and a native host.
- A **.NET MAUI Blazor Hybrid** host is an optional operator/power-user Console surface, not a replacement for the web Console.
- The native Console host should support multiple saved Honua environment profiles and optional per-environment mTLS/client-certificate configuration.
- JavaScript should be used as contained interop for specialized rendering engines where the ecosystem is clearly stronger: maps, 3D scenes, Vega-Lite/Vega chart rendering, Monaco-style editors, and other standards-based browser engines.

This keeps the long-term product direction aligned with Honua Server's .NET-owned API and contract model while preserving browser access and the strongest GIS/dashboard rendering engines.

The existing Blazor Admin can remain as a transitional legacy route while operator workflows are redesigned. Redesigned Admin/Operate workflows should move toward the shared Blazor Web/Razor component architecture rather than a separate MudBlazor-only center of gravity.

## Contract Direction

The non-negotiable unifier is shared .NET-owned contracts, not duplicated UI code.

Honua Server remains the authoritative contract owner for metadata, content, RBAC, publishing, jobs, observability, temporal data history, disconnected sync, GitOps, GP/ETL, and AI-generated artifact workflows.

Transport guidance:

- Browser Console uses HTTP/OpenAPI and SignalR/SSE for browser-compatible realtime flows.
- Native MAUI Console and internal services can use full gRPC, including streaming, for jobs, telemetry, logs, realtime events, GP/ETL execution, AI DevOps, and high-throughput data flows.
- Native MAUI Console can optionally use mutual TLS/client certificates per environment when a server requires stronger operator trust.
- gRPC-Web may be used selectively, but it is not the default browser contract because browser gRPC has streaming limitations.
- JavaScript SDK contracts remain important for generated apps, browser embeds, MCP/QGIS/browser integrations, and map/chart/editor runtimes. They should be generated or validated from the same server-owned contracts rather than becoming a separate source of truth.

The following contracts must be shared across Studio, Catalog, Operate, Share, MCP clients, QGIS plugin flows, and generated apps:

- Metadata v2 and content items.
- Content versions, publications, data bindings, and job runs.
- Service-to-item provenance.
- Workspace, Studio project, conversation, and provenance references.
- Query, analysis, map, dashboard, report, form, app, workflow, and publication package contracts.
- Vega-Lite chart specs embedded or referenced by dashboard/report/app packages.
- Build/spec/plan/apply contracts for AI-generated spatial outputs.
- Sharing, embed, and authorization contracts.
- Audit, lineage, and generated-output provenance.
- Jobs, telemetry, alerting, realtime, temporal data history, disconnected sync, and rollback contracts.
- Multi-environment Console connection profiles, transport capabilities, certificate trust state, and native mTLS policy.

The detailed Studio information model and package response expectations are maintained in [Honua Studio Information Model And Workflows](../architecture/studio-information-model-and-workflows.md).

UI implementation may transition over time. Contract divergence is not acceptable.

## Consequences

### Positive

- Honua presents a simpler product story: create, operate, publish, and share from one place.
- Deployment becomes a differentiator against complex multi-component GIS stacks.
- AI-generated maps, dashboards, reports, forms, apps, and workflows can move naturally from prompt to preview to saved content to published artifact.
- Metadata v2 becomes the shared information model instead of an Admin-only or Portal-only schema.
- The QGIS plugin, MCP clients, and Studio can target the same content/package contracts.
- Blazor Web and MAUI Blazor Hybrid can share Razor components, .NET clients, validation, auth helpers, and workflow models.
- Native operator workflows can use full gRPC streaming without forcing browser gRPC complexity onto the web Console.
- Native operator workflows can use per-environment mTLS without imposing client-certificate complexity on browser users.

### Negative

- The previous Portal/Admin separation needs to be reframed in backlog and docs.
- The current UI framework split remains during transition.
- Some existing Admin routes may need temporary embedding, redirecting, or reimplementation.
- A single shell raises the bar for IA, RBAC, feature flags, and route-level permission handling.
- Rich map/chart/editor components still require careful JavaScript interop boundaries.
- A native MAUI host adds release/signing/update work if it becomes a supported product surface.
- mTLS adds certificate lifecycle, trust-profile, revocation, and environment-mapping complexity that must be explicit in server and SDK contracts.

### Neutral

- `honua-console` is the target Console repo and should contain the Blazor Web shell plus shared Razor component architecture.
- A future MAUI host can live in `honua-console` or a dedicated companion repo if release engineering requires separation.
- Multi-environment connection profile metadata belongs in shared Console/.NET SDK contracts; server-side mTLS enforcement belongs in `honua-server`.
- `honua-portal` remains the short-term source repo for current Studio/Catalog/Share behavior until parity is accepted.
- `honua-server-admin` remains the short-term source repo for legacy operator surfaces.
- Physical monorepo consolidation is optional and should not block deployment/runtime consolidation.

## Implementation Guidance

1. Define the Honua Console IA and route map with `Studio`, `Catalog`, `Operate`, and `Share` as first-class areas. Filed as [docs/console-route-map.md](../console-route-map.md) ([honua-console#3](https://github.com/honua-io/honua-console/issues/3)).
2. Scaffold the Blazor Web Console shell and a shared Razor component library before porting major workflows.
3. Use Metadata v2 as the shared model consumed by both operator and builder workflows.
4. Generate or validate .NET and JavaScript client contracts from server-owned OpenAPI/JSON Schema/proto sources.
5. Use SignalR/SSE for browser realtime and full gRPC streaming for native/internal clients.
6. Model saved environment profiles for Console, including server URL, tenant/environment identity, transport capability, auth mode, and optional native mTLS trust state. In 2026.1, GA deployments are single-tenant; multi-tenancy is Preview/trial only for non-production evaluation, with no customer production data or GA, availability, performance, durability, SLA, or SLO commitment. Honua provides no SaaS or managed hosting. Tenant authorization and isolation remain mandatory at full severity.
7. Bundle current Studio/Portal and Admin outputs into one deployed runtime as an interim step.
8. Put both surfaces behind the same auth/session/RBAC path.
9. Hide or redirect duplicate builder/app-builder routes from legacy Admin.
10. Rebuild redesigned Admin workflows inside the unified Blazor shell as the new metadata contract and mockups land.
11. Add an optional MAUI Blazor Hybrid host once the shared Razor component library and .NET client contracts are stable.
12. Add end-to-end smoke for publish service -> catalog item -> Studio map/dashboard/app/generated artifact -> share/embed, with job-run evidence for workflow-backed artifacts. See [`docs/smoke/parity.md`](../smoke/parity.md) for the scenario, owning-layer taxonomy, and evidence contract owned by [honua-console#9](https://github.com/honua-io/honua-console/issues/9).

## Backlog Implications

Create or update backlog items for:

- Honua Console IA and route taxonomy.
- Blazor Web Console shell and shared Razor component library.
- .NET Console client contracts for server-owned metadata, content, jobs, telemetry, GitOps, temporal, sync, and publishing APIs.
- Optional MAUI Blazor Hybrid native Console host.
- Optional mTLS/client-certificate auth for native Console connections to one or more Honua Server environments.
- Single-runtime deployment bundle for Studio/Catalog/Share/Operate.
- Shared auth/session/RBAC wiring across all web areas.
- Metadata v2 adoption by both Admin and Studio.
- Legacy Admin route inventory: keep, embed temporarily, redirect, or rebuild.
- Studio productionization for AI spatial query, analysis, maps, dashboards, reports, forms, apps, workflows, and publications.
- Vega-Lite dashboard/report rendering as the standard chart specification layer.
- MCP/QGIS/Studio generated-output parity through shared package contracts.
- Cross-surface smoke and upgrade tests.

## Superseded Guidance

This ADR supersedes earlier wording that treated Portal as a separate deployed product from Admin.

Older docs may still use `portal` and `admin` as repo names or workflow shorthand. That is acceptable. They should not be interpreted as a requirement for separate product surfaces, separate deployment runtimes, separate auth models, separate metadata models, or separate design systems.
