using System.Text.Json;
using Honua.Console.Contracts;
using Honua.Console.Shell.Models;

namespace Honua.Console.IntegrationTests;

/// <summary>
/// Docker-free unit coverage for the Console-owned form authoring state: the
/// <see cref="StudioFormPackageMapper"/> round-trip against the server <c>honua.form-package.v1</c>
/// wire records, and the <see cref="StudioFormPublishEvaluator"/> pre-publish gate that enforces an
/// explicit offline policy (AC#2) and a configured + validated submit target (AC#3).
/// </summary>
public sealed class StudioFormPackageMapperTests
{
    [Fact]
    public void ToDocument_ThenToEditorState_RoundTripsAuthoringState()
    {
        var state = new StudioFormEditorState
        {
            Title = "Hydrant inspection",
            Description = "Field capture",
            ServiceId = "inspections",
            LayerId = 7,
            AllowCreate = true,
            AllowUpdate = true,
            RequiresGeometry = true,
            AttachmentsEnabled = true,
            MaxAttachmentsPerSubmission = 4,
            AllowedContentTypes = "image/*, application/pdf",
            RequireExifStripping = true,
            CaptureActor = true,
            PrivacyRetentionDays = 90,
            RequiredTransformations = "auditOnly",
            OfflineEnabled = true,
            ReplicaTransportEnabled = true,
            FieldCollectionTransportEnabled = false,
            ConflictReviewMode = "lastWriteWins"
        };
        state.Fields.Add(new StudioFormFieldEditor
        {
            FieldId = "condition",
            Label = "Condition",
            Group = "Inspection",
            Type = "choice",
            TargetField = "condition",
            Required = true,
            Private = true,
            DomainKind = "coded",
            Choices = "good=Good\nbad=Bad",
            ValidationType = "minLength",
            ValidationValue = "2",
            ValidationMessage = "Pick a value",
            VisibilityDependsOn = "asset_type",
            VisibilityOperator = "equals",
            VisibilityValue = "hydrant"
        });
        state.Fields.Add(new StudioFormFieldEditor
        {
            FieldId = "photo",
            Label = "Photo",
            Group = "Evidence",
            Type = "attachment",
            Required = true,
            AttachmentMaxCount = 3
        });

        var document = StudioFormPackageMapper.ToDocument(state);
        var version = new HonuaFormPackageVersion
        {
            FormId = "form-1",
            Version = 2,
            Status = HonuaFormPackageStatus.Draft,
            ETag = "etag-2",
            Package = document
        };

        var round = StudioFormPackageMapper.ToEditorState(version);

        Assert.Equal("form-1", round.FormId);
        Assert.Equal(2, round.Version);
        Assert.Equal("etag-2", round.ETag);
        Assert.Equal("inspections", round.ServiceId);
        Assert.Equal(7, round.LayerId);
        Assert.True(round.AllowCreate);
        Assert.True(round.AllowUpdate);
        Assert.False(round.AllowDelete);
        Assert.True(round.AttachmentsEnabled);
        Assert.Equal(4, round.MaxAttachmentsPerSubmission);
        Assert.True(round.RequireExifStripping);
        Assert.Equal(90, round.PrivacyRetentionDays);
        Assert.True(round.OfflineEnabled);
        Assert.True(round.ReplicaTransportEnabled);
        Assert.False(round.FieldCollectionTransportEnabled);
        Assert.Equal("lastWriteWins", round.ConflictReviewMode);

        var condition = round.Fields.Single(field => field.FieldId == "condition");
        Assert.Equal("Inspection", condition.Group);
        Assert.Equal("choice", condition.Type);
        Assert.True(condition.Required);
        Assert.True(condition.Private);
        Assert.Equal("coded", condition.DomainKind);
        Assert.Contains("good=Good", condition.Choices, StringComparison.Ordinal);
        Assert.Equal("minLength", condition.ValidationType);
        Assert.Equal("2", condition.ValidationValue);
        Assert.Equal("asset_type", condition.VisibilityDependsOn);
        Assert.Equal("hydrant", condition.VisibilityValue);

        var photo = round.Fields.Single(field => field.FieldId == "photo");
        Assert.True(photo.IsAttachment);
        Assert.Equal(3, photo.AttachmentMaxCount);
    }

    [Fact]
    public void ToDocument_BuildsSectionsFromDistinctGroups()
    {
        var state = StudioFormPackageMapper.CreateTemplate();

        var document = StudioFormPackageMapper.ToDocument(state);

        Assert.Equal("honua.form-package.v1", document.SchemaVersion);
        Assert.Contains(document.Sections, section => section.Label == "Identification");
        Assert.Contains(document.Sections, section => section.Label == "Inspection");
        Assert.All(state.Fields, field => Assert.Contains(document.Fields, mapped => mapped.FieldId == field.FieldId));
    }

