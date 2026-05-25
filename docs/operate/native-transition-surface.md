# Native Operate Transition Surface

Status: implemented for `honua-console#36`, retrofitted for real server bindings in `honua-console#60`.

This slice adds native Blazor Operate pages for the Admin transition while the shared `honua-sdk-dotnet` admin projections are still pending. Normal runtime registration no longer uses the seeded in-memory data source. When `Honua:Server:BaseUrl` or `HONUA_SERVER_BASE_URL` is configured, `IOperateTransitionDataSource` binds through the `Honua.Console.Contracts` Operate admin HTTP shim to live honua-server admin endpoints. When no server binding is configured, the routes render an explicit missing-binding state rather than sample data. The seeded in-memory data source remains available only through tests or the explicit `AddHonuaConsoleDemoOperateTransitionData()` opt-in.

The native routes are the preferred Console navigation for connections, data resources, services, layers, and operator settings. Legacy Admin routes remain available under `/operate/legacy/*` according to the [legacy route disposition](../migration/legacy-admin-route-disposition.md) until parity smoke and SDK-backed data prove each legacy row can be retired.

## Routes

- `/operate`
- `/operate/connections`
- `/operate/connections/new`
- `/operate/connections/{connectionId}`
- `/operate/connections/{connectionId}/diagnostics`
- `/operate/resources`
- `/operate/resources/new`
- `/operate/resources/{resourceId}`
- `/operate/services`
- `/operate/services/{serviceName}/settings`
- `/operate/layers`
- `/operate/layers/{layerId:int}`
- `/operate/settings`

## Server Binding And Admin Shim

The browser host passes `Honua:Server:BaseUrl` or `HONUA_SERVER_BASE_URL`
into `AddHonuaConsoleShell(serverBaseUrl, adminApiKey)`. The optional admin
API key can come from `Honua:Server:AdminApiKey` or `HONUA_ADMIN_API_KEY` and
is sent as `X-API-Key`. Standard ASP.NET environment variable mapping also
supports `Honua__Server__BaseUrl` and `Honua__Server__AdminApiKey`.

Until `honua-sdk-dotnet#166` provides admin projections, the temporary
`HonuaAdminOperateHttpClient` reads the honua-server admin envelope
`{ success, data, message, timestamp }` and maps it into
`HonuaAdminEndpointResult<T>`. Successful responses require
`success == true` and non-null `data`; missing data becomes an unavailable
capability state.

| Console surface | Admin endpoint used now |
| --- | --- |
| Connections | `GET /api/v1/admin/connections/` |
| Published layer/resource projections | `GET /api/v1/admin/connections/{id}/layers/` |
| Services | `GET /api/v1/admin/services/` |
| Service settings | `GET /api/v1/admin/services/{serviceName}/settings` |
| Runtime/version settings | `GET /api/v1/admin/version` and `GET /api/v1/admin/capabilities` |
| License settings | `GET /api/v1/admin/license/` |
| API key inventory | `GET /api/v1/admin/api-keys/` |
| OIDC provider inventory | `GET /api/v1/admin/oidc/providers/` |

Endpoint issues are normalized before they reach Razor components:

| Condition | Capability state |
| --- | --- |
| Request failure or timeout | `Unavailable` |
| HTTP 401 or 403 | `Missing permission` |
| HTTP 404, 405, or 501 | `Unsupported` |
| Other non-success HTTP status | `Unavailable` |
| JSON shape mismatch | `Unsupported` |
| Successful envelope with no data | `Unavailable` |

The server-backed data source starts the independent top-level reads together
to avoid a startup waterfall, then reads layer rows per connection and service
settings per service. It does not fabricate rows for unsupported contracts.

## Response Contract

These are Console transition view models, not server protocol DTOs:

