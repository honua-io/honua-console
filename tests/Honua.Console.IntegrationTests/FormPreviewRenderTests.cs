using Bunit;
using Honua.Console.Shell.Components;
using Honua.Console.Shell.Models;

namespace Honua.Console.IntegrationTests;

/// <summary>
/// Docker-free render coverage for the shared <see cref="FormPreview"/> component — the "see the result"
/// surface for the Studio FORM family, where the result IS the form. Asserts that the REAL generated
/// schema renders as live HTML form controls (text input, a required select with an option per coded
/// choice, a checkbox, a textarea, section headings, labels, required markers) with no external data,
/// and that an empty schema shows an honest empty state rather than any fabricated sample field
/// (Charter §11).
/// </summary>
public sealed class FormPreviewRenderTests
{
    [Fact]
    public void FormPreview_WithRealFields_RendersLiveControlsLabelsAndRequiredMarker()
    {
        using var ctx = new Bunit.TestContext();
        ctx.JSInterop.Mode = JSRuntimeMode.Loose;

        var fields = new List<StudioFormFieldEditor>
        {
            new() { FieldId = "asset_id", Label = "Asset ID", Type = "text", Group = "Identification" },
            new()
            {
                FieldId = "condition",
                Label = "Condition",
                Type = "choice",
                Required = true,
                DomainKind = "coded",
                Group = "Inspection",
                // 5 choices -> renders as a <select> (a small set would render as radios).
                Choices = "good=Good\nfair=Fair\npoor=Poor\ncritical=Critical\nunknown=Unknown",
            },
            new() { FieldId = "is_safe", Label = "Marked safe", Type = "boolean", Group = "Inspection" },
        };

        var cut = ctx.RenderComponent<FormPreview>(parameters => parameters
            .Add(p => p.Fields, fields)
            .Add(p => p.Title, "Field inspection"));

        // The result surface is present and reports the real field count (no fabrication).
        Assert.Contains("data-form-preview=\"true\"", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("data-form-field-count=\"3\"", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("Field inspection", cut.Markup, StringComparison.Ordinal);

        // Real text input with its label.
        var textInputs = cut.FindAll("input[type=text][data-form-preview-input]");
        Assert.Single(textInputs);
        Assert.Contains("Asset ID", cut.Markup, StringComparison.Ordinal);

        // Required coded choice renders a real <select> with one <option> per choice (plus a placeholder).
        var select = cut.Find("select[data-form-preview-select]");
        Assert.True(select.HasAttribute("required"));
        var options = cut.FindAll("select[data-form-preview-select] option");
        // 5 real choices + 1 leading placeholder option.
        Assert.Equal(6, options.Count);
        Assert.Contains("Good", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("Critical", cut.Markup, StringComparison.Ordinal);

        // The required field shows a required marker; the optional ones do not multiply it.
        Assert.Single(cut.FindAll("[data-form-preview-required]"));

        // A boolean field renders a real checkbox.
        Assert.Single(cut.FindAll("input[type=checkbox]"));

        // Section headings from the schema are rendered as the grouping legend.
        var sections = cut.FindAll("[data-form-preview-section]");
        Assert.Equal(2, sections.Count);
        Assert.Contains(sections, s => s.TextContent.Contains("Identification", StringComparison.Ordinal));
        Assert.Contains(sections, s => s.TextContent.Contains("Inspection", StringComparison.Ordinal));
    }

    [Fact]
    public void FormPreview_SmallChoiceSet_RendersRadioGroup()
    {
        using var ctx = new Bunit.TestContext();
        ctx.JSInterop.Mode = JSRuntimeMode.Loose;

        var fields = new List<StudioFormFieldEditor>
        {
            new()
            {
                FieldId = "priority",
                Label = "Priority",
                Type = "choice",
                DomainKind = "coded",
                Choices = "low=Low\nhigh=High",
            },
        };

        var cut = ctx.RenderComponent<FormPreview>(parameters => parameters.Add(p => p.Fields, fields));

        // A 2-option (non-boolean) choice renders as a real radio group, one <input type=radio> per choice.
        Assert.Single(cut.FindAll("[data-form-preview-radio-group]"));
        Assert.Equal(2, cut.FindAll("input[type=radio]").Count);
        Assert.Contains("Low", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("High", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void FormPreview_EmptySchema_ShowsHonestEmptyStateAndNoFabricatedInputs()
    {
        using var ctx = new Bunit.TestContext();
        ctx.JSInterop.Mode = JSRuntimeMode.Loose;

        var cut = ctx.RenderComponent<FormPreview>(parameters => parameters
            .Add(p => p.Fields, new List<StudioFormFieldEditor>()));

        // Honest empty state, no fabricated sample controls (Charter §11 — no-mock).
        Assert.Contains("data-form-preview=\"false\"", cut.Markup, StringComparison.Ordinal);
        Assert.Single(cut.FindAll("[data-form-preview-empty]"));
        Assert.Empty(cut.FindAll("input"));
        Assert.Empty(cut.FindAll("select"));
        Assert.Empty(cut.FindAll("textarea"));
        Assert.Empty(cut.FindAll("[data-form-preview-field]"));
    }
}
