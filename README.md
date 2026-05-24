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
- [GitOps Metadata Publishing Information Model](docs/architecture/gitops-metadata-publishing-information-model.md)
- [GitOps Metadata Publishing Visualization Design](docs/architecture/gitops-metadata-publishing-visualization-design.md)
- [Temporal Data Viewer Information Model](docs/architecture/temporal-data-viewer-information-model.md)
- [Operate Observability Information Model](docs/architecture/operate-observability-information-model.md)
- [Honua Console Design Handoff](docs/design-handoff/README.md)
- [Content Item, Catalog, Share, Embed, And Open Data Contracts](docs/contracts/content-item-v1.md)

## Current Status

This repo is the target home for porting current `honua-portal` logic and converging the long-term web surface.

[honua-console#4](https://github.com/honua-io/honua-console/issues/4) ports Catalog browse + item detail, the MapLibre map viewer, saved maps, sharing + embed, and the public open-data surface from `honua-portal` into Console. The Console worktree now contains the React/TypeScript shell, route IA (Home, Catalog, Share groups), `AppShell`/`EmptyState`/`LoadingShell` primitives, `FixtureCatalogClient` / `FixtureSavedMapClient` / `FixtureShareClient`, the lazy-loaded maps route that contains the MapLibre viewer, the no-shell `/share/embed/maps/:id` embed route, and the anonymous `/share/public` open-data pages. Legacy Portal URLs (`/maps/...`, `/embed/maps/...`, `/public/...`) keep working as compatibility aliases at the router level so saved links and embed snippets in the wild do not break.

For this port, service, layer, and map catalog actions open the viewer through `/maps/new?from=:itemId`. Saved-map workspace cards open the saved map's self URL, currently `/maps/:id`. SharePanel copy links use `/catalog/:slugOrId` and append `?share=...` for public-link items; the lower-level snippet helper still emits `/maps/:id?token=...` for map public links. Embed snippets intentionally emit `/embed/maps/:id` for compatibility, while the Console IA route `/share/embed/maps/:id` serves the same no-shell viewer. Embed URLs preserve `#embedToken=...` fragments, parse chrome / legend / zoom / extent defensively, and resolve fixture token redemption, descriptor scope, and root authorization before MapLibre mounts. Production share/embed token mint, server-backed verification, and closure enforcement remain in the SDK/server follow-up.

Still outside `honua-console#4`:

- Studio app-builder + generated-app preview — `honua-console#5`.
- Operate / Admin transitional surface — `honua-console#6`.
- Server-owned metadata/RBAC wiring — `honua-server#1162` via `honua-console#7`.
- Production share/embed token mint, redeem, and closure enforcement — `honua-server#1162` / `honua-sdk-js#225`.
- Single deployable artifact — `honua-devops#55/#56` via `honua-console#8`.
- Cross-surface parity smoke (publish → catalog → Studio → share/embed) — `honua-console#9`.
- Retiring `honua-portal` — `honua-console#10`.

Active clients are fixture-backed until `honua-sdk-js#225` and `honua-server#1162` ship the network surfaces. The fixture-to-SDK swap is a mechanical follow-up Console ticket, not part of `#4`.

## Develop

```bash
npm install
npm run typecheck
npm test          # vitest
npm run lint      # biome
npm run dev       # vite dev server on http://127.0.0.1:5173
npm run build     # tsc + vite build
npm run smoke     # playwright (smoke:install once first)
```

The dev/preview server reads `VITE_*` env vars; copy `.env.example` to `.env.local` to override. With `VITE_SESSION_DRIVER=fixture` (the default) `/auth/signin` exposes the member / operator / owner presets used by the smoke tests.

Source repos that fed this port:

- `honua-portal` — current Portal, Catalog, Share, and Studio proof work.
- `honua-server-admin` — current legacy Admin and operator workflows.
- `honua-sdk-js` — browser-safe SDK contracts and generated app runtime.
- `honua-server` — server-owned metadata, content, RBAC, provenance, and package APIs.
- `honua-devops` — the single deployable artifact and release pipeline.
