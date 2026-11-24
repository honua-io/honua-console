using System.Globalization;
using Bunit;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Pages;
using Honua.Console.Shell.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Honua.Console.IntegrationTests;

// Nesting the detail inside the populated-list branch swallows a deep link, including a job loaded by its own detail read.
public sealed partial class OperateObservabilityPageRenderTests
{
    [Theory]
    [InlineData(ConsolePresentationMode.Legacy)]
    [InlineData(ConsolePresentationMode.V4)]
    public async Task AgentContextTracksAppliedFiltersAndRouteJobWithoutDraftValues(ConsolePresentationMode mode)
    {
        using var context = new BunitContext();
        var registry = new ConsoleAgentContextRegistry();
        context.Services.AddSingleton(registry);
        context.Services.AddSingleton(new ConsoleV4Options { Enabled = mode == ConsolePresentationMode.V4 });
        context.Services.AddSingleton(ConsoleCapabilityTestManifest.All);
        context.Services.AddSingleton<IConsoleOperateObservabilityClient>(new StubOperateClient());
        context.Services.AddSingleton<IStudioWorkflowPackageClient>(new UnsupportedStudioWorkflowPackageClient());
        context.Services.GetRequiredService<Microsoft.AspNetCore.Components.NavigationManager>()
            .NavigateTo("/operate/jobs/job-first?token=private-content");
        var page = context.Render<OperateObservabilityPage>(parameters => parameters.Add(p => p.SelectedJobRunId, "job-first"));

        if (mode == ConsolePresentationMode.Legacy)
        {
            Assert.Equal(0, registry.Current.Generation);
            return;
        }
        page.WaitForAssertion(() => Assert.Equal("job-first", Assert.Single(registry.Current.Selection).Id));
        Assert.Equal(ConsoleAgentEntityKind.Job, Assert.Single(registry.Current.Entities).Kind);
        Assert.Equal("/operate/jobs/job-first", registry.Current.Route);
        var filters = page.Find("form[aria-label='Operate evidence filters']");
        filters.QuerySelectorAll("label").Single(label => label.QuerySelector("span")?.TextContent == "Event type")
            .QuerySelector("select")!.Change("release");
        filters.QuerySelector("input")!.Input("corr-applied");
        Assert.Equal(string.Empty, registry.Current.Filters["correlationId"]);
        filters.Submit();
        page.WaitForAssertion(() => Assert.Equal("corr-applied", registry.Current.Filters["correlationId"]));
        Assert.Equal("release", registry.Current.Filters["eventType"]);
        page.Render(parameters => parameters.Add(p => p.SelectedJobRunId, "job-next"));
        page.WaitForAssertion(() => Assert.Equal("job-next", Assert.Single(registry.Current.Selection).Id));
        await context.DisposeComponentsAsync();
        Assert.Empty(registry.Current.Selection);
        Assert.Empty(registry.Current.Filters);
    }

