# Honua Console Migration Backlog

Status: filed 2026-05-23.

Decision source: [ADR-0001: Unified Honua Console Runtime](../adr/0001-unified-honua-console-runtime.md).

## Objective

Port current `honua-portal` logic into `honua-console` and consolidate Studio, Catalog, Operate, and Share into one deployable Honua runtime.

This backlog intentionally separates three decisions:

- Product surface: one top-level Honua Console.
- Deployment runtime: one URL/origin, session model, RBAC model, metadata/content model, audit trail, and upgrade path.
- Repository layout: `honua-console` is the new web shell repo; `honua-portal` retires only after parity.

## Console Backlog

### Coordinating Epic

- [honua-console#1](https://github.com/honua-io/honua-console/issues/1): Epic: Honua Console migration and one deployable artifact.

### P0 Blockers

- [honua-console#2](https://github.com/honua-io/honua-console/issues/2): Scaffold React TypeScript Console shell.
- [honua-console#3](https://github.com/honua-io/honua-console/issues/3): Define Console IA route map RBAC and navigation.
- [honua-console#6](https://github.com/honua-io/honua-console/issues/6): Integrate legacy Admin as transitional Operate surface.

### P1 Migration And Runtime Work

- [honua-console#4](https://github.com/honua-io/honua-console/issues/4): Port Catalog Viewer Saved Maps Share Embed and Open Data from `honua-portal`.
- [honua-console#5](https://github.com/honua-io/honua-console/issues/5): Port Honua Studio app-builder and generated-app lifecycle.
- [honua-console#7](https://github.com/honua-io/honua-console/issues/7): Wire Console to shared metadata content package and RBAC contracts.
- [honua-console#8](https://github.com/honua-io/honua-console/issues/8): Integrate Console with single deployable artifact and preview pipeline.
- [honua-console#9](https://github.com/honua-io/honua-console/issues/9): Console parity smoke: publish service to catalog to Studio artifact to share embed.

### Cleanup

- [honua-console#10](https://github.com/honua-io/honua-console/issues/10): Freeze and retire `honua-portal` after Console parity.

## External Owner Dependencies

- [honua-server#1162](https://github.com/honua-io/honua-server/issues/1162): Console metadata v2 content and RBAC API baseline.
- [honua-sdk-js#225](https://github.com/honua-io/honua-sdk-js/issues/225): Console SDK contracts for metadata content packages dashboards reports and apps.
- [honua-sdk-js#226](https://github.com/honua-io/honua-sdk-js/issues/226): MCP QGIS and Studio parity for generated map dashboard report and app packages.
- [honua-server-admin#96](https://github.com/honua-io/honua-server-admin/issues/96): Prepare legacy Admin for Honua Console Operate transition.
- [honua-devops#55](https://github.com/honua-io/honua-devops/issues/55): Build one deployable artifact for Honua Console server and legacy Admin transition.
- [honua-devops#56](https://github.com/honua-io/honua-devops/issues/56): Add Honua Console CI release promotion and preview environment pipeline.

## Order Of Operations

1. Finalize Console IA/RBAC and scaffold the React/TypeScript Console shell.
2. Land the server Metadata v2/content/RBAC baseline and SDK projections.
3. Port Catalog, Viewer, Share, and Open Data from Portal into Console.
4. Port Studio app-builder and generated-app lifecycle into Console.
5. Make legacy Admin available as a transitional Operate surface and hide duplicate builder routes.
6. Build the single deployable artifact and preview/release pipeline.
7. Pass the cross-surface smoke: publish service -> catalog item -> Studio generated artifact -> share/embed.
8. Freeze and retire old Portal deployment paths.

## Parity Gate

Do not retire `honua-portal` or separate Admin deployment paths until:

- Console can run the current catalog item -> viewer -> saved map -> share/embed path.
- Console can run the Studio prompt -> clarification -> spec/plan -> apply -> preview -> edit -> publish/reopen proof path.
- The single deployable artifact serves `/studio`, `/catalog`, `/share`, and `/operate` from one origin.
- Server-authored RBAC/entitlement checks gate route and item actions.
- Metadata v2/content item/provenance data is shared across operator and builder workflows.
- Cross-surface smoke evidence is captured in CI or release promotion.

