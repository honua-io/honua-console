using Honua.Console.Shell.Models;
using Honua.Console.Shell.Validation;

namespace Honua.Console.Native.Core.Tests;

/// <summary>
/// Docker-free coverage for <see cref="StudioFormServerErrorBinder"/>, proving the server
/// <see cref="StudioFormValidationView"/> items are re-homed onto the same per-field / form-level
/// <see cref="StudioFormFieldKeys"/> the client validator uses, so a server finding surfaces inline next to
/// its field.
/// </summary>
public sealed class StudioFormServerErrorBinderTests
{
    private static StudioFormEditorState StateWithFields(params string[] fieldIds)
    {
        var state = new StudioFormEditorState { Title = "t", ServiceId = "s" };
        foreach (var id in fieldIds)
        {
            state.Fields.Add(new StudioFormFieldEditor { FieldId = id });
        }

        return state;
    }

    [Fact]
    public void NullValidation_ReturnsEmpty() =>
        Assert.Empty(StudioFormServerErrorBinder.Map(StateWithFields("a"), null));

    [Fact]
    public void EmptyIssues_ReturnsEmpty() =>
        Assert.Empty(StudioFormServerErrorBinder.Map(StateWithFields("a"), new StudioFormValidationView(true, [])));

    [Fact]
    public void FieldIdItem_ResolvesToFieldIdKey()
    {
        var state = StateWithFields("first", "asset_id");
        var view = new StudioFormValidationView(false,
            [new StudioFormValidationItem("error", "fieldIdDuplicate", "asset_id", "Duplicate field id")]);

        var error = Assert.Single(StudioFormServerErrorBinder.Map(state, view));
        Assert.Equal(StudioFormFieldKeys.FieldId(1), error.FieldKey);
        Assert.Equal(ConsoleValidationSeverity.Error, error.Severity);
        Assert.Equal("asset_id", error.Path);
    }

    [Fact]
    public void RangeCodedFieldItem_ResolvesToRangeKey()
    {
        var state = StateWithFields("temperature");
        var view = new StudioFormValidationView(false,
            [new StudioFormValidationItem("error", "field.range.outsideTarget", "temperature", "out of bounds")]);

        var error = Assert.Single(StudioFormServerErrorBinder.Map(state, view));
        Assert.Equal(StudioFormFieldKeys.FieldRangeMin(0), error.FieldKey);
    }

    [Fact]
    public void TitleCode_WithNoFieldId_ResolvesToTitleKey()
    {
        var state = StateWithFields("a");
        var view = new StudioFormValidationView(false,
            [new StudioFormValidationItem("blocker", "form.title.required", null, "Title required")]);

        var error = Assert.Single(StudioFormServerErrorBinder.Map(state, view));
        Assert.Equal(StudioFormFieldKeys.Title, error.FieldKey);
        Assert.Equal(ConsoleValidationSeverity.Blocker, error.Severity);
    }

    [Fact]
    public void UnknownFieldIdAndCode_FallsBackToLocatorOrFormLevel()
    {
        var state = StateWithFields("a");
        var view = new StudioFormValidationView(false,
            [new StudioFormValidationItem("error", "mystery", null, "huh")]);

        // No fieldId, unmapped code -> form-level fallback key.
        var error = Assert.Single(StudioFormServerErrorBinder.Map(state, view));
        Assert.Equal(ServerFieldErrorMapper.FormLevelFieldKey, error.FieldKey);
    }
}