    [Fact]
    public void ToDocument_OnExistingPackage_PreservesServerFieldsTheBuilderDoesNotModel()
    {
        // A server document carrying contract fields the authoring UI never surfaces.
        var serverDocument = new HonuaFormPackageDocument
        {
            SchemaVersion = "honua.form-package.v1",
            FormId = "form-7",
            Title = "Server form",
            Target = new HonuaFormTargetDefinition { ServiceId = "svc", LayerId = 3 },
            Sections =
            [
                new HonuaFormSectionDefinition
                {
                    SectionId = "identification",
                    Label = "Identification",
                    Description = "Server-authored section description",
                    FieldIds = ["asset_id"]
                }
            ],
            Fields =
            [
                new HonuaFormFieldDefinition
                {
                    FieldId = "asset_id",
                    Label = "Asset",
                    Type = "text",
                    TargetField = "asset_id",
                    SectionId = "identification",
                    Hint = "Scan the asset tag",
                    ReadOnly = true,
                    DefaultValue = JsonSerializer.SerializeToElement("AUTO"),
                    Validation =
                    [
                        new HonuaFormValidationRule { RuleId = "asset_id-minLength", Type = "minLength", Parameters = JsonSerializer.SerializeToElement(new { value = 3 }) },
                        new HonuaFormValidationRule { RuleId = "asset_id-regex", Type = "regex", Parameters = JsonSerializer.SerializeToElement(new { pattern = "^A" }) }
                    ]
                },
                new HonuaFormFieldDefinition
                {
                    FieldId = "photo",
                    Label = "Photo",
                    Type = "attachment",
                    SectionId = "identification"
                }
            ],
            SubmitPolicy = new HonuaFormSubmitPolicy { MaxOfflineAgeSeconds = 3600 },
            AttachmentPolicy = new HonuaFormAttachmentPolicy
            {
                Enabled = true,
                MaxAttachmentBytes = 1_048_576,
                MaxTotalBytes = 4_194_304,
                Fields = [new HonuaFormFieldAttachmentPolicy { FieldId = "photo", MaxCount = 2, AllowedContentTypes = ["image/png"] }]
            },
            OfflinePolicy = new HonuaFormOfflinePolicy { Enabled = true, PreferredTransports = ["custom-transport"] },
            Provenance = new HonuaFormProvenanceRef { Source = "arcgis-import", SourceVersion = "9" },
            Metadata = new Dictionary<string, string> { ["org"] = "public-works", ["template"] = "v3" }
        };
        var version = new HonuaFormPackageVersion
        {
            FormId = "form-7",
            Version = 5,
            Status = HonuaFormPackageStatus.Draft,
            ETag = "etag-5",
            Package = serverDocument
        };

        // Load the package, make a modeled edit, then save back to the wire document.
        var state = StudioFormPackageMapper.ToEditorState(version);
        state.Title = "Edited title";
        var saved = StudioFormPackageMapper.ToDocument(state);

        // The modeled edit applied.
        Assert.Equal("Edited title", saved.Title);

        // Document-level fields the builder does not model survive the round-trip.
        Assert.Equal("honua.form-package.v1", saved.SchemaVersion);
        Assert.NotNull(saved.Metadata);
        Assert.Equal("public-works", saved.Metadata!["org"]);
        Assert.Equal("v3", saved.Metadata["template"]);
        Assert.Equal(3600, saved.SubmitPolicy.MaxOfflineAgeSeconds);
        Assert.Equal(1_048_576, saved.AttachmentPolicy.MaxAttachmentBytes);
        Assert.Equal(4_194_304, saved.AttachmentPolicy.MaxTotalBytes);
        Assert.Equal(new[] { "custom-transport" }, saved.OfflinePolicy.PreferredTransports);
        // The server-recorded provenance is not clobbered by the Console stamp on an existing package.
        Assert.Equal("arcgis-import", saved.Provenance!.Source);

        // The section description survives.
        var section = Assert.Single(saved.Sections);
        Assert.Equal("Server-authored section description", section.Description);

        // Field-level attributes the builder does not model survive.
        var field = saved.Fields.Single(mapped => mapped.FieldId == "asset_id");
        Assert.Equal("Scan the asset tag", field.Hint);
        Assert.True(field.ReadOnly);
        Assert.Equal("AUTO", field.DefaultValue?.GetString());

        // Per-field attachment allowed content types survive (preserved for the attachment-type field).
        var attachment = Assert.Single(saved.AttachmentPolicy.Fields);
        Assert.Equal("photo", attachment.FieldId);
        Assert.Equal(new[] { "image/png" }, attachment.AllowedContentTypes);

        // The extra validation rule beyond the one the editor models survives alongside the modeled one.
        Assert.Contains(field.Validation, rule => rule.Type == "minLength");
        Assert.Contains(field.Validation, rule => rule.Type == "regex");

        // The loaded baseline means an unedited load is not dirty; the title edit registers as dirty.
        var pristine = StudioFormPackageMapper.ToEditorState(version);
        Assert.False(StudioFormPackageMapper.HasUnsavedEdits(pristine));
        Assert.True(StudioFormPackageMapper.HasUnsavedEdits(state));
    }

