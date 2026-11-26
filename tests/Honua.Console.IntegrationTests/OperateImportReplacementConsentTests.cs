using Bunit;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Pages;
using Honua.Console.Shell.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Honua.Console.IntegrationTests;

public sealed class OperateImportReplacementConsentTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MixedSelection_RequiresCurrentActionConfirmationAndPreservesPerTargetIntent(bool enabled)
    {
        var operation = new RecordingImportOperation();
        using var ctx = new BunitContext().AddConsoleNotifications();
        ctx.Services.AddSingleton<IConsoleServiceImportOperation>(operation);
        ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        ctx.Services.AddSingleton(new ConsoleV4Options { Enabled = enabled });
        ctx.Services.AddSingleton<ConsoleAgentContextRegistry>();
        var page = ctx.Render<OperateImportServicePage>();

        page.Find("input[placeholder^='https://']").Input("https://source.example/FeatureServer");
        page.Find("button.console-button").Click();
        page.WaitForAssertion(() => Assert.Equal(2, page.FindAll("[data-layer-checkbox]").Count));

        for (var index = 0; index < 2; index++)
        {
            page.FindAll("[data-layer-checkbox]")[index].Change(true);
        }

        var registry = ctx.Services.GetRequiredService<ConsoleAgentContextRegistry>();
        Assert.Equal(enabled ? "/operate/import/service" : "/", registry.Current.Route);
        Assert.Equal(enabled ? 1 : 0, page.FindAll("section.console-panel[aria-labelledby]").Count);
        if (enabled)
        {
            Assert.Equal("2", registry.Current.Filters["selectedLayerCount"]);
            Assert.True(registry.Current.Unsaved.ChangedFieldCount > 0);
            Assert.DoesNotContain("source.example", System.Text.Json.JsonSerializer.Serialize(registry.Current));
            CatalogAdminPreview.Capture("data-import-service", page.Markup);
        }

        var replacementChoices = page.FindAll("[data-replace-target]");
        Assert.Equal(2, replacementChoices.Count);
        replacementChoices[0].Change(true);
        page.Find("[data-import-selected]").Click();

        Assert.Empty(operation.Requests);
        Assert.Contains("public.imp_roads_roads_1", page.Find("[data-replace-confirmation]").TextContent, StringComparison.Ordinal);
        page.Find("[data-confirm-replacement]").Click();
        page.WaitForAssertion(() => Assert.Equal(2, operation.Requests.Count));
        Assert.True(operation.Requests.Single(request => request.TableName == "imp_roads_roads_1").OverwriteExisting);
        Assert.False(operation.Requests.Single(request => request.TableName == "imp_roads_roads_2").OverwriteExisting);

        page.Find("[data-import-selected]").Click();
        page.WaitForAssertion(() => Assert.Equal(4, operation.Requests.Count));
        Assert.DoesNotContain(operation.Requests.Skip(2), static request => request.OverwriteExisting);
    }

    [Fact]
    public async Task ReplacementConsentIsSnapshottedBeforeQueueingBatch()
    {
        var operation = new RecordingImportOperation(blockFirstRequest: true);
        using var ctx = new BunitContext().AddConsoleNotifications();
        ctx.JSInterop.SetupModule(Honua.Console.Shell.Components.ConsoleOverlayFocusTrap.ModulePath).Mode = JSRuntimeMode.Loose;
        ctx.Services.AddSingleton<IConsoleServiceImportOperation>(operation);
        var page = ctx.Render<OperateImportServicePage>();

        page.Find("input[placeholder^='https://']").Input("https://source.example/FeatureServer");
        page.Find("button.console-button").Click();
        page.WaitForAssertion(() => Assert.Equal(2, page.FindAll("[data-layer-checkbox]").Count));

        for (var index = 0; index < 2; index++)
        {
            page.FindAll("[data-layer-checkbox]")[index].Change(true);
        }

        var replacementChoices = page.FindAll("[data-replace-target]");
        replacementChoices[0].Change(true);
        page.Find("[data-import-selected]").Click();

        var queueTask = page.Find("[data-confirm-replacement]").ClickAsync();
        Assert.Single(operation.Requests);

        // This is possible while the first StartLayerImportAsync call is awaiting.
        page.FindAll("[data-replace-target]")[1].Change(true);
        operation.CompleteFirstRequest();
        await queueTask;

        Assert.True(operation.Requests[0].OverwriteExisting);
        Assert.False(operation.Requests[1].OverwriteExisting);
    }

    [Theory]
    [InlineData("url", "Discovered")]
    [InlineData("url", "Failed")]
    [InlineData("auth", "Discovered")]
    [InlineData("auth", "Failed")]
    public async Task SourceEditsDiscardDelayedDiscovery(string edit, string state)
    {
        var gate = new TaskCompletionSource<ConsoleServiceImportResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var operation = new RecordingImportOperation { Discovery = gate };
        using var ctx = new BunitContext().AddConsoleNotifications();
        ctx.Services.AddSingleton<IConsoleServiceImportOperation>(operation);
        ctx.Services.AddSingleton(new ConsoleV4Options { Enabled = true });
        var page = ctx.Render<OperateImportServicePage>();
        page.Find("input[placeholder^='https://']").Input("https://old.example/FeatureServer");
        var discovery = page.Find("button.console-button").ClickAsync();

        if (edit == "url")
        {
            page.Find("input[placeholder^='https://']").Input("https://current.example/FeatureServer");
        }
        else
        {
            page.Find("[data-auth-mode]").Change("token");
            page.Find("input[aria-label='Token']").Input("current-token");
        }

        gate.SetResult(new ConsoleServiceImportResult
        {
            Succeeded = state == "Discovered",
            State = state,
            Detail = "Obsolete discovery response",
        });
        await discovery;

        Assert.DoesNotContain("Obsolete discovery response", page.Markup, StringComparison.Ordinal);
        Assert.Empty(page.FindAll("[data-layer-checkbox]"));
        Assert.False(page.Find("button.console-button").HasAttribute("disabled"));

        operation.Discovery = null;
        page.Find("button.console-button").Click();
        page.WaitForAssertion(() => Assert.Equal(2, page.FindAll("[data-layer-checkbox]").Count));
    }

    [Fact]
    public async Task PublicationChoiceAppliesToTheWholeSubmittedBatch()
    {
        var operation = new RecordingImportOperation(blockFirstRequest: true);
        using var ctx = new BunitContext().AddConsoleNotifications();
        ctx.JSInterop.SetupModule(Honua.Console.Shell.Components.ConsoleOverlayFocusTrap.ModulePath).Mode = JSRuntimeMode.Loose;
        ctx.Services.AddSingleton<IConsoleServiceImportOperation>(operation);
        ctx.Services.AddSingleton(new ConsoleV4Options { Enabled = true });
        var page = ctx.Render<OperateImportServicePage>();
        page.Find("input[placeholder^='https://']").Input("https://source.example/FeatureServer");
        page.Find("button.console-button").Click();
        page.WaitForAssertion(() => Assert.Equal(2, page.FindAll("[data-layer-checkbox]").Count));
        for (var index = 0; index < 2; index++)
        {
            page.FindAll("[data-layer-checkbox]")[index].Change(true);
        }

        var queue = page.Find("[data-import-selected]").ClickAsync();
        Assert.Single(operation.Requests);
        page.Find("input[aria-label='Auto-publish imported layers']").Change(false);
        operation.CompleteFirstRequest();
        await queue;

        Assert.Equal(2, operation.Requests.Count);
        Assert.All(operation.Requests, request =>
        {
            Assert.True(request.AutoPublish);
            Assert.Equal("imp_roads", request.ServiceName);
        });

        page.Find("[data-import-selected]").Click();
        page.WaitForAssertion(() => Assert.Equal(4, operation.Requests.Count));
        Assert.All(operation.Requests.Skip(2), request =>
        {
            Assert.False(request.AutoPublish);
            Assert.Null(request.ServiceName);
        });
    }

    [Fact]
    public async Task SourceEditDuringABatchKeepsTheSubmittedRequestsOffTheNewScreen()
    {
        var operation = new RecordingImportOperation(blockFirstRequest: true);
        using var ctx = new BunitContext().AddConsoleNotifications();
        ctx.JSInterop.SetupModule(Honua.Console.Shell.Components.ConsoleOverlayFocusTrap.ModulePath).Mode = JSRuntimeMode.Loose;
        ctx.Services.AddSingleton<IConsoleServiceImportOperation>(operation);
        var page = ctx.Render<OperateImportServicePage>();
        page.Find("input[placeholder^='https://']").Input("https://source.example/FeatureServer");
        page.Find("button.console-button").Click();
        page.WaitForAssertion(() => Assert.Equal(2, page.FindAll("[data-layer-checkbox]").Count));
        for (var index = 0; index < 2; index++)
        {
            page.FindAll("[data-layer-checkbox]")[index].Change(true);
        }

        var queue = page.Find("[data-import-selected]").ClickAsync();
        Assert.Single(operation.Requests);
        page.Find("input[placeholder^='https://']").Input("https://current.example/FeatureServer");
        operation.CompleteFirstRequest();
        await queue;

        Assert.Equal(2, operation.Requests.Count);
        Assert.All(operation.Requests, request => Assert.Equal("https://source.example/FeatureServer", request.ServiceUrl));
        Assert.Empty(page.FindAll("[data-import-job]"));
        Assert.False(page.Find("button.console-button").HasAttribute("disabled"));
    }

    private sealed class RecordingImportOperation : IConsoleServiceImportOperation
    {
        private readonly TaskCompletionSource<ConsoleServiceImportRun>? _firstRequestGate;

        public RecordingImportOperation(bool blockFirstRequest = false)
        {
            if (blockFirstRequest)
            {
                _firstRequestGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
            }
        }

        public TaskCompletionSource<ConsoleServiceImportResult>? Discovery { get; set; }

        public List<ConsoleServiceImportRunRequest> Requests { get; } = [];

        public void CompleteFirstRequest() => _firstRequestGate!.SetResult(FailedRun());

        public Task<ConsoleServiceImportResult> DiscoverAsync(
            string serviceUrl,
            ConsoleServiceImportAuth? auth = null,
            CancellationToken cancellationToken = default) => Discovery?.Task ?? Task.FromResult(new ConsoleServiceImportResult
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
                        Layers =
                        [
                            new ConsoleServiceImportLayer { LayerId = 1, Name = "Roads" },
                            new ConsoleServiceImportLayer { LayerId = 2, Name = "Roads" },
                        ],
                    },
                ],
            });

        public Task<ConsoleServiceImportRun> StartLayerImportAsync(
            ConsoleServiceImportRunRequest request,
            CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            if (Requests.Count == 1 && _firstRequestGate is { } gate)
            {
                return gate.Task;
            }

            return Task.FromResult(FailedRun());
        }

        private static ConsoleServiceImportRun FailedRun() => new()
        {
            Succeeded = false,
            State = "Conflict",
            Detail = "Existing targets are not overwritten without authorization.",
        };

        public Task<ConsoleServiceImportJob> GetImportJobAsync(string jobId, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("No job was queued.");
    }
}
