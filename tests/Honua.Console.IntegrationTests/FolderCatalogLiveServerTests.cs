using Bunit;
using Honua.Console.Contracts;
using Honua.Console.Shell.Components.Operate;
using Honua.Console.Shell.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Honua.Console.IntegrationTests;

[Collection(ServiceLayerPublishIntegrationCollection.Name)]
public sealed class FolderCatalogLiveServerTests(ServiceLayerPublishFixture fixture)
{
    [SkippableFact]
    public async Task CatalogAndRootRenderFromTheLiveServerAndAnonymousWritesAreRefused()
    {
        Skip.If(fixture.SkipReason is not null, fixture.SkipReason ?? "");
        using var http = fixture.CreateHttpClient();
        http.DefaultRequestHeaders.Add("X-API-Key", fixture.Options.StudioAdminApiKey);
        using var client = new ServerConsoleFolderClient(http, new FixtureAuthority());
        var read = await client.ReadAsync();
        Assert.True(read.IsAllowed, read.Message);
        Assert.False(string.IsNullOrWhiteSpace(read.Value!.ETag));

        using var context = new BunitContext();
        context.Services.AddSingleton<IConsoleFolderClient>(client);
        var panel = context.Render<FolderManagement>();
        panel.WaitForAssertion(() => Assert.Contains("Root", panel.Markup));
        Assert.Equal(read.Value.ActiveFolders.Count() + 1, panel.FindAll("[data-folder-select] option").Count);

        using var anonymousHttp = fixture.CreateHttpClient();
        using var anonymous = new ServerConsoleFolderClient(anonymousHttp, new FixtureAuthority());
        var refused = await anonymous.DeleteAsync(read.Value, "untrusted-folder");
        Assert.False(refused.Succeeded);
        Assert.Contains(refused.State, new[] { "Sign-in required", "Forbidden" });
        using var deniedContext = new BunitContext();
        deniedContext.Services.AddSingleton<IConsoleFolderClient>(anonymous);
        var deniedPanel = deniedContext.Render<FolderManagement>();
        deniedPanel.WaitForAssertion(() => Assert.Empty(deniedPanel.FindAll("[data-folder-save], [data-service-move]")));
        deniedPanel.WaitForAssertion(() => Assert.Contains("Permission", deniedPanel.Markup));
    }

    private sealed class FixtureAuthority : IConsoleAuthorityBindingValidator
    {
        public Task<string?> CaptureAsync(CancellationToken cancellationToken = default) => Task.FromResult<string?>("test-server");
        public Task<bool> IsCurrentAsync(string binding, CancellationToken cancellationToken = default) => Task.FromResult(binding == "test-server");
    }
}
