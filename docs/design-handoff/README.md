# Honua Console Design Handoff

Status: handoff draft.

Audience: product design, UX research, frontend architecture, and implementation leads.

## Purpose

This package summarizes the information model and workflows needed to design Honua Console across the pieces discussed so far:

- AI GIS Studio: natural language to spatial query, analysis, maps, dashboards, reports, forms, apps, GP services, and ETL workflows.
- GitOps metadata publishing across environments.
- Temporal data history, disconnected sync conflict review, and governed rollback.
- Operate observability: servers, telemetry, logs, events, alerts, realtime/geofence rules, jobs, and investigations.
- Optional native Console host for multi-environment operations, native gRPC streaming, and mTLS/client-certificate trust.

The goal is to give design enough structure to create flows, screen maps, and interaction models without making UI layout decisions here.

## Handoff Files

- [Information Model Summary](information-model-summary.md)
- [Workflow Catalog](workflow-catalog.md)
- [UI Surface Briefs](ui-surface-briefs.md)
- [Console Canvas Handoff](console-canvas/README.md)
- [Console Canvas Model Decisions](console-canvas/decisions.md)

## Source Architecture

- [ADR-0001: Unified Honua Console Runtime](../adr/0001-unified-honua-console-runtime.md)
- [Honua Studio Information Model And Workflows](../architecture/studio-information-model-and-workflows.md)
- [GitOps Metadata Publishing Information Model](../architecture/gitops-metadata-publishing-information-model.md)
- [GitOps Metadata Publishing Visualization Design](../architecture/gitops-metadata-publishing-visualization-design.md)
- [Temporal Data Viewer Information Model](../architecture/temporal-data-viewer-information-model.md)
- [Operate Observability Information Model](../architecture/operate-observability-information-model.md)
- [Honua Console Migration Backlog](../roadmap/HONUA_CONSOLE_MIGRATION_BACKLOG.md)
- [Console Backend Capability Backlog](../roadmap/CONSOLE_BACKEND_CAPABILITY_BACKLOG.md)
- [Console UI Implementation Backlog](../roadmap/CONSOLE_UI_IMPLEMENTATION_BACKLOG.md)
- [Design Artifact Work Breakdown Matrix](../roadmap/DESIGN_ARTIFACT_WORK_BREAKDOWN.md)

## Design Direction

Honua Console is one product surface with four workflow areas:

- `Studio`: create spatial outputs with AI and editors.
- `Catalog`: find, inspect, version, and reuse content.
- `Operate`: administer, publish, monitor, troubleshoot, and govern.
- `Share`: publish public links, embeds, open data, and exports.

The default web Console is Blazor Web with shared Razor components. An optional MAUI Blazor Hybrid host can reuse those components for native operator workflows. JavaScript interop is reserved for specialized engines such as maps, 3D, Vega-Lite/Vega, and editors.

## Design Principles

- Keep the product surface simple even when the backend is powerful.
- Show semantic resources and workflow state before raw manifests, logs, or protocol details.
- Make AI output inspectable, editable, and publishable rather than magical.
- Treat publishing, rollback, conflict resolution, and alert actions as explicit governed operations.
- Prefer dense operational surfaces for repeated work; avoid marketing-style layouts inside Console.
- Always show what is source-of-truth: package, version, environment, job, release, event, or server state.
- Preserve evidence links for every AI explanation, release action, rollback, conflict resolution, and alert decision.

## Primary Personas

- GIS analyst: creates queries, maps, dashboards, reports, forms, and analysis outputs.
- GIS developer: builds apps, workflows, GP services, ETL pipelines, and integrations.
- Data steward: reviews metadata, lineage, versions, temporal history, and compatibility.
- Operator/admin: manages environments, services, alerts, jobs, releases, auth, and observability.
- Field operations lead: manages forms, offline replicas, sync conflicts, and field data quality.
- Executive/reviewer: consumes dashboards, reports, published apps, and release evidence.

## First Design Deliverables Needed

1. Console IA and navigation model for `Studio`, `Catalog`, `Operate`, and `Share`.
2. Studio creation flow for prompt -> clarify -> spec/package -> preview -> edit -> publish.
3. Publishing review flow for maps/dashboards/reports/apps/forms/workflows.
4. GitOps release flow for proposal -> preflight -> PR -> deploy -> monitor -> rollback.
5. Operate observability workspace for servers, event viewer, alerts, jobs, logs, and investigations.
6. Temporal data viewer and sync conflict review surface.
7. Native Console environment switcher and trust/profile state.
