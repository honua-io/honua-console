using Honua.Console.Shell.Models;
using Honua.Console.Shell.Validation;

namespace Honua.Console.Native.Core.Tests;

/// <summary>
/// Docker-free unit coverage for <see cref="StudioFormValidator"/>, the Wave-1 client cross-field/bounds
/// validator for the Studio form builder. Each cross-field, bounds, uniqueness, referential, and presence
/// rule from the catalog is proven in its pass and fail state, keyed by <see cref="StudioFormFieldKeys"/>.
/// </summary>
public sealed class StudioFormValidatorTests
{
    private static IReadOnlyList<ConsoleFieldError> Evaluate(StudioFormEditorState state) =>
        StudioFormValidator.Instance.Evaluate(state);

    /// <summary>A fully valid baseline editor so a single failing rule can be asserted in isolation.</summary>
    private static StudioFormEditorState Valid()
    {
        var state = new StudioFormEditorState
        {
            Title = "Hydrant inspection",
            ServiceId = "inspections",
            AllowCreate = true,
        };
        state.Fields.Add(new StudioFormFieldEditor { FieldId = "asset_id", Label = "Asset ID", TargetField = "asset_id" });
        return state;
    }

    [Fact]
    public void ValidForm_ProducesNoErrors() => Assert.Empty(Evaluate(Valid()));

    [Fact]
    public void MissingTitle_BlocksOnTitleKey()
    {
        var state = Valid();
        state.Title = "   ";

        var error = Assert.Single(Evaluate(state), e => e.FieldKey == StudioFormFieldKeys.Title);
        Assert.Equal(ConsoleValidationSeverity.Blocker, error.Severity);
    }

    [Fact]
    public void NoFields_BlocksOnFieldsKey()
    {
        var state = Valid();
        state.Fields.Clear();

        Assert.Contains(Evaluate(state), e =>
            e.FieldKey == StudioFormFieldKeys.Fields && e.Severity == ConsoleValidationSeverity.Blocker);
    }

    [Fact]
    public void MissingServiceId_BlocksOnServiceIdKey()
    {
        var state = Valid();
        state.ServiceId = string.Empty;

        Assert.Contains(Evaluate(state), e => e.FieldKey == StudioFormFieldKeys.ServiceId);
    }

    [Fact]
    public void NoSubmitOperation_BlocksOnSubmitOpsKey()
    {
        var state = Valid();
        state.AllowCreate = false;
        state.AllowUpdate = false;
        state.AllowDelete = false;

        Assert.Contains(Evaluate(state), e => e.FieldKey == StudioFormFieldKeys.SubmitOps);
    }

    [Theory]
    [InlineData("1", "10", true)]   // ordered
    [InlineData("5", "5", true)]    // equal is ordered
    [InlineData("10", "1", false)]  // inverted -> error
    public void RangeOrdering(string min, string max, bool valid)
    {
        var state = Valid();
        state.Fields[0].DomainKind = "range";
        state.Fields[0].RangeMin = min;
        state.Fields[0].RangeMax = max;

        var rangeErrors = Evaluate(state).Where(e => e.Code == "form.field.range.order").ToList();
        if (valid)
        {
            Assert.Empty(rangeErrors);
        }
        else
        {
            var error = Assert.Single(rangeErrors);
            Assert.Equal(StudioFormFieldKeys.FieldRangeMin(0), error.FieldKey);
        }
    }

    [Fact]
    public void RangeOrdering_OnlyAppliesWhenDomainIsRange()
    {
        var state = Valid();
        state.Fields[0].DomainKind = "none";
        state.Fields[0].RangeMin = "10";
        state.Fields[0].RangeMax = "1";

        Assert.DoesNotContain(Evaluate(state), e => e.Code == "form.field.range.order");
    }

    [Fact]
    public void DuplicateFieldId_FlagsTheSecondOccurrence()
    {
        var state = Valid();
        state.Fields.Add(new StudioFormFieldEditor { FieldId = "asset_id", Label = "Dup" });

        var error = Assert.Single(Evaluate(state), e => e.Code == "form.field.id.duplicate");
        Assert.Equal(StudioFormFieldKeys.FieldId(1), error.FieldKey); // second row flagged, not the first
    }

    [Fact]
    public void DuplicateFieldId_IsCaseInsensitive()
    {
        var state = Valid();
        state.Fields.Add(new StudioFormFieldEditor { FieldId = "ASSET_ID" });

        Assert.Contains(Evaluate(state), e => e.Code == "form.field.id.duplicate");
    }

