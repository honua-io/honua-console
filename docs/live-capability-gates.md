---
type: concept
title: "Live Console capability gates"
description: "How Console derives feature gates from the live server capabilities manifest, and how refresh, retarget, and outage behavior are guaranteed."
resource: "honua://capability/capabilities.manifest"
tags: [capabilities, manifest, gates]
---
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
| `persistent-catalog` | `console.content.persistent` |

`persistent-catalog` gates Catalog, Share, open-data and the catalog map viewer: until the server keeps
that content across restarts, those routes point to Data & Layers and stay out of the navigation.

Opt-in Preview features — such as Geofence Alerts (`alerts.geofence`) — are off
by default in 2026.1-rc.2. When disabled on the server, Console displays a clear
Preview / opt-in status rather than treating the surface as missing or broken.
Geoprocessing services (`GPServer` and OGC API - Processes) are non-layer
services that expose executable processes/tasks rather than feature layers.

The deployed static Console always uses the full set above; the server decides
what is available. For tests, `HonuaConsoleShellOptions.AdvertisedCapabilities`
(read by `tests/Honua.Console.LegacyHost` from `Honua:Console:Capabilities` /
`HONUA_CONSOLE_CAPABILITIES`) restricts the set to a comma-separated list of
Console keys from the left column, such as `temporal`. A key opens only when
the server also reports it available; leaving the list empty adds no restriction.

Availability belongs to the scoped service of one browser tab, not to an
application-wide singleton.
Pages refresh before loading gated feature data; activating an environment
profile refreshes the same service and updates the existing navigation. The SDK
HTTP binding retargets each request and forwards the active operator's bearer.
An old response cannot overwrite a newer refresh. Refreshes clear prior positive
state immediately and are bounded to five seconds. Failed, missing, unauthorized,
unreadable, unsupported-schema, duplicate, absent, unsupported, and unavailable
manifest predicates keep the feature gate closed. The rest of the shell remains
usable during a manifest outage.
