# Local and Staging Startup

Decision source: [ADR-0001: Unified Honua Console Runtime](../adr/0001-unified-honua-console-runtime.md).
Build contract: [BUILD_ARTIFACT.md](BUILD_ARTIFACT.md).

## Prerequisites

- Node.js 20.x (matches `@types/node` baseline).
- npm 10.x or later.

## Local development

```
npm ci
npm run dev
```

`npm run dev` starts Vite on `127.0.0.1:5174`. The SPA is mounted at `/`, so
`/studio`, `/catalog`, `/operate`, and `/share` resolve through Vite's
client-side routing fallback during development.

Same-origin API expectations:

- API calls are relative paths (`/api/...`). For local dev, run `honua-server`
  on a port the dev proxy can reach (configure via a future Vite proxy or
  reverse-proxy in front of both processes). Console build itself does not
  bake in an API base URL.

Common scripts:

- `npm run typecheck` — TypeScript with `--noEmit`.
- `npm run lint` — Biome lint over `src` and `tests`.
- `npm test` — Vitest run.
- `npm run build` — Production build into `dist/`.
- `npm run preview` — Serve the built bundle on `127.0.0.1:4174` for a
  same-origin verification before promotion.

## Verifying the production bundle locally

```
npm run build
npm run preview
```

Then open `http://127.0.0.1:4174/` and confirm:

1. `/studio`, `/catalog`, `/operate`, and `/share` each render the area
   placeholder (or the area surface once the porting tickets land).
2. Direct navigation to any of those paths (and a reload) resolves through
   the SPA fallback. The preview server uses Vite's built-in fallback;
   production deployments must do the same (see [BUILD_ARTIFACT.md](BUILD_ARTIFACT.md)).
3. `http://127.0.0.1:4174/version.json` returns the build metadata block.

## Staging preview environments

Staging is owned by `honua-devops` (see honua-devops#55 / #56). Console only
needs to:

- Produce `dist/` and `dist/version.json` as documented in
  [BUILD_ARTIFACT.md](BUILD_ARTIFACT.md).
- Surface the build metadata so release-promotion tooling can attach
  Console artifact versions to release notes alongside legacy Portal/Admin
  deployment status.

For smoke evidence during staging promotion, exercise:

- Same-origin auth cookie set by `honua-server` survives a navigation
  between `/studio`, `/catalog`, `/operate`, and `/share`.
- `/version.json` is reachable on the deployed origin.
- A direct page load of `/operate/anything` returns the SPA (not a 404 from
  the static server).

## CI expectations

The release pipeline (`honua-devops#56`) should run:

1. `npm ci`
2. `npm run typecheck`
3. `npm run lint`
4. `npm test`
5. `npm run build`

Then upload `dist/` (including `dist/version.json`) as the Console artifact
for the single deployable bundle.
