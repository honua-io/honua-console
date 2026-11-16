using Bunit;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Pages;
using Honua.Console.Shell.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Honua.Console.IntegrationTests;

public sealed partial class OperateObservabilityPageRenderTests
{
    [Theory]
    [InlineData(ConsolePresentationMode.Legacy)]
    [InlineData(ConsolePresentationMode.V4)]
    public async Task EarlierJobResponseCannotReplaceCurrentSelection(ConsolePresentationMode mode)
    {
        var earlier = new TaskCompletionSource<OperateSectionResult<OperateJobRun>>();
        using var context = JobNavigationContext(mode, earlier);
        var page = context.Render<OperateObservabilityPage>(p => p.Add(x => x.SelectedJobRunId, "earlier"));
        page.Render(p => p.Add(x => x.SelectedJobRunId, "current"));
        page.WaitForAssertion(() => Assert.Equal("current", page.Find("#job-detail-heading").TextContent));

        await page.InvokeAsync(() => earlier.SetResult(JobResult("earlier")));

        page.WaitForAssertion(() => Assert.Equal("current", page.Find("#job-detail-heading").TextContent));
    }

    [Theory]
    [InlineData(ConsolePresentationMode.Legacy)]
    [InlineData(ConsolePresentationMode.V4)]
    public async Task ClearingJobSelectionRejectsPendingDetail(ConsolePresentationMode mode)
    {
        var earlier = new TaskCompletionSource<OperateSectionResult<OperateJobRun>>();
        using var context = JobNavigationContext(mode, earlier);
        var registry = new ConsoleAgentContextRegistry();
        context.Services.AddSingleton(registry);
        var page = context.Render<OperateObservabilityPage>(p => p.Add(x => x.SelectedJobRunId, "earlier"));
        page.Render(p => p.Add(x => x.SelectedJobRunId, (string?)null));

        await page.InvokeAsync(() => earlier.SetResult(JobResult("earlier")));

        Assert.Empty(page.FindAll("#job-detail-heading"));
        Assert.Empty(registry.Current.Selection);
    }

    [Theory]
    [InlineData(ConsolePresentationMode.Legacy)]
    [InlineData(ConsolePresentationMode.V4)]
    public async Task LoadingAnotherJobClearsPreviouslyLoadedDetail(ConsolePresentationMode mode)
    {
        var earlier = new TaskCompletionSource<OperateSectionResult<OperateJobRun>>();
        using var context = JobNavigationContext(mode, earlier);
        var page = context.Render<OperateObservabilityPage>(p => p.Add(x => x.SelectedJobRunId, "current"));
        page.WaitForAssertion(() => Assert.Equal("current", page.Find("#job-detail-heading").TextContent));

        page.Render(p => p.Add(x => x.SelectedJobRunId, "earlier"));

        Assert.Empty(page.FindAll("#job-detail-heading"));
        await page.InvokeAsync(() => earlier.SetResult(JobResult("earlier")));
        page.WaitForAssertion(() => Assert.Equal("earlier", page.Find("#job-detail-heading").TextContent));
    }

    [Theory]
    [InlineData(ConsolePresentationMode.Legacy, "pending")]
    [InlineData(ConsolePresentationMode.Legacy, "loaded")]
    [InlineData(ConsolePresentationMode.V4, "pending")]
    [InlineData(ConsolePresentationMode.V4, "loaded")]
    public async Task CorrelationQueryCannotDelayJobSelectionChange(ConsolePresentationMode mode, string earlierState)
    {
        var earlier = new TaskCompletionSource<OperateSectionResult<OperateJobRun>>();
        var events = new TaskCompletionSource<OperateSectionResult<IReadOnlyList<OperateEventRow>>>();
        if (earlierState == "loaded")
        {
            earlier.SetResult(JobResult("earlier"));
        }
        using var context = JobNavigationContext(mode, earlier, query => query.CorrelationId == "current-correlation"
            ? events.Task
            : Task.FromResult(OperateSectionResult<IReadOnlyList<OperateEventRow>>.Allowed([])));
        var navigation = context.Services.GetRequiredService<NavigationManager>();
        navigation.NavigateTo("/operate/jobs/earlier");
        var router = context.Render<Router>(p => p
            .Add(x => x.AppAssembly, typeof(OperateObservabilityPage).Assembly)
            .Add(x => x.Found, route => builder =>
            {
                builder.OpenComponent<RouteView>(0);
                builder.AddComponentParameter(1, nameof(RouteView.RouteData), route);
                builder.CloseComponent();
            }));
        var page = router.FindComponent<OperateObservabilityPage>();

        await page.InvokeAsync(() => navigation.NavigateTo("/operate/jobs/current?correlationId=current-correlation"));
        if (earlierState == "pending")
        {
            await page.InvokeAsync(() => earlier.SetResult(JobResult("earlier")));
        }

        page.WaitForAssertion(() => Assert.Equal("current", page.Find("#job-detail-heading").TextContent));
        Assert.False(events.Task.IsCompleted);
        await page.InvokeAsync(() => events.SetResult(OperateSectionResult<IReadOnlyList<OperateEventRow>>.Allowed([])));
        page.WaitForAssertion(() => Assert.Equal("current", page.Find("#job-detail-heading").TextContent));
    }

    private static BunitContext JobNavigationContext(ConsolePresentationMode mode,
        TaskCompletionSource<OperateSectionResult<OperateJobRun>> earlier,
        Func<OperateEventQuery, Task<OperateSectionResult<IReadOnlyList<OperateEventRow>>>>? eventQuery = null)
    {
        var context = new BunitContext();
        context.Services.AddSingleton(new ConsoleV4Options { Enabled = mode == ConsolePresentationMode.V4 });
        context.Services.AddSingleton(ConsoleCapabilityTestManifest.All);
        context.Services.AddSingleton<IStudioWorkflowPackageClient>(new UnsupportedStudioWorkflowPackageClient());
        context.Services.AddSingleton<IConsoleOperateObservabilityClient>(new StubOperateClient
        {
            AsyncJobDetail = id => id == "earlier" ? earlier.Task : Task.FromResult(JobResult(id)),
            AsyncEventQuery = eventQuery
        });
        return context;
    }

    private static OperateSectionResult<OperateJobRun> JobResult(string id) =>
        OperateSectionResult<OperateJobRun>.Allowed(BuildJob(id));
}
