# ADR-0001: Unified Honua Console Runtime

## Status

Accepted

## Date

2026-05-23

## Context

Honua has three related web surfaces:

- Admin/operator workflows for publishing, runtime configuration, identity, connectors, observability, and service operations.
- Studio workflows for AI-assisted map, dashboard, report, and app creation.
- Portal/catalog/share workflows for finding, composing, publishing, embedding, and sharing spatial content.

Earlier planning separated `honua-portal` from `honua-server-admin` to prevent end-user content workflows from becoming another operator screen. That boundary was useful, but it now creates a different risk: Honua can appear fragmented before the user has even created a map.

That risk conflicts with a core Honua premise: spatial software should not inherit the deployment complexity and fragility associated with ArcGIS Enterprise-style stacks. A user should not need to reason about separate products, deployment artifacts, auth systems, UI conventions, or metadata models just to publish a service and turn it into a map, dashboard, report, or app.

Current implementation also has a framework split:

- `honua-portal` uses React/Vite and contains the recent Studio/app-builder work.
- `honua-server-admin` uses Blazor WebAssembly/MudBlazor and contains existing operator workflows.

Admin is expected to be redesigned around a new metadata contract and UI mockups. That makes this the right time to converge the product shell and deployment model instead of polishing two separate web products.

## Decision

Honua will ship a single top-level web product surface and deployment runtime: **Honua Console**.

Honua Console contains workflow areas, not separately deployed products:

- **Studio**: AI-assisted spatial query, analysis, map, dashboard, report, and app creation.
- **Catalog**: data, layers, services, saved maps, dashboards, reports, generated apps, metadata, and provenance.
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

Net-new and redesigned web surfaces should converge on one frontend shell.

Because Studio already depends on rich browser-native interaction patterns such as MapLibre maps, Vega-Lite charts, linked dashboard state, inspectors, drag/drop, live previews, and AI tool integration, the preferred long-term shell is React/TypeScript.

The existing Blazor Admin can remain as a transitional legacy route while operator workflows are redesigned. We should not invest in making the current Blazor/MudBlazor Admin the long-term center of gravity unless that decision is explicitly revisited.

## Contract Direction

The non-negotiable unifier is shared contracts, not duplicated UI code.

The following contracts must be shared across Studio, Catalog, Operate, Share, MCP clients, QGIS plugin flows, and generated apps:

- Metadata v2 and content items.
- Service-to-item provenance.
- Saved map and map package contracts.
- Dashboard/report/app package contracts.
- Vega-Lite chart specs embedded or referenced by dashboard/report/app packages.
- Build/spec/plan/apply contracts for AI-generated spatial outputs.
- Sharing, embed, and authorization contracts.
- Audit, lineage, and generated-output provenance.

UI implementation may transition over time. Contract divergence is not acceptable.

## Consequences

### Positive

- Honua presents a simpler product story: create, operate, publish, and share from one place.
- Deployment becomes a differentiator against complex multi-component GIS stacks.
- AI-generated maps, dashboards, reports, and apps can move naturally from prompt to preview to saved content to published artifact.
- Metadata v2 becomes the shared information model instead of an Admin-only or Portal-only schema.
- The QGIS plugin, MCP clients, and Studio can target the same content/package contracts.

### Negative

- The previous Portal/Admin separation needs to be reframed in backlog and docs.
- The current UI framework split remains during transition.
- Some existing Admin routes may need temporary embedding, redirecting, or reimplementation.
- A single shell raises the bar for IA, RBAC, feature flags, and route-level permission handling.

### Neutral

- `honua-console` is the target web shell repo.
- `honua-portal` remains the short-term source repo for current Studio/Catalog/Share behavior until parity is accepted.
- `honua-server-admin` remains the short-term source repo for legacy operator surfaces.
- Physical monorepo consolidation is optional and should not block deployment/runtime consolidation.

## Implementation Guidance

1. Define the Honua Console IA and route map with `Studio`, `Catalog`, `Operate`, and `Share` as first-class areas.
2. Use Metadata v2 as the shared model consumed by both operator and builder workflows.
3. Bundle current Studio/Portal and Admin outputs into one deployed runtime as an interim step.
4. Put both surfaces behind the same auth/session/RBAC path.
5. Hide or redirect duplicate builder/app-builder routes from legacy Admin.
6. Rebuild redesigned Admin workflows inside the unified shell as the new metadata contract and mockups land.
7. Add end-to-end smoke for publish service -> catalog item -> Studio map/dashboard/app -> share/embed.

## Backlog Implications

Create or update backlog items for:

- Honua Console IA and route taxonomy.
- Single-runtime deployment bundle for Studio/Catalog/Share/Operate.
- Shared auth/session/RBAC wiring across all web areas.
- Metadata v2 adoption by both Admin and Studio.
- Legacy Admin route inventory: keep, embed temporarily, redirect, or rebuild.
- Studio productionization for AI spatial query, analysis, maps, dashboards, reports, and apps.
- Vega-Lite dashboard/report rendering as the standard chart specification layer.
- MCP/QGIS/Studio generated-output parity through shared package contracts.
- Cross-surface smoke and upgrade tests.

## Superseded Guidance

This ADR supersedes earlier wording that treated Portal as a separate deployed product from Admin.

Older docs may still use `portal` and `admin` as repo names or workflow shorthand. That is acceptable. They should not be interpreted as a requirement for separate product surfaces, separate deployment runtimes, separate auth models, separate metadata models, or separate design systems.

