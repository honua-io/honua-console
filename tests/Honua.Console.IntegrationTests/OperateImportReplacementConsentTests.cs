using Bunit;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Pages;
using Honua.Console.Shell.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Honua.Console.IntegrationTests;

public sealed class OperateImportReplacementConsentTests
{
    [Fact]
    public void MixedSelection_RequiresCurrentActionConfirmationAndPreservesPerTargetIntent()
    {
        var operation = new RecordingImportOperation();
        using var ctx = new BunitContext().AddConsoleNotifications();
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

    private sealed class RecordingImportOperation : IConsoleServiceImportOperation
    {
        public List<ConsoleServiceImportRunRequest> Requests { get; } = [];

        public Task<ConsoleServiceImportResult> DiscoverAsync(
            string serviceUrl,
            ConsoleServiceImportAuth? auth = null,
            CancellationToken cancellationToken = default) => Task.FromResult(new ConsoleServiceImportResult
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
            return Task.FromResult(new ConsoleServiceImportRun
            {
                Succeeded = false,
                State = "Conflict",
                Detail = "Existing targets are not overwritten without authorization.",
            });
        }

        public Task<ConsoleServiceImportJob> GetImportJobAsync(string jobId, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("No job was queued.");
    }
}
