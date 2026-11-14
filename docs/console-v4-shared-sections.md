---
type: reference
title: "Console v4 shared sections"
description: "Shared quiet panels, informational read states, and responsive filter and table composition."
---
# Console v4 shared sections

The shared v4 flag controls these presentation options. Defaults and flag-off styling keep the boxed panels and existing state presentation.

Use `ConsolePanel` with `Surface="ConsolePanelSurface.Quiet"` for page-wide controls or tab content. Set `Heading="ConsolePanelHeading.VisuallyHidden"` when the page already has a visible title for that section. Always supply a meaningful `Title` and a unique `HeadingId`: the heading stays in the accessibility tree and names the section through `aria-labelledby`. Actions stay visible even when the title is hidden. The default `Heading` is `Visible`.

Use `ConsoleStateView` with `Treatment="ConsoleStateTreatment.Informational"` for neutral read-state guidance. This removes the outer box and status edge in v4. Keep the original `Kind`, title, message, heading level and actions; this option changes appearance only. It also works on `HonuaState`. Do not apply it to failures that require a warning or danger treatment.

For filters above a table, use `class="console-filter-bar console-filter-bar-quiet"` on the filter container, followed by `ConsoleTableRegion` as a sibling. Keep labels associated with their controls. At 390px filters form a single column; at wider widths they wrap into columns of at least 12rem. The quiet filter modifier removes the outer box in v4.

```razor
<ConsolePanel Title="Layers" HeadingId="service-layers"
              Surface="ConsolePanelSurface.Quiet"
              Heading="ConsolePanelHeading.VisuallyHidden">
    <div class="console-filter-bar console-filter-bar-quiet">
        <label>Search layers<input type="search" @bind="search" /></label>
        <label>Geometry<select @bind="geometry"><option>All types</option></select></label>
    </div>
    <ConsoleTableRegion Label="Service layers">
        <table class="console-table">
            <caption>Service layers</caption>
            @* Existing headers, rows and actions. *@
        </table>
    </ConsoleTableRegion>
</ConsolePanel>
```

Retain the table's columns, caption and row actions on mobile. The existing table region provides its accessible name, keyboard focus and horizontal scrolling; keep scrolling inside that region rather than around the page or filters. Avoid a fixed minimum width on the enclosing panel or grid item. Existing `console-summary` metrics remain the compact summary contract.
