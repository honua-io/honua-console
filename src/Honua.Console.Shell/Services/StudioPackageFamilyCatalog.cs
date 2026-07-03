using Honua.Sdk.Studio.Packages;
using Honua.Console.Contracts;
using Honua.Console.Shell.Models;
// Disambiguate from the editor-catalog enum Honua.Console.Shell.Models.StudioPackageFamily; this
// catalog maps the honua-server wire enum to Console display metadata.
using StudioPackageFamily = Honua.Sdk.Studio.Packages.StudioPackageFamily;

namespace Honua.Console.Shell.Services;

/// <summary>
/// Single source of the Console-facing display metadata for each honua-server Studio package family,
/// and the mapping between the server <see cref="StudioPackageFamily"/> enum and the Console workflow
/// id used in routes/UI. Shared by the server-backed and demo authoring shells so family naming stays
/// consistent and is not duplicated.
/// </summary>
public static class StudioPackageFamilyCatalog
{
    private sealed record FamilyMeta(
        StudioPackageFamily Family,
        string Id,
        string Label,
        string PackageType,
        string Description);

    private static readonly IReadOnlyList<FamilyMeta> Families =
    [
        new(StudioPackageFamily.Map, "map", "Map", "map.package", "Layers, style, filters, popups, legend, and extent."),
        new(StudioPackageFamily.Dashboard, "dashboard", "Dashboard", "dashboard.package", "Charts, maps, KPIs, filters, and linked selections."),
        new(StudioPackageFamily.Report, "report", "Report", "report.package", "Narrative sections, maps, tables, charts, and export settings."),
        new(StudioPackageFamily.Form, "form", "Form", "form.package", "Fields, rules, attachments, offline policy, and submit action."),
        new(StudioPackageFamily.App, "app", "App", "app.package", "Routes, panels, generated app assets, permissions, and theme tokens."),
        new(StudioPackageFamily.Query, "query", "Query", "query.package", "Source layers, filters, joins, spatial predicates, and output schema."),
        new(StudioPackageFamily.Analysis, "analysis", "Analysis", "analysis.package", "Inputs, method, parameters, output package, and job profile."),
        new(StudioPackageFamily.Workflow, "workflow", "Workflow", "workflow.package", "Sources, transforms, sinks, triggers, dry-runs, and artifacts."),
        new(StudioPackageFamily.Geoprocessing, "gp", "GP service", "workflow.package", "Process endpoint, parameters, worker profile, and invocation policy."),
        new(StudioPackageFamily.Etl, "etl", "ETL", "workflow.package", "Extract, transform, load targets, schedules, and rejected-row evidence.")
    ];

    public static IReadOnlyList<StudioPackageFamily> AllFamilies { get; } =
        Families.Select(meta => meta.Family).ToArray();

    public static bool IsPublicationFamily(StudioPackageFamily family) =>
        family is not StudioPackageFamily.Query and not StudioPackageFamily.Analysis;

    public static StudioPackageFamily? ParseId(string? workflowId) =>
        Families.FirstOrDefault(meta => string.Equals(meta.Id, workflowId, StringComparison.OrdinalIgnoreCase))?.Family;

    public static string GetWorkflowId(StudioPackageFamily family) => Resolve(family).Id;

    public static string GetPackageType(StudioPackageFamily family) => Resolve(family).PackageType;

    /// <summary>Builds the Console workflow option for a server-reported family descriptor.</summary>
    public static StudioWorkflowOption ToOption(StudioPackageFamilyDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        var meta = Resolve(descriptor.Family);
        return new StudioWorkflowOption(
            meta.Id,
            meta.Label,
            meta.PackageType,
            meta.Description,
            descriptor.CurrentSchemaVersion,
            descriptor.Format,
            FormatSupportLevel(descriptor.SupportLevel),
            descriptor.PreviewSupported,
            descriptor.PublishSupported,
            descriptor.Limitations);
    }

    /// <summary>Builds a Console workflow option without server capability data (demo composition).</summary>
    public static StudioWorkflowOption ToOption(StudioPackageFamily family)
    {
        var meta = Resolve(family);
        return new StudioWorkflowOption(meta.Id, meta.Label, meta.PackageType, meta.Description);
    }

    public static string FormatSupportLevel(StudioPackageSupportLevel level) => level switch
    {
        StudioPackageSupportLevel.Supported => "Supported",
        StudioPackageSupportLevel.Limited => "Limited",
        _ => "Unsupported"
    };

    private static FamilyMeta Resolve(StudioPackageFamily family) =>
        Families.FirstOrDefault(meta => meta.Family == family)
            ?? new FamilyMeta(family, family.ToString().ToLowerInvariant(), family.ToString(), "package", string.Empty);
}
