namespace Honua.Console.Shell.Models;

public enum StudioPackageFamily
{
    Query,
    Analysis,
    Map,
    Dashboard,
    Report,
    Form,
    App
}

public enum StudioLifecycleOperation
{
    Save,
    Reopen,
    Edit,
    Publish,
    Share,
    Embed,
    Rollback
}

public sealed record StudioPackageEditorCapability(
    string Title,
    string Summary,
    IReadOnlyList<string> Controls);

public sealed record StudioPackageEditorDefinition(
    StudioPackageFamily Family,
    string RouteSegment,
    string DisplayName,
    string PackageType,
    string ContentType,
    string PromptExample,
    string Summary,
    IReadOnlyList<StudioPackageEditorCapability> RequiredEditors,
    IReadOnlyList<string> ValidationChecks,
    IReadOnlyList<string> PreviewPanels,
    IReadOnlyList<string> PublishReviewItems,
    bool UsesVegaLite = false,
    bool RequiresOfflinePolicyBeforePublish = false,
    bool ReopenedEditsCreateNewContentVersions = false,
    bool SupportsContentLifecycle = true)
{
    public string Path => $"/studio/{RouteSegment}";
}

public sealed record StudioPackageLifecycleSnapshot(
    string ItemId,
    int CurrentVersion,
    int PublishedVersion,
    int? ReopenedFromVersion,
    bool Published,
    bool Shared,
    bool Embedded,
    int? RollbackFromVersion,
    IReadOnlyList<string> Evidence)
{
    public string CurrentVersionId => $"v{CurrentVersion}";

    public string PublishedVersionId => PublishedVersion > 0 ? $"v{PublishedVersion}" : "none";
}

public sealed record StudioPublicationReadiness(
    bool OfflinePolicyReviewed = true,
    string? OfflineSyncPolicy = null);

public static class StudioPackageLifecycleSimulator
{
    public static StudioPackageLifecycleSnapshot Create(StudioPackageEditorDefinition editor)
    {
        ArgumentNullException.ThrowIfNull(editor);

        return new StudioPackageLifecycleSnapshot(
            ItemId: $"studio-{editor.RouteSegment}-mock-content",
            CurrentVersion: 1,
            PublishedVersion: 0,
            ReopenedFromVersion: null,
            Published: false,
            Shared: false,
            Embedded: false,
            RollbackFromVersion: null,
            Evidence:
            [
                $"{editor.PackageType}: draft package loaded from stable Console mock contract",
                "backend: content-version, publication, share, embed, and rollback APIs pending honua-server/honua-sdk projections"
            ]);
    }

    public static bool CanPublish(StudioPackageEditorDefinition editor, StudioPublicationReadiness readiness)
    {
        ArgumentNullException.ThrowIfNull(editor);
        ArgumentNullException.ThrowIfNull(readiness);

        if (!editor.RequiresOfflinePolicyBeforePublish)
        {
            return true;
        }

        return readiness.OfflinePolicyReviewed && !string.IsNullOrWhiteSpace(readiness.OfflineSyncPolicy);
    }

