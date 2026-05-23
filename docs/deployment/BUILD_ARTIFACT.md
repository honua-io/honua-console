# Honua Console Build Artifact Contract

This is the contract between `honua-console` and the single deployable artifact
produced by `honua-devops` (see [honua-devops#55](https://github.com/honua-io/honua-devops/issues/55)
and [honua-devops#56](https://github.com/honua-io/honua-devops/issues/56)).

Decision source: [ADR-0001: Unified Honua Console Runtime](../adr/0001-unified-honua-console-runtime.md).

## Producing the artifact

```
npm ci
npm run build
```

`npm run build` runs `tsc --noEmit` then `vite build`. Output goes to `dist/`.
Devops MUST be able to invoke these two commands and pick up `dist/` with no
additional manual steps.

Environment variables consumed at build time:

- `HONUA_CONSOLE_BASE_PATH` — Vite `base` for the bundle. Defaults to `/` so
  the same-origin route map is `/studio`, `/catalog`, `/operate`, and `/share`.
  Set to `/console/` (or similar) only if a deployment topology mounts Console
  under a subpath.
- `HONUA_CONSOLE_COMMIT_SHA` — Override the git SHA stamped into the bundle.
  Useful in CI where the checkout may be detached.
- `HONUA_CONSOLE_REF` — Override the git ref (branch/tag) recorded in metadata.
- `HONUA_CONSOLE_LEGACY_PORTAL_STATUS` — One of `active`, `retiring`, `retired`.
  Defaults to `active` until `honua-portal` is frozen.
- `HONUA_CONSOLE_LEGACY_ADMIN_STATUS` — One of `active`, `retiring`, `retired`.
  Defaults to `active` until `honua-server-admin` legacy routes are retired.

## Artifact layout

```
dist/
  index.html            # SPA entrypoint
  assets/               # Hashed JS, CSS, and chunked vendor bundles
  version.json          # Build metadata (see below)
```

`index.html` is the SPA entrypoint for every Console route. Devops MUST serve
`index.html` as the fallback for unknown paths under the configured base path
so client-side routing for `/studio`, `/catalog`, `/operate`, and `/share`
works on direct navigation and page reload.

## `version.json` schema

`dist/version.json` is the single source of truth for release-promotion
tooling. Schema:

```json
{
  "name": "honua-console",
  "version": "0.1.0",
  "commit": "<full sha>",
  "shortCommit": "<12-char sha>",
  "ref": "<branch or tag>",
  "builtAt": "<iso-8601 utc>",
  "legacy": {
    "portal": "active | retiring | retired",
    "admin":  "active | retiring | retired"
  },
  "areas": ["studio", "catalog", "share", "operate"]
}
```

Devops release notes use this file to identify the deployed Console artifact
version and the legacy `portal` / `admin` deployment status at promotion time.

Promotion-time tooling can re-stamp the file without rebuilding the bundle:

```
HONUA_CONSOLE_LEGACY_PORTAL_STATUS=retiring \
HONUA_CONSOLE_REF=release/2026.06 \
  npm run build:metadata
```

## Same-origin routing

The single deployable artifact serves all four Console areas from one origin.
That means:

- No cross-origin XHR / fetch is required for Studio, Catalog, Operate, or
  Share to talk to `honua-server`. Auth/session cookies live on the same
  origin as the API.
- The reverse proxy in front of Console must route API calls (e.g.
  `/api/*`, `/healthz/*`) to `honua-server` on the same origin. Console
  build does not embed an API base URL — same-origin relative paths are
  the contract.
- The legacy Blazor Admin, while it remains in the deployment, is served as a
  transitional surface under the same origin. Operate routes that have been
  ported land under `/operate/*`; routes that have not yet been ported can be
  embedded or redirected (see [ADR-0001](../adr/0001-unified-honua-console-runtime.md)).

## Caching guidance for the proxy

- `index.html`: never long-cache. Serve with `Cache-Control: no-store`
  (or `no-cache, must-revalidate`) so users pick up new bundle hashes on
  next navigation.
- `assets/*`: long-cache (`Cache-Control: public, max-age=31536000, immutable`).
  Vite emits content-hashed filenames so the cache is safe.
- `version.json`: short-cache or `no-store`. Promotion tooling and release
  notes read it freshly.

## Health and readiness

Console itself is a static bundle and has no runtime health check beyond the
proxy returning 200 for `index.html`. Backend health probes remain on
`honua-server` (`/healthz/ready`).
