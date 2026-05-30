# Honua Console Shared Component API Reference

Status: filed 2026-05-28 as a reference doc for the shared Razor component
library.

Decision source:
[ADR-0001: Unified Honua Console Runtime](../adr/0001-unified-honua-console-runtime.md).
Pattern source:
[Console Patterns Charter](../migration/CONSOLE_PATTERNS_CHARTER.md) §1, §5, §10.

This document enumerates the reusable Razor components that ship in the
shared component library and their public parameters/events. It is
generated from the component source under
`src/Honua.Console.Shell/Components/` and
`src/Honua.Console.Shell/Layout/`; every parameter listed below maps to a
`[Parameter]` declaration in the cited `.razor` file. When a component
gains or changes a parameter, update the matching row in the same PR.

## Project note

The Console Patterns Charter (§1, §10) names the shared component project
`Honua.Console.Components`. The implemented project is currently
`Honua.Console.Shell` (`src/Honua.Console.Shell/`), and the reusable
components live under its `Components/` and `Layout/` folders. The charter
intent — one shared Razor library consumed by the web host and the
optional MAUI Blazor Hybrid host — is unchanged; only the assembly name
differs from the charter's working name. This reference tracks the
implemented project.

Namespaces (from `src/Honua.Console.Shell/_Imports.razor`):

- `Honua.Console.Shell.Components`
- `Honua.Console.Shell.Components.Studio`
- `Honua.Console.Shell.Layout`
- `Honua.Console.Shell.Models` (parameter model types)

All `[Parameter]` types and their members are owned by
`src/Honua.Console.Shell/Models/`; this reference does not redefine them
(charter §6, DRY).

---

## 1. Surface and state components

These are the shell-owned exception/empty/state primitives the charter
(§5) requires feature routes to reuse instead of authoring bespoke
403/404/empty copy.

### `<ConsoleStateView>`

Source: `Components/ConsoleStateView.razor`.

The single canonical state surface. Routes render it with a stable `Kind`
string for loading, empty, unauthenticated, forbidden, missing,
unavailable, unsupported-service, and unsupported-package states (route
map §7). It renders a kicker, title, message, and one optional action
link.

| Parameter | Type | Default | Notes |
|---|---|---|---|
| `Kind` | `string` | `"empty"` | Sets the `console-state-<Kind>` CSS class. Conventional values per route map §7: `loading`, `empty`, `unauthenticated`, `forbidden`, `missing`, `unavailable`, `unsupported-service`, `unsupported-package`. |
| `Kicker` | `string` | `"Console state"` | Small eyebrow label above the title. |
| `Title` | `string` | `"Nothing to show"` | Heading text. |
| `Message` | `string` | `"This route did not return content."` | Body copy. |
| `ActionHref` | `string` | `""` | Optional CTA link target. The action renders only when both `ActionHref` and `ActionLabel` are non-empty. |
| `ActionLabel` | `string` | `""` | Optional CTA link text. |

Events: none. Accessibility: wraps content in `aria-live="polite"`.

Usage (from `Pages/CatalogPage.razor`):

```razor
<ConsoleStateView Kind="empty"
                  Kicker="Empty catalog"
                  Title="No content matched"
                  Message="Try a broader search or publish the first item into this workspace."
                  ActionHref="/operate/publishing"
                  ActionLabel="Open Publishing" />
```

### `<EmptyState>`

Source: `Components/EmptyState.razor`.

A per-area "no items here" panel. It composes a `No <Subject>` heading and
an area-scoped muted line, plus one optional action link. Prefer
`<ConsoleStateView Kind="empty">` for route-level empties; `<EmptyState>`
is the lighter in-panel variant used inside Operate area lists.

| Parameter | Type | Default | Notes |
|---|---|---|---|
| `Area` | `string` | `"operate"` | Area label; title-cased for display. Empty/whitespace renders as `Console`. |
| `Subject` | `string` | `"items"` | Pluralized noun for the heading (`No <Subject>`). |
| `ActionHref` | `string?` | `null` | Optional CTA link target. |
| `ActionText` | `string?` | `null` | Optional CTA link text. The action renders only when both `ActionHref` and `ActionText` are non-empty. |