| Surface | Contract fields rendered |
| --- | --- |
| Operate workspace | Four bounded collections: `connections`, `resourceEdits`, `services`, and `settingsChanges`, plus `capabilityStates` for missing binding, missing permission, unsupported, or unavailable backend contracts. The landing page renders counts, current actionable items, and capability states. |
| Connections | `id`, `name`, `provider`, `target`, `principal`, `status`, `lastTested`, optional safe diagnostic. |
| Connection diagnostics | The detail and `/diagnostics` routes share the same component and render `outcome`, `failureCode`, redacted `summary`, structured `signals`, redacted `operatorActions`, and redacted evidence key/value rows. |
| Resource edits | `resourceId`, `name`, `source`, `draftChange`, `validationState`, `validationIssues`, `editTabs`, and blast-radius lists for catalog items, services, layers, saved maps, share links, and generated apps. |
| Services | `name`, `displayName`, `serviceType`, `runtimeStatus`, `metadataOwnership`, layer projections, runtime settings, and publication slots. |
| Layers | Flattened service-layer projections with `layerId`, `name`, `geometry`, service link, and canonical resource link. |
| Settings | `category`, `name`, `proposedChange`, `applyScope`, `requiresRestart`, `restartRequirement`, and `policyState`. |

Missing detail records render the shared Console missing-item surface:

- Unknown connection: `<MissingItemView kind="connection">`.
- Unknown resource: `<MissingItemView kind="resource">`.
- Unknown service: `<MissingItemView kind="service">`.
- Unknown layer ID: `<MissingItemView kind="layer">`.

Empty list states render the shared `<EmptyState area="operate">` surface with the list subject and any available primary action. Routes do not author bespoke 403, 404, or empty-state copy; they supply only the item kind, area, subject, and action target required by the shared component contract in [Console Route Map](../console-route-map.md#7-exception-surfaces).

Missing backend contracts render `<OperateCapabilityStateList>` entries that name the contract. Current known gaps are the Metadata v2 resource edit validation/blast-radius projection (`GET /api/v1/admin/metadata/resources`), service-scope TimeInfo in `GET /api/v1/admin/services/{serviceName}/settings`, a CORS admin read/write contract, and a catalog endpoint visibility admin contract.

## Acceptance Mapping

- Failed connection diagnostics are rendered as structured checks, evidence, and operator actions. `OperateSecretRedactor` removes connection strings, passwords, API keys, bearer tokens, and secret-reference values while preserving non-secret secret identifiers in the form `secret://{scope}/{identifier}/[redacted]`.
- Resource edit pages show validation issues, edit tabs, and blast radius across catalog items, services, layers, saved maps, share links, and generated apps.
- Service and layer pages expose service layer projections but link metadata ownership back to canonical data resources.
- Settings rows show proposed change, apply scope, policy state, and restart requirement before application.

## Usage Notes

- New connections capture provider/target details separately from the credential reference. Console displays only non-secret identifiers; the secret value is stored by the configured server secret store.
- New resources can start from an owned table, uploaded file, or one-time remote-service migration. Migration copies schema, metadata, and supported features into a Console-owned resource. It is not a proxy, sync, or mirror contract.
- Resource detail tabs are represented by the current edit preview: Overview, Source, Fields, Metadata, Publish, Access, Validation, Presentation, and Advanced.
- Service settings control runtime, exposure, restart-scoped options, and publication slots. Canonical resource metadata stays owned by data resources.
- Settings changes must show apply scope, policy state, and restart impact before the operator applies the change. API key secret values are server-owned one-time reveals and are never kept in Console state.

## SDK Swap Point

`AddHonuaConsoleShell()` now registers `UnsupportedOperateTransitionDataSource` unless a server base URL is provided. `AddHonuaConsoleShell(serverBaseUrl, adminApiKey)` registers `HonuaServerOperateTransitionDataSource`, which maps the temporary `Honua.Console.Contracts` HTTP shim records into Console view models. Replace `HonuaAdminOperateHttpClient` with `honua-sdk-dotnet` admin clients once those contracts are available, without moving server protocol DTOs into Shell pages or services.

The SDK-backed replacement must preserve the redaction boundary in `IOperateTransitionDataSource` before values reach Razor rendering. Tests in `OperateTransitionDataSourceTests` cover runtime DI, server-response mapping, missing-contract states, diagnostic redaction, blast radius, service metadata ownership, and settings restart requirements. `OperateTransitionLiveServerTests` is an opt-in xUnit/Testcontainers integration test (`HONUA_CONSOLE_RUN_LIVE_SERVER_TESTS=true`) that starts PostgreSQL, boots a real honua-server checkout, creates a connection/layer fixture, and renders Razor pages from live data; it skips cleanly when not opted in or when Docker/server checkout prerequisites are unavailable.
