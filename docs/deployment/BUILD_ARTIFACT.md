---
type: reference
title: "Build artifact contract"
description: "The standalone Console browser bundle and its release receipt."
tags: [build, artifact, contract]
---
# Honua Console build artifact contract

Console ships as `honua-console-<version>.zip`, a separate release asset containing
static browser files for `/console/`. It requires no Console application server or
container. [Static deployment](STATIC_SPA.md) describes configuration, routing,
caching and the remaining migration gates.

## Producing the artifact

```bash
cp src/Honua.Console.Web/global.json global.json
dotnet restore Honua.Console.slnx --configfile NuGet.config --locked-mode
node scripts/fetch-cesium.mjs --force
node scripts/fetch-studio-release.mjs
dotnet publish src/Honua.Console.Web/Honua.Console.Web.csproj -c Release --no-restore -o artifacts/static-publish
node scripts/verify-published-cesium.mjs artifacts/static-publish
node scripts/fetch-studio-release.mjs --verify artifacts/static-publish/wwwroot/console/_studio
python3 scripts/package-console-static.py artifacts/static-publish/wwwroot/console --version 0.1.0
```

The release workflow also verifies Studio's build provenance against its locked
upstream tag and commit before publication. Pull requests produce a read-only
validation artifact. Published releases attach the versioned ZIP and JSON receipt;
the workflow refuses to overwrite existing release assets.

## Artifact layout

```text
honua-console-<version>.zip
  index.html
  config.json
  _framework/
  _content/
  _studio/
  css/
  js/
honua-console-<version>.zip.json
```

The ZIP contains the contents of the publish output's `wwwroot/console` directory,
without an extra directory prefix. Extract beneath `/console/`. The package step
rejects missing entrypoints, a different base path, symbolic links and output inside
the source tree. The receipt records `asset`, `version`, `files`,
`uncompressedBytes`, `zipBytes` and `sha256` for artifact inspection.

`config.json` is public deployment configuration. Credentials and secrets must
never be added to the bundle. API authentication and authorization belong to
Honua Server. The retained legacy host is a non-publishable test fixture; its
server-side `version.json`, proxy and authentication endpoints are not part of
this static artifact.
