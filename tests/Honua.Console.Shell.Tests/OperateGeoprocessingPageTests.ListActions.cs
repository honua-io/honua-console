using Bunit;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Pages;
using Honua.Console.Shell.Services;

namespace Honua.Console.Shell.Tests;

// The jobs list summary carries no action state. Active rows read the server's action gate so an
// acknowledged cancel is not offered again from the list while the worker is still stopping.
public sealed partial class OperateGeoprocessingPageTests
{
    private const string CancelPendingReason = "cancellation requested; waiting for the worker to stop";

    [Theory]
    [InlineData(ConsolePresentationMode.Legacy)]
    [InlineData(ConsolePresentationMode.V4)]
    public void ListRowsFollowTheServerCancelGateForActiveJobs(ConsolePresentationMode mode)
    {
        var client = new StubGeoprocessingClient
        {
            Jobs = OperateSectionResult<IReadOnlyList<OperateJobRun>>.Allowed(
            [
                Job("gp-stopping", "Running", "Executing", 40, "analyst.live"),
                Job("gp-open", "Running", "Executing", 10, "analyst.live"),
                Job("gp-done", "Succeeded", "Done", 100, "analyst.live"),
            ]),
            ReadActions = id => OperateSectionResult<IReadOnlyList<OperateJobAction>>.Allowed(
                [id == "gp-stopping" ? PendingCancelAction() : OpenCancelAction()])
        };

        using var ctx = NavigationContext(mode, client);
        var page = ctx.Render<OperateGeoprocessingPage>();

        Assert.Empty(CancelButtons(page, "gp-stopping"));
        Assert.Single(CancelButtons(page, "gp-open"));
        Assert.Equal(["gp-open", "gp-stopping"], client.ActionReads.Distinct().Order(StringComparer.Ordinal));
    }

    [Theory]
    [InlineData(ConsolePresentationMode.Legacy)]
    [InlineData(ConsolePresentationMode.V4)]
    public void ListRowKeepsCancelWhenTheActionGateCannotBeRead(ConsolePresentationMode mode)
    {
        var client = new StubGeoprocessingClient
        {
            Jobs = OperateSectionResult<IReadOnlyList<OperateJobRun>>.Allowed(
                [Job("gp-run", "Running", "Executing", 40, "analyst.live")]),
            ReadActions = _ => OperateSectionResult<IReadOnlyList<OperateJobAction>>.Denied(
                OperateSectionStatus.Unavailable, "actions unreadable")
        };

        using var ctx = NavigationContext(mode, client);
        var page = ctx.Render<OperateGeoprocessingPage>();

        Assert.Single(CancelButtons(page, "gp-run"));
        Assert.Equal("gp-run", Assert.Single(client.ActionReads.Distinct()));
    }

    [Fact]
    public void ListPollDropsCancelOnceAnotherClientRequestedIt()
    {
        var stopping = 0;
        var client = new StubGeoprocessingClient
        {
            Jobs = OperateSectionResult<IReadOnlyList<OperateJobRun>>.Allowed(
                [Job("gp-run", "Running", "Executing", 40, "analyst.live")]),
            ReadActions = _ => OperateSectionResult<IReadOnlyList<OperateJobAction>>.Allowed(
                [Volatile.Read(ref stopping) == 1 ? PendingCancelAction() : OpenCancelAction()])
        };

        using var ctx = NavigationContext(ConsolePresentationMode.Legacy, client);
        var page = ctx.Render<OperateGeoprocessingPage>(p => p.Add(x => x.PollIntervalMilliseconds, 20));
        Assert.Single(CancelButtons(page, "gp-run"));

        Volatile.Write(ref stopping, 1);
        page.WaitForAssertion(() => Assert.Empty(CancelButtons(page, "gp-run")), TimeSpan.FromSeconds(5));
        Assert.Empty(client.CancelCalls);
    }

    [Fact]
    public void ListShowsRowsWhenAnActionReadStalls()
    {
        var client = new StubGeoprocessingClient
        {
            Jobs = OperateSectionResult<IReadOnlyList<OperateJobRun>>.Allowed(
                [Job("gp-run", "Running", "Executing", 40, "analyst.live")]),
            StallActionReads = true
        };

        using var ctx = NavigationContext(ConsolePresentationMode.Legacy, client);
        var page = ctx.Render<OperateGeoprocessingPage>();

        page.WaitForAssertion(() => Assert.Single(CancelButtons(page, "gp-run")), TimeSpan.FromSeconds(10));
    }

    private static OperateJobAction OpenCancelAction() =>
        new("Cancel", true, OperateActionPresentation.ActionExecutionDeferredReason) { Name = OperateJobAction.CancelName };

    private static OperateJobAction PendingCancelAction() =>
        new("Cancel", false, CancelPendingReason) { Name = OperateJobAction.CancelName };

    private static IEnumerable<AngleSharp.Dom.IElement> CancelButtons(IRenderedComponent<OperateGeoprocessingPage> page, string jobRunId) =>
        page.FindAll("tbody tr")
            .Where(row => row.QuerySelector("a")?.TextContent.Trim() == jobRunId)
            .SelectMany(row => row.QuerySelectorAll("button"))
            .Where(button => button.TextContent.Trim() == "Cancel");

    private sealed partial class StubGeoprocessingClient
    {
        public Func<string, OperateSectionResult<IReadOnlyList<OperateJobAction>>>? ReadActions { get; init; }

        public bool StallActionReads { get; init; }

        public System.Collections.Concurrent.ConcurrentQueue<string> ActionReads { get; } = new();

        public async Task<OperateSectionResult<IReadOnlyList<OperateJobAction>>> GetJobActionsAsync(
            string jobRunId,
            CancellationToken cancellationToken = default)
        {
            ActionReads.Enqueue(jobRunId);
            if (StallActionReads)
            {
                await Task.Delay(Timeout.Infinite, cancellationToken);
            }

            return ReadActions?.Invoke(jobRunId)
                ?? OperateSectionResult<IReadOnlyList<OperateJobAction>>.Denied(OperateSectionStatus.Unsupported, "n/a");
        }
    }
}
