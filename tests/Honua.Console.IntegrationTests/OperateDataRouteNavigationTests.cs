using Bunit;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Pages;
using Honua.Console.Shell.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Honua.Console.IntegrationTests;

public sealed class OperateDataRouteNavigationTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task DelayedSceneCannotReplaceCurrentViewer(bool enabled, bool returnToFirst)
    {
        using var context = Context(enabled);
        var client = new DelayedSceneClient();
        context.Services.AddSingleton<IConsoleSceneClient>(client);
        var page = context.Render<OperateScenesPage>(p => p.Add(c => c.SceneId, "first"));

        page.Render(p => p.Add(c => c.SceneId, returnToFirst ? null : "second"));
        if (returnToFirst) page.Render(p => p.Add(c => c.SceneId, "first"));
        var currentUrl = returnToFirst ? "/first/current.json" : "/second/current.json";
        page.WaitForAssertion(() => Assert.Contains(currentUrl, page.Markup));
        var renders = page.RenderCount;
        await page.InvokeAsync(() => client.First.SetResult("/first/stale.json"));

        page.WaitForState(() => page.RenderCount > renders);
        Assert.Contains(currentUrl, page.Markup);
        Assert.DoesNotContain("/first/stale.json", page.Markup);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LeavingSceneDetailDiscardsPendingViewer(bool enabled)
    {
        using var context = Context(enabled);
        var client = new DelayedSceneClient { DelaySecond = true };
        context.Services.AddSingleton<IConsoleSceneClient>(client);
        var page = context.Render<OperateScenesPage>(p => p.Add(c => c.SceneId, "first"));
        page.Render(p => p.Add(c => c.SceneId, null));
        var renders = page.RenderCount;
        await page.InvokeAsync(() => client.First.SetResult("/first/stale.json"));
        page.WaitForState(() => page.RenderCount > renders);

        page.Render(p => p.Add(c => c.SceneId, "second"));
        Assert.DoesNotContain("/first/stale.json", page.Markup);
        await page.InvokeAsync(() => client.Second.SetResult("/second/current.json"));
        page.WaitForAssertion(() => Assert.Contains("/second/current.json", page.Markup));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task DelayedSeriesCannotReplaceCurrentDatastream(bool enabled, bool returnToFirst)
    {
        using var context = Context(enabled);
        var client = new DelayedSensorClient();
        context.Services.AddSingleton<IConsoleSensorThingsClient>(client);
        var page = context.Render<OperateSensorThingsPage>(p => p.Add(c => c.DatastreamId, 1L));

        page.Render(p => p.Add(c => c.DatastreamId, returnToFirst ? null : 2L));
        if (returnToFirst) page.Render(p => p.Add(c => c.DatastreamId, 1L));
        var currentMessage = returnToFirst ? "current-series-1" : "current-series-2";
        page.WaitForAssertion(() => Assert.Contains(currentMessage, page.Markup));
        var renders = page.RenderCount;
        await page.InvokeAsync(() => client.First.SetResult(Series("stale-series")));

        page.WaitForState(() => page.RenderCount > renders);
        Assert.Contains(currentMessage, page.Markup);
        Assert.DoesNotContain("stale-series", page.Markup);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task DelayedReleaseCannotReplaceCurrentDetail(bool enabled, bool delayCoordinated)
    {
        using var context = Context(enabled);
        var client = new DelayedReleaseClient { DelayCoordinated = delayCoordinated };
        context.Services.AddSingleton<IConsoleGitOpsReleaseClient>(client);
        context.Services.AddSingleton<IConsoleDeployApprovalClient>(new UnsupportedConsoleDeployApprovalClient());
        context.Services.AddSingleton(ConsoleCapabilityTestManifest.All);
        var page = context.Render<OperateReleasesPage>(p => p.Add(c => c.SelectedReleaseId, "first"));
        page.WaitForState(() => delayCoordinated ? client.CoordinatedRequests.Contains("first") : client.DetailRequests.Contains("first"));
        page.Render(p => p.Add(c => c.SelectedReleaseId, "second"));
        page.WaitForAssertion(() => Assert.Contains("Current release second", page.Markup));
        var renders = page.RenderCount;
        if (delayCoordinated)
        {
            await page.InvokeAsync(() => client.FirstCoordinated.SetResult(Coordinated("stale-coordinated")));
        }
        else
        {
            await page.InvokeAsync(() => client.FirstDetail.SetResult(Detail("stale")));
        }

        page.WaitForState(() => page.RenderCount > renders);
        Assert.Contains("Current release second", page.Markup);
        Assert.DoesNotContain("Current release stale", page.Markup);
        Assert.DoesNotContain("stale-coordinated", page.Markup);
        if (!delayCoordinated) Assert.DoesNotContain("first", client.CoordinatedRequests);
    }

    private static BunitContext Context(bool enabled)
    {
        var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        context.Services.AddSingleton(new ConsoleV4Options { Enabled = enabled });
        context.Services.AddSingleton<ConsoleAgentContextRegistry>();
        return context;
    }

    private static SensorThingsReadResult<SensorThingsTimeSeries> Series(string message) =>
        SensorThingsReadResult<SensorThingsTimeSeries>.Denied(SensorThingsReadStatus.Unavailable, message);

    private static OperateSectionResult<GitOpsReleaseDetail> Detail(string id) =>
        OperateSectionResult<GitOpsReleaseDetail>.Allowed(new(
            new(id, $"Current release {id}", "Route navigation regression", "staging", ["prod"], "rev-1", [],
                GitOpsRollbackClassification.Unknown, false),
            [], null, OperateSectionStatus.Missing, "No release operation"));

    private static OperateSectionResult<GitOpsCoordinatedRelease> Coordinated(string message) =>
        OperateSectionResult<GitOpsCoordinatedRelease>.Denied(OperateSectionStatus.Missing, message);

    private sealed class DelayedSceneClient : IConsoleSceneClient
    {
        private readonly InMemoryConsoleSceneClient _inner = new();
        private int _reads;
        public bool DelaySecond { get; init; }
        public TaskCompletionSource<string?> Second { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<string?> First { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<SceneReadResult<SceneListResponse>> ListScenesAsync(CancellationToken cancellationToken = default) =>
            _inner.ListScenesAsync(cancellationToken);
        public Task<SceneReadResult<PointCloudIngestResult>> IngestPointCloudAsync(PointCloudIngestRequest request, CancellationToken cancellationToken = default) =>
            _inner.IngestPointCloudAsync(request, cancellationToken);
        public Task<string?> ResolveTilesetUrlAsync(string sceneId, CancellationToken cancellationToken = default) =>
            ++_reads == 1 ? First.Task : DelaySecond ? Second.Task : Task.FromResult<string?>($"/{sceneId}/current.json");
    }

    private sealed class DelayedSensorClient : IConsoleSensorThingsClient
    {
        private readonly InMemoryConsoleSensorThingsClient _inner = new();
        private int _reads;
        public TaskCompletionSource<SensorThingsReadResult<SensorThingsTimeSeries>> First { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<SensorThingsReadResult<StaEntitySet<StaThing>>> ListThingsAsync(int top = 50, int skip = 0, CancellationToken cancellationToken = default) =>
            _inner.ListThingsAsync(top, skip, cancellationToken);
        public Task<SensorThingsReadResult<StaEntitySet<StaDatastream>>> ListDatastreamsAsync(int top = 50, int skip = 0, CancellationToken cancellationToken = default) =>
            _inner.ListDatastreamsAsync(top, skip, cancellationToken);
        public Task<SensorThingsReadResult<StaDatastream>> GetDatastreamAsync(long datastreamId, bool expandObservations = false, int observationTop = 200, CancellationToken cancellationToken = default) =>
            _inner.GetDatastreamAsync(datastreamId, expandObservations, observationTop, cancellationToken);
        public Task<SensorThingsReadResult<StaEntitySet<StaObservation>>> ListDatastreamObservationsAsync(long datastreamId, int top = 200, int skip = 0, CancellationToken cancellationToken = default) =>
            _inner.ListDatastreamObservationsAsync(datastreamId, top, skip, cancellationToken);
        public Task<SensorThingsReadResult<SensorThingsTimeSeries>> GetDatastreamTimeSeriesAsync(long datastreamId, int top = 500, CancellationToken cancellationToken = default) =>
            ++_reads == 1 ? First.Task : Task.FromResult(Series($"current-series-{datastreamId}"));
    }

    private sealed class DelayedReleaseClient : IConsoleGitOpsReleaseClient
    {
        public bool DelayCoordinated { get; init; }
        public List<string> DetailRequests { get; } = [];
        public List<string> CoordinatedRequests { get; } = [];
        public TaskCompletionSource<OperateSectionResult<GitOpsReleaseDetail>> FirstDetail { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<OperateSectionResult<GitOpsCoordinatedRelease>> FirstCoordinated { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<OperateSectionResult<IReadOnlyList<GitOpsReleaseProposal>>> GetReleaseProposalsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(OperateSectionResult<IReadOnlyList<GitOpsReleaseProposal>>.Allowed([]));
        public Task<OperateSectionResult<GitOpsReleaseProposal>> GetReleaseProposalAsync(string releasePackageId, CancellationToken cancellationToken = default) =>
            Task.FromResult(OperateSectionResult<GitOpsReleaseProposal>.Allowed(Detail(releasePackageId).Value!.Proposal));
        public Task<OperateSectionResult<GitOpsReleaseDetail>> GetReleaseDetailAsync(string releasePackageId, CancellationToken cancellationToken = default)
        {
            DetailRequests.Add(releasePackageId);
            return releasePackageId == "first" && !DelayCoordinated ? FirstDetail.Task : Task.FromResult(Detail(releasePackageId));
        }
        public Task<OperateSectionResult<GitOpsCoordinatedRelease>> GetCoordinatedReleaseAsync(string releasePackageId, CancellationToken cancellationToken = default)
        {
            CoordinatedRequests.Add(releasePackageId);
            return releasePackageId == "first" && DelayCoordinated ? FirstCoordinated.Task : Task.FromResult(Coordinated($"current-coordinated-{releasePackageId}"));
        }
    }
}
