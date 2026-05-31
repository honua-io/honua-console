using Bunit;
using Honua.Console.Shell.Components;
using Honua.Console.Shell.Models;
using Microsoft.AspNetCore.Components;

namespace Honua.Console.IntegrationTests;

/// <summary>
/// Docker-free render coverage for the Wave-0 error slot added to <see cref="FieldStateRow"/>: the row
/// must surface inline messages + aria-invalid when findings are present, render nothing extra when
/// absent (existing callers unchanged), and reflect the worst severity in the message styling.
/// </summary>
public sealed class FieldStateRowErrorSlotTests
{
    private static RenderFragment Control(string testId) => builder =>
    {
        builder.OpenElement(0, "input");
        builder.AddAttribute(1, "class", "console-input");
        builder.AddAttribute(2, "data-test", testId);
        builder.CloseElement();
    };

    [Fact]
    public void NoErrors_RendersNoInlineErrorAndNotInvalid()
    {
        using var ctx = new Bunit.TestContext();

        var cut = ctx.RenderComponent<FieldStateRow>(parameters => parameters
            .Add(p => p.State, FieldState.Input)
            .Add(p => p.Label, "Title")
            .Add(p => p.ChildContent, Control("title")));

        var row = cut.Find(".console-field-state");
        Assert.DoesNotContain("console-field-state--invalid", row.ClassName, StringComparison.Ordinal);
        Assert.Null(row.GetAttribute("aria-invalid"));
        Assert.Empty(cut.FindAll(".console-field-state__error"));
    }

    [Fact]
    public void WithErrors_RendersInlineMessageAndAriaInvalid()
    {
        using var ctx = new Bunit.TestContext();

        var errors = new List<ConsoleFieldError>
        {
            new("map.initialExtent", "bbox.order", ConsoleValidationSeverity.Blocker, "minX must be <= maxX"),
        };

        var cut = ctx.RenderComponent<FieldStateRow>(parameters => parameters
            .Add(p => p.State, FieldState.Input)
            .Add(p => p.Label, "Initial extent")
            .Add(p => p.ChildContent, Control("extent"))
            .Add(p => p.Errors, errors));

        var row = cut.Find(".console-field-state");
        Assert.Contains("console-field-state--invalid", row.ClassName, StringComparison.Ordinal);
        Assert.Equal("true", row.GetAttribute("aria-invalid"));

        var error = cut.Find(".console-field-state__error");
        Assert.Contains("minX must be <= maxX", error.TextContent, StringComparison.Ordinal);
        Assert.Contains("console-field-state__error--blocker", error.ClassName, StringComparison.Ordinal);
    }

    [Fact]
    public void Errors_RenderInValueSlotAlongsideControl()
    {
        using var ctx = new Bunit.TestContext();

        var cut = ctx.RenderComponent<FieldStateRow>(parameters => parameters
            .Add(p => p.State, FieldState.Input)
            .Add(p => p.Label, "Field")
            .Add(p => p.ChildContent, Control("field"))
            .Add(p => p.Errors, new List<ConsoleFieldError>
            {
                new("f", "c", ConsoleValidationSeverity.Error, "bad value"),
            }));

        // Control and the error list both live inside the value slot (so @bind stays intact).
        Assert.NotNull(cut.Find(".console-field-state__value [data-test=\"field\"]"));
        Assert.NotNull(cut.Find(".console-field-state__value .console-field-state__errors"));
    }

    [Fact]
    public void MultipleErrors_AllRender()
    {
        using var ctx = new Bunit.TestContext();

        var cut = ctx.RenderComponent<FieldStateRow>(parameters => parameters
            .Add(p => p.State, FieldState.Input)
            .Add(p => p.Label, "Field")
            .Add(p => p.ChildContent, Control("field"))
            .Add(p => p.Errors, new List<ConsoleFieldError>
            {
                new("f", "c1", ConsoleValidationSeverity.Error, "first"),
                new("f", "c2", ConsoleValidationSeverity.Warning, "second"),
            }));

        Assert.Equal(2, cut.FindAll(".console-field-state__error").Count);
    }

    [Fact]
    public void InvalidFlagWithoutErrors_StillMarksInvalid()
    {
        using var ctx = new Bunit.TestContext();

        var cut = ctx.RenderComponent<FieldStateRow>(parameters => parameters
            .Add(p => p.State, FieldState.Input)
            .Add(p => p.Label, "Field")
            .Add(p => p.ChildContent, Control("field"))
            .Add(p => p.Invalid, true));

        var row = cut.Find(".console-field-state");
        Assert.Contains("console-field-state--invalid", row.ClassName, StringComparison.Ordinal);
        Assert.Equal("true", row.GetAttribute("aria-invalid"));
        // No messages because no Errors were supplied.
        Assert.Empty(cut.FindAll(".console-field-state__error"));
    }

    [Fact]
    public void ReadonlyState_AlsoRendersErrors()
    {
        using var ctx = new Bunit.TestContext();

        var cut = ctx.RenderComponent<FieldStateRow>(parameters => parameters
            .Add(p => p.State, FieldState.System)
            .Add(p => p.Label, "Form id")
            .Add(p => p.Value, "form-123")
            .Add(p => p.Errors, new List<ConsoleFieldError>
            {
                new("formId", "c", ConsoleValidationSeverity.Error, "conflict"),
            }));

        Assert.Contains("conflict", cut.Find(".console-field-state__error").TextContent, StringComparison.Ordinal);
    }
}
