using Honua.Console.Shell.Models;

namespace Honua.Console.Shell.Validation;

/// <summary>
/// Binds the server-returned <see cref="StudioFormValidationView"/> (the console projection of the
/// honua-server form package validate response — items <c>{severity,code,fieldId,message}</c>) into the
/// editor's shared <see cref="ValidationState"/> server channel, keyed by the same
/// <see cref="StudioFormFieldKeys"/> the client validator uses. This lets a server finding for a field
/// surface inline next to that field's input, merged with the client findings, instead of only in the flat
/// validation list. It complements server validation — the authoritative result still drives the publish
/// gate; this binder only re-homes the items onto fields.
/// </summary>
public static class StudioFormServerErrorBinder
{
    /// <summary>
    /// Maps each <see cref="StudioFormValidationItem"/> in <paramref name="validation"/> onto a
    /// <see cref="ConsoleFieldError"/>, resolving the item's <see cref="StudioFormValidationItem.FieldId"/>
    /// (a field-id value) to the matching per-field console key against <paramref name="state"/>, and a
    /// handful of well-known form-level codes to their form-level key. Items with an unresolvable locator
    /// fall back to <see cref="ServerFieldErrorMapper.FormLevelFieldKey"/> so they still surface. Returns an
    /// empty list when there is nothing to bind.
    /// </summary>
    public static IReadOnlyList<ConsoleFieldError> Map(
        StudioFormEditorState state,
        StudioFormValidationView? validation)
    {
        ArgumentNullException.ThrowIfNull(state);

        if (validation is null || validation.Issues.Count == 0)
        {
            return Array.Empty<ConsoleFieldError>();
        }

        // First field index per (trimmed, case-insensitive) field id, so a server fieldId resolves to the
        // field row the operator sees. Built once per bind.
        var fieldIndexById = BuildFieldIndex(state);
        var mapper = new ServerFieldErrorMapper(
            (locator, code) => ResolveKey(locator, code, fieldIndexById));

        return mapper.Map(validation.Issues.Select(ToError));
    }

    private static ConsoleFieldValidationError ToError(StudioFormValidationItem item) => new()
    {
        Code = item.Code,
        Severity = item.Severity,
        FieldId = item.FieldId,
        Message = item.Message,
    };

    private static Dictionary<string, int> BuildFieldIndex(StudioFormEditorState state)
    {
        var index = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < state.Fields.Count; i++)
        {
            var id = state.Fields[i].FieldId?.Trim();
            if (!string.IsNullOrEmpty(id) && !index.ContainsKey(id))
            {
                index[id] = i;
            }
        }

        return index;
    }

    private static string? ResolveKey(string? locator, string code, IReadOnlyDictionary<string, int> fieldIndexById)
    {
        var trimmed = locator?.Trim();

        // A field-id locator resolves to that field's range/id/attachment/visibility surface. Pick the most
        // specific per-field key the server code implies, falling back to the field-id row.
        if (!string.IsNullOrEmpty(trimmed) && fieldIndexById.TryGetValue(trimmed, out var fieldIndex))
        {
            return ResolveFieldKey(code, fieldIndex);
        }

        // Form-level codes the server may report without a fieldId.
        return ResolveFormLevelKey(code);
    }

    private static string ResolveFieldKey(string code, int fieldIndex)
    {
        var normalized = code?.Trim().ToLowerInvariant() ?? string.Empty;

        if (normalized.Contains("range", StringComparison.Ordinal))
        {
            return StudioFormFieldKeys.FieldRangeMin(fieldIndex);
        }

        if (normalized.Contains("attachment", StringComparison.Ordinal))
        {
            return StudioFormFieldKeys.FieldAttachmentMaxCount(fieldIndex);
        }

        if (normalized.Contains("visib", StringComparison.Ordinal))
        {
            return StudioFormFieldKeys.FieldVisibilityDependsOn(fieldIndex);
        }

        // Default: the field's id row carries the finding (duplicate id, type compat, required, etc.).
        return StudioFormFieldKeys.FieldId(fieldIndex);
    }

    private static string? ResolveFormLevelKey(string code)
    {
        var normalized = code?.Trim().ToLowerInvariant() ?? string.Empty;

        return normalized switch
        {
            _ when normalized.Contains("title", StringComparison.Ordinal) => StudioFormFieldKeys.Title,
            _ when normalized.Contains("service", StringComparison.Ordinal)
                || normalized.Contains("target", StringComparison.Ordinal) => StudioFormFieldKeys.ServiceId,
            _ when normalized.Contains("submit", StringComparison.Ordinal)
                || normalized.Contains("policy", StringComparison.Ordinal) => StudioFormFieldKeys.SubmitOps,
            _ when normalized.Contains("retention", StringComparison.Ordinal)
                || normalized.Contains("privacy", StringComparison.Ordinal) => StudioFormFieldKeys.PrivacyRetentionDays,
            _ when normalized.Contains("offline", StringComparison.Ordinal)
                || normalized.Contains("transport", StringComparison.Ordinal) => StudioFormFieldKeys.OfflineTransports,
            // No mapping: let the mapper fall back to the raw locator / form-level key.
            _ => null,
        };
    }
}
