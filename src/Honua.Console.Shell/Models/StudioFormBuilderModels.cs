namespace Honua.Console.Shell.Models;

/// <summary>
/// Mutable Studio form-builder editor state. This is Console-owned authoring state bound to the
/// editor UI; it is mapped to and from the server-owned <c>honua.form-package.v1</c> document
/// (honua-server#1184) by <see cref="StudioFormPackageMapper"/>. It is not a server wire contract
/// and is never persisted as a standing mock data source: the only persistence path is the real
/// honua-server form package lifecycle behind the Honua.Console.Contracts shim.
/// </summary>
public sealed class StudioFormEditorState
{
    /// <summary>Stable server package id. Null until the first draft is created server-side.</summary>
    public string? FormId { get; set; }

    /// <summary>Server package version currently open in the editor.</summary>
    public int Version { get; set; } = 1;

    /// <summary>Server lifecycle status: draft, published, or archived.</summary>
    public string Status { get; set; } = HonuaFormStatuses.Draft;

    /// <summary>Optimistic-concurrency ETag for draft updates. Empty for a not-yet-saved draft.</summary>
    public string ETag { get; set; } = string.Empty;

    /// <summary>Published version a reopened draft was created from, when applicable.</summary>
    public int? ReopenedFromVersion { get; set; }

    public string Title { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    // Submit target (AC#3): the feature service + layer accepting submissions.
    public string ServiceId { get; set; } = string.Empty;

    public int LayerId { get; set; }

    public List<StudioFormFieldEditor> Fields { get; } = [];

    // Submit policy.
    public bool AllowCreate { get; set; } = true;

    public bool AllowUpdate { get; set; }

    public bool AllowDelete { get; set; }

    public bool RequiresGeometry { get; set; } = true;

    public bool AllowAttachments { get; set; } = true;

    // Attachment policy.
    public bool AttachmentsEnabled { get; set; }

    public int? MaxAttachmentsPerSubmission { get; set; }

    /// <summary>Comma- or newline-separated MIME allowlist, e.g. <c>image/*, application/pdf</c>.</summary>
    public string AllowedContentTypes { get; set; } = string.Empty;

    public bool RequireExifStripping { get; set; }

    public bool RequireFaceBlur { get; set; }

    public bool RequireRedaction { get; set; }

    // Privacy policy.
    public int? PrivacyRetentionDays { get; set; }

    public bool CaptureActor { get; set; } = true;

    public bool CaptureDeviceId { get; set; } = true;

    /// <summary>Comma- or newline-separated privacy transforms (none, auditOnly, minimizeAudit).</summary>
    public string RequiredTransformations { get; set; } = string.Empty;

    // Offline / sync policy (AC#2).
    public bool OfflineEnabled { get; set; }

    public bool ReplicaTransportEnabled { get; set; } = true;

    public bool FieldCollectionTransportEnabled { get; set; } = true;

    public string ConflictReviewMode { get; set; } = "defer";

    /// <summary>
    /// Explicit operator acknowledgment that the offline/sync policy was reviewed. Publish is blocked
    /// until this is set, making the offline policy an explicit pre-publish decision (AC#2).
    /// </summary>
    public bool OfflinePolicyReviewed { get; set; }

    /// <summary>The most recent server validation result for the open version, or null when stale.</summary>
    public StudioFormValidationView? LastValidation { get; set; }

    public bool IsExistingPackage => !string.IsNullOrWhiteSpace(FormId);

    public bool IsPublished =>
        string.Equals(Status, HonuaFormStatuses.Published, StringComparison.OrdinalIgnoreCase);
}

public static class HonuaFormStatuses
{
    public const string Draft = "draft";
    public const string Published = "published";
    public const string Archived = "archived";
}

/// <summary>Mutable per-field authoring row.</summary>
public sealed class StudioFormFieldEditor
{
    public string FieldId { get; set; } = string.Empty;

    public string Label { get; set; } = string.Empty;

    /// <summary>Section / group label. Sections are derived from distinct group labels on save.</summary>
    public string Group { get; set; } = string.Empty;

    public string Type { get; set; } = "text";

    /// <summary>Target layer attribute. Not required for attachment-type fields.</summary>
    public string TargetField { get; set; } = string.Empty;

    public bool Required { get; set; }

    public bool Private { get; set; }

    // Domain.
    public string DomainKind { get; set; } = "none"; // none | coded | range

    /// <summary>Coded-value choices, one per line as <c>code</c> or <c>code=label</c>.</summary>
    public string Choices { get; set; } = string.Empty;

    public string RangeMin { get; set; } = string.Empty;

    public string RangeMax { get; set; } = string.Empty;

