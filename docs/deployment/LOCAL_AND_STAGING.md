# Local and Staging Startup

Decision source: [ADR-0001: Unified Honua Console Runtime](../adr/0001-unified-honua-console-runtime.md).
Build contract: [BUILD_ARTIFACT.md](BUILD_ARTIFACT.md).

## Prerequisites

- .NET SDK 10.x.
- Node.js 20.x only for the dependency-free smoke harness and metadata writer.

## Local development

```bash
dotnet run --project src/Honua.Console.Web/Honua.Console.Web.csproj --urls http://127.0.0.1:5174
```

`/studio`, `/catalog`, `/operate`, and `/share` are Razor routes from the
shared shell and resolve from the same origin.

Same-origin API expectations:

- API calls are relative paths (`/api/...`). For local dev, run `honua-server`
  behind the same reverse proxy shape used by staging, or add a development
  proxy in front of both processes. Console build itself does not bake in an
  API base URL.

Common commands:

- `dotnet test tests/Honua.Console.Native.Core.Tests/Honua.Console.Native.Core.Tests.csproj`
- `dotnet build src/Honua.Console.Web/Honua.Console.Web.csproj`
- `./scripts/fast-local-check.sh`
- `npm test` - Node smoke and metadata-writer unit tests; no npm dependencies.
- `npm run smoke:parity` - fixture/local artifact parity smoke.

## Verifying the production artifact locally

```bash
rm -rf artifacts/honua-console-web
dotnet publish src/Honua.Console.Web/Honua.Console.Web.csproj -c Release -o artifacts/honua-console-web
HONUA_CONSOLE_ARTIFACT_DIR=artifacts/honua-console-web node scripts/write-build-metadata.mjs
dotnet artifacts/honua-console-web/Honua.Console.Web.dll --urls http://127.0.0.1:4174
```

Then open `http://127.0.0.1:4174/` and confirm:

1. `/studio`, `/catalog`, `/operate`, and `/share` each render the shared
   Console area surface.
2. Direct navigation to any of those paths resolves through the Blazor Web
   host.
3. `http://127.0.0.1:4174/version.json` returns the build metadata block.

## Staging preview environments

Staging is owned by `honua-devops` (see honua-devops#55 / #56). Console only
needs to:

- Produce the published Blazor artifact documented in
  [BUILD_ARTIFACT.md](BUILD_ARTIFACT.md).
- Surface `/version.json` so release-promotion tooling can attach Console
  artifact versions to release notes alongside legacy Portal/Admin deployment
  status.

For smoke evidence during staging promotion, exercise:

- Same-origin auth cookie set by `honua-server` survives navigation between
  `/studio`, `/catalog`, `/operate`, and `/share`.
- `/version.json` is reachable on the deployed origin.
- A direct page load of `/operate/anything` is handled by the Console host
  rather than a static-server 404.

## CI expectations

The in-repo GitHub Actions workflow at `.github/workflows/ci.yml` implements
this gate for pushes to `main` and pull requests.

The release pipeline (`honua-devops#56`) should run:

1. `dotnet restore Honua.Console.slnx`
2. `dotnet test tests/Honua.Console.Native.Core.Tests/Honua.Console.Native.Core.Tests.csproj --nologo --verbosity minimal`
3. `dotnet build src/Honua.Console.Web/Honua.Console.Web.csproj --no-restore --nologo --verbosity minimal`
4. `npm test`
5. `dotnet publish src/Honua.Console.Web/Honua.Console.Web.csproj -c Release -o artifacts/honua-console-web`
6. `HONUA_CONSOLE_ARTIFACT_DIR=artifacts/honua-console-web node scripts/write-build-metadata.mjs`

Then upload `artifacts/honua-console-web/` as the Console artifact.
