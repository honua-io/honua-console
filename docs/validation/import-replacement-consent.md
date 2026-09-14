# Selected-layer import replacement authorization (#363)

Release promise: the 2026.1 focused Console Operate surface must require explicit
operator authorization before destructive table replacement. This issue is in the
must-fix-before-cut bucket (ruling B, 2026-09-05).

## Local verification

Executed 2026-09-05 HST with the PATH .NET lane shim (SDK 10.0.400,
`HONUA_MSBUILD_NODE_CAP=4`, shared compilation enabled):

```bash
dotnet test tests/Honua.Console.IntegrationTests/Honua.Console.IntegrationTests.csproj --filter 'FullyQualifiedName~OperateImportReplacement|FullyQualifiedName~ServiceImportOperationTests' --nologo --verbosity minimal
```

Result: **19 passed, 0 failed, 0 skipped**. The sandbox denied MSBuild IPC sockets;
the same command passed with command escalation.

Additional local checks: solution formatting passed; native-core tests passed
(1,382 passed, 0 failed, 3 existing opt-in live tests skipped). The web build
passed with 0 warnings and 0 errors on an unchanged retry after an MSBuild
child-process exit (`MSB4166`) during `fast-local-check.sh`.

`OperateImportReplacementHttpTests` renders the real import page, production import
operation, and production admin HTTP client. Only the HTTP transport is stubbed.
The assertions inspect serialized schema/table, source URL/layer ID, overwrite
flags, and API-key forwarding. They cover:

- default create-only requests for every selected target;
- immediate existing-target conflicts and accepted jobs that later report conflicts;
- deselection/skip without a request for the skipped target;
- one named replacement and mixed create/replace batches;
- destination and affected-layer counts, with no request before confirmation;
- cancellation, changed selection, success then retry, HTTP 503, transport failure,
  and server authorization denial (HTTP 403).

`OperateImportReplacementConsentTests` additionally blocks the first queue call,
changes a later checkbox while it awaits, and verifies that the later target is
not authorized by that change. `ServiceImportOperationTests` checks wire mapping
and job-status handling.

## Server contract verification

Source inspected at `honua-server@ac5723545ac008b1f0357b35d0cbb8bfd67eff39`:

- `src/Honua.Import/Features/Migration/GeoservicesImportEndpoints.cs` requires admin
  authorization and maps an omitted overwrite flag to false.
- `src/Honua.Db/Postgres/Features/Migration/GeoservicesImportService.ImportSteps.cs`
  drops an existing table only when overwrite is true; failed table creation rolls
  back the transaction and reports failure.
- `src/Honua.Db/Postgres/Features/Migration/GeoservicesImportService.cs` resolves an
  unspecified schema from server configuration. Console therefore explicitly sends
  the `public` schema named in its replacement controls and confirmation.

The Console confirmation does not grant server privileges or replace the server
as the authorization/audit boundary. This verification does not claim live database
execution or exact-candidate release certification.
