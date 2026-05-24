# Native Operate Transition Surface

Status: implemented for `honua-console#36`.

This slice adds native Blazor Operate pages for the Admin transition while the shared `honua-sdk-dotnet` admin projections are still pending. The data source is intentionally bounded to `IOperateTransitionDataSource` in `Honua.Console.Shell`; it is UI sample data, not a server DTO mirror. When SDK clients land, replace that service implementation and keep the Razor routes.

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
- `/operate/layers/{layerId}`
- `/operate/settings`

## Response Contract

These are Console transition view models, not server protocol DTOs:

| Surface | Contract fields rendered |
| --- | --- |
| Connections | `id`, `name`, `provider`, `target`, `principal`, `status`, `lastTested`, optional safe diagnostic. |
| Connection diagnostics | `outcome`, `failureCode`, redacted `summary`, structured `signals`, redacted `operatorActions`, and redacted evidence key/value rows. |
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

The current implementation registers `InMemoryOperateTransitionDataSource.CreateSeeded()` from `AddHonuaConsoleShell()`. The replacement should bind `IOperateTransitionDataSource` to `honua-sdk-dotnet` admin clients once those contracts are available, without duplicating server protocol DTOs in Console.

The SDK-backed replacement must preserve the redaction boundary in `IOperateTransitionDataSource` before values reach Razor rendering. Tests in `OperateTransitionDataSourceTests` cover the transition behavior for data-source diagnostic redaction, blast radius, service metadata ownership, and settings restart requirements.
