using Honua.Console.Shell.Services;

namespace Honua.Console.Native.Core.Tests;

/// <summary>
/// The runtime default when no honua-server is configured must render the missing-binding surface for the
/// query builder (/studio/query, honua-console#52) - never seeded query data (Console Patterns Charter
/// section 11). The server-bound saved-query lifecycle (honua-server#1182) replaces this source once its
/// wire shape is projected into the Honua.Console.Contracts shim.
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
        Assert.Contains("#1182", state.Detail, StringComparison.Ordinal);
    }
}
