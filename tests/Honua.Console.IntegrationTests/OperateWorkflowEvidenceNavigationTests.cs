using Bunit;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Pages;
using Honua.Console.Shell.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

namespace Honua.Console.IntegrationTests;

public sealed class OperateWorkflowEvidenceNavigationTests
{
    public enum Surface { LegacyEvents, V4Events, JobMonitor }

    [Theory]
    [InlineData(Surface.LegacyEvents)]
    [InlineData(Surface.V4Events)]
    [InlineData(Surface.JobMonitor)]
    public async Task OldReadCannotReplaceCurrentEvidence(Surface surface)
    {
        using var view = new EvidenceView(surface);
        var first = view.Source.Delay("first");
        view.Open("first");
        view.Select("second");
        view.AssertEvidence("second");

        await view.Complete(first, "first");

        view.AssertEvidence("second");
        Assert.Equal(["first", "second"], view.Source.Reads);
    }

    [Theory]
    [InlineData(Surface.LegacyEvents)]
    [InlineData(Surface.V4Events)]
    [InlineData(Surface.JobMonitor)]
    public async Task ClearingSelectionInvalidatesPendingRead(Surface surface)
    {
        using var view = new EvidenceView(surface);
        var first = view.Source.Delay("first");
        view.Open("first");
        view.Select("");
        view.AssertEmpty();

        await view.Complete(first, "first");

        view.AssertEmpty();
        Assert.Equal(["first"], view.Source.Reads);
    }

    [Theory]
    [InlineData(Surface.LegacyEvents)]
    [InlineData(Surface.V4Events)]
    [InlineData(Surface.JobMonitor)]
    public async Task NewSelectionClearsPreviousEvidenceWhileLoading(Surface surface)
    {
        using var view = new EvidenceView(surface);
        view.Open("first");
        view.AssertEvidence("first");
        var second = view.Source.Delay("second");

        view.Select("second");
        view.AssertEmpty();
        await view.Complete(second, "second");

        view.AssertEvidence("second");
    }

    [Theory]
    [InlineData(Surface.LegacyEvents)]
    [InlineData(Surface.V4Events)]
    [InlineData(Surface.JobMonitor)]
    public async Task OldVisitCannotReplaceReturnVisit(Surface surface)
    {
        using var view = new EvidenceView(surface);
        var original = view.Source.Delay("first");
        view.Open("first");
        view.Select("second");
        var returning = view.Source.Delay("first");
        view.Select("first");
        view.AssertEmpty();
        await view.Complete(returning, "first", "return visit");
        view.AssertEvidence("first", "return visit");

        await view.Complete(original, "first", "original visit");

        view.AssertEvidence("first", "return visit");
        Assert.Equal(["first", "second", "first"], view.Source.Reads);
    }

    [Theory]
    [InlineData(Surface.LegacyEvents)]
    [InlineData(Surface.V4Events)]
    [InlineData(Surface.JobMonitor)]
    public async Task OldMissingResultCannotClearCurrentEvidence(Surface surface)
    {
        using var view = new EvidenceView(surface);
        var first = view.Source.Delay("first");
        view.Open("first");
        view.Select("second");
        view.AssertEvidence("second");

        await view.CompleteMissing(first);

        view.AssertEvidence("second");
    }

    [Theory]
    [InlineData(Surface.LegacyEvents)]
    [InlineData(Surface.V4Events)]
    [InlineData(Surface.JobMonitor)]
    public async Task CurrentMissingResultLeavesNoPreviousEvidence(Surface surface)
    {
        using var view = new EvidenceView(surface);
        view.Open("first");
        var missing = view.Source.Delay("missing");
        view.Select("missing");
        await view.CompleteMissing(missing);

        view.AssertEmpty();
        Assert.Equal(["first", "missing"], view.Source.Reads);
    }

    [Fact]
    public async Task SameMonitorParametersSharePendingRead()
    {
        using var view = new EvidenceView(Surface.JobMonitor);
        var first = view.Source.Delay("first");
        view.Open("first");
        view.Select("first");
        Assert.Equal(["first"], view.Source.Reads);

        await view.Complete(first, "first");

        view.AssertEvidence("first");
    }

    [Fact]
    public void SameMonitorParametersPreserveResolvedEvidence()
    {
        using var view = new EvidenceView(Surface.JobMonitor);
        view.Open("first");
        view.Select("first");

        view.AssertEvidence("first");
        Assert.Equal(["first"], view.Source.Reads);
    }

    private sealed class EvidenceView : IDisposable
    {
        private readonly BunitContext _context = new();
        private readonly Surface _surface;
        private IRenderedComponent<OperateEventsPage>? _events;
        private IRenderedComponent<OperateWorkflowJobEvidencePanel>? _monitor;
        public DelayedClient Source { get; } = new();
        private IRenderedComponent<IComponent> Page => (IRenderedComponent<IComponent>?)_events ?? _monitor!;

