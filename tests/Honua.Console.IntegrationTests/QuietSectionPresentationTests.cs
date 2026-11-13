using Bunit;
using Honua.Console.Shell.Components;
using Honua.Console.Shell.Models;

namespace Honua.Console.IntegrationTests;

public sealed class QuietSectionPresentationTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Quiet_sections_keep_named_regions_filters_and_read_state(bool enabled)
    {
        using var context = new BunitContext();
        var table = context.Render<ConsoleTableRegion>(p => p
            .Add(c => c.Label, "Service layers")
            .AddChildContent("<table class='console-table'><caption>Service layers</caption><thead><tr><th>Layer</th><th>Service</th><th>Geometry</th><th>Last updated</th></tr></thead><tbody><tr><td><a href='/catalog/layers/roads'>Roads</a></td><td>Transportation</td><td>Line</td><td>12 November 2026</td></tr></tbody></table>"));
        var panel = context.Render<ConsolePanel>(p => p
            .Add(c => c.Title, "Layers")
            .Add(c => c.HeadingId, "layers")
            .Add(c => c.Surface, ConsolePanelSurface.Quiet)
            .Add(c => c.Heading, ConsolePanelHeading.VisuallyHidden)
            .AddChildContent("<div class='console-filter-bar console-filter-bar-quiet'><label>Search layers<input type='search' /></label><label>Geometry<select><option>All types</option><option>Line</option></select></label><button type='button'>Apply filters</button></div>" + table.Markup));
        var state = context.Render<ConsoleStateView>(p => p
            .Add(c => c.Kind, "unavailable")
            .Add(c => c.Treatment, ConsoleStateTreatment.Informational)
            .Add(c => c.HeadingLevel, 2)
            .Add(c => c.Kicker, "Catalog")
            .Add(c => c.Title, "Usage unavailable")
            .Add(c => c.Message, "Usage could not be read from the connected server."));

        Assert.Equal("layers", panel.Find("section").GetAttribute("aria-labelledby"));
        Assert.Equal("0", panel.Find("[role=region]").GetAttribute("tabindex"));
        Assert.Equal("polite", state.Find("section").GetAttribute("aria-live"));
        CatalogAdminPreview.Capture($"quiet-sections-{enabled.ToString().ToLowerInvariant()}",
            $"<div class=\"console-shell\" data-console-v4=\"{enabled.ToString().ToLowerInvariant()}\"><main class=\"console-main\"><h1>Service overview</h1>{panel.Markup}{state.Markup}</main></div>");
    }
}
