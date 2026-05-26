using System.Globalization;
using System.Text;
using System.Text.Json;
using Honua.Console.Contracts;

namespace Honua.Console.Shell.Models;

/// <summary>
/// Maps between the Console-owned <see cref="StudioFormEditorState"/> and the server-owned
/// <c>honua.form-package.v1</c> document (honua-server#1184). The wire records are the single source
/// of truth for the form package shape; this mapper keeps the authoring UI decoupled from that shape
/// without duplicating the contract.
/// </summary>
public static class StudioFormPackageMapper
{
    private const string ProvenanceSource = "honua-console.studio.form-builder";

    // Options for the content-signature serialization. Only ever compared to another signature built
    // the same way, so the exact format is irrelevant — it just needs to be stable for equal content.
    private static readonly JsonSerializerOptions SignatureOptions = new(JsonSerializerDefaults.Web);

    public static StudioFormEditorState CreateTemplate()
    {
        var state = new StudioFormEditorState
        {
            Title = "Field inspection form",
            Description = "Authored in Honua Studio.",
            OfflineEnabled = true
        };

        state.Fields.Add(new StudioFormFieldEditor
        {
            FieldId = "asset_id",
            Label = "Asset ID",
            Group = "Identification",
            Type = "text",
            TargetField = "asset_id",
            Required = true
        });

        state.Fields.Add(new StudioFormFieldEditor
        {
            FieldId = "condition",
            Label = "Condition",
            Group = "Inspection",
            Type = "choice",
            TargetField = "condition",
            Required = true,
            DomainKind = "coded",
            Choices = "good=Good\nneeds-repair=Needs repair\ncritical=Critical"
        });

        return state;
    }

    public static HonuaFormPackageDocument ToDocument(StudioFormEditorState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        // Merge the modeled authoring fields onto the document this state was loaded from, so server-owned
        // fields the builder does not model survive a load → edit → save round-trip rather than being
        // dropped: the record `with` updates carry SchemaVersion, Metadata, the attachment byte limits,
        // SubmitPolicy.MaxOfflineAgeSeconds, OfflinePolicy.PreferredTransports, and (via the field/section
        // merges) per-field hints/defaults/read-only, the server-assigned section ids and descriptions, and
        // extra validation rules. A never-loaded draft has no baseline, so it builds from a fresh contract
        // document.
        var baseDocument = state.OriginalDocument ?? new HonuaFormPackageDocument();

        // The editor only carries a section's label (as each field's Group), not its server-assigned id, so
        // index the loaded sections by that same group key to recover the original id + description on save.
        // Shared by the section and field merges so a field's SectionId stays consistent with the section it
        // lands in (see BuildSectionLookup / ResolveSectionId).
        var sectionsByGroup = BuildSectionLookup(baseDocument.Sections);

        return baseDocument with
        {
            FormId = string.IsNullOrWhiteSpace(state.FormId) ? null : state.FormId,
            Title = NullIfBlank(state.Title),
            Description = NullIfBlank(state.Description),
            Target = (baseDocument.Target ?? new HonuaFormTargetDefinition()) with
            {
                ServiceId = NullIfBlank(state.ServiceId),
                LayerId = state.LayerId
            },
            Sections = BuildSections(state.Fields, sectionsByGroup),
            Fields = BuildFields(state.Fields, baseDocument.Fields, sectionsByGroup),
            SubmitPolicy = baseDocument.SubmitPolicy with
            {
                AllowedOperations = BuildOperations(state),
                RequiresGeometry = state.RequiresGeometry,
                AllowAttachments = state.AllowAttachments
            },
            AttachmentPolicy = baseDocument.AttachmentPolicy with
            {
                Enabled = state.AttachmentsEnabled,
                MaxAttachmentsPerSubmission = state.MaxAttachmentsPerSubmission,
                AllowedContentTypes = SplitList(state.AllowedContentTypes),
                Fields = BuildAttachmentFields(state.Fields, baseDocument.AttachmentPolicy.Fields),
                RequireExifStripping = state.RequireExifStripping,
                RequireFaceBlur = state.RequireFaceBlur,
                RequireRedaction = state.RequireRedaction
            },
            PrivacyPolicy = baseDocument.PrivacyPolicy with
            {
                PrivateFieldIds = state.Fields
                    .Where(field => field.Private && !string.IsNullOrWhiteSpace(field.FieldId))
                    .Select(field => field.FieldId.Trim())
                    .Distinct(StringComparer.Ordinal)
                    .ToArray(),
                RequiredTransformations = SplitList(state.RequiredTransformations),
                CaptureActor = state.CaptureActor,
                CaptureDeviceId = state.CaptureDeviceId,
                RetentionDays = state.PrivacyRetentionDays
            },
            OfflinePolicy = baseDocument.OfflinePolicy with
            {
                Enabled = state.OfflineEnabled,
                ReplicaTransportEnabled = state.ReplicaTransportEnabled,
                FieldCollectionTransportEnabled = state.FieldCollectionTransportEnabled,
                ConflictReviewMode = NullIfBlank(state.ConflictReviewMode) ?? "defer"
            },
            // Preserve the server-recorded provenance of an existing package; stamp Console provenance only
            // when authoring a brand-new draft that has none (the builder does not model provenance).
            Provenance = baseDocument.Provenance ?? new HonuaFormProvenanceRef
            {
                Source = ProvenanceSource,
                SourceVersion = "1"
            }
        };
    }