    [Fact]
    public void ToDocument_OnExistingPackageWithNullProvenance_PreservesNullInsteadOfStampingConsole()
    {
        // Regression: a server package the server never attributed carries null provenance. Saving an
        // authoring edit must not relabel it as Console-authored — provenance is server-owned and the
        // builder does not model it, so "existing package" (a loaded baseline), not "null provenance",
        // is what decides whether the Console stamp applies.
        var serverDocument = new HonuaFormPackageDocument
        {
            FormId = "form-noprov",
            Title = "Server form",
            Target = new HonuaFormTargetDefinition { ServiceId = "svc", LayerId = 1 },
            Fields =
            [
                new HonuaFormFieldDefinition
                {
                    FieldId = "asset_id",
                    Label = "Asset",
                    Type = "text",
                    TargetField = "asset_id"
                }
            ]
            // Provenance intentionally left null: the server never recorded one.
        };
        var version = new HonuaFormPackageVersion
        {
            FormId = "form-noprov",
            Version = 4,
            Status = HonuaFormPackageStatus.Draft,
            ETag = "etag-4",
            Package = serverDocument
        };

        var state = StudioFormPackageMapper.ToEditorState(version);
        state.Title = "Edited title";
        var saved = StudioFormPackageMapper.ToDocument(state);

        Assert.Equal("Edited title", saved.Title);
        Assert.Null(saved.Provenance);
    }

    [Fact]
    public void ToDocument_OnBrandNewDraft_StampsConsoleProvenance()
    {
        // The only case Console stamps its own provenance: a never-loaded draft with no OriginalDocument.
        var saved = StudioFormPackageMapper.ToDocument(StudioFormPackageMapper.CreateTemplate());

        Assert.NotNull(saved.Provenance);
        Assert.Equal("honua-console.studio.form-builder", saved.Provenance!.Source);
    }

    [Fact]
    public void ToDocument_OnExistingPackage_PreservesJsonDomainAndVisibilityValues()
    {
        var serverDocument = new HonuaFormPackageDocument
        {
            FormId = "form-json",
            Title = "JSON form",
            Fields =
            [
                new HonuaFormFieldDefinition
                {
                    FieldId = "condition",
                    Label = "Condition",
                    Type = "choice",
                    Domain = new HonuaFormFieldDomainDefinition
                    {
                        Type = "codedValue",
                        Choices =
                        [
                            new HonuaFormDomainChoice
                            {
                                Code = JsonSerializer.SerializeToElement(1),
                                Label = "One"
                            },
                            new HonuaFormDomainChoice
                            {
                                Code = JsonSerializer.SerializeToElement(true),
                                Label = "Enabled"
                            },
                            new HonuaFormDomainChoice
                            {
                                Code = JsonSerializer.SerializeToElement(new { code = "nested" }),
                                Label = "Nested"
                            }
                        ]
                    }
                },
                new HonuaFormFieldDefinition
                {
                    FieldId = "numeric_visible",
                    Label = "Numeric visibility",
                    Type = "text",
                    Visibility = new HonuaFormConditionalRule
                    {
                        DependsOnFieldId = "condition",
                        Operator = "equals",
                        Value = JsonSerializer.SerializeToElement(1)
                    }
                },
                new HonuaFormFieldDefinition
                {
                    FieldId = "boolean_visible",
                    Label = "Boolean visibility",
                    Type = "text",
                    Visibility = new HonuaFormConditionalRule
                    {
                        DependsOnFieldId = "condition",
                        Operator = "equals",
                        Value = JsonSerializer.SerializeToElement(true)
                    }
                },
                new HonuaFormFieldDefinition
                {
                    FieldId = "array_visible",
                    Label = "Array visibility",
                    Type = "text",
                    Visibility = new HonuaFormConditionalRule
                    {
                        DependsOnFieldId = "condition",
                        Operator = "in",
                        Value = JsonSerializer.SerializeToElement(new[] { 1, 2 })
                    }
                }
            ]
        };
        var version = new HonuaFormPackageVersion
        {
            FormId = "form-json",
            Version = 1,
            Status = HonuaFormPackageStatus.Draft,
            Package = serverDocument
        };

        var state = StudioFormPackageMapper.ToEditorState(version);
        Assert.False(StudioFormPackageMapper.HasUnsavedEdits(state));

        var condition = state.Fields.Single(field => field.FieldId == "condition");
        condition.Choices = condition.Choices.Replace("1=One", "1=First", StringComparison.Ordinal);
        var saved = StudioFormPackageMapper.ToDocument(state);

        var choices = saved.Fields.Single(field => field.FieldId == "condition").Domain!.Choices;
        var numericChoice = choices.Single(choice => choice.Label == "First");
        Assert.Equal(JsonValueKind.Number, numericChoice.Code.ValueKind);
        Assert.Equal(1, numericChoice.Code.GetInt32());

        var booleanChoice = choices.Single(choice => choice.Label == "Enabled");
        Assert.Equal(JsonValueKind.True, booleanChoice.Code.ValueKind);
        Assert.True(booleanChoice.Code.GetBoolean());

        var objectChoice = choices.Single(choice => choice.Label == "Nested");
        Assert.Equal(JsonValueKind.Object, objectChoice.Code.ValueKind);
        Assert.Equal("nested", objectChoice.Code.GetProperty("code").GetString());

        var numericVisibility = saved.Fields.Single(field => field.FieldId == "numeric_visible").Visibility!.Value;
        Assert.True(numericVisibility.HasValue);
        Assert.Equal(JsonValueKind.Number, numericVisibility.Value.ValueKind);
        Assert.Equal(1, numericVisibility.Value.GetInt32());

        var booleanVisibility = saved.Fields.Single(field => field.FieldId == "boolean_visible").Visibility!.Value;
        Assert.True(booleanVisibility.HasValue);
        Assert.Equal(JsonValueKind.True, booleanVisibility.Value.ValueKind);
        Assert.True(booleanVisibility.Value.GetBoolean());

        var arrayVisibility = saved.Fields.Single(field => field.FieldId == "array_visible").Visibility!.Value;
        Assert.True(arrayVisibility.HasValue);
        Assert.Equal(JsonValueKind.Array, arrayVisibility.Value.ValueKind);
        Assert.Equal([1, 2], arrayVisibility.Value.EnumerateArray().Select(item => item.GetInt32()).ToArray());
    }

