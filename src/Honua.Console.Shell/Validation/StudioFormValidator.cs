using System.Globalization;
using Honua.Console.Shell.Models;

namespace Honua.Console.Shell.Validation;

public static class StudioFormFieldKeys
{
    public const string Title = "form.title";
    public const string Fields = "form.fields";
    public const string ServiceId = "form.serviceId";
    public const string SubmitOps = "form.submitOps";
    public const string MaxAttachmentsPerSubmission = "form.maxAttachmentsPerSubmission";
    public const string PrivacyRetentionDays = "form.privacyRetentionDays";
    public const string OfflineTransports = "form.offlineTransports";

    // Key rows by position because a blank or duplicate FieldId must stay independently addressable.
    public static string FieldRangeMin(int index) => $"form.field[{index}].rangeMin";

    public static string FieldRangeMax(int index) => $"form.field[{index}].rangeMax";

    public static string FieldId(int index) => $"form.field[{index}].id";

    public static string FieldAttachmentMaxCount(int index) => $"form.field[{index}].attachmentMaxCount";

    public static string FieldVisibilityDependsOn(int index) => $"form.field[{index}].visibilityDependsOn";
}

public sealed class StudioFormValidator : IFieldValidator<StudioFormEditorState>
{
    public static StudioFormValidator Instance { get; } = new();

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
