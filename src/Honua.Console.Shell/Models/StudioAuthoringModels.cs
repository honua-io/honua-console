namespace Honua.Console.Shell.Models;

public static class StudioAuthoringContract
{
    public const string Name = "studio-authoring-shell";
    public const string Version = "v1";

    // The lifecycle rail reflects the honua-server package lifecycle: a mutable draft becomes an
    // immutable content version (saved), which can be published. "Preview" is a transient action
    // (preview-plan), not a stored server state, so it is not a rail step.
    public static IReadOnlyList<StudioLifecycleDescriptor> LifecycleDescriptors { get; } =
    [
        new(StudioPackageLifecycleState.Draft, "Draft", "Editable package draft", "studio-lifecycle-draft"),
        new(StudioPackageLifecycleState.SavedVersion, "Saved version", "Immutable content version", "studio-lifecycle-saved"),
        new(StudioPackageLifecycleState.Published, "Published", "Published release", "studio-lifecycle-published")
    ];
}

public enum StudioPackageLifecycleState
{
    Draft,
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
    string Description,
    string SchemaVersion = "",
    string Format = "",
    string SupportLevel = "",
    bool PreviewSupported = false,
    bool PublishSupported = false,
    IReadOnlyList<string>? Limitations = null)
{
    public IReadOnlyList<string> Limitations { get; init; } = Limitations ?? [];
}

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

/// <summary>
/// A blocked / missing-binding surface for the Studio shell, mirroring the Operate capability-state
/// pattern. Non-null when the server-owned package lifecycle cannot be reached or is not configured
/// (e.g. <c>Honua:Server:BaseUrl</c> unset), or when the server rejects/omits data. Never carries mock
/// package data.
/// </summary>
public sealed record StudioAuthoringBindingState(
    string State,
    string Contract,
    string Detail);

/// <summary>
/// The transient result of a server <c>preview-plan</c> call (honua-server#1181). The plan describes
/// whether preview runs synchronously or requires a job and the planned steps; it is not a rendered
/// preview. Rendered preview output / generated-app preview has no server contract yet, so the page
/// surfaces a missing-binding note rather than a fabricated canvas.
/// </summary>
public sealed record StudioPreviewPlanView(
    bool Synchronous,
    bool RequiresJob,
    IReadOnlyList<string> Steps);

/// <summary>
/// Identifies the live server draft (and saved version, once created) backing the current circuit.
/// <see cref="Generation"/> is the optimistic-concurrency token the adapter advances after each
/// server mutation.
/// </summary>
public sealed record StudioDraftHandle(
    string DraftId,
    string ItemId,
    string PackageKey,
    long Generation,
    string? CurrentVersionId = null);

public sealed record StudioAuthoringSession(
    IReadOnlyList<StudioWorkflowOption> Workflows,
    string SelectedWorkflowId,
    string Prompt,
    IReadOnlyList<StudioClarificationQuestion> Clarifications,
    StudioPackageSnapshot ActivePackage,
    StudioAuthoringBindingState? BindingState = null,
    StudioPreviewPlanView? PreviewPlan = null,
    StudioDraftHandle? Draft = null,
    string? StatusMessage = null)
{
    /// <summary>True when the active package is backed by a live server draft.</summary>
    public bool HasServerDraft => Draft is not null;
}
