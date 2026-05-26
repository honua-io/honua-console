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
}