        public EvidenceView(Surface surface)
        {
            _surface = surface;
            _context.Services.AddSingleton<IStudioWorkflowPackageClient>(Source);
            _context.Services.AddSingleton(new ConsoleV4Options { Enabled = surface == Surface.V4Events });
            _context.Services.AddSingleton<ConsoleAgentContextRegistry>();
        }

        public void Open(string jobId)
        {
            if (_surface == Surface.JobMonitor)
                _monitor = _context.Render<OperateWorkflowJobEvidencePanel>(p => p.Add(c => c.JobId, jobId));
            else
            {
                Navigate(jobId);
                _events = _context.Render<OperateEventsPage>();
            }
        }

        public void Select(string jobId)
        {
            if (_surface == Surface.JobMonitor)
                _monitor!.Render(p => p.Add(c => c.JobId, jobId));
            else
                Navigate(jobId);
        }

        private void Navigate(string jobId) => _context.Services.GetRequiredService<NavigationManager>()
            .NavigateTo(string.IsNullOrEmpty(jobId) ? "/operate/events" : $"/operate/events?jobId={jobId}");

        public void AssertEvidence(string jobId, string log = "completed")
        {
            Page.WaitForAssertion(() =>
            {
                Assert.Equal([log], Page.FindAll(".console-event-list strong").Select(n => n.TextContent));
                Assert.Contains(jobId, Page.Markup);
                Assert.Contains($"version-{jobId}", Page.Markup);
                if (_surface == Surface.JobMonitor)
                    Assert.Equal($"/operate/events?jobId={jobId}", Page.Find("a").GetAttribute("href"));
                if (_surface == Surface.V4Events)
                    Assert.Equal(jobId, Assert.Single(_context.Services.GetRequiredService<ConsoleAgentContextRegistry>().Current.Selection).Id);
            });
        }

        public void AssertEmpty()
        {
            Page.WaitForAssertion(() =>
            {
                Assert.Empty(Page.FindAll(".console-event-list"));
                Assert.Empty(Page.FindAll(".console-status"));
            });
        }

        public Task Complete(TaskCompletionSource<StudioWorkflowJobEvidence?> pending, string jobId, string log = "completed")
            => CompleteRead(pending, Evidence(jobId, log));

        public Task CompleteMissing(TaskCompletionSource<StudioWorkflowJobEvidence?> pending)
            => CompleteRead(pending, null);

        private async Task CompleteRead(TaskCompletionSource<StudioWorkflowJobEvidence?> pending, StudioWorkflowJobEvidence? evidence)
        {
            var renders = Page.RenderCount;
            await Page.InvokeAsync(() => pending.SetResult(evidence));
            Page.WaitForState(() => Page.RenderCount > renders);
        }

        public void Dispose() => _context.Dispose();
    }

    private static StudioWorkflowJobEvidence Evidence(string jobId, string log) => new()
    {
        JobId = jobId,
        JobKind = "workflow",
        VersionId = $"version-{jobId}",
        Logs = [log],
        Artifacts = [$"artifact-{jobId}"],
        OperateEventsUrl = $"/operate/events?jobId={jobId}"
    };

    private sealed class DelayedClient : IStudioWorkflowPackageClient
    {
        private readonly Dictionary<string, Queue<TaskCompletionSource<StudioWorkflowJobEvidence?>>> _pending = [];
        public List<string> Reads { get; } = [];

        public TaskCompletionSource<StudioWorkflowJobEvidence?> Delay(string jobId)
        {
            var pending = new TaskCompletionSource<StudioWorkflowJobEvidence?>(TaskCreationOptions.RunContinuationsAsynchronously);
            if (!_pending.TryGetValue(jobId, out var queue)) _pending[jobId] = queue = new();
            queue.Enqueue(pending);
            return pending;
        }

        public Task<StudioWorkflowJobEvidence?> GetJobEvidenceAsync(string jobId, CancellationToken cancellationToken = default)
        {
            Reads.Add(jobId);
            return _pending.TryGetValue(jobId, out var queue) && queue.TryDequeue(out var pending)
                ? pending.Task : Task.FromResult<StudioWorkflowJobEvidence?>(Evidence(jobId, "completed"));
        }

        public Task<StudioWorkflowEditorContext> OpenEditorAsync(string? draftId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<StudioWorkflowDraftSummary>> ListDraftsAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<StudioWorkflowNodeDefinition>> ListNodeDefinitionsAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<StudioWorkflowPackageDraft> CreateDraftAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<StudioWorkflowPackageDraft?> GetDraftAsync(string draftId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<StudioWorkflowSaveResult> SaveVersionAsync(StudioWorkflowPackageDraft draft, string changeNote, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<StudioWorkflowDryRunResult> DryRunAsync(StudioWorkflowPackageDraft draft, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<StudioWorkflowPublishResult> PublishAsync(StudioWorkflowPackageDraft draft, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<StudioWorkflowRunHistory> ListRunHistoryAsync(string contentItemId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