    [Fact]
    public void ToDocument_OnExistingPackage_PreservesCodedChoicesContainingEquals()
    {
        // Regression: a string coded value can itself contain '=' (valid server content). The editor
        // encodes choices as "code=label" lines, so a naive split on the first '=' would truncate such a
        // code and bleed the remainder into the label. An untouched choice must survive a load → save.
        var serverDocument = new HonuaFormPackageDocument
        {
            FormId = "form-eq",
            Title = "Server form",
            Fields =
            [
                new HonuaFormFieldDefinition
                {
                    FieldId = "ratio",
                    Label = "Ratio",
                    Type = "choice",
                    Domain = new HonuaFormFieldDomainDefinition
                    {
                        Type = "codedValue",
                        Choices =
                        [
                            // Code embeds '=' — the corruption case.
                            new HonuaFormDomainChoice { Code = JsonSerializer.SerializeToElement("a=b"), Label = "A to B" },
                            // Plain code, and a code whose label embeds '=' — must also stay intact.
                            new HonuaFormDomainChoice { Code = JsonSerializer.SerializeToElement("c"), Label = "Plain C" },
                            new HonuaFormDomainChoice { Code = JsonSerializer.SerializeToElement("d"), Label = "w=2" }
                        ]
                    }
                }
            ]
        };
        var version = new HonuaFormPackageVersion
        {
            FormId = "form-eq",
            Version = 1,
            Status = HonuaFormPackageStatus.Draft,
            Package = serverDocument
        };

        var state = StudioFormPackageMapper.ToEditorState(version);
        // A modeled edit elsewhere; the coded domain is left untouched.
        state.Title = "Edited title";
        var saved = StudioFormPackageMapper.ToDocument(state);

        var choices = saved.Fields.Single(field => field.FieldId == "ratio").Domain!.Choices;
        Assert.Equal(3, choices.Length);

        var embedded = choices.Single(choice => choice.Label == "A to B");
        Assert.Equal(JsonValueKind.String, embedded.Code.ValueKind);
        Assert.Equal("a=b", embedded.Code.GetString());

        var plain = choices.Single(choice => choice.Label == "Plain C");
        Assert.Equal("c", plain.Code.GetString());

        // A label containing '=' is preserved whole alongside its plain code.
        var labelWithEquals = choices.Single(choice => choice.Code.GetString() == "d");
        Assert.Equal("w=2", labelWithEquals.Label);
    }

    [Fact]
    public void ToDocument_OnExistingPackage_PreservesLosslessCodedChoiceLines()
    {
        // Server-coded choices are valid even when semicolons, significant whitespace, or empty/null
        // labels appear in codes or labels. An unrelated save must not normalize those values.
        var serverDocument = new HonuaFormPackageDocument
        {
            FormId = "form-lossless-choices",
            Title = "Server form",
            Fields =
            [
                new HonuaFormFieldDefinition
                {
                    FieldId = "condition",
                    Label = "Condition",
                    Type = "choice",
                    Domain = new HonuaFormFieldDomainDefinition
                    {
                        Type = "codedValue",
                        Choices =
                        [
                            new HonuaFormDomainChoice { Code = JsonSerializer.SerializeToElement("semi;colon"), Label = "Label; With; Semis" },
                            new HonuaFormDomainChoice { Code = JsonSerializer.SerializeToElement(" spaced "), Label = " Label " },
                            new HonuaFormDomainChoice { Code = JsonSerializer.SerializeToElement("blank"), Label = string.Empty },
                            new HonuaFormDomainChoice { Code = JsonSerializer.SerializeToElement("null-label"), Label = null }
                        ]
                    }
                }
            ]
        };
        var version = new HonuaFormPackageVersion
        {
            FormId = "form-lossless-choices",
            Version = 1,
            Status = HonuaFormPackageStatus.Draft,
            Package = serverDocument
        };

        var state = StudioFormPackageMapper.ToEditorState(version);
        state.Title = "Edited title";
        var saved = StudioFormPackageMapper.ToDocument(state);

        var choices = saved.Fields.Single(field => field.FieldId == "condition").Domain!.Choices;
        Assert.Equal(4, choices.Length);

        var semicolon = choices.Single(choice => choice.Code.GetString() == "semi;colon");
        Assert.Equal("Label; With; Semis", semicolon.Label);

        var spaced = choices.Single(choice => choice.Code.GetString() == " spaced ");
        Assert.Equal(" Label ", spaced.Label);

        var blank = choices.Single(choice => choice.Code.GetString() == "blank");
        Assert.Equal(string.Empty, blank.Label);

        var nullLabel = choices.Single(choice => choice.Code.GetString() == "null-label");
        Assert.Null(nullLabel.Label);
    }

