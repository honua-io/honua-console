using Bunit;
using Honua.Console.Shell.Components;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Pages;
using Honua.Console.Shell.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Honua.Console.Shell.Tests;

public sealed partial class OperateGeoprocessingPageTests
{
    [Theory]
    [InlineData(false, "detail")]
    [InlineData(true, "detail")]
    [InlineData(false, "steps")]
    [InlineData(true, "steps")]
    [InlineData(false, "diagnostics")]
    [InlineData(true, "diagnostics")]
    public async Task EarlierJobReadCannotReplaceCurrentEvidence(bool v4, string stage)
    {
        var pending = new TaskCompletionSource<bool>();
        var client = new StubGeoprocessingClient
        {
            ReadDetail = async id =>
            {
                if (id == "earlier" && stage == "detail") await pending.Task;
                return NavigationJob(id);
            },
            ReadSteps = async id =>
            {
                if (id == "earlier" && stage == "steps") await pending.Task;
                return OperateSectionResult<OperateJobStepsView>.Denied(OperateSectionStatus.Unavailable, $"{id} steps");
            },
            ReadDiagnostics = async (id, _) =>
            {
                if (id == "earlier" && stage == "diagnostics") await pending.Task;
                return OperateSectionResult<OperateJobDiagnosticsView>.Denied(OperateSectionStatus.Unavailable, $"{id} diagnostics");
            }
        };
        using var context = NavigationContext(v4, client);
        var page = context.Render<OperateGeoprocessingPage>(p => p.Add(x => x.SelectedJobRunId, "earlier"));
        page.Render(p => p.Add(x => x.SelectedJobRunId, "current"));
        page.WaitForAssertion(() => Assert.Equal("current", page.Find("#job-detail-heading").TextContent));

        await page.InvokeAsync(() => pending.SetResult(true));

        page.WaitForAssertion(() =>
        {
            Assert.Equal("current", page.Find("#job-detail-heading").TextContent);
            Assert.Contains("current steps", page.Markup);
            Assert.Contains("current diagnostics", page.Markup);
            Assert.DoesNotContain("earlier steps", page.Markup);
            Assert.DoesNotContain("earlier diagnostics", page.Markup);
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ReturningToSameJobReloadsItsEvidence(bool v4)
    {
        var client = new StubGeoprocessingClient { Detail = NavigationJob("same") };
        using var context = NavigationContext(v4, client);
        var page = context.Render<OperateGeoprocessingPage>(p => p.Add(x => x.SelectedJobRunId, "same"));
        page.Render(p => p.Add(x => x.SelectedJobRunId, (string?)null));
        client.Detail = OperateSectionResult<OperateJobRun>.Denied(OperateSectionStatus.Missing, "Job removed on server");

        page.Render(p => p.Add(x => x.SelectedJobRunId, "same"));

        Assert.Empty(page.FindAll("#job-detail-heading"));
        Assert.Contains("Job removed on server", page.Markup);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task EarlierEventPageCannotReplaceCurrentDiagnostics(bool v4, bool earlier)
    {
        var pending = new TaskCompletionSource<OperateSectionResult<OperateJobDiagnosticsView>>();
        var client = new StubGeoprocessingClient
        {
            ReadDetail = id => Task.FromResult(NavigationJob(id)),
            ReadDiagnostics = (id, cursor) => cursor is not null ? pending.Task
                : Task.FromResult(OperateSectionResult<OperateJobDiagnosticsView>.Allowed(Projection(id)))
        };
        using var context = NavigationContext(v4, client);
        var page = context.Render<OperateGeoprocessingPage>(p => p.Add(x => x.SelectedJobRunId, "earlier"));
        var timeline = page.FindComponent<JobDiagnosticsTimeline>();
        var read = page.InvokeAsync(() => earlier
            ? timeline.Instance.OnReadEarlier.InvokeAsync("older")
            : timeline.Instance.OnReadNext.InvokeAsync("next"));
        page.Render(p => p.Add(x => x.SelectedJobRunId, "current"));

        await page.InvokeAsync(() => pending.SetResult(
            OperateSectionResult<OperateJobDiagnosticsView>.Denied(OperateSectionStatus.Unavailable, "Earlier page unavailable")));
        await read;

        Assert.Equal("current", page.FindComponent<JobDiagnosticsTimeline>().Instance.View.JobRunId);
        Assert.DoesNotContain("Earlier page unavailable", page.Markup);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CancelCompletionDoesNotPublishOnAnotherJob(bool v4)
    {
        var pending = new TaskCompletionSource<OperateSectionResult<OperateJobControlOutcome>>();
        var client = new StubGeoprocessingClient
        {
            ReadDetail = id => Task.FromResult(id == "earlier"
                ? OperateSectionResult<OperateJobRun>.Allowed(Job(id, "Running", "Executing", 40, "operator"))
                : NavigationJob(id)),
            ReadDiagnostics = (id, _) => Task.FromResult(
                OperateSectionResult<OperateJobDiagnosticsView>.Allowed(Projection(id, cancel: id == "earlier"))),
            PendingCancel = pending.Task
        };
        using var context = NavigationContext(v4, client);
        var page = context.Render<OperateGeoprocessingPage>(p => p.Add(x => x.SelectedJobRunId, "earlier"));
        page.FindAll("button").Single(button => button.TextContent == "Cancel job").Click();
        var cancel = page.Find("[data-console-confirm-accept]").ClickAsync(new Microsoft.AspNetCore.Components.Web.MouseEventArgs());
        page.WaitForAssertion(() => Assert.Single(client.CancelCalls));
        page.Render(p => p.Add(x => x.SelectedJobRunId, "current"));

        await page.InvokeAsync(() => pending.SetResult(
            OperateSectionResult<OperateJobControlOutcome>.Denied(OperateSectionStatus.Unavailable, "Earlier cancel failed")));
        await cancel;

        Assert.Equal("current", page.Find("#job-detail-heading").TextContent);
        Assert.DoesNotContain("Earlier cancel failed", page.Markup);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NonCooperativePollCannotRestoreAnEarlierJob(bool v4)
    {
        var pending = new TaskCompletionSource<OperateSectionResult<OperateJobRun>>();
        var earlierReads = 0;
        var pollStarted = new TaskCompletionSource();
        var client = new StubGeoprocessingClient
        {
            ReadDetail = id =>
            {
                if (id == "earlier" && ++earlierReads > 1)
                {
                    pollStarted.SetResult();
                    return pending.Task;
                }
                return Task.FromResult(id == "earlier"
                    ? OperateSectionResult<OperateJobRun>.Allowed(Job(id, "Running", "Executing", 40, "operator"))
                    : NavigationJob(id));
            }
        };
        using var context = NavigationContext(v4, client);
        var page = context.Render<OperateGeoprocessingPage>(p => p
            .Add(x => x.SelectedJobRunId, "earlier").Add(x => x.PollIntervalMilliseconds, 5));
        await pollStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        page.Render(p => p.Add(x => x.SelectedJobRunId, "current"));

        await page.InvokeAsync(() => pending.SetResult(NavigationJob("earlier")));

        page.WaitForAssertion(() => Assert.Equal("current", page.Find("#job-detail-heading").TextContent));
        Assert.Equal("current", page.FindComponent<JobDiagnosticsTimeline>().Instance.View.JobRunId);
    }

    private static OperateSectionResult<OperateJobRun> NavigationJob(string id) =>
        OperateSectionResult<OperateJobRun>.Allowed(Job(id, "Succeeded", "Done", 100, "operator"));

    private static BunitContext NavigationContext(bool v4, StubGeoprocessingClient client)
    {
        var context = new BunitContext();
        context.JSInterop.SetupModule("./_content/Honua.Console.Shell/console-confirm-dialog.js").Mode = JSRuntimeMode.Loose;
        context.Services.AddSingleton(new ConsoleV4Options { Enabled = v4 });
        context.Services.AddSingleton<IConsoleOperateObservabilityClient>(client);
        return context;
    }
}