    public static StudioFormEditorState ToEditorState(HonuaFormPackageVersion version)
    {
        ArgumentNullException.ThrowIfNull(version);

        var document = version.Package;
        var privateIds = new HashSet<string>(
            document.PrivacyPolicy.PrivateFieldIds.Where(id => !string.IsNullOrWhiteSpace(id)),
            StringComparer.Ordinal);
        var attachmentByField = document.AttachmentPolicy.Fields
            .Where(field => !string.IsNullOrWhiteSpace(field.FieldId))
            .GroupBy(field => field.FieldId!, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
        var sectionLabels = document.Sections
            .Where(section => !string.IsNullOrWhiteSpace(section.SectionId))
            .ToDictionary(
                section => section.SectionId!,
                section => NullIfBlank(section.Label) ?? section.SectionId!,
                StringComparer.Ordinal);

        var state = new StudioFormEditorState
        {
            FormId = version.FormId,
            Version = version.Version,
            Status = version.Status,
            ETag = version.ETag,
            ReopenedFromVersion = version.ReopenedFromVersion,
            Title = document.Title ?? string.Empty,
            Description = document.Description ?? string.Empty,
            ServiceId = document.Target?.ServiceId ?? string.Empty,
            LayerId = document.Target?.LayerId ?? 0,
            AllowCreate = HasOperation(document.SubmitPolicy, HonuaFormSubmissionOperations.Create),
            AllowUpdate = HasOperation(document.SubmitPolicy, HonuaFormSubmissionOperations.Update),
            AllowDelete = HasOperation(document.SubmitPolicy, HonuaFormSubmissionOperations.Delete),
            RequiresGeometry = document.SubmitPolicy.RequiresGeometry,
            AllowAttachments = document.SubmitPolicy.AllowAttachments,
            AttachmentsEnabled = document.AttachmentPolicy.Enabled,
            MaxAttachmentsPerSubmission = document.AttachmentPolicy.MaxAttachmentsPerSubmission,
            AllowedContentTypes = JoinList(document.AttachmentPolicy.AllowedContentTypes),
            RequireExifStripping = document.AttachmentPolicy.RequireExifStripping,
            RequireFaceBlur = document.AttachmentPolicy.RequireFaceBlur,
            RequireRedaction = document.AttachmentPolicy.RequireRedaction,
            PrivacyRetentionDays = document.PrivacyPolicy.RetentionDays,
            CaptureActor = document.PrivacyPolicy.CaptureActor,
            CaptureDeviceId = document.PrivacyPolicy.CaptureDeviceId,
            RequiredTransformations = JoinList(document.PrivacyPolicy.RequiredTransformations),
            OfflineEnabled = document.OfflinePolicy.Enabled,
            ReplicaTransportEnabled = document.OfflinePolicy.ReplicaTransportEnabled,
            FieldCollectionTransportEnabled = document.OfflinePolicy.FieldCollectionTransportEnabled,
            ConflictReviewMode = document.OfflinePolicy.ConflictReviewMode ?? "defer",
            LastValidation = ToValidationView(version.Validation)
        };

        foreach (var field in document.Fields)
        {
            state.Fields.Add(ToFieldEditor(field, privateIds, attachmentByField, sectionLabels));
        }

        // Keep the loaded server document so a later ToDocument merges modeled edits onto it instead of
        // dropping server-owned fields the authoring UI never models. Set before the signature is computed
        // so the dirty baseline reflects the same merged document a save would send.
        state.OriginalDocument = document;

        // Capture the baseline content signature so later local edits register as unsaved (dirty).
        state.SavedSignature = ComputeContentSignature(state);
        return state;
    }

    /// <summary>
    /// Deterministic signature of the publishable content of <paramref name="state"/> — everything the
    /// server document carries (title, target, fields, and the submit/attachment/privacy/offline
    /// policies). Used to detect edits made after a save or validate so the publish gate can re-require
    /// a fresh server validation. The operator's offline-policy review acknowledgment is intentionally
    /// excluded: it is a Console-side decision, not part of the server document the validate runs over.
    /// </summary>
    public static string ComputeContentSignature(StudioFormEditorState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        return JsonSerializer.Serialize(ToDocument(state), SignatureOptions);
    }

    /// <summary>
    /// True when the editor's current content differs from the baseline captured the last time it was
    /// loaded from or saved to the server (<see cref="StudioFormEditorState.SavedSignature"/>). A
    /// never-saved draft (empty baseline) is always treated as having unsaved edits.
    /// </summary>
    public static bool HasUnsavedEdits(StudioFormEditorState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        return !string.Equals(ComputeContentSignature(state), state.SavedSignature, StringComparison.Ordinal);
    }

    public static StudioFormValidationView? ToValidationView(HonuaFormPackageValidationResult? result)
    {
        if (result is null)
        {
            return null;
        }

        var issues = result.Issues
            .Select(issue => new StudioFormValidationItem(
                string.IsNullOrWhiteSpace(issue.Severity) ? "error" : issue.Severity,
                issue.Code,
                issue.FieldId,
                issue.Message))
            .ToArray();

        return new StudioFormValidationView(result.IsValid, issues);
    }

    public static StudioFormPackageListItem ToListItem(HonuaFormPackageSummary summary) =>
        new(
            summary.FormId,
            string.IsNullOrWhiteSpace(summary.Title) ? summary.FormId : summary.Title,
            summary.ServiceId,
            summary.LayerId,
            summary.CurrentDraftVersion,
            summary.CurrentPublishedVersion,
            summary.UpdatedAt);

    private static HonuaFormFieldDefinition[] BuildFields(
        IReadOnlyList<StudioFormFieldEditor> fields,
        IReadOnlyList<HonuaFormFieldDefinition> originalFields,
        IReadOnlyDictionary<string, HonuaFormSectionDefinition> sectionsByGroup)
    {
        var originalById = originalFields
            .Where(field => !string.IsNullOrWhiteSpace(field.FieldId))
            .GroupBy(field => field.FieldId!, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);

        return fields.Select(field => ToFieldDefinition(field, originalById, sectionsByGroup)).ToArray();
    }

    private static HonuaFormFieldDefinition ToFieldDefinition(
        StudioFormFieldEditor field,
        IReadOnlyDictionary<string, HonuaFormFieldDefinition> originalById,
        IReadOnlyDictionary<string, HonuaFormSectionDefinition> sectionsByGroup)
    {
        var fieldId = NullIfBlank(field.FieldId);
        var original = fieldId is not null && originalById.TryGetValue(fieldId, out var match) ? match : null;

        // Merge onto the original field so its unmodeled server attributes (hint, read-only, default value)
        // survive; the modeled attributes below are replaced from the editor. SectionId resolves through the
        // same shared lookup the sections use, so the field references the section's preserved server id.
        return (original ?? new HonuaFormFieldDefinition()) with
        {
            FieldId = fieldId,
            Label = NullIfBlank(field.Label),
            Type = NullIfBlank(field.Type) ?? "text",
            TargetField = field.IsAttachment ? null : NullIfBlank(field.TargetField),
            SectionId = ResolveSectionId(field.Group, sectionsByGroup),
            Required = field.Required,
            Private = field.Private,
            Domain = ToDomain(field),
            Validation = ToValidationRules(field, original?.Validation ?? []),
            Visibility = ToVisibility(field)
        };
    }

    private static StudioFormFieldEditor ToFieldEditor(
        HonuaFormFieldDefinition field,
        HashSet<string> privateIds,
        IReadOnlyDictionary<string, HonuaFormFieldAttachmentPolicy> attachmentByField,
        IReadOnlyDictionary<string, string> sectionLabels)
    {
        var editor = new StudioFormFieldEditor
        {
            FieldId = field.FieldId ?? string.Empty,
            Label = field.Label ?? string.Empty,
            Type = field.Type ?? "text",
            TargetField = field.TargetField ?? string.Empty,
            Group = ResolveGroup(field.SectionId, sectionLabels),
            Required = field.Required,
            Private = field.Private || (field.FieldId is not null && privateIds.Contains(field.FieldId))
        };

        if (field.Domain is { } domain)
        {
            if (domain.Choices.Length > 0)
            {
                editor.DomainKind = "coded";
                editor.Choices = string.Join(
                    '\n',
                    domain.Choices.Select(choice =>
                    {
                        var code = JsonElementToString(choice.Code);
                        return string.IsNullOrWhiteSpace(choice.Label) ? code : $"{code}={choice.Label}";
                    }));
            }
            else if (domain.Min is not null || domain.Max is not null)
            {
                editor.DomainKind = "range";
                editor.RangeMin = domain.Min is { } min ? JsonElementToString(min) : string.Empty;
                editor.RangeMax = domain.Max is { } max ? JsonElementToString(max) : string.Empty;
            }
        }

        if (field.Validation.FirstOrDefault() is { } rule && !string.IsNullOrWhiteSpace(rule.Type))
        {
            editor.ValidationType = rule.Type!;
            editor.ValidationMessage = rule.Message ?? string.Empty;
            editor.ValidationValue = ExtractRuleValue(rule.Parameters);
        }

        if (field.Visibility is { } visibility && !string.IsNullOrWhiteSpace(visibility.DependsOnFieldId))
        {
            editor.VisibilityDependsOn = visibility.DependsOnFieldId!;
            editor.VisibilityOperator = NullIfBlank(visibility.Operator) ?? "equals";
            editor.VisibilityValue = visibility.Value is { } value ? JsonElementToString(value) : string.Empty;
        }

        if (field.FieldId is not null && attachmentByField.TryGetValue(field.FieldId, out var attachment))
        {
            editor.AttachmentMaxCount = attachment.MaxCount;
        }

        return editor;
    }

    private static HonuaFormFieldDomainDefinition? ToDomain(StudioFormFieldEditor field)
    {
        if (string.Equals(field.DomainKind, "coded", StringComparison.OrdinalIgnoreCase))
        {
            var choices = ParseChoices(field.Choices);
            return choices.Length == 0
                ? null
                : new HonuaFormFieldDomainDefinition { Type = "codedValue", Choices = choices };
        }

        if (string.Equals(field.DomainKind, "range", StringComparison.OrdinalIgnoreCase))
        {
            var min = ParseNumberElement(field.RangeMin);
            var max = ParseNumberElement(field.RangeMax);
            if (min is null && max is null)
            {
                return null;
            }

            return new HonuaFormFieldDomainDefinition { Type = "range", Min = min, Max = max };
        }

        return null;
    }

    private static HonuaFormValidationRule[] ToValidationRules(
        StudioFormFieldEditor field,
        IReadOnlyList<HonuaFormValidationRule> originalRules)
    {
        // The editor hydrates a single rule from the server's first rule (ToFieldEditor reads
        // Validation.FirstOrDefault), so it owns rule[0]; preserve any further server rules it never
        // modeled rather than dropping them on save.
        var preserved = originalRules.Skip(1).ToArray();

        if (string.IsNullOrWhiteSpace(field.ValidationType)
            || string.Equals(field.ValidationType, "none", StringComparison.OrdinalIgnoreCase))
        {
            return preserved;
        }

        var numeric = field.ValidationType is "minLength" or "maxLength" or "min" or "max";
        JsonElement? parameters = field.ValidationType switch
        {
            "regex" => string.IsNullOrWhiteSpace(field.ValidationValue)
                ? null
                : JsonSerializer.SerializeToElement(new { pattern = field.ValidationValue }),
            _ when numeric => ParseNumberElement(field.ValidationValue) is { } number
                ? JsonSerializer.SerializeToElement(new { value = number })
                : null,
            _ => null
        };

        return
        [
            new HonuaFormValidationRule
            {
                RuleId = $"{field.FieldId}-{field.ValidationType}",
                Type = field.ValidationType,
                Message = NullIfBlank(field.ValidationMessage),
                Parameters = parameters
            },
            .. preserved
        ];
    }

    private static HonuaFormConditionalRule? ToVisibility(StudioFormFieldEditor field)
    {
        if (string.IsNullOrWhiteSpace(field.VisibilityDependsOn))
        {
            return null;
        }

        var op = NullIfBlank(field.VisibilityOperator) ?? "equals";
        var needsValue = op is not ("isEmpty" or "isNotEmpty");
        return new HonuaFormConditionalRule
        {
            DependsOnFieldId = field.VisibilityDependsOn.Trim(),
            Operator = op,
            Value = needsValue && !string.IsNullOrWhiteSpace(field.VisibilityValue)
                ? JsonSerializer.SerializeToElement(field.VisibilityValue)
                : null
        };
    }

    private static HonuaFormSectionDefinition[] BuildSections(
        IReadOnlyList<StudioFormFieldEditor> fields,
        IReadOnlyDictionary<string, HonuaFormSectionDefinition> sectionsByGroup)
    {
        return fields
            .Where(field => !string.IsNullOrWhiteSpace(field.Group))
            .GroupBy(field => field.Group.Trim(), StringComparer.Ordinal)
            .Select(group =>
            {
                var label = group.Key;
                // Merge onto the original section matched by the group label the editor carries — not the
                // slug of that label — so the server-assigned section id and description survive a round-trip
                // even when the id is not the slug of the label; a brand-new group label synthesizes a slug id.
                sectionsByGroup.TryGetValue(label, out var original);
                return (original ?? new HonuaFormSectionDefinition()) with
                {
                    SectionId = ResolveSectionId(label, sectionsByGroup),
                    Label = label,
                    FieldIds = group
                        .Where(field => !string.IsNullOrWhiteSpace(field.FieldId))
                        .Select(field => field.FieldId.Trim())
                        .ToArray()
                };
            })
            .ToArray();
    }

    // Index the loaded sections by the same key ToEditorState projects into a field's Group — the section
    // label, or its id when the label is blank — so a save recovers each section's server-assigned id and
    // description by the group the editor still carries, even when that id is not the slug of the label.
    // First-wins on a duplicate group key, matching the editor's lossy label-based grouping.
    private static Dictionary<string, HonuaFormSectionDefinition> BuildSectionLookup(
        IReadOnlyList<HonuaFormSectionDefinition> originalSections) =>
        originalSections
            .Where(section => !string.IsNullOrWhiteSpace(section.SectionId))
            .GroupBy(section => NullIfBlank(section.Label) ?? section.SectionId!, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);

    // Resolve the wire SectionId for an editor group: reuse the server-assigned id of the section the group
    // came from when it still matches, otherwise synthesize a slug id for a new group. Shared by the section
    // and field merges so a field's SectionId always matches the section it is placed in.
    private static string? ResolveSectionId(
        string? group,
        IReadOnlyDictionary<string, HonuaFormSectionDefinition> sectionsByGroup)
    {
        if (string.IsNullOrWhiteSpace(group))
        {
            return null;
        }

        var label = group.Trim();
        return sectionsByGroup.TryGetValue(label, out var original) && !string.IsNullOrWhiteSpace(original.SectionId)
            ? original.SectionId
            : Slug(label);
    }

    private static HonuaFormFieldAttachmentPolicy[] BuildAttachmentFields(
        IReadOnlyList<StudioFormFieldEditor> fields,
        IReadOnlyList<HonuaFormFieldAttachmentPolicy> originalFields)
    {
        var originalById = originalFields
            .Where(field => !string.IsNullOrWhiteSpace(field.FieldId))
            .GroupBy(field => field.FieldId!, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);

        return fields
            .Where(field => field.IsAttachment && !string.IsNullOrWhiteSpace(field.FieldId))
            .Select(field =>
            {
                var fieldId = field.FieldId.Trim();
                // Merge onto the original per-field attachment policy so its server-owned allowed content
                // types survive; the count/required come from the editor.
                return (originalById.TryGetValue(fieldId, out var original) ? original : new HonuaFormFieldAttachmentPolicy()) with
                {
                    FieldId = fieldId,
                    Required = field.Required,
                    MaxCount = field.AttachmentMaxCount
                };
            })
            .ToArray();
    }

    private static string[] BuildOperations(StudioFormEditorState state)
    {
        var operations = new List<string>();
        if (state.AllowCreate)
        {
            operations.Add(HonuaFormSubmissionOperations.Create);
        }

        if (state.AllowUpdate)
        {
            operations.Add(HonuaFormSubmissionOperations.Update);
        }

        if (state.AllowDelete)
        {
            operations.Add(HonuaFormSubmissionOperations.Delete);
        }

        return operations.Count == 0 ? [HonuaFormSubmissionOperations.Create] : operations.ToArray();
    }

    private static HonuaFormDomainChoice[] ParseChoices(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return [];
        }

        return raw
            .Split(['\n', '\r', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(line =>
            {
                var separator = line.IndexOf('=', StringComparison.Ordinal);
                var code = separator > 0 ? line[..separator].Trim() : line.Trim();
                var label = separator > 0 ? line[(separator + 1)..].Trim() : line.Trim();
                return new HonuaFormDomainChoice
                {
                    Code = JsonSerializer.SerializeToElement(code),
                    Label = label
                };
            })
            .Where(choice => !string.IsNullOrWhiteSpace(choice.Label))
            .ToArray();
    }

    private static bool HasOperation(HonuaFormSubmitPolicy policy, string operation) =>
        policy.AllowedOperations.Any(value => string.Equals(value, operation, StringComparison.OrdinalIgnoreCase));

    private static string ResolveGroup(string? sectionId, IReadOnlyDictionary<string, string> sectionLabels)
    {
        if (string.IsNullOrWhiteSpace(sectionId))
        {
            return string.Empty;
        }

        return sectionLabels.TryGetValue(sectionId, out var label) ? label : sectionId;
    }

    private static string ExtractRuleValue(JsonElement? parameters)
    {
        if (parameters is not { } element || element.ValueKind != JsonValueKind.Object)
        {
            return string.Empty;
        }

        if (element.TryGetProperty("pattern", out var pattern) && pattern.ValueKind == JsonValueKind.String)
        {
            return pattern.GetString() ?? string.Empty;
        }

        if (element.TryGetProperty("value", out var value))
        {
            return JsonElementToString(value);
        }

        return string.Empty;
    }

    private static JsonElement? ParseNumberElement(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        return double.TryParse(raw.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var number)
            ? JsonSerializer.SerializeToElement(number)
            : JsonSerializer.SerializeToElement(raw.Trim());
    }

    private static string JsonElementToString(JsonElement element) =>
        element.ValueKind switch
        {
            JsonValueKind.String => element.GetString() ?? string.Empty,
            JsonValueKind.Null or JsonValueKind.Undefined => string.Empty,
            _ => element.GetRawText()
        };

    private static string[] SplitList(string raw) =>
        string.IsNullOrWhiteSpace(raw)
            ? []
            : raw
                .Split([',', '\n', '\r', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

    private static string JoinList(IReadOnlyList<string> values) =>
        values.Count == 0 ? string.Empty : string.Join(", ", values);

    private static string? NullIfBlank(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string Slug(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var character in value.Trim())
        {
            if (char.IsAsciiLetterOrDigit(character))
            {
                builder.Append(char.ToLowerInvariant(character));
            }
            else if (character is '-' or '_')
            {
                builder.Append(character);
            }
            else if (char.IsWhiteSpace(character))
            {
                builder.Append('-');
            }
        }

        var slug = builder.ToString().Trim('-');
        return string.IsNullOrWhiteSpace(slug) ? "section" : slug;
    }
}
