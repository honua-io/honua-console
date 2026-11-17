using Bunit;
using Honua.Console.Contracts;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Pages;
using Honua.Console.Shell.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Honua.Console.Shell.Tests;

// The server applies a cancel asynchronously: the job can stay Running after the cancel is
// acknowledged. The page then shows one state and does not offer Cancel a second time.
public sealed partial class OperateGeoprocessingPageTests
{
    [Fact]
    public void AcknowledgedCancelIsNotOfferedAgainWhileTheJobStillRuns()
    {
        using var ctx = new BunitContext();
        ctx.JSInterop.SetupModule("./_content/Honua.Console.Shell/console-confirm-dialog.js").Mode = JSRuntimeMode.Loose;
        var client = new StubGeoprocessingClient
        {
            Detail = OperateSectionResult<OperateJobRun>.Allowed(Job("gp-run", "Running", "Executing", 40, "analyst.live")),
            Diagnostics = OperateSectionResult<OperateJobDiagnosticsView>.Allowed(Projection("gp-run", cancel: true, phase: "executing")),
            CancelResult = OperateSectionResult<OperateJobControlOutcome>.Allowed(
                new OperateJobControlOutcome("gp-run", "cancel", "Running", "Job cancellation requested.", "corr-2"))
        };
        ctx.Services.AddSingleton<IConsoleOperateObservabilityClient>(client);

        var page = ctx.Render<OperateGeoprocessingPage>(p => p.Add(x => x.SelectedJobRunId, "gp-run"));
        page.FindAll("button").Single(b => b.TextContent.Contains("Cancel job", StringComparison.Ordinal)).Click();
        page.Find("[data-console-confirm-accept]").Click();

        Assert.Equal("gp-run", Assert.Single(client.CancelCalls));
        Assert.Equal("executing", page.Find("[data-job-diagnostics-phase]").TextContent);
        Assert.Contains("Job cancellation requested.", page.Markup);
        Assert.DoesNotContain(page.FindAll("button"), b => b.TextContent.Contains("Cancel job", StringComparison.Ordinal));
    }

    [Fact]
    public void CancelIsNotOfferedWhenTheServerListsItAsNotAllowed()
    {
        using var ctx = new BunitContext();
        ctx.JSInterop.SetupModule("./_content/Honua.Console.Shell/console-confirm-dialog.js").Mode = JSRuntimeMode.Loose;
        var pending = Job("gp-run", "Running", "Executing", 40, "analyst.live") with
        {
            AllowedActions = [new OperateJobAction("Cancel", false, "cancellation requested; waiting for the worker") { Name = OperateJobAction.CancelName }]
        };
        var client = new StubGeoprocessingClient
        {
            Detail = OperateSectionResult<OperateJobRun>.Allowed(pending),
            Diagnostics = OperateSectionResult<OperateJobDiagnosticsView>.Allowed(Projection("gp-run", cancel: true, phase: "executing"))
        };
        ctx.Services.AddSingleton<IConsoleOperateObservabilityClient>(client);

        var page = ctx.Render<OperateGeoprocessingPage>(p => p.Add(x => x.SelectedJobRunId, "gp-run"));

        Assert.False(pending.CanCancel);
        Assert.DoesNotContain(page.FindAll("button"), b => b.TextContent.Contains("Cancel job", StringComparison.Ordinal));
    }

    [Fact]
    public void CancelIsOfferedAgainAfterTheCancelledJobRunsAgain()
    {
        using var ctx = new BunitContext();
        ctx.JSInterop.SetupModule("./_content/Honua.Console.Shell/console-confirm-dialog.js").Mode = JSRuntimeMode.Loose;
        var client = new StubGeoprocessingClient
        {
            Detail = OperateSectionResult<OperateJobRun>.Allowed(Job("gp-run", "Running", "Executing", 40, "analyst.live")),
            DetailAfterControl = OperateSectionResult<OperateJobRun>.Allowed(Job("gp-run", "Cancelled", "Cancelled", 40, "analyst.live")),
            Diagnostics = OperateSectionResult<OperateJobDiagnosticsView>.Allowed(Projection("gp-run", cancel: true, phase: "executing")),
            DiagnosticsAfterControl = OperateSectionResult<OperateJobDiagnosticsView>.Allowed(Projection("gp-run", cancel: true, phase: "executing")),
            CancelResult = OperateSectionResult<OperateJobControlOutcome>.Allowed(
                new OperateJobControlOutcome("gp-run", "cancel", "Cancelled", "Job cancellation requested.", "corr-2"))
        };
        ctx.Services.AddSingleton<IConsoleOperateObservabilityClient>(client);

        var page = ctx.Render<OperateGeoprocessingPage>(p => p.Add(x => x.SelectedJobRunId, "gp-run"));
        page.FindAll("button").Single(b => b.TextContent.Contains("Cancel job", StringComparison.Ordinal)).Click();
        page.Find("[data-console-confirm-accept]").Click();

        // Another client retries the cancelled job, which runs again under the same id.
        client.DetailAfterControl = OperateSectionResult<OperateJobRun>.Allowed(Job("gp-run", "Running", "Executing", 10, "analyst.live"));
        page.Render(p => p.Add(x => x.SelectedJobRunId, (string?)null));
        page.Render(p => p.Add(x => x.SelectedJobRunId, "gp-run"));

        Assert.Contains(page.FindAll("button"), b => b.TextContent.Contains("Cancel job", StringComparison.Ordinal));
    }

    [Fact]
    public void ServerCancelActionIsRecognisedByItsNameNotItsLabel()
    {
        var action = OperateObservabilityMapper.MapJobAction(new ConsoleJobActionDescriptor
        {
            Name = "cancel",
            Method = "POST",
            Allowed = false,
            DisabledReason = "cancellation requested; waiting for the worker to stop"
        });
        var relabelled = action with { Label = "Stop job" };

        Assert.Equal(OperateJobAction.CancelName, action.Name);
        Assert.False((Job("gp-run", "Running", "Executing", 40, "analyst.live") with { AllowedActions = [relabelled] }).CanCancel);
    }
}
