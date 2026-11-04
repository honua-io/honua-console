using Honua.Console.Contracts;

namespace Honua.Console.Shell.Models;

public sealed class StudioFormEditorState
{
    public string? FormId { get; set; }

    public int Version { get; set; } = 1;

    public string Status { get; set; } = HonuaFormStatuses.Draft;

    // Empty means the draft has not been saved, so there is no concurrency token yet.
    public string ETag { get; set; } = string.Empty;

    public int? ReopenedFromVersion { get; set; }

    public string Title { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public string ServiceId { get; set; } = string.Empty;

    public int LayerId { get; set; }

    public List<StudioFormFieldEditor> Fields { get; } = [];

    public bool AllowCreate { get; set; } = true;

    public bool AllowUpdate { get; set; }

    public bool AllowDelete { get; set; }

    public bool RequiresGeometry { get; set; } = true;

    public bool AllowAttachments { get; set; } = true;

    public bool AttachmentsEnabled { get; set; }

    public int? MaxAttachmentsPerSubmission { get; set; }

    // Comma- or newline-separated MIME allowlist.
    public string AllowedContentTypes { get; set; } = string.Empty;

    public bool RequireExifStripping { get; set; }

    public bool RequireFaceBlur { get; set; }

    public bool RequireRedaction { get; set; }

    public int? PrivacyRetentionDays { get; set; }

    public bool CaptureActor { get; set; } = true;

    public bool CaptureDeviceId { get; set; } = true;

    // Comma- or newline-separated: none, auditOnly, or minimizeAudit.
    public string RequiredTransformations { get; set; } = string.Empty;

    public bool OfflineEnabled { get; set; }

    public bool ReplicaTransportEnabled { get; set; } = true;

    public bool FieldCollectionTransportEnabled { get; set; } = true;

    public string ConflictReviewMode { get; set; } = "defer";

    public bool OfflinePolicyReviewed { get; set; }

    public StudioFormValidationView? LastValidation { get; set; }

    // Baseline for fields the builder does not model; save merges edits onto it instead of dropping them.
    public HonuaFormPackageDocument? OriginalDocument { get; set; }

    public string SavedSignature { get; set; } = string.Empty;

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

public sealed class StudioFormFieldEditor
{
    public string FieldId { get; set; } = string.Empty;

    public string Label { get; set; } = string.Empty;

    public string Group { get; set; } = string.Empty;

    // Kept so sections with duplicate labels are not collapsed or recreated on save.
    public string OriginalSectionId { get; set; } = string.Empty;

    public string Type { get; set; } = "text";

    // Not required when the field type is attachment.
    public string TargetField { get; set; } = string.Empty;

    public bool Required { get; set; }

    public bool Private { get; set; }

    public string DomainKind { get; set; } = "none"; // none | coded | range

    // One coded value per line, as code or code=label.
    public string Choices { get; set; } = string.Empty;

    public string RangeMin { get; set; } = string.Empty;

    public string RangeMax { get; set; } = string.Empty;

    // Single declarative validation rule (kept simple; the server supports a rule array).
    public string ValidationType { get; set; } = "none"; // none | minLength | maxLength | min | max | regex

    public string ValidationValue { get; set; } = string.Empty;

    public string ValidationMessage { get; set; } = string.Empty;

    public string VisibilityDependsOn { get; set; } = string.Empty;

    public string VisibilityOperator { get; set; } = "equals";

    public string VisibilityValue { get; set; } = string.Empty;

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

        if (string.IsNullOrWhiteSpace(state.ServiceId))
        {
            unmet.Add("Configure a submit target service and layer.");
        }

        // AC#3: a submit target with no enabled operation cannot accept submissions. Require an explicit
        // choice here rather than letting the mapper default to create, so the operator's selection is what
        // gets published.
        if (!state.AllowCreate && !state.AllowUpdate && !state.AllowDelete)
        {
            unmet.Add("Enable at least one submit operation (create, update, or delete).");
        }

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

        // AC#3: the submit target must validate against the live server before publish, and that
        // validation must still match what is in the editor. Edits after the validated save move the
        // draft off its saved baseline, so re-require a save + server validation rather than trusting
        // the now-stale result (a validate runs against the saved version, not unsaved local edits).
        if (StudioFormPackageMapper.HasUnsavedEdits(state))
        {
            unmet.Add("Save your latest edits, then validate the draft against the server before publish.");
        }
        else if (state.LastValidation is null)
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