Events: none.

### `<MissingItemView>`

Source: `Components/MissingItemView.razor`.

A "not found" heading block for detail routes whose id did not resolve.
For the canonical route-map missing surface use
`<ConsoleStateView Kind="missing">`; this component is the heading-only
variant.

| Parameter | Type | Default | Notes |
|---|---|---|---|
| `Kind` | `string` | `"item"` | Item-kind hint, title-cased per word; renders `<Kind> Not Found`. Empty/whitespace renders `Item`. |
| `AreaLabel` | `string?` | `null` | Optional kicker above the heading; omitted when null/whitespace. |

Events: none.

---

## 2. Operate evidence components

Shared evidence renderers for the Operate observability surface (route map
§6.5, `honua-console#41`). Parameter model types are defined in
`Models/OperateObservabilityModel.cs`.

### `<EvidenceList>`

Source: `Components/EvidenceList.razor`.

Renders a heading plus a list of raw-evidence links (label, kind, detail).
Renders nothing when `Items` is empty.

| Parameter | Type | Default | Notes |
|---|---|---|---|
| `Heading` | `string` | `"Evidence"` | Section heading. |
| `Items` | `IReadOnlyList<OperateEvidenceLink>` | empty array | Evidence link rows; each has `Href`, `Label`, `Kind`, `Detail`. |

Events: none.

### `<AiAdvisoryPanel>`

Source: `Components/AiAdvisoryPanel.razor`.

Renders an AI advisory summary, evidence links, and suggested next actions
beside raw evidence. Renders nothing when `Advisory` is `null`.

| Parameter | Type | Default | Notes |
|---|---|---|---|
| `Advisory` | `OperateAiAdvisory?` | `null` | Advisory record (`Summary`, `EvidenceLinks`, `SuggestedActions`). Null hides the panel. |

Events: none.

### `<EventDetailPanel>`

Source: `Components/EventDetailPanel.razor`.

Detail aside for a single Operate event: message, category, identity/trace
fields, raw evidence (via `<EvidenceList>`), AI advisory (via
`<AiAdvisoryPanel>`), lifecycle, and related objects.

| Parameter | Type | Default | Notes |
|---|---|---|---|
| `Event` | `OperateEventRow` | `OperateObservabilityFixture.Default.Events.First()` | Event to render. Default is the first seeded fixture row, so the panel renders without an explicit binding during scaffolding. |

Events: none. Composes `<EvidenceList>` and `<AiAdvisoryPanel>`.

### `<JobDetailPanel>`

Source: `Components/JobDetailPanel.razor`.

Detail aside for a single job run: status, identity fields, progress bar,
stages, logs, artifacts (via `<EvidenceList>`), metrics, and allowed
actions. Action buttons render disabled with an unavailable-reason title
because job action APIs are not yet wired (charter §11).

| Parameter | Type | Default | Notes |
|---|---|---|---|
| `Job` | `OperateJobRun` | `OperateObservabilityFixture.Default.Jobs.First()` | Job run to render. Default is the first seeded fixture row. |

Events: none. Composes `<EvidenceList>`.

---

## 3. Studio authoring components

Components for the package-first Studio shell (route map §6.2). Parameter
model types are in `Models/StudioAuthoringModels.cs` and
`Models/StudioPackageEditorCatalog.cs`.

### `<StudioLifecycleRail>`

Source: `Components/Studio/StudioLifecycleRail.razor`.

Renders the package lifecycle states from
`StudioAuthoringContract.LifecycleDescriptors` and highlights the current
one. Uses `role="list"` with `aria-current="step"` on the active state.

| Parameter | Type | Default | Notes |
|---|---|---|---|
| `CurrentState` | `StudioPackageLifecycleState` | `StudioPackageLifecycleState.Draft` | The lifecycle state to mark current. |

Events: none.

### `<StudioPackageInspector>`

