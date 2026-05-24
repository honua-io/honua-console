namespace Honua.Console.Shell.Models;

public static class StudioAuthoringContract
{
    public const string Name = "studio-authoring-shell";
    public const string Version = "v1";
    public const string PackageSchemaVersion = "package-shell/v1";

    public static IReadOnlyList<StudioLifecycleDescriptor> LifecycleDescriptors { get; } =
    [
        new(StudioPackageLifecycleState.Draft, "Draft", "Editable package", "studio-lifecycle-draft"),
        new(StudioPackageLifecycleState.Preview, "Preview", "Runnable preview", "studio-lifecycle-preview"),
        new(StudioPackageLifecycleState.SavedVersion, "Saved version", "Versioned content", "studio-lifecycle-saved"),
        new(StudioPackageLifecycleState.Published, "Published", "Shared release", "studio-lifecycle-published")
    ];
}

public enum StudioPackageLifecycleState
{
    Draft,
    Preview,
    SavedVersion,
    Published
}

public enum StudioValidationSeverity
{
    Info,
    Warning,
    Blocker,
    Passed
}

public sealed record StudioLifecycleDescriptor(
    StudioPackageLifecycleState State,
    string Label,
    string Description,
    string CssClass);

public sealed record StudioWorkflowOption(
    string Id,
    string Label,
    string PackageType,
    string Description);

public sealed record StudioRecentProject(
    string Title,
    string PackageType,
    StudioPackageLifecycleState State,
    string UpdatedLabel);

public sealed record StudioClarificationQuestion(
    string Id,
    string Label,
    string Reason,
    IReadOnlyList<StudioClarificationChoice> Choices);

public sealed record StudioClarificationChoice(
    string Id,
    string Label,
    string Effect);

public sealed record StudioPackageSnapshot(
    string ContractName,
    string ContractVersion,
    string PackageRef,
    string PackageType,
    string SchemaVersion,
    string Title,
    string Summary,
    StudioPackageLifecycleState LifecycleState,
    IReadOnlyList<string> Assumptions,
    IReadOnlyList<StudioDataBindingSummary> DataBindings,
    IReadOnlyList<StudioPackageWarning> Warnings,
    IReadOnlyList<StudioValidationItem> ValidationItems,
    IReadOnlyList<StudioProvenanceEvent> Provenance);

public sealed record StudioDataBindingSummary(
    string Id,
    string Label,
    string SourceRef,
    string Status);

public sealed record StudioPackageWarning(
    string Id,
    string Message,
    string Target);

public sealed record StudioValidationItem(
    StudioValidationSeverity Severity,
    string Label,
    string Detail);

public sealed record StudioProvenanceEvent(
    string Actor,
    string Action,
    string Evidence);

public sealed record StudioAuthoringSession(
    IReadOnlyList<StudioWorkflowOption> Workflows,
    string SelectedWorkflowId,
    string Prompt,
    IReadOnlyList<StudioClarificationQuestion> Clarifications,
    StudioPackageSnapshot ActivePackage,
    IReadOnlyList<StudioRecentProject> RecentProjects);
