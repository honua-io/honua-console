using Bunit;
using Honua.Console.Shell.Components;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Validation;

namespace Honua.Console.IntegrationTests;

/// <summary>
/// Docker-free render coverage for <see cref="ValidationMessageInline"/>, the sibling surface for plain
/// studio-form-label fields: it renders the field's findings from a shared <see cref="ValidationState"/>
/// and renders nothing when the field is clean.
/// </summary>
public sealed class ValidationMessageInlineTests
{
    [Fact]
    public void RendersNothing_WhenFieldClean()
    {
        using var ctx = new Bunit.TestContext();

        var state = new ValidationState();
        state.SetServerErrors([new ConsoleFieldError("other", "c", ConsoleValidationSeverity.Error, "boom")]);

        var cut = ctx.RenderComponent<ValidationMessageInline>(parameters => parameters
            .Add(p => p.FieldKey, "title")
            .Add(p => p.State, state));

        Assert.Empty(cut.FindAll(".console-validation-inline"));
    }

    [Fact]
    public void RendersFieldMessages_WhenPresent()
    {
        using var ctx = new Bunit.TestContext();

        var state = new ValidationState();
        state.SetClientErrors([new ConsoleFieldError("title", "required", ConsoleValidationSeverity.Error, "Title is required")]);

        var cut = ctx.RenderComponent<ValidationMessageInline>(parameters => parameters
            .Add(p => p.FieldKey, "title")
            .Add(p => p.State, state));

        var list = cut.Find(".console-validation-inline");
        Assert.Equal("title", list.GetAttribute("data-field-key"));
        Assert.Contains("Title is required", cut.Find(".console-field-state__error").TextContent, StringComparison.Ordinal);
    }
}
