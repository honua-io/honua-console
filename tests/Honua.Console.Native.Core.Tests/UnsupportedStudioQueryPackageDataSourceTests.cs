using Honua.Console.Shell.Models;
using Honua.Console.Shell.Services;

namespace Honua.Console.Native.Core.Tests;

/// <summary>
/// The runtime default when no honua-server is configured must render the missing-binding surface for the
/// query builder (/studio/query, honua-console#52) on every read and command - never seeded query data
/// (Console Patterns Charter section 11). The server-bound saved-query lifecycle (honua-server#1182)
/// replaces this source when a server base URL is configured.
/// </summary>
public sealed class UnsupportedStudioQueryPackageDataSourceTests
{
    [Fact]
    public async Task GetWorkspace_ReturnsMissingBindingAndNoSeededPackages()
    {
        var source = new UnsupportedStudioQueryPackageDataSource();

        var workspace = await source.GetWorkspaceAsync();

        Assert.Empty(workspace.Packages);
        var state = Assert.Single(workspace.CapabilityStates);
        Assert.Equal("Missing binding", state.State);
        Assert.Equal("Query builder", state.Surface);
        // honua-console#311: the missing-binding copy no longer prices operators as contributors — no issue ref.
        Assert.DoesNotContain("#1182", state.Detail, StringComparison.Ordinal);
        Assert.Contains("Honua:Server:BaseUrl", state.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Load_ReturnsMissingBindingAndNoEditor()
    {
        var source = new UnsupportedStudioQueryPackageDataSource();

        var load = await source.LoadAsync("anything");

        Assert.False(load.HasEditor);
        Assert.Equal("Missing binding", Assert.Single(load.CapabilityStates).State);
    }

    [Fact]
    public async Task Save_FailsWithMissingBindingIssue()
    {
        var source = new UnsupportedStudioQueryPackageDataSource();

        var result = await source.SaveAsync(new StudioQueryEditor());

        Assert.False(result.Succeeded);
        Assert.Equal("Missing binding", result.Issue!.State);
    }

    [Fact]
    public async Task Preview_FailsWithMissingBindingIssue()
    {
        var source = new UnsupportedStudioQueryPackageDataSource();

        var result = await source.PreviewAsync(new StudioQueryEditor());

        Assert.False(result.Succeeded);
        Assert.Equal("Missing binding", result.Issue!.State);
    }
}