    public static StudioPackageLifecycleSnapshot Apply(
        StudioPackageEditorDefinition editor,
        StudioPackageLifecycleSnapshot snapshot,
        StudioLifecycleOperation operation,
        StudioPublicationReadiness? readiness = null)
    {
        ArgumentNullException.ThrowIfNull(editor);
        ArgumentNullException.ThrowIfNull(snapshot);

        var evidence = snapshot.Evidence.ToList();
        var readinessSnapshot = readiness ?? new StudioPublicationReadiness();

        switch (operation)
        {
            case StudioLifecycleOperation.Save:
                evidence.Add("content-version.create: saved edited package as an immutable draft version");
                return snapshot with { CurrentVersion = snapshot.CurrentVersion + 1, Evidence = evidence };

            case StudioLifecycleOperation.Reopen:
                var reopenedFrom = snapshot.PublishedVersion > 0 ? snapshot.PublishedVersion : snapshot.CurrentVersion;
                evidence.Add($"content-version.read: reopened {editor.ContentType} version v{reopenedFrom} into Studio");
                return snapshot with { ReopenedFromVersion = reopenedFrom, Evidence = evidence };

            case StudioLifecycleOperation.Edit:
                var editNote = editor.ReopenedEditsCreateNewContentVersions
                    ? "content-version.create: reopened app edit created a new version without mutating the published version"
                    : "content-version.create: structured editor change saved as a new package version";
                evidence.Add(editNote);
                return snapshot with { CurrentVersion = snapshot.CurrentVersion + 1, Evidence = evidence };

            case StudioLifecycleOperation.Publish:
                if (!CanPublish(editor, readinessSnapshot))
                {
                    throw new InvalidOperationException("Offline/sync policy must be selected and reviewed before publishing this form package.");
                }

                evidence.Add("publication.create: publish review accepted and routed to server-owned publication contract");
                return snapshot with
                {
                    Published = true,
                    PublishedVersion = snapshot.CurrentVersion,
                    Evidence = evidence
                };

            case StudioLifecycleOperation.Share:
                EnsurePublished(snapshot, operation);
                evidence.Add("share-access.update: share tier and dependency promotions reviewed");
                return snapshot with { Shared = true, Evidence = evidence };

            case StudioLifecycleOperation.Embed:
                EnsurePublished(snapshot, operation);
                evidence.Add("embed-token.mint: embeddable policy reviewed with closure refs");
                return snapshot with { Embedded = true, Evidence = evidence };

            case StudioLifecycleOperation.Rollback:
                EnsurePublished(snapshot, operation);
                var rollbackSource = snapshot.PublishedVersion;
                evidence.Add($"rollback.create: rollback requested from v{rollbackSource} to prior governed content version");
                return snapshot with
                {
                    CurrentVersion = snapshot.CurrentVersion + 1,
                    Published = true,
                    PublishedVersion = snapshot.CurrentVersion + 1,
                    RollbackFromVersion = rollbackSource,
                    Evidence = evidence
                };

            default:
                throw new ArgumentOutOfRangeException(nameof(operation), operation, "Unsupported Studio lifecycle operation.");
        }
    }

    private static void EnsurePublished(StudioPackageLifecycleSnapshot snapshot, StudioLifecycleOperation operation)
    {
        if (snapshot.Published && snapshot.PublishedVersion > 0)
        {
            return;
        }

        var operationName = operation.ToString().ToLowerInvariant();
        throw new InvalidOperationException($"{operationName} requires a published content version.");
    }
}

