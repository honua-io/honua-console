using Bunit;
using Honua.Console.Shell;
using Honua.Console.Shell.Components.Operate;
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
    public void DisabledTaskImportStaysInsideTheConsoleMount()
    {
        using var context = CreateContext(experimental: false);
        context.Services.AddSingleton<NavigationManager>(new MountedNavigationManager("/console/"));
        var page = context.Render<DatasetImportTaskPage>();
        Assert.Equal("/console/operate/resources/import", page.Find("a").GetAttribute("href"));
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
