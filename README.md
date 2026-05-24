# Honua Console

Honua Console is the unified web surface for Honua.

It brings Studio, Catalog, Operate, and Share into one product surface and one deployment runtime:

- **Studio**: AI-assisted spatial query, analysis, map, dashboard, report, and app creation.
- **Catalog**: data, layers, services, saved maps, dashboards, reports, generated apps, metadata, and provenance.
- **Operate**: publishing, jobs, service configuration, identity, connectors, deployment health, observability, licensing, and runtime administration.
- **Share**: public links, embeds, open-data pages, exports, and external publishing flows.

## Decision Source

- [ADR-0001: Unified Honua Console Runtime](docs/adr/0001-unified-honua-console-runtime.md)
- [Honua Console Migration Backlog](docs/roadmap/HONUA_CONSOLE_MIGRATION_BACKLOG.md)
- [Honua Studio Information Model And Workflows](docs/architecture/studio-information-model-and-workflows.md)
- [Studio Publishing Contract And Usage](docs/architecture/studio-publishing-contract-and-usage.md)
- [GitOps Metadata Publishing Information Model](docs/architecture/gitops-metadata-publishing-information-model.md)
- [GitOps Metadata Publishing Visualization Design](docs/architecture/gitops-metadata-publishing-visualization-design.md)
- [Temporal Data Viewer Information Model](docs/architecture/temporal-data-viewer-information-model.md)
- [Operate Observability Information Model](docs/architecture/operate-observability-information-model.md)
- [Honua Console Design Handoff](docs/design-handoff/README.md)

## Current Status

This repo is the target home for porting current `honua-portal` logic and converging the long-term web surface. The first implementation issue is [honua-console#2](https://github.com/honua-io/honua-console/issues/2), which scaffolds the Blazor Web Console shell and shared Razor component library.

The Studio publishing scaffold for [honua-console#16](https://github.com/honua-io/honua-console/issues/16) is implemented with fixture-backed Console routes for map, dashboard, report, and generated-app publish review. It proves the builder flow from Studio draft or generated preview to a versioned Catalog item, canonical `/catalog/:itemId` route, type-specific preview route, Share/embed route, and Edit in Studio reopen route while the server and SDK publish contracts are still landing. See the [Studio Publishing Contract And Usage](docs/architecture/studio-publishing-contract-and-usage.md) note for the current response shape, route contract, share/embed behavior, telemetry events, and deferred production API work.

Until parity is accepted, source behavior remains in:

- `honua-portal` for current Portal, Catalog, Share, and Studio proof work.
- `honua-server-admin` for current legacy Admin and operator workflows.
- `honua-sdk-dotnet` for Console .NET clients, native gRPC paths, and shared contract packages.
- `honua-sdk-js` for browser-safe SDK contracts and generated app runtime.
- `honua-server` for server-owned metadata, content, RBAC, provenance, and package APIs.
- `honua-devops` for the single deployable artifact and release pipeline.
