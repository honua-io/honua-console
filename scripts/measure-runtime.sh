#!/usr/bin/env bash
# Run with remote-run; .NET commands must execute on the build host.
set -euo pipefail
label=${1:?runtime label required}
cp src/Honua.Console.Web/global.json global.json
node scripts/fetch-studio-release.mjs
node scripts/fetch-cesium.mjs
dotnet restore Honua.Console.slnx --configfile NuGet.config --locked-mode
dotnet publish src/Honua.Console.Web/Honua.Console.Web.csproj -c Release --no-restore -o "artifacts/runtime-${label}" -p:HonuaConsoleCommitSha=runtime-comparison
npm ci --prefix e2e/playwright
node e2e/playwright/measure-startup.mjs "artifacts/runtime-${label}/wwwroot/console" "artifacts/runtime-${label}.json" "$label"
