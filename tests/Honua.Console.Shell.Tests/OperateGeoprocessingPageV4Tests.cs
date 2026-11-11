using Bunit;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Pages;
using Honua.Console.Shell.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Honua.Console.Shell.Tests;

public sealed partial class OperateGeoprocessingPageTests
{
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
            V4PagePreview.Capture("jobs-list", page.Markup);
        }
        page.FindAll("button").Single(button => button.TextContent.Trim().StartsWith("Running", StringComparison.Ordinal)).Click();
        Assert.Contains("gp-running", Assert.Single(page.FindAll("tbody tr")).TextContent);
        Assert.DoesNotContain("gp-done", page.Markup);
        page.FindAll("button").Single(button => button.TextContent.Trim() == "All").Click();
        Assert.Equal(2, page.FindAll("tbody tr").Count);
    }
}
