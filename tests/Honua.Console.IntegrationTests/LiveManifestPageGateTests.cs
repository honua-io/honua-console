using System.Net;
using System.Reflection;
using System.Text;
using Bunit;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Pages;
using Honua.Console.Shell.Services;
using Honua.Sdk.Studio.Capabilities;
using Microsoft.Extensions.DependencyInjection;

namespace Honua.Console.IntegrationTests;

public sealed class LiveManifestPageGateTests
{
    // Canonical predicates from the server CapabilityManifestService, not the Console mapping table.
    private static readonly (string Key, string Id)[] Rows =
    [
        ("temporal", "temporal.filtering"),
        ("disconnected-sync", "sync.offline"),
        ("realtime-alerting", "alerts.geofence"),
        ("cross-environment-promotion", "gitops.release-manifest"),
        ("siem-investigations", "ops.findings"),
    ];

    public static IEnumerable<object[]> Matrix => Rows.SelectMany(row =>
        new[] { "available", "unavailable", "unsupported", "absent", "missing", "unauthorized", "unreachable", "invalid-json", "invalid-schema", "null-capabilities", "duplicate" }
            .Select(state => new object[] { row.Key, row.Id, state }));

    [Theory]
    [MemberData(nameof(Matrix))]
    public async Task LiveSdkManifest_DrivesPageAndFeatureCalls(string key, string id, string state)
    {
        using var ctx = new BunitContext();
        ctx.AddConsoleNotifications();
        using var handler = new ManifestHandler(id, state);
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://server.example") };
        var manifest = new ManifestBackedConsoleCapabilityManifest(
            new HonuaServerCapabilityRegistryClient(new HonuaCapabilityManifestClient(http)), [key]);
        ctx.Services.AddSingleton<IConsoleCapabilityManifest>(manifest);
        var temporal = RecordingProxy<ITemporalCapabilityClient>.Wrap(new UnsupportedTemporalCapabilityClient());
        var rules = RecordingProxy<IOperateAlertRulesDataSource>.Wrap(new UnsupportedOperateAlertRulesDataSource());
        var releases = RecordingProxy<IConsoleGitOpsReleaseClient>.Wrap(new StubReleaseClient());
        var operations = new RecordingOperateClient();
        ctx.Services.AddSingleton(temporal);
        ctx.Services.AddSingleton(rules);
        ctx.Services.AddSingleton(releases);
        ctx.Services.AddSingleton<IConsoleOperateObservabilityClient>(operations);
        ctx.Services.AddSingleton<IConsoleDeployApprovalClient>(new UnsupportedConsoleDeployApprovalClient());

        int FeatureCalls() => key switch
        {
            "temporal" or "disconnected-sync" => ((RecordingProxy<ITemporalCapabilityClient>)temporal).Calls.Count,
            "realtime-alerting" => ((RecordingProxy<IOperateAlertRulesDataSource>)rules).Calls.Count,
            "cross-environment-promotion" => ((RecordingProxy<IConsoleGitOpsReleaseClient>)releases).Calls.Count,
            _ => operations.InvestigationCalls,
        };

        var pageType = key switch
        {
            "temporal" => typeof(OperateTemporalPage),
            "disconnected-sync" => typeof(OperateSyncPage),
            "realtime-alerting" => typeof(OperateAlertRulesPage),
            "cross-environment-promotion" => typeof(OperateReleasesPage),
            _ => typeof(OperateObservabilityPage),
        };
        var rendered = ctx.Render(builder =>
        {
            builder.OpenComponent(0, pageType);
            builder.CloseComponent();
        });
        Func<int> pageRenderCount = key switch
        {
            "temporal" => () => rendered.FindComponent<OperateTemporalPage>().RenderCount,
            "disconnected-sync" => () => rendered.FindComponent<OperateSyncPage>().RenderCount,
            "realtime-alerting" => () => rendered.FindComponent<OperateAlertRulesPage>().RenderCount,
            "cross-environment-promotion" => () => rendered.FindComponent<OperateReleasesPage>().RenderCount,
            _ => () => rendered.FindComponent<OperateObservabilityPage>().RenderCount,
        };
        var initialRenderCount = pageRenderCount();
        Assert.Equal(0, FeatureCalls());
        Assert.False(manifest.IsAdvertised(key));
        handler.Release.TrySetResult();

        var available = state == "available";
        rendered.WaitForAssertion(() =>
        {
            Assert.True(handler.Completed);
            Assert.True(pageRenderCount() > initialRenderCount);
            Assert.Equal(available, manifest.IsAdvertised(key));
            Assert.Equal(available ? 1 : 0, FeatureCalls());
            if (available)
            {
                Assert.DoesNotContain("not available in this release", rendered.Markup, StringComparison.Ordinal);
            }
            else
            {
                Assert.Contains("not available in this release", rendered.Markup, StringComparison.Ordinal);
            }
        });
        // Wait for the page's asynchronous lifecycle, so a late feature call cannot pass unnoticed.
        await ctx.Renderer.Dispatcher.InvokeAsync(() => Task.CompletedTask);
        Assert.Equal(available ? 1 : 0, FeatureCalls());
        Assert.Equal("/api/v1/capabilities/manifest", handler.RequestPath);
    }

