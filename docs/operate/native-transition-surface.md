# Native Operate Transition Surface

Status: implemented for `honua-console#36`.

This slice adds native Blazor Operate pages for the Admin transition while the shared `honua-sdk-dotnet` admin projections are still pending. The data source is intentionally bounded to `IOperateTransitionDataSource` in `Honua.Console.Shell`; it is UI sample data, not a server DTO mirror. When SDK clients land, replace that service implementation and keep the Razor routes.

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

## Acceptance Mapping

- Failed connection diagnostics are rendered as structured checks, evidence, and operator actions. `OperateSecretRedactor` removes connection strings, passwords, API keys, bearer tokens, and secret-reference values while preserving non-secret secret identifiers.
- Resource edit pages show validation issues, edit tabs, and blast radius across catalog items, services, layers, saved maps, share links, and generated apps.
- Service and layer pages expose service layer projections but link metadata ownership back to canonical data resources.
- Settings rows show proposed change, apply scope, policy state, and restart requirement before application.

## SDK Swap Point

The current implementation registers `InMemoryOperateTransitionDataSource.CreateSeeded()` from `AddHonuaConsoleShell()`. The replacement should bind `IOperateTransitionDataSource` to `honua-sdk-dotnet` admin clients once those contracts are available, without duplicating server protocol DTOs in Console.
