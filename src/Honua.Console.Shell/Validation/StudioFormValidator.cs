using System.Globalization;
using Honua.Console.Shell.Models;

namespace Honua.Console.Shell.Validation;

/// <summary>
/// Stable console-owned field keys for the Studio form builder. The client validator
/// (<see cref="StudioFormValidator"/>), the inline render surfaces, and the server-error resolver all
/// share these so a client finding and a server finding for the same input land on the same key. Form-level
/// keys are constants; per-field keys are derived from the field's <em>position</em> (not its FieldId, which
/// can be blank or duplicated) so every authored row is independently addressable.
/// </summary>
public static class StudioFormFieldKeys
{
    public const string Title = "form.title";
    public const string Fields = "form.fields";
    public const string ServiceId = "form.serviceId";
    public const string SubmitOps = "form.submitOps";
    public const string MaxAttachmentsPerSubmission = "form.maxAttachmentsPerSubmission";
    public const string PrivacyRetentionDays = "form.privacyRetentionDays";
    public const string OfflineTransports = "form.offlineTransports";

    /// <summary>Per-field range-min key for the field at <paramref name="index"/>.</summary>
    public static string FieldRangeMin(int index) => $"form.field[{index}].rangeMin";

    /// <summary>Per-field range-max key for the field at <paramref name="index"/>.</summary>
    public static string FieldRangeMax(int index) => $"form.field[{index}].rangeMax";

    /// <summary>Per-field id key for the field at <paramref name="index"/>.</summary>
    public static string FieldId(int index) => $"form.field[{index}].id";

    /// <summary>Per-field attachment-count key for the field at <paramref name="index"/>.</summary>
    public static string FieldAttachmentMaxCount(int index) => $"form.field[{index}].attachmentMaxCount";

    /// <summary>Per-field visibility-dependency key for the field at <paramref name="index"/>.</summary>
    public static string FieldVisibilityDependsOn(int index) => $"form.field[{index}].visibilityDependsOn";
}

/// <summary>
/// Pure client-side cross-field / bounds validator for the Studio form builder, mirroring the existing
/// <see cref="StudioFormPublishEvaluator"/> pattern but emitting field-addressable
/// <see cref="ConsoleFieldError"/> findings (keyed by <see cref="StudioFormFieldKeys"/>) so the editor can
/// surface them inline next to the offending input. It complements — never replaces — server validation: it
/// covers only the rules expressible against console-owned editor state (presence, bounds, ordering,
/// uniqueness, referential), and deliberately omits server-only rules (e.g. maxLength-vs-target-length,
/// target resolution) that need the live feature service.
/// </summary>
public sealed class StudioFormValidator : IFieldValidator<StudioFormEditorState>
{
    /// <summary>Shared singleton; the validator holds no state.</summary>
    public static StudioFormValidator Instance { get; } = new();

    /// <inheritdoc />
    public IReadOnlyList<ConsoleFieldError> Evaluate(StudioFormEditorState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        var errors = new List<ConsoleFieldError>();

        EvaluateRequiredPresence(state, errors);
        EvaluateAttachmentPolicy(state, errors);
        EvaluatePrivacy(state, errors);
        EvaluateOffline(state, errors);
        EvaluateFields(state, errors);

        return errors;
    }

    private static void EvaluateRequiredPresence(StudioFormEditorState state, List<ConsoleFieldError> errors)
    {
        if (string.IsNullOrWhiteSpace(state.Title))
        {
            errors.Add(Blocker(StudioFormFieldKeys.Title, "form.title.required", "Give the form a title."));
        }

        if (state.Fields.Count == 0)
        {
            errors.Add(Blocker(StudioFormFieldKeys.Fields, "form.fields.required", "Add at least one field."));
        }

        if (string.IsNullOrWhiteSpace(state.ServiceId))
        {
            errors.Add(Blocker(StudioFormFieldKeys.ServiceId, "form.serviceId.required", "Configure a submit target service."));
        }

        if (!state.AllowCreate && !state.AllowUpdate && !state.AllowDelete)
        {
            errors.Add(Blocker(
                StudioFormFieldKeys.SubmitOps,
                "form.submitOps.required",
                "Enable at least one submit operation (create, update, or delete)."));
        }
    }

    private static void EvaluateAttachmentPolicy(StudioFormEditorState state, List<ConsoleFieldError> errors)
    {
        // A configured per-submission cap must be at least one (zero disallows every attachment yet leaves
        // attachments "enabled", which the server rejects). A null cap means "server default", which is fine.
        if (state.MaxAttachmentsPerSubmission is { } cap && !NumericBoundsRule.IsWithin(cap, min: 1))
        {
            errors.Add(Error(
                StudioFormFieldKeys.MaxAttachmentsPerSubmission,
                "form.attachments.maxPerSubmission.min",
                "Max attachments per submission must be at least 1."));
        }
    }

