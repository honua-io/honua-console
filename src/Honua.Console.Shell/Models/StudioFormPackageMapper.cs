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
        // The absence of a loaded baseline — not a null provenance value — is what marks a brand-new
        // draft for the Console provenance stamp below.
        var isNewDraft = state.OriginalDocument is null;
        var baseDocument = state.OriginalDocument ?? new HonuaFormPackageDocument();

        // Loaded fields carry their original section id as hidden editor state. Use that stable id for the
        // section and field merges so duplicate-label sections stay distinct and empty original sections
        // survive with their server-owned metadata.
        var sections = BuildSectionLookup(baseDocument.Sections);

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
            Sections = BuildSections(state.Fields, sections),
            Fields = BuildFields(state.Fields, baseDocument.Fields, sections),
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
            // Stamp Console provenance only when authoring a brand-new draft. An existing server-owned
            // package keeps exactly the provenance the server recorded — including none — so loading a
            // package the server never attributed to Console is not silently relabeled as Console-authored
            // on save (the builder does not model provenance).
            Provenance = isNewDraft
                ? new HonuaFormProvenanceRef
                {
                    Source = ProvenanceSource,
                    SourceVersion = "1"
                }
                : baseDocument.Provenance
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
        SectionLookup sections)
    {
        var originalById = originalFields
            .Where(field => !string.IsNullOrWhiteSpace(field.FieldId))
            .GroupBy(field => field.FieldId!, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);

        return fields.Select(field => ToFieldDefinition(field, originalById, sections)).ToArray();
    }

    private static HonuaFormFieldDefinition ToFieldDefinition(
        StudioFormFieldEditor field,
        IReadOnlyDictionary<string, HonuaFormFieldDefinition> originalById,
        SectionLookup sections)
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
            SectionId = ResolveSectionId(field, sections),
            Required = field.Required,
            Private = field.Private,
            Domain = ToDomain(field, original?.Domain),
            Validation = ToValidationRules(field, original?.Validation ?? []),
            Visibility = ToVisibility(field, original?.Visibility)
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
            OriginalSectionId = field.SectionId ?? string.Empty,
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
                    domain.Choices.Select(ToChoiceEditorLine));
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

    private static HonuaFormFieldDomainDefinition? ToDomain(
        StudioFormFieldEditor field,
        HonuaFormFieldDomainDefinition? original)
    {
        if (string.Equals(field.DomainKind, "coded", StringComparison.OrdinalIgnoreCase))
        {
            var choices = ParseChoices(field.Choices, original?.Choices ?? []);
            return choices.Length == 0
                ? null
                : new HonuaFormFieldDomainDefinition { Type = "codedValue", Choices = choices };
        }

        if (string.Equals(field.DomainKind, "range", StringComparison.OrdinalIgnoreCase))
        {
            var min = ParseNumberElement(field.RangeMin, original?.Min);
            var max = ParseNumberElement(field.RangeMax, original?.Max);
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
        var originalFirst = originalRules.FirstOrDefault();
        var preserved = originalRules.Skip(1).ToArray();

        if (string.IsNullOrWhiteSpace(field.ValidationType)
            || string.Equals(field.ValidationType, "none", StringComparison.OrdinalIgnoreCase))
        {
            return preserved;
        }

        var validationType = field.ValidationType.Trim();
        if (!IsSupportedValidationType(validationType))
        {
            return originalFirst is not null
                && string.Equals(validationType, originalFirst.Type, StringComparison.Ordinal)
                ? [originalFirst, .. preserved]
                : preserved;
        }

        if (originalFirst is not null && IsValidationRuleUnchanged(field, originalFirst))
        {
            return originalRules.ToArray();
        }

        if (originalFirst is not null
            && string.Equals(validationType, originalFirst.Type, StringComparison.Ordinal)
            && !IsSupportedValidationRule(originalFirst))
        {
            return [originalFirst, .. preserved];
        }

        var sameSupportedOriginal = originalFirst is not null
            && string.Equals(validationType, originalFirst.Type, StringComparison.Ordinal)
            && IsSupportedValidationRule(originalFirst);
        var numeric = validationType is "minLength" or "maxLength" or "min" or "max";
        JsonElement? parameters = validationType switch
        {
            "regex" => MergeValidationParameter(
                sameSupportedOriginal ? originalFirst?.Parameters : null,
                "pattern",
                string.IsNullOrWhiteSpace(field.ValidationValue)
                    ? null
                    : JsonSerializer.SerializeToElement(field.ValidationValue)),
            _ when numeric => MergeValidationParameter(
                sameSupportedOriginal ? originalFirst?.Parameters : null,
                "value",
                ParseNumberElement(
                    field.ValidationValue,
                    sameSupportedOriginal ? GetModeledParameter(originalFirst?.Parameters, "value") : null)),
            _ => null
        };

        return
        [
            (originalFirst ?? new HonuaFormValidationRule()) with
            {
                RuleId = sameSupportedOriginal && !string.IsNullOrWhiteSpace(originalFirst!.RuleId)
                    ? originalFirst.RuleId
                    : $"{field.FieldId}-{validationType}",
                Type = validationType,
                Message = NullIfBlank(field.ValidationMessage),
                Parameters = parameters
            },
            .. preserved
        ];
    }

    private static HonuaFormConditionalRule? ToVisibility(
        StudioFormFieldEditor field,
        HonuaFormConditionalRule? original)
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
                ? ParseValueElement(field.VisibilityValue, original?.Value)
                : null
        };
    }

    private static HonuaFormSectionDefinition[] BuildSections(
        IReadOnlyList<StudioFormFieldEditor> fields,
        SectionLookup sections)
    {
        var assignments = fields
            .Select(field => new
            {
                Field = field,
                Label = NullIfBlank(field.Group),
                SectionId = ResolveSectionId(field, sections)
            })
            .Where(item => item.Label is not null && item.SectionId is not null)
            .GroupBy(item => item.SectionId!, StringComparer.Ordinal)
            .Select(group => new SectionAssignment(
                group.Key,
                group.First().Label!,
                group
                    .Where(item => !string.IsNullOrWhiteSpace(item.Field.FieldId))
                    .Select(item => item.Field.FieldId.Trim())
                    .Distinct(StringComparer.Ordinal)
                    .ToArray()))
            .ToDictionary(assignment => assignment.SectionId, StringComparer.Ordinal);

        var merged = new List<HonuaFormSectionDefinition>();
        foreach (var original in sections.OriginalSections)
        {
            if (string.IsNullOrWhiteSpace(original.SectionId))
            {
                continue;
            }

            if (assignments.Remove(original.SectionId, out var assignment))
            {
                merged.Add(original with
                {
                    Label = assignment.Label,
                    FieldIds = assignment.FieldIds
                });
            }
            else
            {
                // The editor has no section-delete affordance. Preserve original sections even when they have
                // no currently modeled fields, but keep the field list consistent with the saved Fields array.
                merged.Add(original with { FieldIds = [] });
            }
        }

        merged.AddRange(assignments.Values.Select(assignment => new HonuaFormSectionDefinition
        {
            SectionId = assignment.SectionId,
            Label = assignment.Label,
            FieldIds = assignment.FieldIds
        }));

        return merged.ToArray();
    }

    private static SectionLookup BuildSectionLookup(IReadOnlyList<HonuaFormSectionDefinition> originalSections)
    {
        var ordered = originalSections
            .Where(section => !string.IsNullOrWhiteSpace(section.SectionId))
            .GroupBy(section => section.SectionId!, StringComparer.Ordinal)
            .Select(group => group.First())
            .ToArray();

        var byId = ordered.ToDictionary(section => section.SectionId!, StringComparer.Ordinal);
        var uniqueByGroup = ordered
            .GroupBy(section => NullIfBlank(section.Label) ?? section.SectionId!, StringComparer.Ordinal)
            .Where(group => group.Count() == 1)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);

        return new SectionLookup(ordered, byId, uniqueByGroup);
    }

    private static string? ResolveSectionId(StudioFormFieldEditor field, SectionLookup sections)
    {
        var label = NullIfBlank(field.Group);
        if (label is null)
        {
            return null;
        }

        var originalSectionId = NullIfBlank(field.OriginalSectionId);
        if (originalSectionId is not null
            && sections.ById.TryGetValue(originalSectionId, out var originalSection))
        {
            var originalLabel = NullIfBlank(originalSection.Label) ?? originalSectionId;
            if (string.Equals(label, originalLabel, StringComparison.Ordinal))
            {
                return originalSectionId;
            }
        }

        if (sections.UniqueByGroup.TryGetValue(label, out var original)
            && !string.IsNullOrWhiteSpace(original.SectionId))
        {
            return original.SectionId;
        }

        return sections.ById.TryGetValue(label, out var idMatch) && !string.IsNullOrWhiteSpace(idMatch.SectionId)
            ? idMatch.SectionId
            : Slug(label);
    }

    private sealed record SectionLookup(
        IReadOnlyList<HonuaFormSectionDefinition> OriginalSections,
        IReadOnlyDictionary<string, HonuaFormSectionDefinition> ById,
        IReadOnlyDictionary<string, HonuaFormSectionDefinition> UniqueByGroup);

    private sealed record SectionAssignment(string SectionId, string Label, string[] FieldIds);

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

        // Persist exactly what the operator selected. Never silently fabricate a `create` when every
        // operation is unchecked — that would save a write the editor does not show. A form with no submit
        // operation is instead blocked at the pre-publish gate (StudioFormPublishEvaluator), so the empty
        // selection surfaces as an explicit, fixable requirement rather than a hidden default.
        return operations.ToArray();
    }

    private static HonuaFormDomainChoice[] ParseChoices(
        string raw,
        IReadOnlyList<HonuaFormDomainChoice> originalChoices)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return [];
        }

        var originalByEditorLine = originalChoices
            .Select(choice => new { Line = ToChoiceEditorLine(choice), Choice = choice })
            .GroupBy(item => item.Line, StringComparer.Ordinal)
            .Where(group => group.Count() == 1)
            .ToDictionary(group => group.Key, group => group.First().Choice, StringComparer.Ordinal);

        var originalByDisplayValue = originalChoices
            .GroupBy(choice => JsonElementToString(choice.Code), StringComparer.Ordinal)
            .Where(group => group.Count() == 1)
            .ToDictionary(group => group.Key, group => group.First().Code, StringComparer.Ordinal);

        // Known server codes, longest first. A coded value is an arbitrary JSON value, so a string code can
        // itself contain '=' (valid server content). The editor encodes choices as "code=label" lines, so
        // match a whole known code before falling back to splitting on the first '=' — otherwise an
        // untouched choice whose code embeds '=' would be truncated and its remainder bled into the label
        // on save.
        var knownCodes = originalByDisplayValue.Keys
            .OrderByDescending(value => value.Length)
            .ToArray();

        return raw
            .Split(['\n', '\r'], StringSplitOptions.RemoveEmptyEntries)
            .Select(line =>
            {
                if (originalByEditorLine.TryGetValue(line, out var originalChoice))
                {
                    return originalChoice;
                }

                var (code, label) = SplitChoiceLine(line, knownCodes);
                return new HonuaFormDomainChoice
                {
                    Code = originalByDisplayValue.TryGetValue(code, out var originalCode)
                        ? originalCode
                        : JsonSerializer.SerializeToElement(code),
                    Label = label
                };
            })
            .ToArray();
    }

    private static string ToChoiceEditorLine(HonuaFormDomainChoice choice)
    {
        var code = JsonElementToString(choice.Code);
        return string.IsNullOrWhiteSpace(choice.Label) ? code : $"{code}={choice.Label}";
    }

    // Recover the (code, label) that a "code=label" editor line was built from. A known server code is
    // matched whole (longest first, so a code that is a prefix of another still wins its own line) which
    // lets a code containing '=' survive intact; a line the operator typed that matches no known code
    // splits on the first '='. A line that is exactly a known code (a choice the server stored with a
    // blank label) keeps the prior "label defaults to the code" behavior.
    private static (string Code, string Label) SplitChoiceLine(string line, string[] knownCodes)
    {
        foreach (var code in knownCodes)
        {
            if (string.Equals(line, code, StringComparison.Ordinal))
            {
                return (code, code);
            }

            if (line.Length > code.Length
                && line[code.Length] == '='
                && line.StartsWith(code, StringComparison.Ordinal))
            {
                return (code, line[(code.Length + 1)..]);
            }
        }

        var separator = line.IndexOf('=', StringComparison.Ordinal);
        return separator > 0
            ? (line[..separator], line[(separator + 1)..])
            : (line, line);
    }

    private static JsonElement ParseValueElement(string raw, JsonElement? original)
    {
        if (original is not { } originalValue)
        {
            return JsonSerializer.SerializeToElement(raw);
        }

        if (string.Equals(raw, JsonElementToString(originalValue), StringComparison.Ordinal))
        {
            return originalValue;
        }

        return originalValue.ValueKind switch
        {
            JsonValueKind.Number when TryParseJsonElement(raw, out var parsed) && parsed.ValueKind == JsonValueKind.Number => parsed,
            JsonValueKind.True or JsonValueKind.False when bool.TryParse(raw, out var parsedBool) => JsonSerializer.SerializeToElement(parsedBool),
            JsonValueKind.Array when TryParseJsonElement(raw, out var parsed) && parsed.ValueKind == JsonValueKind.Array => parsed,
            JsonValueKind.Object when TryParseJsonElement(raw, out var parsed) && parsed.ValueKind == JsonValueKind.Object => parsed,
            JsonValueKind.Null when string.Equals(raw, "null", StringComparison.OrdinalIgnoreCase) => JsonSerializer.SerializeToElement<string?>(null),
            _ => JsonSerializer.SerializeToElement(raw)
        };
    }

    private static bool TryParseJsonElement(string raw, out JsonElement element)
    {
        try
        {
            using var document = JsonDocument.Parse(raw);
            element = document.RootElement.Clone();
            return true;
        }
        catch (JsonException)
        {
            element = default;
            return false;
        }
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

    private static bool IsValidationRuleUnchanged(StudioFormFieldEditor field, HonuaFormValidationRule original) =>
        string.Equals(field.ValidationType.Trim(), original.Type, StringComparison.Ordinal)
        && string.Equals(NullIfBlank(field.ValidationMessage), NullIfBlank(original.Message), StringComparison.Ordinal)
        && string.Equals(field.ValidationValue, ExtractRuleValue(original.Parameters), StringComparison.Ordinal);

    private static bool IsSupportedValidationType(string? type) =>
        type is "regex" or "minLength" or "maxLength" or "min" or "max";

    private static bool IsSupportedValidationRule(HonuaFormValidationRule rule)
    {
        var type = NullIfBlank(rule.Type);
        if (!IsSupportedValidationType(type))
        {
            return false;
        }

        if (type == "regex")
        {
            return rule.Parameters is null
                || rule.Parameters.Value.ValueKind == JsonValueKind.Null
                || rule.Parameters.Value.ValueKind == JsonValueKind.Undefined
                || (rule.Parameters.Value.ValueKind == JsonValueKind.Object
                    && rule.Parameters.Value.TryGetProperty("pattern", out var pattern)
                    && pattern.ValueKind == JsonValueKind.String);
        }

        return HasModeledParameter(rule.Parameters, "value");
    }

    private static bool HasModeledParameter(JsonElement? parameters, string propertyName) =>
        parameters is null
        || parameters.Value.ValueKind == JsonValueKind.Null
        || parameters.Value.ValueKind == JsonValueKind.Undefined
        || (parameters.Value.ValueKind == JsonValueKind.Object
            && parameters.Value.TryGetProperty(propertyName, out _));

    private static JsonElement? MergeValidationParameter(
        JsonElement? originalParameters,
        string propertyName,
        JsonElement? value)
    {
        var properties = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        if (originalParameters is { } original && original.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in original.EnumerateObject())
            {
                properties[property.Name] = property.Value.Clone();
            }
        }

        if (value is { } modeledValue)
        {
            properties[propertyName] = modeledValue;
        }

        return properties.Count == 0 ? null : JsonSerializer.SerializeToElement(properties);
    }

    private static JsonElement? GetModeledParameter(JsonElement? parameters, string propertyName) =>
        parameters is { ValueKind: JsonValueKind.Object } element
            && element.TryGetProperty(propertyName, out var property)
            ? property
            : null;

    private static JsonElement? ParseNumberElement(string raw, JsonElement? original = null)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        var trimmed = raw.Trim();
        if (original is { } originalValue
            && string.Equals(trimmed, JsonElementToString(originalValue), StringComparison.Ordinal))
        {
            return originalValue;
        }

        try
        {
            using var document = JsonDocument.Parse(trimmed);
            return document.RootElement.ValueKind == JsonValueKind.Number
                ? document.RootElement.Clone()
                : JsonSerializer.SerializeToElement(trimmed);
        }
        catch (JsonException)
        {
            return JsonSerializer.SerializeToElement(trimmed);
        }
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