    [Fact]
    public async Task AgentContextFollowsTheVisibleJob()
    {
        using var emptyContext = new BunitContext();
        var emptyRegistry = new ConsoleAgentContextRegistry();
        emptyContext.Services.AddSingleton(emptyRegistry);
        emptyContext.Services.AddSingleton(new ConsoleV4Options { Enabled = true });
        emptyContext.Services.AddSingleton(ConsoleCapabilityTestManifest.All);
        emptyContext.Services.AddSingleton<IConsoleOperateObservabilityClient>(new StubOperateClient());
        var emptyPage = emptyContext.Render<OperateObservabilityPage>();
        emptyPage.WaitForAssertion(() => Assert.Contains("No jobs", emptyPage.Markup, StringComparison.Ordinal));
        Assert.Empty(emptyRegistry.Current.Entities);
        Assert.Empty(emptyRegistry.Current.Selection);
        await emptyContext.DisposeComponentsAsync();

        using var context = new BunitContext();
        var registry = new ConsoleAgentContextRegistry();
        context.Services.AddSingleton(registry);
        context.Services.AddSingleton(new ConsoleV4Options { Enabled = true });
        context.Services.AddSingleton(ConsoleCapabilityTestManifest.All);
        context.Services.AddSingleton<IConsoleOperateObservabilityClient>(new StubOperateClient
        {
            Jobs = OperateSectionResult<IReadOnlyList<OperateJobRun>>.Allowed([BuildJob("default-job")])
        });
        var page = context.Render<OperateObservabilityPage>();
        page.WaitForAssertion(() => Assert.Equal("default-job", Assert.Single(registry.Current.Selection).Id));
        Assert.Equal(ConsoleAgentEntityKind.Job, Assert.Single(registry.Current.Entities).Kind);
        Assert.Contains("operate-selected-row", page.Markup, StringComparison.Ordinal);
        await context.DisposeComponentsAsync();
        Assert.Empty(registry.Current.Selection);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EventTimeline_PreservesDeduplicationAndSelectedEventEvidence(bool v4Enabled)
    {
        var first = BuildEvent("evt-first", "First event", "corr-first");
        var selected = BuildEvent("evt-selected", "Selected event", W3cTraceparent);
        // Live-length job values: the Jobs grid and detail card must still fit a phone viewport.
        var liveJob = BuildJob("0f1e2d3c4b5a69788796a5b4c3d2e1f0") with
        {
            SubmittedBy = "apikey:api-key:00000000-0000-0000-0000-000000000001",
            JobType = "export:csv:sample-dataset_000000:10",
            ProgressPercent = 50,
        };
        using var context = new BunitContext();
        context.Services.AddSingleton(new ConsoleV4Options { Enabled = v4Enabled });
        context.Services.AddSingleton(ConsoleCapabilityTestManifest.All);
        context.Services.AddSingleton<IConsoleOperateObservabilityClient>(new StubOperateClient
        {
            Events = OperateSectionResult<IReadOnlyList<OperateEventRow>>.Allowed([first, selected, selected]),
            Jobs = OperateSectionResult<IReadOnlyList<OperateJobRun>>.Allowed([liveJob]),
            JobDetail = _ => OperateSectionResult<OperateJobRun>.Allowed(liveJob),
        });
        var changes = FakeActivityFeedClient.Sample;
        var attributedChange = changes[0] with
        {
            Actor = "regional-planning-and-geospatial-operations@example.org",
            ActorAgent = "Regional Planning and Geospatial Operations Assistant",
        };
        context.Services.AddSingleton<IConsoleActivityFeedClient>(new FakeActivityFeedClient
        {
            Respond = _ => OperateSectionResult<ConsoleActivityFeedPage>.Allowed(
                new([attributedChange, .. changes.Skip(1)], NextCursor: null)),
        });

        var page = context.Render<OperateObservabilityPage>(parameters =>
            parameters.Add(p => p.SelectedEventId, selected.EventId));

        page.WaitForAssertion(() =>
        {
            Assert.Equal(2, page.FindAll("[data-timeline-row]").Count);
            Assert.Equal(selected.DetailHref, page.Find(".operate-selected-row a").GetAttribute("href"));
            Assert.Equal(selected.Message, page.Find("#event-detail-heading").TextContent);
            Assert.Contains(selected.CorrelationId, page.Find("[aria-labelledby='event-detail-heading']").TextContent);
            Assert.Equal(liveJob.JobRunId, page.Find("#jobs table tbody td a").TextContent);
            Assert.Contains(liveJob.SubmittedBy, page.Find("[aria-labelledby='job-detail-heading']").TextContent);
        });
        var progress = page.Find("#jobs table .operate-progress");
        Assert.Equal("progressbar", progress.GetAttribute("role"));
        Assert.Equal(liveJob.ProgressPercent.ToString(CultureInfo.InvariantCulture), progress.GetAttribute("aria-valuenow"));
        Assert.Equal(v4Enabled ? 3 : 0, page.FindAll("[data-feed-row]").Count);
        if (v4Enabled) CatalogAdminPreview.Capture("activity-selected", page.Markup);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EventFilters_SendTheSelectedTypeAndCorrelationToTheServer(bool v4Enabled)
    {
        var client = new StubOperateClient
        {
            Events = OperateSectionResult<IReadOnlyList<OperateEventRow>>.Allowed(
                [BuildEvent("evt-first", "First event", "corr-first")]),
        };
        using var context = new BunitContext();
        context.Services.AddSingleton(new ConsoleV4Options { Enabled = v4Enabled });
        context.Services.AddSingleton(ConsoleCapabilityTestManifest.All);
        context.Services.AddSingleton<IConsoleOperateObservabilityClient>(client);
        var page = context.Render<OperateObservabilityPage>();
        var filters = page.Find("form[aria-label='Operate evidence filters']");

        filters.QuerySelectorAll("label")
            .Single(label => label.QuerySelector("span")?.TextContent == "Event type")
            .QuerySelector("select")!.Change("release");
        filters.QuerySelector("input")!.Input("corr-selected");
        filters.Submit();

        page.WaitForAssertion(() =>
        {
            Assert.Equal("release", client.LastEventQuery?.EventType);
            Assert.Equal("corr-selected", client.LastEventQuery?.CorrelationId);
        });
    }

    [Theory]
    [InlineData(ConsolePresentationMode.Legacy, "")]
    [InlineData(ConsolePresentationMode.Legacy, "corr-first")]
    [InlineData(ConsolePresentationMode.V4, "")]
    [InlineData(ConsolePresentationMode.V4, "corr-first")]
    public void CorrelationNavigationReloadsEventsWithoutReloadingThePage(ConsolePresentationMode presentation, string initialCorrelationId)
    {
        var rows = new[]
        {
            BuildEvent("evt-first", "First event", "corr-first"),
            BuildEvent("evt-next", "Next event", "corr-next")
        };
        var client = new StubOperateClient
        {
            EventQuery = query => OperateSectionResult<IReadOnlyList<OperateEventRow>>.Allowed(
                rows.Where(row => string.IsNullOrEmpty(query.CorrelationId) || row.CorrelationId == query.CorrelationId).ToArray())
        };
        using var context = new BunitContext();
        context.Services.AddSingleton(new ConsoleV4Options { Enabled = presentation == ConsolePresentationMode.V4 });
        context.Services.AddSingleton(ConsoleCapabilityTestManifest.All);
        context.Services.AddSingleton<IConsoleOperateObservabilityClient>(client);
        var navigation = context.Services.GetRequiredService<Microsoft.AspNetCore.Components.NavigationManager>();
        navigation.NavigateTo("/operate/observability" + (string.IsNullOrEmpty(initialCorrelationId) ? "#events" : $"?correlationId={initialCorrelationId}#events"));
        var page = context.Render<OperateObservabilityPage>();
        var original = page.Instance;
        page.WaitForAssertion(() => Assert.Equal(string.IsNullOrEmpty(initialCorrelationId) ? 2 : 1, page.FindAll("[data-timeline-row]").Count));
        Assert.Single(client.EventQueries);

        navigation.NavigateTo("/operate/observability?correlationId=corr-next#events");
        page.WaitForAssertion(() =>
        {
            Assert.Equal("corr-next", client.LastEventQuery?.CorrelationId);
            Assert.Single(page.FindAll("[data-timeline-row]"));
            Assert.Contains("Next event", page.Find("#events").TextContent, StringComparison.Ordinal);
            Assert.Equal("corr-next", page.Find("form[aria-label='Operate evidence filters'] input").GetAttribute("value"));
        });
        Assert.Same(original, page.Instance);

        navigation.NavigateTo("/operate/observability#events");
        page.WaitForAssertion(() =>
        {
            Assert.Equal(string.Empty, client.LastEventQuery?.CorrelationId);
            Assert.Equal(2, page.FindAll("[data-timeline-row]").Count);
            Assert.Equal(string.Empty, page.Find("form[aria-label='Operate evidence filters'] input").GetAttribute("value"));
        });
        Assert.Equal(3, client.EventQueries.Count);
    }

    [Theory]
    [InlineData(ConsolePresentationMode.Legacy)]
    [InlineData(ConsolePresentationMode.V4)]
    public void CorrelationNavigationPreservesOtherAppliedFiltersAndIgnoresUnchangedQuery(ConsolePresentationMode presentation)
    {
        var client = new StubOperateClient();
        using var context = new BunitContext();
        context.Services.AddSingleton(new ConsoleV4Options { Enabled = presentation == ConsolePresentationMode.V4 });
        context.Services.AddSingleton(ConsoleCapabilityTestManifest.All);
        context.Services.AddSingleton<IConsoleOperateObservabilityClient>(client);
        context.Services.AddSingleton<IStudioWorkflowPackageClient>(new UnsupportedStudioWorkflowPackageClient());
        var navigation = context.Services.GetRequiredService<Microsoft.AspNetCore.Components.NavigationManager>();
        navigation.NavigateTo("/operate/observability?correlationId=corr-first#events");
        var page = context.Render<OperateObservabilityPage>();
        var filters = page.Find("form[aria-label='Operate evidence filters']");
        filters.QuerySelectorAll("label").Single(label => label.QuerySelector("span")?.TextContent == "Event type")
            .QuerySelector("select")!.Change("release");
        filters.QuerySelector("input")!.Input("corr-manual");
        filters.Submit();
        page.WaitForAssertion(() => Assert.Equal("corr-manual", client.LastEventQuery?.CorrelationId));

        // A hash or unrelated route parameter change must not restore the old URL filter.
        navigation.NavigateTo("/operate/observability?correlationId=corr-first#jobs");
        page.Render(parameters => parameters.Add(p => p.SelectedJobRunId, "job-next"));
        Assert.Equal("corr-manual", client.LastEventQuery?.CorrelationId);
        Assert.Equal(2, client.EventQueries.Count);

        navigation.NavigateTo("/operate/observability?correlationId=corr-next#events");
        page.WaitForAssertion(() =>
        {
            Assert.Equal("corr-next", client.LastEventQuery?.CorrelationId);
            Assert.Equal("release", client.LastEventQuery?.EventType);
        });
    }

    [Fact]
    public async Task CorrelationNavigationIgnoresAnOlderFilterResponse()
    {
        var olderResponse = new TaskCompletionSource<OperateSectionResult<IReadOnlyList<OperateEventRow>>>();
        var client = new StubOperateClient
        {
            AsyncEventQuery = query => query.CorrelationId == "corr-older"
                ? olderResponse.Task
                : Task.FromResult(OperateSectionResult<IReadOnlyList<OperateEventRow>>.Allowed(
                    [BuildEvent("evt-newer", "Newer event", "corr-newer")]))
        };
        using var context = new BunitContext();
        context.Services.AddSingleton(new ConsoleV4Options { Enabled = true });
        context.Services.AddSingleton(ConsoleCapabilityTestManifest.All);
        context.Services.AddSingleton<IConsoleOperateObservabilityClient>(client);
        var navigation = context.Services.GetRequiredService<Microsoft.AspNetCore.Components.NavigationManager>();
        navigation.NavigateTo("/operate/observability");
        var page = context.Render<OperateObservabilityPage>();

        navigation.NavigateTo("/operate/observability?correlationId=corr-older#events");
        page.WaitForAssertion(() => Assert.Equal("corr-older", client.LastEventQuery?.CorrelationId));
        navigation.NavigateTo("/operate/observability?correlationId=corr-newer#events");
        page.WaitForAssertion(() => Assert.Equal("corr-newer", client.LastEventQuery?.CorrelationId));
        await page.InvokeAsync(() => olderResponse.SetResult(
            OperateSectionResult<IReadOnlyList<OperateEventRow>>.Allowed([BuildEvent("evt-older", "Older event", "corr-older")])));

        page.WaitForAssertion(() =>
        {
            Assert.Contains("Newer event", page.Find("#events").TextContent, StringComparison.Ordinal);
            Assert.DoesNotContain("Older event", page.Find("#events").TextContent, StringComparison.Ordinal);
            Assert.Equal("corr-newer", page.Find("form[aria-label='Operate evidence filters'] input").GetAttribute("value"));
        });
    }

    [Theory]
    [InlineData(false, "Operate Observability")]
    [InlineData(true, "Activity")]
    public void RolloutFlag_SelectsPresentationWithoutDroppingOperationalSections(bool enabled, string heading)
    {
        using var context = new BunitContext();
        context.Services.AddSingleton(new ConsoleV4Options { Enabled = enabled });
        context.Services.AddSingleton(ConsoleCapabilityTestManifest.All);
        context.Services.AddSingleton<IConsoleOperateObservabilityClient>(new StubOperateClient());

        var page = context.Render<OperateObservabilityPage>();

        Assert.Equal(heading, page.Find("h1").TextContent);
        foreach (var id in new[] { "fleet", "events", "logs", "alerts", "rules", "jobs", "investigations" })
        {
            Assert.Single(page.FindAll($"section#{id}"));
        }

        var sections = page.FindAll("section[id]").Select(section => section.Id).ToArray();
        Assert.True(enabled
            ? Array.IndexOf(sections, "events") < Array.IndexOf(sections, "fleet")
            : Array.IndexOf(sections, "fleet") < Array.IndexOf(sections, "events"));
        Assert.Equal(enabled, page.FindAll(".activity-sync-notice").Count == 1);
        Assert.Equal(enabled, page.FindAll(".console-activity-v4").Count == 1);
        Assert.Equal(enabled, page.FindAll("section > link[rel='stylesheet'][href$='operate-activity-v4.css']").Count == 1);
        if (enabled) CatalogAdminPreview.Capture("activity-empty", page.Markup);
    }

    private const string TraceId = "4bf92f3577b34da6a3ce929d0e0e4736";
    private const string W3cTraceparent = $"00-{TraceId}-00f067aa0ba902b7-01";

    private static OperateEventRow BuildEvent(string id, string message, string correlation) =>
        new(id, "2026-11-09T17:30:00Z", "info", "release", "publication", message,
            "production", "server-1", correlation, TraceId, "0HN8R2K4QJ1VA:00000017",
            null, null, [], [], [], null);

    [Fact]
    public void UnadvertisedInvestigationSectionPreservesThePageHeading()
    {
        using var context = new BunitContext();
        context.Services.AddSingleton<IConsoleCapabilityManifest>(new ConsoleCapabilityManifest());
        context.Services.AddSingleton<IConsoleOperateObservabilityClient>(new StubOperateClient());

        var page = context.Render<OperateObservabilityPage>();

        Assert.Single(page.FindAll("h1"));
        Assert.Contains(page.FindAll("h2"), heading => heading.TextContent == "Investigations are not available in this release");
        Assert.Empty(page.FindAll("#investigations"));
    }

    [Fact]
    public void EventDeepLink_WhenEventPageEmpty_StillRendersMissingDetailSurface()
    {
        using var ctx = new Bunit.BunitContext();
        ctx.Services.AddSingleton(ConsoleCapabilityTestManifest.All);
        ctx.Services.AddSingleton<IConsoleOperateObservabilityClient>(new StubOperateClient());

        var page = ctx.Render<OperateObservabilityPage>(parameters =>
            parameters.Add(p => p.SelectedEventId, "evt-unknown"));

        page.WaitForAssertion(
            () =>
            {
                // The empty-list context is still shown...
                Assert.Contains("No matching events", page.Markup, StringComparison.Ordinal);
                // ...but the deep link is no longer swallowed: the missing-detail surface renders.
                Assert.Contains(
                    "Event 'evt-unknown' was not found in the loaded server event page.",
                    page.Markup,
                    StringComparison.Ordinal);
            },
            TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void AlertDeepLink_WhenAlertPageEmpty_StillRendersMissingDetailSurface()
    {
        using var ctx = new Bunit.BunitContext();
        ctx.Services.AddSingleton(ConsoleCapabilityTestManifest.All);
        ctx.Services.AddSingleton<IConsoleOperateObservabilityClient>(new StubOperateClient());

        var page = ctx.Render<OperateObservabilityPage>(parameters =>
            parameters.Add(p => p.SelectedAlertId, "alert-unknown"));

        page.WaitForAssertion(
            () =>
            {
                Assert.Contains("No active alerts", page.Markup, StringComparison.Ordinal);
                Assert.Contains(
                    "Alert 'alert-unknown' was not found in the loaded server alert page.",
                    page.Markup,
                    StringComparison.Ordinal);
            },
            TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void JobDeepLink_WhenJobListEmpty_StillRendersDetailFromIndependentRead()
    {
        // The job list is empty, but the deep-linked job resolves through its own server read.
        var stub = new StubOperateClient
        {
            JobDetail = jobRunId => OperateSectionResult<OperateJobRun>.Allowed(BuildJob(jobRunId)),
        };

        using var ctx = new Bunit.BunitContext();
        ctx.Services.AddSingleton(ConsoleCapabilityTestManifest.All);
        ctx.Services.AddSingleton<IConsoleOperateObservabilityClient>(stub);
        // The unified job-run deep link also embeds the workflow job-evidence panel, which binds
        // IStudioWorkflowPackageClient. The unsupported client returns null evidence (missing-binding).
        ctx.Services.AddSingleton<IStudioWorkflowPackageClient>(new UnsupportedStudioWorkflowPackageClient());

        var page = ctx.Render<OperateObservabilityPage>(parameters =>
            parameters.Add(p => p.SelectedJobRunId, "job-deep-001"));

        page.WaitForAssertion(
            () =>
            {
                // The empty job list is still shown as context...
                Assert.Contains("No jobs", page.Markup, StringComparison.Ordinal);
                // ...and the independently-read job detail renders despite the empty list.
                Assert.Contains("job-deep-001", page.Markup, StringComparison.Ordinal);
                Assert.Contains("Workflow evidence unavailable", page.Markup, StringComparison.Ordinal);
                Assert.DoesNotContain("honua-server#", page.Markup, StringComparison.Ordinal);
                Assert.DoesNotContain("/api/v1/admin/jobs/job-deep-001", page.Markup, StringComparison.Ordinal);
                Assert.Contains("Deep-linked job is running.", page.Markup, StringComparison.Ordinal);
            },
            TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void JobDeepLink_WhenJobListReadFails_StillRendersDetailFromIndependentRead()
    {
        // The job list read is forbidden, but the deep-linked job detail is a separate read
        // that the active profile is allowed to make.
        var stub = new StubOperateClient
        {
            Jobs = OperateSectionResult<IReadOnlyList<OperateJobRun>>.Denied(
                OperateSectionStatus.Forbidden,
                "Jobs list is forbidden for this profile."),
            JobDetail = jobRunId => OperateSectionResult<OperateJobRun>.Allowed(BuildJob(jobRunId)),
        };

        using var ctx = new Bunit.BunitContext();
        ctx.Services.AddSingleton(ConsoleCapabilityTestManifest.All);
        ctx.Services.AddSingleton<IConsoleOperateObservabilityClient>(stub);
        ctx.Services.AddSingleton<IStudioWorkflowPackageClient>(new UnsupportedStudioWorkflowPackageClient());

        var page = ctx.Render<OperateObservabilityPage>(parameters =>
            parameters.Add(p => p.SelectedJobRunId, "job-deep-002"));

        page.WaitForAssertion(
            () =>
            {
                // The failed list read is still surfaced as context...
                Assert.Contains("Jobs list is forbidden for this profile.", page.Markup, StringComparison.Ordinal);
                // ...and the independently-read job detail is no longer hidden by the list failure.
                Assert.Contains("job-deep-002", page.Markup, StringComparison.Ordinal);
                Assert.Contains("Deep-linked job is running.", page.Markup, StringComparison.Ordinal);
            },
            TimeSpan.FromSeconds(5));
    }

    private static OperateJobRun BuildJob(string jobRunId) => new(
        JobRunId: jobRunId,
        Source: "Publishing",
        JobType: "publish",
        Queue: "operator-publishing",
        Status: new OperateStatus("running", "Deep-linked job is running."),
        SubmittedBy: "tester@honua.example",
        SubmittedAt: "2026-05-26 10:00 HST",
        EnvironmentId: "prod",
        ServerId: "honua-prod-01",
        ProgressPercent: 50,
        FailureClassification: "none",
        ResourceRefs: [],
        Stages: [],
        Logs: [],
        Artifacts: [],
        Metrics: [],
        AllowedActions: [],
        RelatedObjects: []);

    private sealed class StubOperateClient : IConsoleOperateObservabilityClient
    {
        public OperateSectionResult<IReadOnlyList<OperateEventRow>> Events { get; init; } =
            OperateSectionResult<IReadOnlyList<OperateEventRow>>.Allowed([]);

        public OperateEventQuery? LastEventQuery { get; private set; }

        public List<OperateEventQuery> EventQueries { get; } = [];

        public Func<OperateEventQuery, OperateSectionResult<IReadOnlyList<OperateEventRow>>>? EventQuery { get; init; }

        public Func<OperateEventQuery, Task<OperateSectionResult<IReadOnlyList<OperateEventRow>>>>? AsyncEventQuery { get; init; }

        public Func<string, Task<OperateSectionResult<OperateJobRun>>>? AsyncJobDetail { get; init; }

        public OperateSectionResult<IReadOnlyList<OperateJobRun>> Jobs { get; init; } =
            OperateSectionResult<IReadOnlyList<OperateJobRun>>.Allowed([]);

        public Func<string, OperateSectionResult<OperateJobRun>> JobDetail { get; init; } =
            _ => OperateSectionResult<OperateJobRun>.Denied(OperateSectionStatus.Missing, "Job not found.");

        public Task<OperateSectionResult<OperateFleetOverview>> GetOverviewAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(OperateSectionResult<OperateFleetOverview>.Allowed(OperateFleetOverview.Empty));

        public Task<OperateSectionResult<IReadOnlyList<OperateEventRow>>> QueryEventsAsync(OperateEventQuery query, CancellationToken cancellationToken = default)
        {
            LastEventQuery = query;
            EventQueries.Add(query);
            return AsyncEventQuery?.Invoke(query) ?? Task.FromResult(EventQuery?.Invoke(query) ?? Events);
        }

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
            AsyncJobDetail?.Invoke(jobRunId) ?? Task.FromResult(JobDetail(jobRunId));

        public Task<OperateSectionResult<OperateJobControlOutcome>> CancelJobAsync(string jobRunId, CancellationToken cancellationToken = default) =>
            Task.FromResult(OperateSectionResult<OperateJobControlOutcome>.Denied(OperateSectionStatus.Unavailable, "n/a"));

        public Task<OperateSectionResult<OperateJobControlOutcome>> RetryJobAsync(string jobRunId, CancellationToken cancellationToken = default) =>
            Task.FromResult(OperateSectionResult<OperateJobControlOutcome>.Denied(OperateSectionStatus.Unavailable, "n/a"));

        public Task<OperateSectionResult<OperateJobStepsView>> GetJobStepsAsync(string jobRunId, CancellationToken cancellationToken = default) =>
            Task.FromResult(OperateSectionResult<OperateJobStepsView>.Allowed(OperateJobStepsView.Empty));

        public Task<OperateSectionResult<IReadOnlyList<OperateInvestigation>>> GetInvestigationsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(OperateSectionResult<IReadOnlyList<OperateInvestigation>>.Allowed([]));

        public Task<OperateSectionResult<IReadOnlyList<OperateRecentError>>> GetRecentErrorsAsync(int limit = 10, CancellationToken cancellationToken = default) =>
            Task.FromResult(OperateSectionResult<IReadOnlyList<OperateRecentError>>.Allowed([]));
    }
}