    private static void EvaluatePrivacy(StudioFormEditorState state, List<ConsoleFieldError> errors)
    {
        // Server enforces retention > 0; a configured zero/negative is invalid. Null = server default.
        if (state.PrivacyRetentionDays is { } retention && !NumericBoundsRule.IsWithin(retention, min: 1))
        {
            errors.Add(Error(
                StudioFormFieldKeys.PrivacyRetentionDays,
                "form.privacy.retention.positive",
                "Privacy retention must be greater than 0 days."));
        }
    }

    private static void EvaluateOffline(StudioFormEditorState state, List<ConsoleFieldError> errors)
    {
        // Offline capture needs somewhere to sync: at least one transport must be enabled.
        if (state.OfflineEnabled && !state.ReplicaTransportEnabled && !state.FieldCollectionTransportEnabled)
        {
            errors.Add(Error(
                StudioFormFieldKeys.OfflineTransports,
                "form.offline.transport.required",
                "Enable at least one offline sync transport, or turn offline use off."));
        }
    }

    private static void EvaluateFields(StudioFormEditorState state, List<ConsoleFieldError> errors)
    {
        // Field ids known to the form, for the VisibilityDependsOn referential check. Built once.
        var knownFieldIds = new HashSet<string>(
            state.Fields
                .Select(f => f.FieldId?.Trim())
                .Where(id => !string.IsNullOrEmpty(id))!,
            StringComparer.OrdinalIgnoreCase);

        // First non-blank occurrence of each field id; later occurrences are flagged duplicate.
        var seenFieldIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (var index = 0; index < state.Fields.Count; index++)
        {
            var field = state.Fields[index];

            EvaluateFieldId(field, index, seenFieldIds, errors);
            EvaluateFieldRange(field, index, errors);
            EvaluateFieldAttachment(field, index, errors);
            EvaluateFieldVisibility(field, index, knownFieldIds, errors);
        }
    }

    private static void EvaluateFieldId(
        StudioFormFieldEditor field,
        int index,
        HashSet<string> seenFieldIds,
        List<ConsoleFieldError> errors)
    {
        var fieldId = field.FieldId?.Trim() ?? string.Empty;
        if (fieldId.Length == 0)
        {
            return; // presence of a field id is a server-side concern; only uniqueness is a console rule here.
        }

        if (!seenFieldIds.Add(fieldId))
        {
            errors.Add(Error(
                StudioFormFieldKeys.FieldId(index),
                "form.field.id.duplicate",
                $"Field id '{fieldId}' is already used. Field ids must be unique."));
        }
    }

    private static void EvaluateFieldRange(StudioFormFieldEditor field, int index, List<ConsoleFieldError> errors)
    {
        if (!string.Equals(field.DomainKind, "range", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var hasMin = TryParseBound(field.RangeMin, out var min);
        var hasMax = TryParseBound(field.RangeMax, out var max);

        // Ordering is the priority cross-field rule (server enforces range completeness but not ordering).
        if (hasMin && hasMax && !NumericBoundsRule.IsOrdered(min, max))
        {
            errors.Add(Error(
                StudioFormFieldKeys.FieldRangeMin(index),
                "form.field.range.order",
                "Range minimum must be less than or equal to the maximum."));
        }
    }

    private static void EvaluateFieldAttachment(StudioFormFieldEditor field, int index, List<ConsoleFieldError> errors)
    {
        if (!field.IsAttachment)
        {
            return;
        }

        if (field.AttachmentMaxCount is { } count && !NumericBoundsRule.IsWithin(count, min: 1))
        {
            errors.Add(Error(
                StudioFormFieldKeys.FieldAttachmentMaxCount(index),
                "form.field.attachment.count.min",
                "Attachment max count must be at least 1."));
        }
    }

    private static void EvaluateFieldVisibility(
        StudioFormFieldEditor field,
        int index,
        HashSet<string> knownFieldIds,
        List<ConsoleFieldError> errors)
    {
        var dependsOn = field.VisibilityDependsOn?.Trim() ?? string.Empty;
        if (dependsOn.Length == 0)
        {
            return; // no dependency configured.
        }

        if (!knownFieldIds.Contains(dependsOn))
        {
            errors.Add(Error(
                StudioFormFieldKeys.FieldVisibilityDependsOn(index),
                "form.field.visibility.dependsOn.unknown",
                $"Visibility depends on '{dependsOn}', which is not an existing field id."));
        }
    }

    private static bool TryParseBound(string? value, out double parsed) =>
        double.TryParse(
            value?.Trim(),
            NumberStyles.Float,
            CultureInfo.InvariantCulture,
            out parsed);

    private static ConsoleFieldError Blocker(string key, string code, string message) =>
        new(key, code, ConsoleValidationSeverity.Blocker, message);

    private static ConsoleFieldError Error(string key, string code, string message) =>
        new(key, code, ConsoleValidationSeverity.Error, message);
}
