using Bunit;
using Honua.Console.Shell.Components;
using Honua.Console.Shell.Models;
using Microsoft.AspNetCore.Components;

namespace Honua.Console.IntegrationTests;

/// <summary>
/// Docker-free render coverage for the shared <see cref="FieldStateRow"/> component
/// (design-handoff console-canvas/field-state.jsx). Asserts that each field-state value renders the
/// correct pill, slug class, and read-only/editable affordances so operators learn one visual
/// language for who owns a value (input / discovered / calculated / system / admin).
/// </summary>
public sealed class FieldStateRowRenderTests
{
    private static RenderFragment Control(string testId) => builder =>
    {
        builder.OpenElement(0, "input");
        builder.AddAttribute(1, "class", "console-input");
        builder.AddAttribute(2, "data-test", testId);
        builder.CloseElement();
    };

    [Theory]
    [InlineData(FieldState.Input, "input", "input")]
    [InlineData(FieldState.Discovered, "discovered", "discover")]
    [InlineData(FieldState.Calculated, "calculated", "calc")]
    [InlineData(FieldState.System, "system", "system")]
    [InlineData(FieldState.Admin, "admin", "admin")]
    public void FieldStateRow_RendersStateSlugAndPill(FieldState state, string stateSlug, string pillSlug)
    {
        using var ctx = new Bunit.TestContext();

        var cut = ctx.RenderComponent<FieldStateRow>(parameters => parameters
            .Add(p => p.State, state)
            .Add(p => p.Label, "Field id")
            .Add(p => p.Value, "asset_id"));

        var row = cut.Find(".console-field-state");
        Assert.Equal(stateSlug, row.GetAttribute("data-field-state"));
        Assert.Contains($"console-field-state--{stateSlug}", row.ClassName, StringComparison.Ordinal);

        var pill = cut.Find(".console-field-state__pill");
        Assert.Contains($"console-field-state__pill--{pillSlug}", pill.ClassName, StringComparison.Ordinal);

        Assert.Contains("Field id", cut.Find(".console-field-state__label-text").TextContent, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(FieldState.Input, "input")]
    [InlineData(FieldState.Discovered, "auto")]
    [InlineData(FieldState.Calculated, "derived")]
    [InlineData(FieldState.System, "system")]
    [InlineData(FieldState.Admin, "admin only")]
    public void FieldStateRow_RendersPillLabelVocabulary(FieldState state, string pillLabel)
    {
        using var ctx = new Bunit.TestContext();

        var cut = ctx.RenderComponent<FieldStateRow>(parameters => parameters
            .Add(p => p.State, state)
            .Add(p => p.Label, "Field")
            .Add(p => p.Value, "value"));

        Assert.Equal(pillLabel, cut.Find(".console-field-state__pill").TextContent.Trim());
    }

    [Fact]
    public void FieldStateRow_WithChildContent_RendersEditableControl()
    {
        using var ctx = new Bunit.TestContext();

        var cut = ctx.RenderComponent<FieldStateRow>(parameters => parameters
            .Add(p => p.State, FieldState.Input)
            .Add(p => p.Label, "Title")
            .Add(p => p.ChildContent, Control("title-input")));

        // The caller-owned control renders inside the value slot (keeps @bind intact on the page side).
        Assert.NotNull(cut.Find(".console-field-state__value [data-test=\"title-input\"]"));
        // No read-only placeholder when a control is supplied.
        Assert.Empty(cut.FindAll(".console-field-state__readonly"));
        // The control is wrapped by the <label> so the visible label is its accessible name and clicking
        // the label focuses the field (no orphaned span label).
        var label = cut.Find("label.console-field-state__field");
        Assert.NotNull(label.QuerySelector("[data-test=\"title-input\"]"));
        Assert.Contains("Title", label.QuerySelector(".console-field-state__label-text")!.TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void FieldStateRow_Calculated_RendersDerivedFromSource()
    {
        using var ctx = new Bunit.TestContext();

        var cut = ctx.RenderComponent<FieldStateRow>(parameters => parameters
            .Add(p => p.State, FieldState.Calculated)
            .Add(p => p.Label, "Estimated runtime")
            .Add(p => p.Value, "12 s")
            .Add(p => p.DerivedFrom, "method · inputs · profile"));

        var derived = cut.Find(".console-field-state__derived");
        Assert.Contains("12 s", derived.TextContent, StringComparison.Ordinal);
        Assert.Contains("method · inputs · profile", cut.Find(".console-field-state__derived-from").TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void FieldStateRow_System_RendersReadonlyValueAndNoControl()
    {
        using var ctx = new Bunit.TestContext();

        var cut = ctx.RenderComponent<FieldStateRow>(parameters => parameters
            .Add(p => p.State, FieldState.System)
            .Add(p => p.Label, "Form id")
            .Add(p => p.Value, "form-123")
            .Add(p => p.Hint, "v2"));

        Assert.Equal("form-123", cut.Find(".console-field-state__readonly").TextContent.Trim());
        Assert.Empty(cut.FindAll("input"));
        Assert.Contains("v2", cut.Find(".console-field-state__hint").TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void FieldStateRow_Discovered_RendersOverrideAndRevertWhenWired()
    {
        using var ctx = new Bunit.TestContext();

        var overrode = false;
        var reverted = false;
        var cut = ctx.RenderComponent<FieldStateRow>(parameters => parameters
            .Add(p => p.State, FieldState.Discovered)
            .Add(p => p.Label, "Target field")
            .Add(p => p.Value, "asset_id")
            .Add(p => p.OnOverride, EventCallback.Factory.Create(this, () => overrode = true))
            .Add(p => p.OnRevert, EventCallback.Factory.Create(this, () => reverted = true)));

        cut.Find(".console-field-state__action--override").Click();
        cut.Find(".console-field-state__action--revert").Click();

        Assert.True(overrode);
        Assert.True(reverted);
    }

    [Fact]
    public void FieldStateRow_Discovered_OmitsAffordancesWhenNoHandlers()
    {
        using var ctx = new Bunit.TestContext();

        var cut = ctx.RenderComponent<FieldStateRow>(parameters => parameters
            .Add(p => p.State, FieldState.Discovered)
            .Add(p => p.Label, "Binding alias")
            .Add(p => p.ChildContent, Control("alias")));

        Assert.Empty(cut.FindAll(".console-field-state__action"));
    }
}
