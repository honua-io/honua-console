using Bunit;
using Honua.Console.Contracts;
using Honua.Console.Shell.Components;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Pages;
using Honua.Console.Shell.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;

namespace Honua.Console.IntegrationTests;

public sealed class OverlayFocusAndEmbedA11yTests
{
    [Fact]
    public void DeployRollbackConfirm_ActivatesFocusTrapAndDismissesOnEscape()
    {
        using var ctx = new BunitContext();
        var overlayModule = ctx.JSInterop.SetupModule(ConsoleOverlayFocusTrap.ModulePath);
        overlayModule.SetupVoid("activate", _ => true);
        overlayModule.SetupVoid("deactivate", _ => true);
        ctx.Services.AddSingleton<IConsoleDeployApprovalClient>(
            new InMemoryConsoleDeployApprovalClient([SubmittedOperation("op-escape")]));

        var panel = ctx.Render<OperateDeploymentApprovalPanel>(p => p
            .Add(x => x.OperationId, "op-escape")
            .Add(x => x.PollInterval, (TimeSpan?)null));

        panel.WaitForAssertion(() => Assert.NotNull(panel.Find("button.operate-deploy-rollback-button")));
        panel.Find("button.operate-deploy-rollback-button").Click();

        var dialog = panel.Find(".operate-deploy-confirm");
        Assert.Equal("alertdialog", dialog.GetAttribute("role"));
        Assert.Equal("true", dialog.GetAttribute("aria-modal"));
        Assert.Single(overlayModule.Invocations, i => i.Identifier == "activate");

        dialog.KeyDown(new KeyboardEventArgs { Key = "Escape" });

        Assert.Empty(panel.FindAll(".operate-deploy-confirm"));
        Assert.Single(overlayModule.Invocations, i => i.Identifier == "deactivate");
    }

    [Fact]
    public void ImportReplacementConfirm_ActivatesFocusTrapAndDismissesOnEscape()
    {
        using var ctx = new BunitContext().AddConsoleNotifications();
        var overlayModule = ctx.JSInterop.SetupModule(ConsoleOverlayFocusTrap.ModulePath);
        overlayModule.SetupVoid("activate", _ => true);
        overlayModule.SetupVoid("deactivate", _ => true);
        ctx.Services.AddSingleton<IConsoleServiceImportOperation>(new SingleLayerImportOperation());

        var page = ctx.Render<OperateImportServicePage>();
        page.Find("input[placeholder^='https://']").Input("https://source.example/FeatureServer");
        page.Find("button.console-button").Click();
        page.WaitForAssertion(() => Assert.Single(page.FindAll("[data-layer-checkbox]")));
        page.Find("[data-layer-checkbox]").Change(true);
        page.Find("[data-replace-target]").Change(true);
        page.Find("[data-import-selected]").Click();

        var dialog = page.Find("[data-replace-confirmation]");
        Assert.Equal("alertdialog", dialog.GetAttribute("role"));
        Assert.Equal("true", dialog.GetAttribute("aria-modal"));
        Assert.Single(overlayModule.Invocations, i => i.Identifier == "activate");

        dialog.KeyDown(new KeyboardEventArgs { Key = "Escape" });

        Assert.Empty(page.FindAll("[data-replace-confirmation]"));
        Assert.Single(overlayModule.Invocations, i => i.Identifier == "deactivate");
    }

    [Fact]
    public void EmbedMapPage_ZoomButtonsHaveAccessibleLabels()
    {
        using var ctx = new BunitContext();
        ctx.Services.AddSingleton<IConsoleCatalogClient>(new InMemoryConsoleCatalogClient());
        ctx.Services.GetRequiredService<NavigationManager>().NavigateTo("http://localhost/embed/maps/map-public-field?zoom=true");

        var page = ctx.Render<EmbedMapPage>(p => p.Add(x => x.MapId, "map-public-field"));

        var zoomGroup = page.Find(".console-embed-zoom");
        Assert.Equal("group", zoomGroup.GetAttribute("role"));
        var buttons = zoomGroup.QuerySelectorAll("button");
        Assert.Equal(2, buttons.Length);
        Assert.Equal("Zoom in", buttons[0].GetAttribute("aria-label"));
        Assert.Equal("Zoom out", buttons[1].GetAttribute("aria-label"));
    }

    private static DeployOperationProposal SubmittedOperation(string operationId) => new(
        OperationId: operationId,
        Lifecycle: DeployOperationLifecycle.Submitted,
        RawStatus: "Submitted",
        Kind: "gitops-deploy",
        Priority: "normal",
        Service: "roads-api",
        Environment: "prod",
        DesiredRevision: "rev-2",
        CurrentRevision: "rev-1",
        Action: "deploy",
        ChangeSummary: "Promote roads-api.",
        RequestedBy: "operator",
        Reason: null,
        PrUrl: null,
        CommitSha: null,
        Evidence: [],
        RollbackPlan: new DeployOperationRollbackSummary(
            GitOpsRollbackClassification.SnapshotRequired,
            IsDataAffecting: true,
            RequiresExplicitApproval: true,
            Steps: ["restore snapshot"],
            EvidenceRequired: [],
            ApprovalPolicyRef: null),
        Warnings: [],
        BlockingReasons: [],
        CreatedAt: DateTimeOffset.UtcNow,
        UpdatedAt: DateTimeOffset.UtcNow);

    private sealed class SingleLayerImportOperation : IConsoleServiceImportOperation
    {
        public Task<ConsoleServiceImportResult> DiscoverAsync(
            string serviceUrl, ConsoleServiceImportAuth? auth = null, CancellationToken cancellationToken = default) =>
            Task.FromResult(new ConsoleServiceImportResult
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
                        Layers = [new ConsoleServiceImportLayer { LayerId = 1, Name = "Roads" }]
                    }
                ]
            });

        public Task<ConsoleServiceImportRun> StartLayerImportAsync(
            ConsoleServiceImportRunRequest request, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Import should not be queued before confirmation.");

        public Task<ConsoleServiceImportJob> GetImportJobAsync(
            string jobId, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("No job was queued.");
    }
}
