# SDK Shim Policy For The Console Migration

Status: filed 2026-05-23.

Decision source: [ADR-0001: Unified Honua Console Runtime](../adr/0001-unified-honua-console-runtime.md).
Related: [`CONSOLE_PATTERNS_CHARTER.md`](./CONSOLE_PATTERNS_CHARTER.md) section "DRY: contracts only via SDK".
Cleanup ticket: [`honua-console#7`](https://github.com/honua-io/honua-console/issues/7) (wire Console to shared metadata / content / package / RBAC contracts).
External dependency: [`honua-sdk-js#225`](https://github.com/honua-io/honua-sdk-js/issues/225) (Console SDK contracts), [`honua-server#1162`](https://github.com/honua-io/honua-server/issues/1162) (Metadata v2 API baseline).

## Purpose

ADR-0001 is unambiguous: contracts are shared via `@honua/sdk-js`, not duplicated in Console. The risk this policy addresses is that porting tickets (`honua-console#4`, `#5`, `#6`) need types and helpers that may not yet exist in `@honua/sdk-js` if `honua-sdk-js#225` slips behind the porting schedule.

Without a written shim policy, the porting tickets are either blocked on the SDK ticket or quietly duplicate DTOs across the Console codebase. This policy chooses a third path: a single, auditable boundary file where temporary shims live until `#7` removes them.

## Policy

Temporary shims for SDK contracts are permitted in Console during the migration, **only** under all of the following conditions:

1. Every shim is declared in a single boundary file: `src/contracts/sdk-shims.ts` (path established by the scaffold ticket).
2. Every shim has an inline `// SHIM(honua-sdk-js#225): <reason>` comment identifying the external ticket the shim is waiting on.
3. Every shim has a corresponding entry in this document's "Active Shims" section below, with an owner and a removal target ticket.
4. No shim is imported from outside `src/contracts/`. Feature code imports from `src/contracts/`, never directly from `@honua/sdk-js` types the shim covers - this keeps the cleanup localized.
5. No shim redefines a contract that already exists in `@honua/sdk-js`. Existing contracts must be consumed directly.
6. [`honua-console#7`](https://github.com/honua-io/honua-console/issues/7) is gated on this file containing zero entries in "Active Shims". When `#7` is accepted, the shim boundary file is deleted along with this section.

## What A Shim Looks Like

```ts
// src/contracts/sdk-shims.ts

// SHIM(honua-sdk-js#225): MetadataV2 not yet projected. Mirrors the server payload
// described in honua-server#1162 section "GET /content/items/:id". Remove when the SDK
// exports `import type { MetadataV2 } from "@honua/sdk-js"`.
export interface MetadataV2 {
  // ...
}
```

The shape mirrors the upstream contract exactly - the goal is that switching to the real SDK type later is a one-line import change inside the boundary file.

## What Is Not A Shim

The following are not covered by this policy and remain prohibited:

- Re-declaring a server DTO inline inside a feature module.
- Re-exporting an existing `@honua/sdk-js` type under a Console-flavored name.
- Wrapping an existing `@honua/sdk-js` type with a Console-specific field. (Add the field to the SDK projection via `honua-sdk-js#225`, or carry the Console-only field as a sibling type that composes the SDK type - not as a renamed clone.)

## Acceptance Of A Shim

A new shim is added by:

1. Opening a PR that adds the entry to `src/contracts/sdk-shims.ts` with the inline `SHIM(...)` comment.
2. Adding an "Active Shims" row in this document in the same PR.
3. Linking the PR from `honua-sdk-js#225` (or the appropriate external ticket) so the SDK owner sees the demand signal.
4. Linking the PR from `honua-console#7` so the cleanup ticket inherits the removal task.

A shim that has not been added to this document does not satisfy the policy and is treated as a regression in code review.

## Removal Of A Shim

When the upstream contract lands in `@honua/sdk-js`:

1. Replace the shim's body with `export type { Foo } from "@honua/sdk-js";` (or remove and update the import in `src/contracts/index.ts`).
2. Remove the shim's row from "Active Shims" below.
3. Verify no feature code reaches around the boundary file with a direct import.

When "Active Shims" is empty:

1. Delete `src/contracts/sdk-shims.ts`.
2. Delete this document (or trim it to a single line noting the cleanup happened, depending on which is more useful for archeology).
3. Close [`honua-console#7`](https://github.com/honua-io/honua-console/issues/7).

## Active Shims

None yet. Entries are added by the scaffold (`honua-console#2`) and the porting tickets (`#4`, `#5`, `#6`) as they discover gaps in `honua-sdk-js#225`.

| Shim                | Added in PR | Waiting on              | Owner | Target removal       |
|---------------------|-------------|-------------------------|-------|----------------------|
| (none)              | -           | -                       | -     | -                    |

## Why This Boundary, Not "Whatever Works"

Two alternatives were considered:

- **Block all porting on `honua-sdk-js#225`.** Cleaner, but stalls porting work for the duration of an external ticket Console does not own. Rejected.
- **Allow inline DTO duplication wherever needed, clean up later.** Faster in the short term, but creates a diffuse cleanup with no list of what needs to be deleted. Rejected - this is the exact failure mode the DRY constraint in ADR-0001 warns against.

The single-boundary approach takes a measured shortcut: porting work continues, every shortcut is visible in one file and one document, and removal is a single coordinated PR rather than a scavenger hunt.