    [Fact]
    public void ToDocument_OnExistingPackage_PreservesVisibilityJsonTypeWhenEdited()
    {
        var serverDocument = new HonuaFormPackageDocument
        {
            FormId = "form-visibility-types",
            Title = "Visibility type form",
            Fields =
            [
                new HonuaFormFieldDefinition
                {
                    FieldId = "numeric_visible",
                    Label = "Numeric visibility",
                    Type = "text",
                    Visibility = new HonuaFormConditionalRule
                    {
                        DependsOnFieldId = "condition",
                        Operator = "greaterThan",
                        Value = JsonSerializer.SerializeToElement(1)
                    }
                },
                new HonuaFormFieldDefinition
                {
                    FieldId = "boolean_visible",
                    Label = "Boolean visibility",
                    Type = "text",
                    Visibility = new HonuaFormConditionalRule
                    {
                        DependsOnFieldId = "condition",
                        Operator = "equals",
                        Value = JsonSerializer.SerializeToElement(true)
                    }
                },
                new HonuaFormFieldDefinition
                {
                    FieldId = "array_visible",
                    Label = "Array visibility",
                    Type = "text",
                    Visibility = new HonuaFormConditionalRule
                    {
                        DependsOnFieldId = "condition",
                        Operator = "in",
                        Value = JsonSerializer.SerializeToElement(new[] { 1, 2 })
                    }
                }
            ]
        };
        var version = new HonuaFormPackageVersion
        {
            FormId = "form-visibility-types",
            Version = 1,
            Status = HonuaFormPackageStatus.Draft,
            Package = serverDocument
        };

        var state = StudioFormPackageMapper.ToEditorState(version);
        state.Fields.Single(field => field.FieldId == "numeric_visible").VisibilityValue = "2";
        state.Fields.Single(field => field.FieldId == "boolean_visible").VisibilityValue = "false";
        state.Fields.Single(field => field.FieldId == "array_visible").VisibilityValue = "[3,4]";
        var saved = StudioFormPackageMapper.ToDocument(state);

        var numericVisibility = saved.Fields.Single(field => field.FieldId == "numeric_visible").Visibility!.Value;
        Assert.True(numericVisibility.HasValue);
        Assert.Equal(JsonValueKind.Number, numericVisibility.Value.ValueKind);
        Assert.Equal(2, numericVisibility.Value.GetInt32());

        var booleanVisibility = saved.Fields.Single(field => field.FieldId == "boolean_visible").Visibility!.Value;
        Assert.True(booleanVisibility.HasValue);
        Assert.Equal(JsonValueKind.False, booleanVisibility.Value.ValueKind);
        Assert.False(booleanVisibility.Value.GetBoolean());

        var arrayVisibility = saved.Fields.Single(field => field.FieldId == "array_visible").Visibility!.Value;
        Assert.True(arrayVisibility.HasValue);
        Assert.Equal(JsonValueKind.Array, arrayVisibility.Value.ValueKind);
        Assert.Equal([3, 4], arrayVisibility.Value.EnumerateArray().Select(item => item.GetInt32()).ToArray());
    }

