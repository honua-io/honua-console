# SDK Shim Policy For The Console Migration

Status: filed 2026-05-23, reconciled with ADR-0001 .NET-first amendment.

Decision source: [ADR-0001: Unified Honua Console Runtime](../adr/0001-unified-honua-console-runtime.md).
Related: [`CONSOLE_PATTERNS_CHARTER.md`](./CONSOLE_PATTERNS_CHARTER.md) section "DRY: contracts only via SDK".
Cleanup ticket: [`honua-console#7`](https://github.com/honua-io/honua-console/issues/7) (wire Console to shared metadata / content / package / RBAC contracts).
External dependencies: [`honua-sdk-dotnet#166`](https://github.com/honua-io/honua-sdk-dotnet/issues/166) (Console .NET client contracts), [`honua-sdk-js#225`](https://github.com/honua-io/honua-sdk-js/issues/225) (browser SDK contracts), [`honua-server#1162`](https://github.com/honua-io/honua-server/issues/1162) (Metadata v2 API baseline).

## Purpose

ADR-0001 is unambiguous: contracts are shared via the server-owned SDK projections, not duplicated in Console. Console-side code (Blazor Web + shared Razor components) consumes `honua-sdk-dotnet`; the embedded/generated-app JS bundles continue to consume `honua-sdk-js`.

The risk this policy addresses is that porting tickets (`honua-console#4`, `#5`, `#6`) need types and helpers that may not yet exist in either SDK if [`honua-sdk-dotnet#166`](https://github.com/honua-io/honua-sdk-dotnet/issues/166) or [`honua-sdk-js#225`](https://github.com/honua-io/honua-sdk-js/issues/225) slip behind the porting schedule.

Without a written shim policy, the porting tickets are either blocked on the SDK tickets or quietly duplicate DTOs across the Console codebase. This policy chooses a third path: a single, auditable boundary per language where temporary shims live until `#7` removes them.

## Policy

Temporary shims for SDK contracts are permitted in Console during the migration, **only** under all of the following conditions:

1. **.NET shims** live in a single boundary project: `src/Honua.Console.Contracts/SdkShims.cs` (path established by the scaffold ticket). Any partial-class files used for organization sit beside it under `src/Honua.Console.Contracts/`.
2. **Browser/JS shims** (only for code that runs inside generated-app or embed JS bundles, not Razor code paths) live in a single boundary module: `src/Honua.Console.Web/wwwroot/interop/sdk-shims.ts`.
3. Every shim has an inline `// SHIM(honua-sdk-dotnet#166): <reason>` (or `honua-sdk-js#225`, as appropriate) comment identifying the external ticket the shim is waiting on.
4. Every shim has a corresponding entry in this document's "Active Shims" section below, with an owner, language, and a removal target ticket.
5. No shim is imported from outside its boundary project/module. Razor and .NET feature code imports from `Honua.Console.Contracts`; JS interop bundles import from `wwwroot/interop/sdk-shims.ts`. This keeps the cleanup localized.
6. No shim redefines a contract that already exists in `honua-sdk-dotnet` or `honua-sdk-js`. Existing contracts must be consumed directly.
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

1. Replace the shim's body in `SdkShims.cs` with `global using Honua.Sdk.Metadata; // MetadataV2` (or similar), or remove the shim and update consumers' `using`/`import` statements to point at the SDK.
2. Remove the shim's row from "Active Shims" below.
3. Verify no feature code reaches around the boundary file with a direct import.

When "Active Shims" is empty:

1. Delete `src/Honua.Console.Contracts/SdkShims.cs` (and the `Honua.Console.Contracts` project if no other contract glue remains).
2. Delete `src/Honua.Console.Web/wwwroot/interop/sdk-shims.ts`.
3. Delete this document (or trim it to a single line noting the cleanup happened, depending on which is more useful for archeology).
4. Close [`honua-console#7`](https://github.com/honua-io/honua-console/issues/7).

## Active Shims

The current active shims are bounded to catalog/viewer/share route parity, Studio workflow package work, and the native trust/mTLS contract added by `honua-console#44`. Catalog/viewer/share shims live in `src/Honua.Console.Contracts/SdkShims.cs`; the native trust wire contract lives in `src/Honua.Console.Contracts/EnvironmentTrustShims.cs`; the Studio workflow projections currently live in the shared shell model boundary until the SDK workflow contracts land. They provide stable Console-side shapes until the `honua-sdk-dotnet#166` projection lands and `honua-console#7` replaces the shim imports.

| Shim   | Language | Added in PR | Waiting on | Owner | Target removal |
|--------|----------|-------------|------------|-------|----------------|
| Catalog search query and list request (`CatalogSearchState`, `CatalogListRequest`) | .NET | honua-console#34 | honua-sdk-dotnet#166 | Console | honua-console#7 |
| Content summary/detail route payloads (`ConsoleContentSummary`, `ConsoleContentDetail`, versions, lineage, bindings, publication, permissions, activity, usage) | .NET | honua-console#34 | honua-sdk-dotnet#166 / honua-server#1162 | Console | honua-console#7 |
| Share access and public-link fields (`ConsoleShareAccess`) | .NET | honua-console#34 | honua-sdk-dotnet#166 / honua-server#1162 | Console | honua-console#7 |
| Saved-map package and embed options (`ConsoleMapPackage`, `EmbedRouteOptions`) | .NET | honua-console#34 | honua-sdk-dotnet#166 / honua-sdk-js#225 | Console | honua-console#7 |
| Studio workflow package projections in `src/Honua.Console.Shell/Models/StudioWorkflowPackage.cs` | .NET | honua-console#40 | honua-sdk-dotnet#166 workflow/package projections and honua-server#724 workflow DAG contracts | Studio | honua-console#7 replaces with shared SDK/server projections or moves through the dedicated contract boundary |
| Environment trust contracts (`HonuaCertificateValidationStatus`, `HonuaEnvironmentTrustState`) in `src/Honua.Console.Contracts/EnvironmentTrustShims.cs` | .NET | honua-console#44 | honua-sdk-dotnet#166 (`Honua.Sdk.Abstractions.Environments`, merged on SDK trunk but not yet in a consumable package) | Console | honua-console#7 swaps to `global using …Environments.*` once the package ships #166 |
| Client-certificate validate wire contracts (`ConsoleClientCertificateValidationRequest`, `ConsoleClientCertificateValidationResult`, `ConsoleServerEnvelope<T>`, `ConsoleCertificateValidationCodes`) in `src/Honua.Console.Contracts/EnvironmentTrustShims.cs` | .NET | honua-console#44 | honua-server#1171 (`POST /api/v1/admin/security/client-certificates/validate`) / honua-sdk-dotnet#166 | Console | honua-console#7 replaces with the SDK trust client when projected |
| Operate admin HTTP shim (`HonuaAdminOperateHttpClient` and admin response records) | .NET | honua-console#60 | honua-sdk-dotnet#166 admin projections / honua-server#1162 Metadata v2 admin gaps | Operate | honua-console#7 |

On rule 6 ("no shim redefines a contract that already exists in `honua-sdk-dotnet`"): "exists" means **consumable in a restorable package**. The `honua-sdk-dotnet#166` `Honua.Sdk.Abstractions.Environments` contracts are merged on the SDK trunk but are **not** in the latest published `Honua.Sdk.Abstractions` package, and `honua-console` has no SDK package reference yet. Until that package ships #166, the trust/environment shapes are mirrored exactly behind this boundary (matching the SDK shape so the `#7` swap is a `global using` alias), not consumed directly. This is the same "do not block porting on #166" stance this policy takes for the catalog/share shims.

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
