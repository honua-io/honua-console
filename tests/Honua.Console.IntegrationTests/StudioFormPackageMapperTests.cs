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
        return state;
    }
}