    [Fact]
    public void ToDocument_OnExistingPackage_PreservesRangeAndValidationServerJson()
    {
        var serverDocument = new HonuaFormPackageDocument
        {
            FormId = "form-rules",
            Title = "Server rules",
            Fields =
            [
                new HonuaFormFieldDefinition
                {
                    FieldId = "rating",
                    Label = "Rating",
                    Type = "number",
                    Domain = new HonuaFormFieldDomainDefinition
                    {
                        Type = "range",
                        Min = Json("9007199254740993"),
                        Max = Json("1.0")
                    }
                },
                new HonuaFormFieldDefinition
                {
                    FieldId = "asset_id",
                    Label = "Asset",
                    Type = "text",
                    Validation =
                    [
                        new HonuaFormValidationRule
                        {
                            RuleId = "stable-min",
                            Type = "minLength",
                            Message = "Keep length",
                            Parameters = JsonSerializer.SerializeToElement(new { value = 3, mode = "grapheme" })
                        }
                    ]
                },
                new HonuaFormFieldDefinition
                {
                    FieldId = "custom_check",
                    Label = "Custom check",
                    Type = "text",
                    Validation =
                    [
                        new HonuaFormValidationRule
                        {
                            RuleId = "custom-rule",
                            Type = "serverOnly",
                            Message = "Server check",
                            Parameters = JsonSerializer.SerializeToElement(new { expression = "value IS NOT NULL", severity = "warning" })
                        }
                    ]
                }
            ]
        };
        var version = new HonuaFormPackageVersion
        {
            FormId = "form-rules",
            Version = 1,
            Status = HonuaFormPackageStatus.Draft,
            Package = serverDocument
        };

        var state = StudioFormPackageMapper.ToEditorState(version);
        Assert.False(StudioFormPackageMapper.HasUnsavedEdits(state));

        state.Title = "Edited title";
        var saved = StudioFormPackageMapper.ToDocument(state);

        var range = saved.Fields.Single(field => field.FieldId == "rating").Domain!;
        Assert.Equal("9007199254740993", range.Min!.Value.GetRawText());
        Assert.Equal("1.0", range.Max!.Value.GetRawText());

        var unchangedRule = Assert.Single(saved.Fields.Single(field => field.FieldId == "asset_id").Validation);
        Assert.Equal("stable-min", unchangedRule.RuleId);
        Assert.Equal("grapheme", unchangedRule.Parameters!.Value.GetProperty("mode").GetString());
        Assert.Equal(3, unchangedRule.Parameters.Value.GetProperty("value").GetInt32());

        var customRule = Assert.Single(saved.Fields.Single(field => field.FieldId == "custom_check").Validation);
        Assert.Equal("custom-rule", customRule.RuleId);
        Assert.Equal("serverOnly", customRule.Type);
        Assert.Equal("value IS NOT NULL", customRule.Parameters!.Value.GetProperty("expression").GetString());

        var editedState = StudioFormPackageMapper.ToEditorState(version);
        var editedRule = editedState.Fields.Single(field => field.FieldId == "asset_id");
        editedRule.ValidationValue = "4";
        editedRule.ValidationMessage = "Longer asset id";
        var savedEditedRule = StudioFormPackageMapper.ToDocument(editedState)
            .Fields
            .Single(field => field.FieldId == "asset_id")
            .Validation
            .Single();

        Assert.Equal("stable-min", savedEditedRule.RuleId);
        Assert.Equal("Longer asset id", savedEditedRule.Message);
        Assert.Equal("grapheme", savedEditedRule.Parameters!.Value.GetProperty("mode").GetString());
        Assert.Equal(4, savedEditedRule.Parameters.Value.GetProperty("value").GetInt32());
    }

    [Fact]
    public void ToDocument_OnExistingPackage_PreservesServerSectionIdentityWhenIdIsNotLabelSlug()
    {
        // A server section whose id is NOT the slug of its label (an opaque server-assigned id). The editor
        // only carries the section's label (as the field Group), so a save that re-slugs the label would mint
        // a new id ("site-inspection") and drop the description — losing server section identity.
        var serverDocument = new HonuaFormPackageDocument
        {
            FormId = "form-9",
            Title = "Server form",
            Target = new HonuaFormTargetDefinition { ServiceId = "svc", LayerId = 1 },
            Sections =
            [
                new HonuaFormSectionDefinition
                {
                    SectionId = "sec-7f3a9c",
                    Label = "Site Inspection",
                    Description = "Server-authored section description",
                    FieldIds = ["condition"]
                }
            ],
            Fields =
            [
                new HonuaFormFieldDefinition
                {
                    FieldId = "condition",
                    Label = "Condition",
                    Type = "text",
                    TargetField = "condition",
                    SectionId = "sec-7f3a9c"
                }
            ]
        };
        var version = new HonuaFormPackageVersion
        {
            FormId = "form-9",
            Version = 3,
            Status = HonuaFormPackageStatus.Draft,
            ETag = "etag-3",
            Package = serverDocument
        };

        // Load, make a modeled edit that does not touch the section, then save back to the wire document.
        var state = StudioFormPackageMapper.ToEditorState(version);
        state.Title = "Edited title";
        var saved = StudioFormPackageMapper.ToDocument(state);

        // The server-assigned section id and description survive instead of being re-derived from the label.
        var section = Assert.Single(saved.Sections);
        Assert.Equal("sec-7f3a9c", section.SectionId);
        Assert.Equal("Site Inspection", section.Label);
        Assert.Equal("Server-authored section description", section.Description);

        // The field still references the preserved section id, so field → section linkage stays consistent.
        var field = saved.Fields.Single(mapped => mapped.FieldId == "condition");
        Assert.Equal("sec-7f3a9c", field.SectionId);
    }

