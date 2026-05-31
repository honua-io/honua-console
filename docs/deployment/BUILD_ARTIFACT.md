# Honua Console Build Artifact Contract

This is the contract between `honua-console` and the single deployable artifact
produced by `honua-devops` (see [honua-devops#55](https://github.com/honua-io/honua-devops/issues/55)
and [honua-devops#56](https://github.com/honua-io/honua-devops/issues/56)).

Decision source: [ADR-0001: Unified Honua Console Runtime](../adr/0001-unified-honua-console-runtime.md).

## Producing the artifact

```bash
dotnet restore Honua.Console.slnx
dotnet test tests/Honua.Console.Native.Core.Tests/Honua.Console.Native.Core.Tests.csproj --nologo --verbosity minimal
dotnet publish src/Honua.Console.Web/Honua.Console.Web.csproj -c Release -o artifacts/honua-console-web
HONUA_CONSOLE_ARTIFACT_DIR=artifacts/honua-console-web node scripts/write-build-metadata.mjs
```

The published Blazor Web host is the deployable Console artifact. The metadata
writer has no third-party npm dependencies; it writes `version.json` beside the
published host so promotion tooling can inspect the artifact before deployment.
The running web host also exposes the same schema at `/version.json`.

Environment variables consumed by the web host and metadata writer:

- `HONUA_CONSOLE_COMMIT_SHA` - Override the git SHA stamped into metadata.
  Useful in CI where the checkout may be detached.
- `HONUA_CONSOLE_REF` - Override the git ref (branch/tag) recorded in metadata.
- `HONUA_CONSOLE_BUILT_AT` - Override the ISO-8601 build timestamp.
- `HONUA_CONSOLE_LEGACY_PORTAL_STATUS` - One of `active`, `retiring`, `retired`.
  Defaults to `active` until `honua-portal` is frozen.
- `HONUA_CONSOLE_LEGACY_ADMIN_STATUS` - One of `active`, `retiring`, `retired`.
  Defaults to `active` until `honua-server-admin` legacy routes are retired.
- `HONUA_CONSOLE_ARTIFACT_DIR` - Output directory for
  `scripts/write-build-metadata.mjs`. Defaults to `artifacts/honua-console-web`.

## Artifact layout

```text
artifacts/honua-console-web/
  Honua.Console.Web.dll
  Honua.Console.Web.runtimeconfig.json
  appsettings.json
  wwwroot/
  version.json          # Build metadata copy for promotion tooling
```

Devops deploys this published directory as the Console web host. Static assets
are served from `wwwroot/`; Razor routes are served by the Blazor Web app.

## `version.json` schema

`/version.json` on the deployed origin is the release-promotion source of
truth. The same JSON is written to `artifacts/honua-console-web/version.json`
for artifact inspection before deployment.

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
    "admin": "active | retiring | retired"
  },
  "areas": ["studio", "catalog", "operate", "share"]
}
```

The web host sources `areas` from `ConsoleRouteMap`. The metadata writer reads
`config/console-areas.json`, and the .NET test suite verifies both registries
stay aligned. `ConsoleBuildMetadataTests` additionally exercises the served
`/version.json` payload directly: it asserts the contract keys, the
`HONUA_CONSOLE_LEGACY_*` Portal/Admin status gates (defaulting to `active`),
the commit/short-commit/ref gates, and that the served `areas` match the same
`config/console-areas.json` registry the standalone writer consumes.

Promotion-time tooling can re-stamp the artifact metadata without rebuilding:

```bash
HONUA_CONSOLE_LEGACY_PORTAL_STATUS=retiring \
HONUA_CONSOLE_REF=release/2026.06 \
HONUA_CONSOLE_ARTIFACT_DIR=artifacts/honua-console-web \
  node scripts/write-build-metadata.mjs
```

## Same-origin routing

The single deployable artifact serves all four Console areas from one origin.
That means:

- No cross-origin XHR/fetch is required for Studio, Catalog, Operate, or Share
  to talk to `honua-server`. Auth/session cookies live on the same origin as
  the API.
- The reverse proxy in front of Console must route API calls (for example,
  `/api/*`, `/healthz/*`) to `honua-server` on the same origin. Console build
  does not embed an API base URL.
- The legacy Blazor Admin, while it remains in the deployment, is served as a
  transitional surface under the same origin. Ported Operate routes land under
  `/operate/*`; routes not yet ported can be embedded or redirected.

## Caching guidance for the proxy

- Blazor/static framework assets: long-cache content-hashed assets when the
  published manifest marks them as fingerprinted.
- Host HTML and dynamic route responses: do not long-cache.
- `/version.json`: `no-store` or short-cache. Promotion tooling and release
  notes read it freshly.

## Health and readiness

Console is an ASP.NET Core web host. Runtime health/readiness probes should be
owned by the hosting layer and the backend `honua-server` probes
(`/healthz/ready`). The Console artifact contract requires `/version.json` and
the top-level Console routes to respond from the deployed origin.
