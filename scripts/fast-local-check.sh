#!/usr/bin/env bash
set -euo pipefail

dotnet test tests/Honua.Console.Shell.Tests/Honua.Console.Shell.Tests.csproj --nologo --verbosity minimal
dotnet build src/Honua.Console.Web/Honua.Console.Web.csproj --nologo --verbosity minimal
