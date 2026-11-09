using AngleSharp.Html.Parser;
using Bunit;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Pages;
using Honua.Console.Shell.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Honua.Console.Shell.Tests;

public sealed partial class OperateGeoprocessingPageTests
{
    [Fact]
    public void AgentContextTracksDetailSelectionAndReturnToList()
    {
        using var context = new BunitContext();
        var registry = new ConsoleAgentContextRegistry();
        context.Services.AddSingleton(registry);
        context.Services.AddSingleton(new ConsoleV4Options { Enabled = true });
        context.Services.AddSingleton<IConsoleOperateObservabilityClient>(new StubGeoprocessingClient
        {
            Detail = OperateSectionResult<OperateJobRun>.Allowed(
                Job("gp-done", "Succeeded", "Done", 100, "analyst.live"))
        });
        var page = context.Render<OperateGeoprocessingPage>(parameters => parameters
            .Add(value => value.SelectedJobRunId, " gp-done "));

        var selected = Assert.Single(registry.Current.Selection);
        Assert.Equal(ConsoleAgentEntityKind.Job, selected.Kind);
        Assert.Equal("gp-done", selected.Id);
        Assert.Equal(selected, Assert.Single(registry.Current.Entities));

        page.Render(parameters => parameters.Add(value => value.SelectedJobRunId, (string?)null));
        Assert.Empty(registry.Current.Selection);
        Assert.Empty(registry.Current.Entities);
    }

    [Theory]
    [InlineData(ConsolePresentationMode.Legacy)]
    [InlineData(ConsolePresentationMode.V4)]
    public void AgentContextTracksTheVisibleStateFilterAndClearsOnDisposal(ConsolePresentationMode mode)
    {
        using var context = new BunitContext();
        var registry = new ConsoleAgentContextRegistry();
        context.Services.AddSingleton(registry);
        context.Services.AddSingleton(new ConsoleV4Options { Enabled = mode == ConsolePresentationMode.V4 });
        context.Services.AddSingleton<IConsoleOperateObservabilityClient>(new StubGeoprocessingClient
        {
            Jobs = OperateSectionResult<IReadOnlyList<OperateJobRun>>.Allowed(
                [Job("gp-running", "Running", "Executing", 40, "analyst.live")])
        });
        var page = context.Render<OperateGeoprocessingPage>();
        page.FindAll("button").Single(button => button.TextContent.Trim().StartsWith("Running", StringComparison.Ordinal)).Click();
        if (mode == ConsolePresentationMode.Legacy)
        {
            Assert.Equal(0, registry.Current.Generation);
            return;
        }
        Assert.Equal("Running", registry.Current.Filters["state"]);
        Assert.Empty(registry.Current.Selection);
        page.Dispose();
        Assert.Empty(registry.Current.Filters);
    }

    [Theory]
    [InlineData(false, "Geoprocessing Jobs")]
    [InlineData(true, "Geoprocessing jobs")]
    public void ListModePreservesStateFilteringAcrossRollout(bool v4Enabled, string heading)
    {
        using var ctx = new BunitContext();
        ctx.Services.AddSingleton(new ConsoleV4Options { Enabled = v4Enabled });
        ctx.Services.AddSingleton<IConsoleOperateObservabilityClient>(new StubGeoprocessingClient
        {
            Jobs = OperateSectionResult<IReadOnlyList<OperateJobRun>>.Allowed(
            [
                Job("gp-running", "Running", "Executing", 40, "analyst.live"),
                Job("gp-done", "Succeeded", "Done", 100, "analyst.live"),
            ])
        });

        var page = ctx.Render<OperateGeoprocessingPage>();

        Assert.Equal(heading, page.Find("h1").TextContent);
        Assert.Equal(v4Enabled ? "Jobs" : "Job board", page.Find("#gp-jobs-heading").TextContent);
        Assert.Equal(v4Enabled ? 0 : 1, page.FindAll(".console-kicker").Count(kicker => kicker.TextContent == "Operator workflow"));
        Assert.Equal(v4Enabled ? 0 : 1, page.FindAll(".console-kicker").Count(kicker => kicker.TextContent == "Geoprocessing"));
        Assert.Equal(v4Enabled, page.FindAll("section > link[rel='stylesheet'][href$='operate-jobs-v4.css']").Count == 1);
        Assert.Equal(v4Enabled, page.FindAll(".console-jobs-v4").Count == 1);
        Assert.Single(page.FindAll("h1"));
        Assert.Equal(v4Enabled, page.FindAll(".console-page-heading-v4").Count == 1);
        Assert.Equal(2, page.FindAll("tbody tr").Count);
        if (v4Enabled)
        {
            CapturePreview("jobs-list", page.Markup);
        }
        page.FindAll("button").Single(button => button.TextContent.Trim().StartsWith("Running", StringComparison.Ordinal)).Click();
        Assert.Contains("gp-running", Assert.Single(page.FindAll("tbody tr")).TextContent);
        Assert.DoesNotContain("gp-done", page.Markup);
        page.FindAll("button").Single(button => button.TextContent.Trim() == "All").Click();
        Assert.Equal(2, page.FindAll("tbody tr").Count);
    }

    private static void CapturePreview(string name, string markup)
    {
        var directory = Environment.GetEnvironmentVariable("HONUA_CATALOG_PREVIEW_DIR");
        if (string.IsNullOrWhiteSpace(directory))
        {
            return;
        }

        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, $"{name}.html"),
            $"<div class=\"console-shell\" data-console-v4=\"true\">{new HtmlParser().ParseDocument(markup).Body!.InnerHtml}</div>");
    }
}
