# Live Console capability gates

Server-backed Console gates consume `GET /api/v1/capabilities/manifest` through
`Honua.Sdk.Studio`. They use the connected operator's server binding and require
both `supported` and `available` on the mapped predicate. This enforces the
2026.1 promise that Console agrees with the server's runtime availability,
including Preview opt-ins.

| Console key | Server predicate |
| --- | --- |
| `temporal` | `temporal.filtering` |
| `disconnected-sync` | `sync.offline` |
| `realtime-alerting` | `alerts.geofence` |
| `cross-environment-promotion` | `gitops.release-manifest` |
| `siem-investigations` | `ops.findings` |

`Honua:Console:Capabilities` / `HONUA_CONSOLE_CAPABILITIES` can restrict this set.
An omitted server policy imposes no additional restriction. A configured list
of server keys permits only those keys that the server also permits.
`studio-builders` is a separate, local-only switch and is excluded from that
server policy; setting only `studio-builders` does not hide server capabilities.

Availability belongs to a Blazor circuit, not to the application singleton.
Pages refresh before loading gated feature data; activating an environment
profile refreshes the same service and updates the existing navigation. The SDK
HTTP binding retargets each request and forwards the active operator's bearer.
An old response cannot overwrite a newer refresh. Refreshes clear prior positive
state immediately and are bounded to five seconds. Failed, missing, unauthorized,
unreadable, unsupported-schema, duplicate, absent, unsupported, and unavailable
manifest predicates keep the feature gate closed. The rest of the shell remains
usable during a manifest outage.

The Docker-free `LiveManifestPageGateTests` suite sends manifest JSON through the
real SDK client and renders the five actual Console pages. Each row checks the
expected gate and feature-client call count, including a delayed initial response.
`LiveManifestBindingTests` switches between two server authorities and operator
bearers and checks the existing navigation. Refresh-order and cancellation
regressions live in `ManifestBackedConsoleCapabilityManifestTests`.

Local verification on 2026-09-05 (HST) used the .NET 10.0.400 lane shim with the four-node
MSBuild cap. The focused `LiveManifest` and
`ManifestBackedConsoleCapabilityManifestTests` filter executed 77 tests: 77 passed,
zero failed, zero skipped (55 SDK-to-page cases plus binding/policy/refresh tests).

The full native-core suite passed 1,383 tests (three existing opt-in tests skipped),
and the full integration/render suite passed 899 (56 existing opt-in live-server
cases skipped). Those skips are not evidence for this issue; its focused 77 cases
all executed. The web build passed with zero warnings and errors, solution
formatting completed without edits, Node tests passed 12/12, and the vendored
server ops parity-map check passed.