    // Single declarative validation rule (kept simple; the server supports a rule array).
    public string ValidationType { get; set; } = "none"; // none | minLength | maxLength | min | max | regex

    public string ValidationValue { get; set; } = string.Empty;

    public string ValidationMessage { get; set; } = string.Empty;

    // Conditional visibility.
    public string VisibilityDependsOn { get; set; } = string.Empty;

    public string VisibilityOperator { get; set; } = "equals";

    public string VisibilityValue { get; set; } = string.Empty;

    // Attachment-field policy (used when Type == attachment).
    public int? AttachmentMaxCount { get; set; }

    public bool IsAttachment =>
        string.Equals(Type, "attachment", StringComparison.OrdinalIgnoreCase);
}

public static class StudioFormFieldTypes
{
    public static IReadOnlyList<string> All { get; } =
        ["text", "number", "date", "choice", "geometry", "attachment"];
}

public static class StudioFormVisibilityOperators
{
    public static IReadOnlyList<string> All { get; } =
        ["equals", "notEquals", "gt", "gte", "lt", "lte", "isEmpty", "isNotEmpty", "in"];
}

public static class StudioFormConflictModes
{
    public static IReadOnlyList<string> All { get; } = ["defer", "lastWriteWins"];
}

// --- Data-source-facing projections (immutable). ---

public sealed record StudioFormWorkspace(
    IReadOnlyList<StudioFormPackageListItem> Packages,
    IReadOnlyList<StudioFormCapabilityState> CapabilityStates);

public sealed record StudioFormPackageListItem(
    string FormId,
    string Title,
    string ServiceId,
    int LayerId,
    int? DraftVersion,
    int? PublishedVersion,
    DateTimeOffset UpdatedAt);

/// <summary>
/// A binding/permission/empty surface for the form builder, mirroring the Operate capability-state
/// pattern so missing bindings, missing permissions, and unsupported contracts render consistently.
/// </summary>
public sealed record StudioFormCapabilityState(
    string Surface,
    string State,
    string Contract,
    string Detail);

public sealed record StudioFormEditorLoad(
    StudioFormEditorState? State,
    IReadOnlyList<StudioFormCapabilityState> CapabilityStates)
{
    public bool HasEditor => State is not null;
}

public sealed record StudioFormValidationView(
    bool IsValid,
    IReadOnlyList<StudioFormValidationItem> Issues);

public sealed record StudioFormValidationItem(
    string Severity,
    string Code,
    string? FieldId,
    string Message);

/// <summary>Outcome of a mutating form lifecycle command (save, validate, publish, reopen).</summary>
public sealed record StudioFormCommandResult(
    bool Succeeded,
    string Message,
    StudioFormEditorState? State = null,
    StudioFormValidationView? Validation = null,
    StudioFormCapabilityState? Issue = null);

public sealed record StudioFormOfflinePolicyView(
    bool Enabled,
    IReadOnlyList<string> AvailableTransports,
    StudioFormCapabilityState? Issue = null);

/// <summary>
/// Pure pre-publish gate. Publish requires a configured + validated submit target (AC#3) and an
/// explicit, reviewed offline/sync policy (AC#2). Server-side publish validation still applies; this
/// gate keeps the Console from offering publish before those operator decisions are made.
/// </summary>
public sealed record StudioFormPublishReadiness(bool CanPublish, IReadOnlyList<string> UnmetRequirements);

public static class StudioFormPublishEvaluator
{
    public static StudioFormPublishReadiness Evaluate(StudioFormEditorState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        var unmet = new List<string>();

        if (string.IsNullOrWhiteSpace(state.Title))
        {
            unmet.Add("Give the form a title.");
        }

        if (state.Fields.Count == 0)
        {
            unmet.Add("Add at least one field.");
        }

        // AC#3: submit target configured and validated before publish.
        if (string.IsNullOrWhiteSpace(state.ServiceId))
        {
            unmet.Add("Configure a submit target service and layer.");
        }

        // AC#2: offline/sync policy is an explicit, reviewed decision before publish.
        if (!state.OfflinePolicyReviewed)
        {
            unmet.Add("Review the offline/sync policy before publish.");
        }
        else if (state.OfflineEnabled
            && !state.ReplicaTransportEnabled
            && !state.FieldCollectionTransportEnabled)
        {
            unmet.Add("Enable at least one offline sync transport, or turn offline use off.");
        }

        // AC#3: the submit target must validate against the live server before publish.
        if (state.LastValidation is null)
        {
            unmet.Add("Validate the draft against the server before publish.");
        }
        else if (!state.LastValidation.IsValid)
        {
            unmet.Add("Resolve the server validation errors before publish.");
        }

        return new StudioFormPublishReadiness(unmet.Count == 0, unmet);
    }
}