    [Fact]
    public void ToDocument_OnExistingPackage_PreservesDuplicateLabelAndEmptySections()
    {
        // Duplicate section labels are valid server content; section identity is the sectionId. The editor
        // displays only the label, so the hidden OriginalSectionId must keep these from collapsing together.
        var serverDocument = new HonuaFormPackageDocument
        {
            FormId = "form-10",
            Title = "Server form",
            Sections =
            [
                new HonuaFormSectionDefinition
                {
                    SectionId = "sec-a",
                    Label = "Inspection",
                    Description = "First inspection section",
                    FieldIds = ["condition"]
                },
                new HonuaFormSectionDefinition
                {
                    SectionId = "sec-b",
                    Label = "Inspection",
                    Description = "Second inspection section",
                    FieldIds = ["photo"]
                },
                new HonuaFormSectionDefinition
                {
                    SectionId = "sec-empty",
                    Label = "Office review",
                    Description = "Server-authored empty section"
                }
            ],
            Fields =
            [
                new HonuaFormFieldDefinition
                {
                    FieldId = "condition",
                    Label = "Condition",
                    Type = "text",
                    SectionId = "sec-a"
                },
                new HonuaFormFieldDefinition
                {
                    FieldId = "photo",
                    Label = "Photo",
                    Type = "attachment",
                    SectionId = "sec-b"
                }
            ]
        };
        var version = new HonuaFormPackageVersion
        {
            FormId = "form-10",
            Version = 1,
            Status = HonuaFormPackageStatus.Draft,
            Package = serverDocument
        };

        var state = StudioFormPackageMapper.ToEditorState(version);
        state.Title = "Edited title";
        var saved = StudioFormPackageMapper.ToDocument(state);

        Assert.Equal(3, saved.Sections.Length);

        var first = saved.Sections.Single(section => section.SectionId == "sec-a");
        Assert.Equal("Inspection", first.Label);
        Assert.Equal("First inspection section", first.Description);
        Assert.Equal(["condition"], first.FieldIds);

        var second = saved.Sections.Single(section => section.SectionId == "sec-b");
        Assert.Equal("Inspection", second.Label);
        Assert.Equal("Second inspection section", second.Description);
        Assert.Equal(["photo"], second.FieldIds);

        var empty = saved.Sections.Single(section => section.SectionId == "sec-empty");
        Assert.Equal("Office review", empty.Label);
        Assert.Equal("Server-authored empty section", empty.Description);
        Assert.Empty(empty.FieldIds);

        Assert.Equal("sec-a", saved.Fields.Single(field => field.FieldId == "condition").SectionId);
        Assert.Equal("sec-b", saved.Fields.Single(field => field.FieldId == "photo").SectionId);
    }

    [Fact]
    public void ToDocument_OnExistingPackage_AllowsLoadedFieldToMoveToExistingSection()
    {
        var serverDocument = new HonuaFormPackageDocument
        {
            FormId = "form-11",
            Title = "Server form",
            Sections =
            [
                new HonuaFormSectionDefinition
                {
                    SectionId = "sec-inspection",
                    Label = "Inspection",
                    Description = "Original section",
                    FieldIds = ["condition", "photo"]
                },
                new HonuaFormSectionDefinition
                {
                    SectionId = "sec-evidence",
                    Label = "Evidence",
                    Description = "Target section"
                }
            ],
            Fields =
            [
                new HonuaFormFieldDefinition
                {
                    FieldId = "condition",
                    Label = "Condition",
                    Type = "text",
                    SectionId = "sec-inspection"
                },
                new HonuaFormFieldDefinition
                {
                    FieldId = "photo",
                    Label = "Photo",
                    Type = "attachment",
                    SectionId = "sec-inspection"
                }
            ]
        };
        var version = new HonuaFormPackageVersion
        {
            FormId = "form-11",
            Version = 1,
            Status = HonuaFormPackageStatus.Draft,
            Package = serverDocument
        };

        var state = StudioFormPackageMapper.ToEditorState(version);
        state.Fields.Single(field => field.FieldId == "photo").Group = "Evidence";
        var saved = StudioFormPackageMapper.ToDocument(state);

        var original = saved.Sections.Single(section => section.SectionId == "sec-inspection");
        Assert.Equal("Inspection", original.Label);
        Assert.Equal("Original section", original.Description);
        Assert.Equal(["condition"], original.FieldIds);

        var moved = saved.Sections.Single(section => section.SectionId == "sec-evidence");
        Assert.Equal("Evidence", moved.Label);
        Assert.Equal("Target section", moved.Description);
        Assert.Equal(["photo"], moved.FieldIds);

        Assert.Equal("sec-inspection", saved.Fields.Single(field => field.FieldId == "condition").SectionId);
        Assert.Equal("sec-evidence", saved.Fields.Single(field => field.FieldId == "photo").SectionId);
    }