Source: `Components/Studio/StudioPackageInspector.razor`.

The package inspector aside: contract/version, package ref, schema,
lifecycle state, title/summary, assumptions, data bindings, validation
(via `<StudioValidationPanel>`), and provenance.

| Parameter | Type | Default | Notes |
|---|---|---|---|
| `Package` | `StudioPackageSnapshot` | (required) | `[Parameter, EditorRequired]`. Snapshot supplying all inspector fields including `ValidationItems` and `Warnings`. |

Events: none. Composes `<StudioValidationPanel>`.

### `<StudioValidationPanel>`

Source: `Components/Studio/StudioValidationPanel.razor`.

Renders warnings and validation items with severity-based CSS classes
(`Blocker`, `Warning`, `Passed`, info). Used standalone or inside
`<StudioPackageInspector>`.

| Parameter | Type | Default | Notes |
|---|---|---|---|
| `Items` | `IReadOnlyList<StudioValidationItem>` | empty array | Validation rows (`Severity`, `Label`, `Detail`). |
| `Warnings` | `IReadOnlyList<StudioPackageWarning>` | empty array | Warning rows (`Target`, `Message`); the warning list is omitted when empty. |

Events: none.

### `<StudioPackageEditor>`

Source: `Components/StudioPackageEditor.razor`.

The Console-native Studio package editor for the seven package families
(`honua-console#39`). It renders a family-specific field set (query,
analysis, map, dashboard/report, form, app), a validation/preview pair,
editor-coverage, publish-review, lifecycle controls, and a live package
JSON inspector. Publish is gated on the family's publication readiness
(e.g. offline/sync policy review for forms). Lifecycle and bindings use
stable mock refs until shared content/version/publication contracts land
(see [`docs/studio/package-editor-routes.md`](../studio/package-editor-routes.md)).

| Parameter | Type | Default | Notes |
|---|---|---|---|
| `Editor` | `StudioPackageEditorDefinition` | (required) | `[Parameter, EditorRequired]`. Drives display name, package/content type, family-specific section switch, validation checks, preview panels, required editors, publish-review items, and the offline-policy publish gate. |

Events: none (lifecycle/validation/preview actions are internal
`@onclick` handlers, not exposed as `EventCallback` parameters).

### `<WorkflowAreaPage>`

Source: `Components/WorkflowAreaPage.razor`.

A generic top-level area landing page that looks up a
`ConsoleWorkflowArea` from `ConsoleRouteMap` by id and renders its name,
description, boundary, and path. Renders an "Unsupported Console Area"
state when the id is not registered.

| Parameter | Type | Default | Notes |
|---|---|---|---|
| `AreaId` | `string` | `""` | Area id resolved via `ConsoleRouteMap.FindArea(AreaId)` in `OnParametersSet`. |

Events: none.

---

## 4. Layouts

Source: `src/Honua.Console.Shell/Layout/`. Both inherit
`LayoutComponentBase`; their content is supplied through the standard
`@Body` render fragment (no custom `[Parameter]` members).

### `ConsoleLayout`

Source: `Layout/ConsoleLayout.razor`. The default shell layout
(`DefaultLayout` in `ConsoleRoutes.razor`). Renders the brand, primary nav
from `ConsoleRouteMap.Areas`, the Operate secondary nav (shown only on
`/operate/*` routes via `ConsoleRouteMap.IsOperateRoute`), the native-host
nav, and `@Body`. Injects `NavigationManager`.

### `EmbedLayout`

Source: `Layout/EmbedLayout.razor`. The shell-less layout for
`/embed/maps/:mapId` (route map §6.7): a single `console-embed-root`
`<main>` wrapping `@Body`, with no nav chrome.

---

## 5. Maintenance

When a reusable component is added, removed, or has its `[Parameter]`
surface changed under `src/Honua.Console.Shell/Components/` or
`Layout/`, update the matching section here in the same PR. Parameter
model types remain owned by `src/Honua.Console.Shell/Models/`; this doc
cites them but does not duplicate their definitions (charter §6).
