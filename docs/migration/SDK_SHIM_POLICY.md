# SDK Shim Policy For The Console Migration

Status: filed 2026-05-23, reconciled with ADR-0001 .NET-first amendment.

Decision source: [ADR-0001: Unified Honua Console Runtime](../adr/0001-unified-honua-console-runtime.md).
Related: [`CONSOLE_PATTERNS_CHARTER.md`](./CONSOLE_PATTERNS_CHARTER.md) section "DRY: contracts only via SDK".
Cleanup ticket: [`honua-console#7`](https://github.com/honua-io/honua-console/issues/7) (wire Console to shared metadata / content / package / RBAC contracts).
External dependencies: [`honua-sdk-dotnet#166`](https://github.com/honua-io/honua-sdk-dotnet/issues/166) (Console .NET client contracts), [`honua-sdk-dotnet#231`](https://github.com/honua-io/honua-sdk-dotnet/issues/231) (Operate observability .NET projection), [`honua-sdk-js#225`](https://github.com/honua-io/honua-sdk-js/issues/225) (browser SDK contracts), [`honua-server#1162`](https://github.com/honua-io/honua-server/issues/1162) (Metadata v2 API baseline).

## Purpose

ADR-0001 is unambiguous: contracts are shared via the server-owned SDK projections, not duplicated in Console. Console-side code (Blazor Web + shared Razor components) consumes `honua-sdk-dotnet`; the embedded/generated-app JS bundles continue to consume `honua-sdk-js`.

The risk this policy addresses is that porting tickets (`honua-console#4`, `#5`, `#6`) need types and helpers that may not yet exist in either SDK if [`honua-sdk-dotnet#166`](https://github.com/honua-io/honua-sdk-dotnet/issues/166) or [`honua-sdk-js#225`](https://github.com/honua-io/honua-sdk-js/issues/225) slip behind the porting schedule.

Without a written shim policy, the porting tickets are either blocked on the SDK tickets or quietly duplicate DTOs across the Console codebase. This policy chooses a third path: a single, auditable boundary per language where temporary shims live until `#7` removes them.

## Policy

Temporary shims for SDK contracts are permitted in Console during the migration, **only** under all of the following conditions:

1. **.NET shims** live in a single boundary project: `src/Honua.Console.Contracts`. `SdkShims.cs` remains the default file for small/general shims; broad contract-family files may sit beside it when keeping them split makes the removal boundary clearer.
2. **Browser/JS shims** (only for code that runs inside generated-app or embed JS bundles, not Razor code paths) live in a single boundary module: `src/Honua.Console.Web/wwwroot/interop/sdk-shims.ts`.
3. Every shim has an inline `// SHIM(honua-sdk-dotnet#166): <reason>` (or another exact SDK issue such as `honua-sdk-dotnet#231` / `honua-sdk-js#225`, as appropriate) comment identifying the external ticket the shim is waiting on.
4. Every shim has a corresponding entry in this document's "Active Shims" section below, with an owner, language, and a removal target ticket.
5. No shim is imported from outside its boundary project/module. Razor and .NET feature code imports from `Honua.Console.Contracts`; JS interop bundles import from `wwwroot/interop/sdk-shims.ts`. This keeps the cleanup localized.
6. No shim redefines a contract that already exists in a **consumable published** `honua-sdk-dotnet` or `honua-sdk-js` package. Such contracts must be consumed directly. A contract that exists only in unmerged or prerelease SDK source — with no restorable stable package and no Console feed wired to it — is treated as pending, and a bounded shim is allowed until the package ships. An existing projection that deliberately preserves a different compatibility shape from a consumed SDK contract may remain only with an Active Shims row naming that difference, the tests that pin it, and a `honua-console#7` removal target.
7. [`honua-console#7`](https://github.com/honua-io/honua-console/issues/7) is gated on this document containing zero entries in "Active Shims". When `#7` is accepted, both boundary files are deleted along with this section.

## What A Shim Looks Like

.NET (the common case for Console code):

```csharp
// src/Honua.Console.Contracts/SdkShims.cs
namespace Honua.Console.Contracts;

// SHIM(honua-sdk-dotnet#166): MetadataV2 not yet projected to the .NET SDK.
// Mirrors the server payload described in honua-server#1162 section
// "GET /content/items/:id". Remove when the SDK exports
// `using Honua.Sdk.Metadata; // MetadataV2`.
public sealed record MetadataV2(
    string Id,
    string Title,
    // ...
);
```

Browser interop (only when the gap is in `honua-sdk-js`, used by generated-app/embed bundles):

```ts
// src/Honua.Console.Web/wwwroot/interop/sdk-shims.ts

// SHIM(honua-sdk-js#225): MapPackage not yet projected to the browser SDK.
// Mirrors the server payload described in honua-server#1162 section
// "GET /packages/maps/:id". Remove when the SDK exports
// `import type { MapPackage } from "@honua/sdk-js"`.
export interface MapPackage {
  // ...
}
```

In both forms, the shape mirrors the upstream contract exactly - the goal is that switching to the real SDK type later is a one-line import change inside the boundary file.

## What Is Not A Shim

The following are not covered by this policy and remain prohibited:

- Re-declaring a server DTO inline inside a feature component, page, or interop module.
- Re-exporting an existing `honua-sdk-dotnet` or `honua-sdk-js` type under a Console-flavored name.
- Wrapping an existing SDK type with a Console-specific field. Add the field to the SDK projection via the upstream ticket, or carry the Console-only field as a sibling type that composes the SDK type - not as a renamed clone.
- Using a .NET shim from JS code, or vice versa. The two boundaries do not cross.

## Acceptance Of A Shim

A new shim is added by:

1. Opening a PR that adds the entry to the appropriate boundary file with the inline `SHIM(...)` comment.
2. Adding an "Active Shims" row in this document in the same PR.
3. Linking the PR from `honua-sdk-dotnet#166` or `honua-sdk-js#225` (as appropriate) so the SDK owner sees the demand signal.
4. Linking the PR from `honua-console#7` so the cleanup ticket inherits the removal task.

A shim that has not been added to this document does not satisfy the policy and is treated as a regression in code review.

## Removal Of A Shim

When the upstream contract lands in the SDK:

1. Replace the shim's body in its boundary file with `global using Honua.Sdk.Metadata; // MetadataV2` (or similar), or remove the shim and update consumers' `using`/`import` statements to point at the SDK.
2. Remove the shim's row from "Active Shims" below.
3. Verify no feature code reaches around the boundary file with a direct import.

When "Active Shims" is empty:

1. Delete empty .NET shim files under `src/Honua.Console.Contracts` (and the `Honua.Console.Contracts` project if no other contract glue remains).
2. Delete `src/Honua.Console.Web/wwwroot/interop/sdk-shims.ts` if no browser shims remain.
3. Delete this document (or trim it to a single line noting the cleanup happened, depending on which is more useful for archeology).
4. Close [`honua-console#7`](https://github.com/honua-io/honua-console/issues/7).

## Active Shims

The current active shims are bounded to catalog/viewer/share route parity, Studio workflow package work, the Studio form builder (`honua-console#57`), the Studio GP/ETL workflow package retrofit (`honua-console#62`), the native trust/mTLS contract added by `honua-console#44`, the Operate admin transition retrofit, and Operate observability admin reads (`honua-console#24`). Catalog/viewer/share shims live in `src/Honua.Console.Contracts/SdkShims.cs`; the native trust wire contract lives in `src/Honua.Console.Contracts/EnvironmentTrustShims.cs`; the Operate admin HTTP shim lives in `src/Honua.Console.Contracts/OperateAdminShims.cs`; the Operate observability contracts live in `src/Honua.Console.Contracts/OperateObservabilityContracts.cs`; the Studio package lifecycle HTTP shim lives in `src/Honua.Console.Contracts/StudioPackageShims.cs`; the Studio form package HTTP shim lives in `src/Honua.Console.Contracts/FormPackageShims.cs`; the Studio GP/ETL workflow package HTTP shim lives in `src/Honua.Console.Contracts/StudioWorkflowShims.cs`, with the editor-side workflow projections in the shared shell model boundary mapped onto it. They provide stable Console-side shapes until the relevant `honua-sdk-dotnet` projection lands and `honua-console#7` replaces the shim imports.

| Shim   | Language | Added in PR | Waiting on | Owner | Target removal |
|--------|----------|-------------|------------|-------|----------------|
| Catalog search query and list request (`CatalogSearchState`, `CatalogListRequest`) | .NET | honua-console#34 | honua-sdk-dotnet#166 | Console | honua-console#7 |
| Content summary/detail route payloads (`ConsoleContentSummary`, `ConsoleContentDetail`, versions, lineage, bindings, publication, permissions, activity, usage) | .NET | honua-console#34 | honua-sdk-dotnet#166 / honua-server#1162 | Console | honua-console#7 |
| Share access and public-link fields (`ConsoleShareAccess`) | .NET | honua-console#34 | honua-sdk-dotnet#166 / honua-server#1162 | Console | honua-console#7 |
| Saved map and embed options (`ConsoleMap`, `EmbedRouteOptions`) | .NET | honua-console#34 | honua-sdk-dotnet#166 / honua-sdk-js#225 | Console | honua-console#7 |
| Studio workflow package editor projections in `src/Honua.Console.Shell/Models/StudioWorkflowPackage.cs` | .NET | honua-console#40 | honua-sdk-dotnet#169 workflow projections (honua-server#1185 landed; `honua-console#62` retrofitted the runtime binding through the workflow HTTP shim below, so these are now editor-side models mapped to live server data, not a standing mock) | Studio | honua-console#7 replaces with shared SDK/server projections |
| Environment trust contracts (`HonuaCertificateValidationStatus`, `HonuaEnvironmentTrustState`) in `src/Honua.Console.Contracts/EnvironmentTrustShims.cs` | .NET | honua-console#44 | honua-sdk-dotnet#166 (`Honua.Sdk.Abstractions.Environments`, merged on SDK trunk but not yet in a consumable package) | Console | honua-console#7 swaps to `global using …Environments.*` once the package ships #166 |
| Client-certificate validate wire contracts (`ConsoleClientCertificateValidationRequest`, `ConsoleClientCertificateValidationResult`, `ConsoleServerEnvelope<T>`, `ConsoleCertificateValidationCodes`) in `src/Honua.Console.Contracts/EnvironmentTrustShims.cs` | .NET | honua-console#44 | honua-server#1171 (`POST /api/v1/admin/security/client-certificates/validate`) / honua-sdk-dotnet#166 | Console | honua-console#7 replaces with the SDK trust client when projected |
| Discovery metadata authoring (`DiscoveryMetadataContracts.cs`: narrow resource/service title, description, keywords and publication impact; reuses the Operate HTTP transport) | .NET | metadata authoring | honua-sdk-dotnet#166 projecting the discovery routes | Operate | honua-console#7 |
| Service navigation folder metadata (`HonuaAdminServiceSummary.FolderId`, `FolderName`, `PublicName`, `Title`; nullable fields on the existing summary, no second client) | .NET | service navigation | honua-server#6951 additive service projection and a consumable Admin SDK projection | Operate | honua-console#7 |
| File-import coordinate-system preview, lookup and options (`OperateAdminImportCoordinates.cs`; new direct Admin contract, no published SDK projection yet) | .NET | coordinate-system upload | honua-sdk-dotnet#166 / honua-server#7148 | Operate | honua-console#7 |
| Operate admin HTTP shim (`HonuaAdminOperateHttpClient` and admin response records) | .NET | honua-console#60 | migration to the consumed stable `Honua.Sdk.Admin` 2.0.0 client and projections, retaining the nullable progress-response compatibility described below | Operate | honua-console#7 |
| Connection provider inventory read (`ConnectionProviderManifest.cs`, raw manifest through the existing admin transport) | .NET | honua-console#589 | honua-sdk-dotnet#166: the consumed capability-manifest projection has no `dataStores` section; no duplicate wire DTO is introduced | Operate | honua-console#7 replaces this read when a published SDK exposes installed providers and image guidance |
| Studio GP/ETL workflow package HTTP shim (`IWorkflowPackageApiClient`/`HttpWorkflowPackageApiClient`, the `Workflow*` wire records, enums, and `WorkflowApiResponse<T>`) in `src/Honua.Console.Contracts/StudioWorkflowShims.cs` | .NET | honua-console#62 | honua-sdk-dotnet#169 projecting the workflow package DTOs (honua-server#1185 merged on server trunk; no consumable SDK projection yet) | Studio | honua-console#7 swaps to the SDK workflow client when projected |
| Workflow node execution settings (`StepRetryPolicy`, failure policy, timeout, condition and static fan-out) in `WorkflowNodeExecution.cs`, extending the frozen `WorkflowNode` wire record | .NET | honua-console#610 | honua-sdk-dotnet#169 publishing the executable workflow contract | Studio | honua-console#612 replaces these records during the 2026.2 pipelines work |
| Studio form package HTTP shim (`IHonuaFormPackageClient`/`HonuaFormPackageHttpClient`, the `HonuaForm*` wire records mirroring `honua.form-package.v1`, and `HonuaFormPackageClientOptions`; reuses the `HonuaAdminEndpointResult<T>`/`HonuaAdminEndpointIssue` envelope) in `src/Honua.Console.Contracts/FormPackageShims.cs` | .NET | honua-console#57 | honua-sdk-dotnet#166 projecting the form package DTOs (honua-server#1184 merged on server trunk; no consumable SDK projection yet) | Studio | honua-console#7 swaps to the SDK form client when projected |
| Operate observability admin contracts (`OperateObservabilityContracts.cs`: server overview/status, events, logs, audit, alert lifecycle (including suppress/resolve request bodies), alert rules, geofence zones, durable jobs, investigations) | .NET | honua-console#24 | honua-sdk-dotnet#231 plus honua-server#1168/#1169/#1170 | Console | honua-console#7 replaces with the SDK Operate projection |

Console consumes stable `Honua.Sdk.Admin` 2.0.0 from nuget.org. The import
progress projection uses its `GeoservicesImportStatus` enum for both numeric
and named wire values; it maintains no local status enum.

`HonuaAdminGeoservicesImportProgress` remains a bounded nullable projection.
The SDK's `GeoservicesImportProgress` requires members such as `JobId` and
`Status`, while the existing Operate transport accepts null and empty progress
bodies. `HonuaAdminOperateHttpClientPipelineTests` pins that compatibility.
Replacing the projection directly would change those responses into contract
errors. Removal under `honua-console#7` must migrate the Operate client and
progress record together, with an explicit decision and tests for null and
empty bodies. Stable package availability no longer blocks that migration.
New shims still require the admission checks above.

The catalog shim preserves the Portal URL contract at the route edge:
`/catalog` accepts `visibility`, not `sharing`. `CatalogListRequest`
maps that value to the SDK/server request field `sharing`, and
`ToSdkParameters()` must not emit `visibility`. `CatalogSearchState`
normalizes `visibility` to `private`, `org`, `group`, `public-link`, or
`public`, normalizes `sort` to `relevance`, `modified-desc`,
`modified-asc`, `title-asc`, or `title-desc`, and drops unsupported type
filters before creating the request. Embed bearer placement is
also pinned here until the SDK projections land: `EmbedRouteOptions`
reads `#embedToken=` from the URL fragment for token-authorized embeds
and flags query-string `token` or `embedToken` as an unavailable-route
regression. Public embeddable maps may still authorize without a token.
Its query parser accepts Portal snippet `chrome` profiles (`full`,
`minimal`, `none`), `legend`/`zoom` `on/off` controls, and only valid,
non-degenerate WGS84 `extent=W,S,E,N` bounds.

## Why This Boundary, Not "Whatever Works"

Two alternatives were considered:

- **Block all porting on `honua-sdk-dotnet#166` (and `#225` for embed/generated-app paths).** Cleaner, but stalls porting work for the duration of external tickets Console does not own. Rejected.
- **Allow inline DTO duplication wherever needed, clean up later.** Faster in the short term, but creates a diffuse cleanup with no list of what needs to be deleted. Rejected - this is the exact failure mode the DRY constraint in ADR-0001 warns against.

The single-boundary-per-language approach takes a measured shortcut: porting work continues, every shortcut is visible in one file per runtime and one document, and removal is a single coordinated PR rather than a scavenger hunt.