public static class StudioPackageEditorCatalog
{
    public static IReadOnlyList<StudioPackageEditorDefinition> Editors { get; } =
    [
        new(
            Family: StudioPackageFamily.Query,
            RouteSegment: "query",
            DisplayName: "Query",
            PackageType: "query.package",
            ContentType: "query",
            PromptExample: "Find permits issued in the last 90 days inside the flood zone.",
            Summary: "Natural-language spatial query output with source binding, predicate builder, filter readout, parameters, and map/table preview.",
            RequiredEditors:
            [
                new("Source binding", "Layer or table reference plus field mapping.", ["Source item", "Selected fields", "Spatial reference"]),
                new("Predicate builder", "Attribute and spatial predicates compiled into a readable filter.", ["Attribute rule", "Spatial relation", "Generated SQL/filter"]),
                new("Preview", "Sample result table and map extent before saving as content.", ["Map sample", "Table sample", "Result limit"])
            ],
            ValidationChecks:
            [
                "Source item is readable by the current workspace user.",
                "Selected fields exist on the source schema.",
                "Spatial predicate is supported by the source service."
            ],
            PreviewPanels: ["Generated SQL/filter", "Map preview", "Table preview"],
            PublishReviewItems: ["Content item title", "Dependency refs", "Permission requirements", "Version note"]),

        new(
            Family: StudioPackageFamily.Analysis,
            RouteSegment: "analysis",
            DisplayName: "Analysis",
            PackageType: "analysis.package",
            ContentType: "analysis",
            PromptExample: "Buffer evacuation shelters by five minutes of drive time and intersect flood risk zones.",
            Summary: "Executable spatial analysis plan with parameters, output schema, compute estimate, DAG view, preview job, and result artifact panel.",
            RequiredEditors:
            [
                new("Plan card", "Operation sequence and dependency bindings.", ["Operation", "Input binding", "Output artifact"]),
                new("Parameters", "Runtime inputs submitted to the server job runner.", ["Distance", "Units", "Worker profile"]),
                new("Pipeline", "DAG preview for queued analysis execution.", ["Nodes", "Edges", "Dry-run job"])
            ],
            ValidationChecks:
            [
                "Job runner capability is available for the requested operation.",
                "Output schema fields are named and typed.",
                "Compute estimate is acknowledged before job submit."
            ],
            PreviewPanels: ["Plan", "DAG", "Output schema", "Result artifact"],
            PublishReviewItems: ["Job policy", "Result item target", "Lineage refs", "Version note"]),

        new(
            Family: StudioPackageFamily.Map,
            RouteSegment: "map",
            DisplayName: "Map",
            PackageType: "map.package",
            ContentType: "map",
            PromptExample: "Create a public works map showing hydrants, parcels, and flood zones with inspection popups.",
            Summary: "Saved map package editor with layers, filters, style, popup, legend, basemap, extent, interactions, and publish review.",
            RequiredEditors:
            [
                new("Layer stack", "Ordered source bindings with visibility and scale rules.", ["Layer order", "Filters", "Legend"]),
                new("Style and popup", "Cartography and identify content stored in the map package.", ["Style", "Labels", "Popup fields"]),
                new("Publication", "Route, share, embed, and rollback review for a saved map.", ["Publish route", "Share tier", "Embed policy", "Rollback target"])
            ],
            ValidationChecks:
            [
                "Every layer binding resolves to a supported map source.",
                "Popup fields exist on the referenced layer.",
                "Embed closure includes all dependent source items."
            ],
            PreviewPanels: ["Map canvas", "Legend", "Popup preview", "Publish review"],
            PublishReviewItems: ["Basemap", "Layer refs", "Initial extent", "Share route", "Embed policy", "Rollback target"]),

        new(
            Family: StudioPackageFamily.Dashboard,
            RouteSegment: "dashboard",
            DisplayName: "Dashboard",
            PackageType: "dashboard.package",
            ContentType: "dashboard",
            PromptExample: "Build an operations dashboard with service requests by district and a linked inspection map.",
            Summary: "Dashboard package editor with data bindings, layout, Vega-Lite charts, map panels, tables, filters, version pinning, and responsive preview.",
            RequiredEditors:
            [
                new("Data bindings", "Source refs, field mappings, and version pins for dashboard panels.", ["Binding", "Version pin", "Refresh policy"]),
                new("Vega-Lite charts", "Charts are stored as Vega-Lite specs with visual controls and raw spec inspection.", ["Measure", "Dimension", "Vega-Lite JSON"]),
                new("Layout", "Responsive panel regions with filters and linked interactions.", ["Panels", "Filters", "Cross-filter"])
            ],
            ValidationChecks:
            [
                "Every chart declares a Vega-Lite schema URL.",
                "Panel bindings point at readable content versions.",
                "Responsive preview has desktop and narrow layouts."
            ],
            PreviewPanels: ["Responsive dashboard", "Chart preview", "Map panel", "Filter state"],
            PublishReviewItems: ["Route", "Visibility", "Embed policy", "Refresh policy", "Version pins"],
            UsesVegaLite: true),

        new(
            Family: StudioPackageFamily.Report,
            RouteSegment: "report",
            DisplayName: "Report",
            PackageType: "report.package",
            ContentType: "report",
            PromptExample: "Create a monthly infrastructure report with charts, maps, tables, and export settings.",
            Summary: "Report package editor with narrative, data bindings, Vega-Lite charts, map panels, tables, filters, version pinning, and responsive preview.",
            RequiredEditors:
            [
                new("Narrative", "Section structure, text blocks, and table/map placement.", ["Sections", "Tables", "Map snapshots"]),
                new("Vega-Lite charts", "Report charts use the same Vega-Lite spec layer as dashboards.", ["Measure", "Dimension", "Vega-Lite JSON"]),
                new("Export", "Print/export policy and scheduled refresh settings.", ["Format", "Refresh", "Embed/export policy"])
            ],
            ValidationChecks:
            [
                "Every chart declares a Vega-Lite schema URL.",
                "Narrative sections reference existing bindings.",
                "Export policy is compatible with visibility and embed settings."
            ],
            PreviewPanels: ["Report preview", "Chart preview", "Map snapshot", "Export review"],
            PublishReviewItems: ["Route", "Visibility", "Embed/export policy", "Refresh policy", "Version pins"],
            UsesVegaLite: true),

        new(
            Family: StudioPackageFamily.Form,
            RouteSegment: "form",
            DisplayName: "Form",
            PackageType: "form.package",
            ContentType: "form",
            PromptExample: "Create a field inspection form for hydrants with photos, required condition, and offline capture.",
            Summary: "Form package editor with fields, groups, validation, domains, conditional visibility, attachments, privacy, offline policy, and submit target.",
            RequiredEditors:
            [
                new("Fields", "Ordered field groups, required state, domains, and validation.", ["Group", "Field", "Domain", "Required"]),
                new("Rules", "Conditional visibility and calculated values.", ["Visibility", "Validation", "Calculated value"]),
                new("Offline and submit", "Offline/sync policy, attachment policy, privacy, and target endpoint.", ["Offline policy", "Attachment policy", "Submit target"])
            ],
            ValidationChecks:
            [
                "Offline/sync policy is reviewed before publish.",
                "Required fields have validation copy and domains where applicable.",
                "Submit target is writable by the current workspace user."
            ],
            PreviewPanels: ["Desktop form", "Mobile form", "Validation preview", "Offline policy review"],
            PublishReviewItems: ["Submit target", "Privacy", "Attachment policy", "Offline/sync policy", "Embed policy"],
            RequiresOfflinePolicyBeforePublish: true),

        new(
            Family: StudioPackageFamily.App,
            RouteSegment: "app",
            DisplayName: "App",
            PackageType: "app.package",
            ContentType: "app",
            PromptExample: "Assemble a field operations app with map, dashboard, inspection form, and supervisor report.",
            Summary: "App package editor with pages, components, navigation, bindings, actions, permissions, preview/reopen, and share/embed policy.",
            RequiredEditors:
            [
                new("Pages", "Routes and page composition from map, dashboard, form, and report components.", ["Route", "Page", "Component"]),
                new("Actions", "Bound actions and permissions for generated-app runtime.", ["Action", "Permission", "Binding"]),
                new("Lifecycle", "Preview, reopen, share, embed, and versioned edits for published apps.", ["Preview", "Share/embed", "New content version"])
            ],
            ValidationChecks:
            [
                "Every component references a saved package or content version.",
                "App actions declare permission requirements.",
                "Reopened edits create new content versions instead of mutating published state."
            ],
            PreviewPanels: ["Generated app preview", "Navigation", "Action trace", "Share/embed review"],
            PublishReviewItems: ["Stable routes", "Permissions", "Share policy", "Embed policy", "Version note"],
            ReopenedEditsCreateNewContentVersions: true)
    ];

    public static IReadOnlyList<StudioLifecycleOperation> FullLifecycleOperations { get; } =
    [
        StudioLifecycleOperation.Save,
        StudioLifecycleOperation.Reopen,
        StudioLifecycleOperation.Edit,
        StudioLifecycleOperation.Publish,
        StudioLifecycleOperation.Share,
        StudioLifecycleOperation.Embed,
        StudioLifecycleOperation.Rollback
    ];

    public static StudioPackageEditorDefinition? Find(string? routeSegment)
    {
        if (string.IsNullOrWhiteSpace(routeSegment))
        {
            return null;
        }

        return Editors.FirstOrDefault(editor =>
            string.Equals(editor.RouteSegment, routeSegment, StringComparison.OrdinalIgnoreCase));
    }
}