    [Fact]
    public void ToDocument_WithAllSubmitOperationsUnchecked_DoesNotDefaultToCreate()
    {
        // Regression: unchecking every submit operation must persist an empty operation set, not a silent
        // `create`. The saved document has to reflect exactly what the editor shows.
        var state = new StudioFormEditorState
        {
            Title = "No-op form",
            ServiceId = "inspections",
            LayerId = 1,
            AllowCreate = false,
            AllowUpdate = false,
            AllowDelete = false
        };
        state.Fields.Add(new StudioFormFieldEditor { FieldId = "asset_id", Label = "Asset", TargetField = "asset_id" });

        var saved = StudioFormPackageMapper.ToDocument(state);

        Assert.Empty(saved.SubmitPolicy.AllowedOperations);
    }

    [Fact]
    public void PublishEvaluator_BlocksUntilTargetOfflineAndValidationSatisfied()
    {
        var state = new StudioFormEditorState();

        var empty = StudioFormPublishEvaluator.Evaluate(state);

        Assert.False(empty.CanPublish);
        Assert.Contains(empty.UnmetRequirements, requirement => requirement.Contains("title", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(empty.UnmetRequirements, requirement => requirement.Contains("field", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(empty.UnmetRequirements, requirement => requirement.Contains("submit target", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(empty.UnmetRequirements, requirement => requirement.Contains("offline", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(empty.UnmetRequirements, requirement => requirement.Contains("Validate", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void PublishEvaluator_RequiresAtLeastOneOfflineTransportWhenOfflineEnabled()
    {
        var state = ReadyToPublishState();
        state.OfflineEnabled = true;
        state.ReplicaTransportEnabled = false;
        state.FieldCollectionTransportEnabled = false;

        var readiness = StudioFormPublishEvaluator.Evaluate(state);

        Assert.False(readiness.CanPublish);
        Assert.Contains(readiness.UnmetRequirements, requirement => requirement.Contains("transport", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void PublishEvaluator_AllowsPublishWhenGateSatisfied()
    {
        var readiness = StudioFormPublishEvaluator.Evaluate(ReadyToPublishState());

        Assert.True(readiness.CanPublish);
        Assert.Empty(readiness.UnmetRequirements);
    }

    [Fact]
    public void PublishEvaluator_BlocksWhenNoSubmitOperationSelected()
    {
        var state = ReadyToPublishState();
        state.AllowCreate = false;
        state.AllowUpdate = false;
        state.AllowDelete = false;

        var readiness = StudioFormPublishEvaluator.Evaluate(state);

        Assert.False(readiness.CanPublish);
        Assert.Contains(
            readiness.UnmetRequirements,
            requirement => requirement.Contains("submit operation", StringComparison.OrdinalIgnoreCase));
    }

    private static StudioFormEditorState ReadyToPublishState()
    {
        var state = new StudioFormEditorState
        {
            FormId = "form-1",
            Title = "Ready form",
            ServiceId = "inspections",
            LayerId = 1,
            OfflineEnabled = true,
            ReplicaTransportEnabled = true,
            OfflinePolicyReviewed = true,
            LastValidation = new StudioFormValidationView(true, [])
        };
        state.Fields.Add(new StudioFormFieldEditor { FieldId = "asset_id", Label = "Asset", TargetField = "asset_id" });
        // Represent a draft freshly loaded/saved from the server with no unsaved edits.
        state.SavedSignature = StudioFormPackageMapper.ComputeContentSignature(state);
        return state;
    }

    [Fact]
    public void PublishEvaluator_ReGatesPublish_AfterEditFollowingValidation()
    {
        var state = ReadyToPublishState();
        Assert.True(StudioFormPublishEvaluator.Evaluate(state).CanPublish);

        // Editing the submit target after the validated save moves the draft off its saved baseline, so
        // the gate must re-require a save + server validation instead of trusting the stale result.
        state.ServiceId = "different-service";

        var readiness = StudioFormPublishEvaluator.Evaluate(state);

        Assert.False(readiness.CanPublish);
        Assert.Contains(
            readiness.UnmetRequirements,
            requirement => requirement.Contains("Save your latest edits", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void HasUnsavedEdits_TracksEditsAgainstSavedBaseline()
    {
        var version = new HonuaFormPackageVersion
        {
            FormId = "form-1",
            Version = 1,
            Status = HonuaFormPackageStatus.Draft,
            Package = StudioFormPackageMapper.ToDocument(StudioFormPackageMapper.CreateTemplate())
        };

        var state = StudioFormPackageMapper.ToEditorState(version);
        Assert.False(StudioFormPackageMapper.HasUnsavedEdits(state));

        state.Fields[0].Label = "Edited label";
        Assert.True(StudioFormPackageMapper.HasUnsavedEdits(state));

        // The offline-policy review is a Console-side acknowledgment, not server content, so it does not
        // by itself make the draft dirty.
        var pristine = StudioFormPackageMapper.ToEditorState(version);
        pristine.OfflinePolicyReviewed = !pristine.OfflinePolicyReviewed;
        Assert.False(StudioFormPackageMapper.HasUnsavedEdits(pristine));
    }

    private static JsonElement Json(string raw)
    {
        using var document = JsonDocument.Parse(raw);
        return document.RootElement.Clone();
    }
}
