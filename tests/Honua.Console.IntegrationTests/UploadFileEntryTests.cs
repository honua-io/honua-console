using Bunit;
using Honua.Console.Shell;
using Honua.Console.Shell.Components.Operate;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Pages;
using Honua.Console.Shell.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Honua.Console.IntegrationTests;

public sealed class UploadFileEntryTests
{
    [Theory]
    [InlineData(false, "/operate/resources/import")]
    [InlineData(true, "/operate/resources/import/task")]
    public void UploadFileUsesTheImplementedImportUnlessWorkspaceIsEnabled(bool experimental, string expected)
    {
        using var context = CreateContext(experimental);
        context.Services.AddSingleton<IConsoleFileImportOperation>(new UnsupportedConsoleFileImportOperation());
        context.Services.AddSingleton<IOperateTransitionDataSource>(new UnsupportedOperateTransitionDataSource());
        context.Services.AddSingleton<IStudioMapStyleCatalogDataSource>(new UnsupportedStudioMapStyleCatalogDataSource());
        var page = context.Render<DataToPublishFlow>(parameters => parameters.Add(flow => flow.InitialSource, "file"));
        page.WaitForAssertion(() => Assert.Equal(expected, page.Find("a.console-button").GetAttribute("href")));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TaskUploadControlsRequireTheExperimentalWorkspaceFlag(bool experimental)
    {
        using var context = CreateContext(experimental);
        var page = context.Render<DatasetImportTaskPage>();
        Assert.Equal(experimental ? 1 : 0, page.FindAll("[data-dataset-import-panel]").Count);
        if (!experimental)
            Assert.Equal("/operate/resources/import", page.Find("a").GetAttribute("href"));
    }

    [Fact]
    public void EnabledTaskImportUsesTheV4HeadingAndPanel()
    {
        using var context = CreateV4ImportContext(experimental: true);
        var page = context.Render<DatasetImportTaskPage>();
        Assert.Equal("Import dataset", page.Find(".console-page-heading-v4 h1").TextContent);
        var panel = page.Find("[data-dataset-import-panel] > section.console-panel[aria-labelledby='dataset-import-file-heading']");
        Assert.Equal("Original file", panel.QuerySelector("h2")!.TextContent);
        foreach (var hook in new[] { "status", "destination", "source", "file", "progress", "check", "clear", "transfer", "submit", "recover", "pause", "cancel" })
            Assert.Single(panel.QuerySelectorAll($"[data-import-{hook}]"));
        CatalogAdminPreview.Capture("data-import-task", page.Markup);
    }

    [Fact]
    public void DisabledTaskImportUsesTheV4HeadingWithoutThePanel()
    {
        using var context = CreateV4ImportContext(experimental: false);
        var page = context.Render<DatasetImportTaskPage>();
        Assert.Equal("Import a file", page.Find(".console-page-heading-v4 h1").TextContent);
        Assert.Equal("/console/operate/resources/import", page.Find(".console-page-heading-actions a").GetAttribute("href"));
        Assert.Empty(page.FindAll("[data-dataset-import-panel]"));
        CatalogAdminPreview.Capture("data-import-task-off", page.Markup);
    }

    [Fact]
    public void DisabledTaskImportStaysInsideTheConsoleMount()
    {
        using var context = CreateContext(experimental: false);
        context.Services.AddSingleton<NavigationManager>(new MountedNavigationManager("/console/"));
        var page = context.Render<DatasetImportTaskPage>();
        Assert.Equal("/console/operate/resources/import", page.Find("a").GetAttribute("href"));
    }

    private static BunitContext CreateV4ImportContext(bool experimental)
    {
        var context = CreateContext(experimental);
        context.Services.AddSingleton(new ConsoleV4Options { Enabled = true });
        context.Services.AddSingleton<NavigationManager>(new MountedNavigationManager("/console/"));
        return context;
    }

    private static BunitContext CreateContext(bool experimental)
    {
        var context = new BunitContext();
        context.AddConsoleNotifications();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        context.Services.AddSingleton<IConfiguration>(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Honua:Agent:WorkspaceApiEnabled"] = experimental.ToString()
            }).Build());
        return context;
    }
}