    [Fact]
    public void BlankFieldIds_AreNotTreatedAsDuplicates()
    {
        var state = Valid();
        state.Fields.Clear();
        state.Fields.Add(new StudioFormFieldEditor { FieldId = string.Empty });
        state.Fields.Add(new StudioFormFieldEditor { FieldId = string.Empty });

        Assert.DoesNotContain(Evaluate(state), e => e.Code == "form.field.id.duplicate");
    }

    [Theory]
    [InlineData(1, true)]
    [InlineData(5, true)]
    [InlineData(0, false)]
    [InlineData(-2, false)]
    public void MaxAttachmentsPerSubmission_Bounds(int value, bool valid)
    {
        var state = Valid();
        state.MaxAttachmentsPerSubmission = value;

        var errors = Evaluate(state).Where(e => e.FieldKey == StudioFormFieldKeys.MaxAttachmentsPerSubmission).ToList();
        Assert.Equal(valid, errors.Count == 0);
    }

    [Fact]
    public void MaxAttachmentsPerSubmission_NullIsServerDefault_NoError()
    {
        var state = Valid();
        state.MaxAttachmentsPerSubmission = null;

        Assert.DoesNotContain(Evaluate(state), e => e.FieldKey == StudioFormFieldKeys.MaxAttachmentsPerSubmission);
    }

    [Theory]
    [InlineData(1, true)]
    [InlineData(0, false)]
    public void PerFieldAttachmentCount_Bounds(int count, bool valid)
    {
        var state = Valid();
        state.Fields[0].Type = "attachment";
        state.Fields[0].AttachmentMaxCount = count;

        var errors = Evaluate(state).Where(e => e.Code == "form.field.attachment.count.min").ToList();
        Assert.Equal(valid, errors.Count == 0);
    }

    [Theory]
    [InlineData(1, true)]
    [InlineData(30, true)]
    [InlineData(0, false)]
    [InlineData(-5, false)]
    public void PrivacyRetention_MustBePositive(int days, bool valid)
    {
        var state = Valid();
        state.PrivacyRetentionDays = days;

        var errors = Evaluate(state).Where(e => e.FieldKey == StudioFormFieldKeys.PrivacyRetentionDays).ToList();
        Assert.Equal(valid, errors.Count == 0);
    }

    [Fact]
    public void OfflineEnabled_WithNoTransport_FlagsTransportRequired()
    {
        var state = Valid();
        state.OfflineEnabled = true;
        state.ReplicaTransportEnabled = false;
        state.FieldCollectionTransportEnabled = false;

        var error = Assert.Single(Evaluate(state), e => e.Code == "form.offline.transport.required");
        Assert.Equal(StudioFormFieldKeys.OfflineTransports, error.FieldKey);
    }

    [Fact]
    public void OfflineEnabled_WithOneTransport_Passes()
    {
        var state = Valid();
        state.OfflineEnabled = true;
        state.ReplicaTransportEnabled = true;
        state.FieldCollectionTransportEnabled = false;

        Assert.DoesNotContain(Evaluate(state), e => e.Code == "form.offline.transport.required");
    }

    [Fact]
    public void OfflineDisabled_DoesNotRequireTransport()
    {
        var state = Valid();
        state.OfflineEnabled = false;
        state.ReplicaTransportEnabled = false;
        state.FieldCollectionTransportEnabled = false;

        Assert.DoesNotContain(Evaluate(state), e => e.Code == "form.offline.transport.required");
    }

    [Fact]
    public void VisibilityDependsOn_ExistingField_Passes()
    {
        var state = Valid();
        state.Fields.Add(new StudioFormFieldEditor { FieldId = "status", VisibilityDependsOn = "asset_id" });

        Assert.DoesNotContain(Evaluate(state), e => e.Code == "form.field.visibility.dependsOn.unknown");
    }

    [Fact]
    public void VisibilityDependsOn_UnknownField_Flags()
    {
        var state = Valid();
        state.Fields[0].VisibilityDependsOn = "nonexistent";

        var error = Assert.Single(Evaluate(state), e => e.Code == "form.field.visibility.dependsOn.unknown");
        Assert.Equal(StudioFormFieldKeys.FieldVisibilityDependsOn(0), error.FieldKey);
    }

    [Fact]
    public void NullState_Throws() =>
        Assert.Throws<ArgumentNullException>(() => StudioFormValidator.Instance.Evaluate(null!));
}