    public class RecordingProxy<T> : DispatchProxy where T : class
    {
        public List<string> Calls { get; } = [];
        private T _target = null!;
        public static T Wrap(T target)
        {
            var proxy = Create<T, RecordingProxy<T>>();
            ((RecordingProxy<T>)(object)proxy)._target = target;
            return proxy;
        }
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            Calls.Add(targetMethod!.Name);
            return targetMethod.Invoke(_target, args);
        }
    }

    private sealed class ManifestHandler(string id, string state) : HttpMessageHandler
    {
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public string? RequestPath { get; private set; }
        public bool Completed { get; private set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestPath = request.RequestUri!.AbsolutePath;
            await Release.Task.WaitAsync(cancellationToken);
            Completed = true;
            if (state == "unreachable") throw new HttpRequestException("fixture offline");
            var status = state switch
            {
                "missing" => HttpStatusCode.NotFound,
                "unauthorized" => HttpStatusCode.Unauthorized,
                _ => HttpStatusCode.OK,
            };
            var entry = $$"""{"id":"{{id}}","available":{{(state != "unavailable").ToString().ToLowerInvariant()}},"supported":{{(state != "unsupported").ToString().ToLowerInvariant()}}} """;
            var entries = state switch { "absent" => "[]", "null-capabilities" => "null", "duplicate" => $"[{entry},{entry}]", _ => $"[{entry}]" };
            var schema = state == "invalid-schema" ? "unknown.v99" : "honua.capability_manifest.v1";
            var json = state == "invalid-json" ? "{" : $$"""{"schemaVersion":"{{schema}}","capabilities":{{entries}}}""";
            return new HttpResponseMessage(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
        }
    }
    private sealed class RecordingOperateClient : IConsoleOperateObservabilityClient
    {
        public int InvestigationCalls { get; private set; }

        public OperateSectionResult<IReadOnlyList<OperateJobRun>> Jobs { get; init; } =
            OperateSectionResult<IReadOnlyList<OperateJobRun>>.Allowed([]);

        public Func<string, OperateSectionResult<OperateJobRun>> JobDetail { get; init; } =
            _ => OperateSectionResult<OperateJobRun>.Denied(OperateSectionStatus.Missing, "Job not found.");

        public Task<OperateSectionResult<OperateFleetOverview>> GetOverviewAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(OperateSectionResult<OperateFleetOverview>.Allowed(OperateFleetOverview.Empty));

        public Task<OperateSectionResult<IReadOnlyList<OperateEventRow>>> QueryEventsAsync(OperateEventQuery query, CancellationToken cancellationToken = default) =>
            Task.FromResult(OperateSectionResult<IReadOnlyList<OperateEventRow>>.Allowed([]));

        public Task<OperateSectionResult<OperateLogsView>> GetLogsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(OperateSectionResult<OperateLogsView>.Allowed(OperateLogsView.Empty));

        public Task<OperateSectionResult<IReadOnlyList<OperateAlertRecord>>> GetAlertsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(OperateSectionResult<IReadOnlyList<OperateAlertRecord>>.Allowed([]));

        public Task<OperateSectionResult<OperateRulesView>> GetRulesAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(OperateSectionResult<OperateRulesView>.Allowed(OperateRulesView.Empty));

        public Task<OperateSectionResult<IReadOnlyList<OperateJobRun>>> GetJobsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(Jobs);

        public Task<OperateSectionResult<IReadOnlyList<OperateJobRun>>> GetJobsAsync(string? kind, CancellationToken cancellationToken = default) =>
            GetJobsAsync(cancellationToken);

        public Task<OperateSectionResult<IReadOnlyList<OperateJobRun>>> GetGeoprocessingJobsAsync(CancellationToken cancellationToken = default) =>
            GetJobsAsync(cancellationToken);

        public Task<OperateSectionResult<OperateJobRun>> GetJobDetailAsync(string jobRunId, CancellationToken cancellationToken = default) =>
            Task.FromResult(JobDetail(jobRunId));

        public Task<OperateSectionResult<OperateJobControlOutcome>> CancelJobAsync(string jobRunId, CancellationToken cancellationToken = default) =>
            Task.FromResult(OperateSectionResult<OperateJobControlOutcome>.Denied(OperateSectionStatus.Unavailable, "n/a"));

        public Task<OperateSectionResult<OperateJobControlOutcome>> RetryJobAsync(string jobRunId, CancellationToken cancellationToken = default) =>
            Task.FromResult(OperateSectionResult<OperateJobControlOutcome>.Denied(OperateSectionStatus.Unavailable, "n/a"));

        public Task<OperateSectionResult<OperateJobStepsView>> GetJobStepsAsync(string jobRunId, CancellationToken cancellationToken = default) =>
            Task.FromResult(OperateSectionResult<OperateJobStepsView>.Allowed(OperateJobStepsView.Empty));

        public Task<OperateSectionResult<IReadOnlyList<OperateInvestigation>>> GetInvestigationsAsync(CancellationToken cancellationToken = default)
        {
            InvestigationCalls++;
            return Task.FromResult(OperateSectionResult<IReadOnlyList<OperateInvestigation>>.Allowed([]));
        }

        public Task<OperateSectionResult<IReadOnlyList<OperateRecentError>>> GetRecentErrorsAsync(int limit = 10, CancellationToken cancellationToken = default) =>
            Task.FromResult(OperateSectionResult<IReadOnlyList<OperateRecentError>>.Allowed([]));
    }
    private sealed class StubReleaseClient : IConsoleGitOpsReleaseClient
    {
        public OperateSectionResult<IReadOnlyList<GitOpsReleaseProposal>> Proposals { get; init; } =
            OperateSectionResult<IReadOnlyList<GitOpsReleaseProposal>>.Denied(
                OperateSectionStatus.Unsupported,
                "No release-package list endpoint.");

        public Func<string, OperateSectionResult<GitOpsReleaseDetail>> Detail { get; init; } =
            _ => OperateSectionResult<GitOpsReleaseDetail>.Denied(OperateSectionStatus.Missing, "Release not found.");

        public Func<string, OperateSectionResult<GitOpsCoordinatedRelease>> Coordinated { get; init; } =
            _ => OperateSectionResult<GitOpsCoordinatedRelease>.Denied(OperateSectionStatus.Missing, "No coordinated release.");

        public Task<OperateSectionResult<IReadOnlyList<GitOpsReleaseProposal>>> GetReleaseProposalsAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Proposals);

        public Task<OperateSectionResult<GitOpsReleaseProposal>> GetReleaseProposalAsync(
            string releasePackageId,
            CancellationToken cancellationToken = default)
        {
            var detail = Detail(releasePackageId);
            return Task.FromResult(detail.IsAllowed
                ? OperateSectionResult<GitOpsReleaseProposal>.Allowed(detail.Value!.Proposal)
                : OperateSectionResult<GitOpsReleaseProposal>.Denied(detail.Status, detail.Message));
        }

        public Task<OperateSectionResult<GitOpsReleaseDetail>> GetReleaseDetailAsync(
            string releasePackageId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Detail(releasePackageId));

        public Task<OperateSectionResult<GitOpsCoordinatedRelease>> GetCoordinatedReleaseAsync(
            string releasePackageId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Coordinated(releasePackageId));
    }
}
