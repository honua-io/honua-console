using Bunit;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Pages;
using Honua.Console.Shell.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Honua.Console.IntegrationTests;

public sealed class OperateImportDiscoveryLifetimeTests
{
    private const string UrlInput = "input[placeholder^='https://']";

    [Theory]
    [InlineData(ConsolePresentationMode.Legacy)]
    [InlineData(ConsolePresentationMode.V4)]
    public async Task SourceEditCancelsDiscoveryAndUnlocksTheNewSource(ConsolePresentationMode presentation)
    {
        var operation = new GatedDiscoveryOperation();
        using var ctx = CreateContext(operation, presentation);
        var page = ctx.Render<OperateImportServicePage>();
        page.Find(UrlInput).Input("https://old.example/FeatureServer");
        page.Find("button.console-button").Click();
        Assert.Single(page.FindAll("[data-discover-progress]"));

        page.Find(UrlInput).Input("https://new.example/FeatureServer");

        Assert.True(operation.Calls[0].Token.IsCancellationRequested);
        Assert.Empty(page.FindAll("[data-discover-progress]"));
        Assert.False(page.Find("button.console-button").HasAttribute("disabled"));

        page.Find("button.console-button").Click();
        Assert.Equal(2, operation.Calls.Count);
        await page.InvokeAsync(() => operation.Calls[0].Result.SetCanceled(operation.Calls[0].Token));
        Assert.Single(page.FindAll("[data-discover-progress]"));
        Assert.True(page.Find("button.console-button").HasAttribute("disabled"));

        await page.InvokeAsync(() => operation.Calls[1].Result.SetResult(Discovered("https://new.example/FeatureServer")));
        page.WaitForAssertion(() => Assert.Single(page.FindAll("[data-layer-checkbox]")));
        Assert.Empty(page.FindAll("[data-discover-progress]"));
    }

    [Theory]
    [InlineData(ConsolePresentationMode.Legacy)]
    [InlineData(ConsolePresentationMode.V4)]
    public async Task LeavingThePageCancelsDiscovery(ConsolePresentationMode presentation)
    {
        var operation = new GatedDiscoveryOperation();
        using var ctx = CreateContext(operation, presentation);
        var page = ctx.Render<OperateImportServicePage>();
        page.Find(UrlInput).Input("https://old.example/FeatureServer");
        page.Find("button.console-button").Click();

        await ctx.DisposeComponentsAsync();

        Assert.True(operation.Calls.Single().Token.IsCancellationRequested);
    }

    private static BunitContext CreateContext(GatedDiscoveryOperation operation, ConsolePresentationMode presentation)
    {
        var ctx = new BunitContext().AddConsoleNotifications();
        ctx.Services.AddSingleton<IConsoleServiceImportOperation>(operation);
        ctx.Services.AddSingleton(new ConsoleV4Options { Enabled = presentation == ConsolePresentationMode.V4 });
        ctx.Services.AddSingleton<ConsoleAgentContextRegistry>();
        ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        return ctx;
    }

    private static ConsoleServiceImportResult Discovered(string serviceUrl) => new()
    {
        Succeeded = true,
        State = "Discovered",
        ServiceName = "Roads",
        ServiceType = "FeatureServer",
        Services =
        [
            new ConsoleServiceImportService
            {
                ServiceName = "Roads",
                ServiceType = "FeatureServer",
                ServiceUrl = serviceUrl,
                Layers = [new ConsoleServiceImportLayer { LayerId = 1, Name = "Roads" }],
            },
        ],
    };

    private sealed record DiscoveryCall(
        string ServiceUrl,
        CancellationToken Token,
        TaskCompletionSource<ConsoleServiceImportResult> Result);

    private sealed class GatedDiscoveryOperation : IConsoleServiceImportOperation
    {
        public List<DiscoveryCall> Calls { get; } = [];

        public Task<ConsoleServiceImportResult> DiscoverAsync(
            string serviceUrl,
            ConsoleServiceImportAuth? auth = null,
            CancellationToken cancellationToken = default)
        {
            var call = new DiscoveryCall(serviceUrl, cancellationToken, new());
            Calls.Add(call);
            return call.Result.Task;
        }

        public Task<ConsoleServiceImportRun> StartLayerImportAsync(
            ConsoleServiceImportRunRequest request,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("No import was queued.");

        public Task<ConsoleServiceImportJob> GetImportJobAsync(string jobId, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("No job was queued.");
    }
}
