using Bunit;
using Honua.Console.Contracts;
using Honua.Console.Shell.Components;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Honua.Console.IntegrationTests;

public sealed class DiscoveryMetadataLifecycleTests
{
    [Theory]
    [InlineData(DiscoveryMetadataTargetKind.Resource)]
    [InlineData(DiscoveryMetadataTargetKind.Service)]
    public async Task NavigationDoesNotWaitForThePreviousSave(DiscoveryMetadataTargetKind kind)
    {
        using var context = new BunitContext();
        var client = RegisterClient(context);
        var page = Render(context, kind);
        page.Find("[data-discovery-title]").Change("Original edit");
        var saving = page.Find("[data-discovery-save]").ClickAsync(new());
        page.WaitForAssertion(() => Assert.Single(client.Writes));

        page.Render(p => p.Add(c => c.Address, Address(kind, 2)));

        Assert.Equal("Resource 2", Title(page));
        Assert.False(IsSaving(page));
        Assert.Empty(page.FindAll("[data-discovery-saved]"));
        await CompleteSave(page, client, 0);
        await saving;
        Assert.Equal("Resource 2", Title(page));
    }

    [Theory]
    [InlineData(DiscoveryMetadataTargetKind.Resource)]
    [InlineData(DiscoveryMetadataTargetKind.Service)]
    public async Task OldCompletionCannotReleaseTheCurrentSave(DiscoveryMetadataTargetKind kind)
    {
        using var context = new BunitContext();
        var client = RegisterClient(context);
        var summaries = new List<ConsoleAgentUnsavedSummary>();
        var page = context.Render<DiscoveryMetadataEditor>(p => p
            .Add(c => c.Address, Address(kind, 1))
            .Add(c => c.UnsavedChanged, summary => summaries.Add(summary)));
        var first = page.Find("[data-discovery-save]").ClickAsync(new());
        page.Render(p => p.Add(c => c.Address, Address(kind, 2)));
        page.Find("[data-discovery-title]").Change("Second edit");
        var second = page.Find("[data-discovery-save]").ClickAsync(new());
        page.WaitForAssertion(() => Assert.Equal(2, client.Writes.Count));

        await CompleteSave(page, client, 0);
        await first;

        Assert.True(IsSaving(page));
        Assert.Equal(1, summaries[^1].PendingOperationCount);
        Assert.Equal("Second edit", Title(page));
        Assert.Empty(page.FindAll("[data-discovery-saved]"));
        await CompleteSave(page, client, 1);
        await second;
        Assert.False(IsSaving(page));
        Assert.Equal(0, summaries[^1].PendingOperationCount);
        Assert.NotNull(page.Find("[data-discovery-saved]"));
    }

    [Theory]
    [InlineData(DiscoveryMetadataTargetKind.Resource)]
    [InlineData(DiscoveryMetadataTargetKind.Service)]
    public async Task ReturningToTheSameAddressDoesNotAcceptItsOldSave(DiscoveryMetadataTargetKind kind)
    {
        using var context = new BunitContext();
        var client = RegisterClient(context);
        var page = Render(context, kind);
        page.Find("[data-discovery-title]").Change("Old edit");
        var first = page.Find("[data-discovery-save]").ClickAsync(new());
        page.Render(p => p.Add(c => c.Address, Address(kind, 2)));
        page.Render(p => p.Add(c => c.Address, Address(kind, 1)));
        page.Find("[data-discovery-title]").Change("New edit");

        await CompleteSave(page, client, 0);
        await first;

        Assert.Equal("New edit", Title(page));
        Assert.Empty(page.FindAll("[data-discovery-saved]"));
        Assert.False(IsSaving(page));
    }

    [Theory]
    [InlineData(DiscoveryMetadataTargetKind.Resource)]
    [InlineData(DiscoveryMetadataTargetKind.Service)]
    public async Task NavigationDuringSummaryDoesNotDispatchTheOldSave(DiscoveryMetadataTargetKind kind)
    {
        using var context = new BunitContext();
        var client = RegisterClient(context);
        client.CompleteImmediately = true;
        var summary = new PendingSummary();
        var page = Render(context, kind, summary);
        page.Find("[data-discovery-title]").Change("First edit");
        summary.PauseNext();
        var saving = page.Find("[data-discovery-save]").ClickAsync(new());
        page.Render(p => p.Add(c => c.Address, Address(kind, 2)));

        await page.InvokeAsync(summary.Resume);
        await saving;

        Assert.Empty(client.Writes);
        Assert.Equal("Resource 2", Title(page));
        Assert.False(IsSaving(page));
    }

    [Theory]
    [InlineData(DiscoveryMetadataTargetKind.Resource)]
    [InlineData(DiscoveryMetadataTargetKind.Service)]
    public async Task DisposalDuringSummaryDoesNotDispatchASave(DiscoveryMetadataTargetKind kind)
    {
        using var context = new BunitContext();
        var client = RegisterClient(context);
        client.CompleteImmediately = true;
        var summary = new PendingSummary();
        var page = Render(context, kind, summary);
        summary.PauseNext();
        var saving = page.Find("[data-discovery-save]").ClickAsync(new());
        await page.InvokeAsync(async () => await page.Instance.DisposeAsync());
        var reportsAfterDisposal = summary.Reports.Count;

        await page.InvokeAsync(summary.Resume);
        await saving;

        Assert.Empty(client.Writes);
        Assert.Equal(reportsAfterDisposal, summary.Reports.Count);
        Assert.Equal(0, summary.Reports[^1].PendingOperationCount);
    }

    [Theory]
    [InlineData(DiscoveryMetadataTargetKind.Resource)]
    [InlineData(DiscoveryMetadataTargetKind.Service)]
    public async Task NavigationDuringSummaryDoesNotDispatchTheOldReload(DiscoveryMetadataTargetKind kind)
    {
        using var context = new BunitContext();
        var client = RegisterClient(context);
        var summary = new PendingSummary();
        var page = Render(context, kind, summary);
        summary.PauseNext();
        var reloading = page.Find("[data-discovery-reload]").ClickAsync(new());
        page.Render(p => p.Add(c => c.Address, Address(kind, 2)));

        await page.InvokeAsync(summary.Resume);
        await reloading;

        Assert.Equal([Address(kind, 1), Address(kind, 2)], client.Reads);
        Assert.Equal("Resource 2", Title(page));
        Assert.Equal(0, summary.Reports[^1].PendingOperationCount);
    }

    [Theory]
    [InlineData(DiscoveryMetadataTargetKind.Resource)]
    [InlineData(DiscoveryMetadataTargetKind.Service)]
    public async Task DisposalDuringSummaryDoesNotDispatchAReload(DiscoveryMetadataTargetKind kind)
    {
        using var context = new BunitContext();
        var client = RegisterClient(context);
        var summary = new PendingSummary();
        var page = Render(context, kind, summary);
        summary.PauseNext();
        var reloading = page.Find("[data-discovery-reload]").ClickAsync(new());
        await page.InvokeAsync(async () => await page.Instance.DisposeAsync());
        var reportsAfterDisposal = summary.Reports.Count;

        await page.InvokeAsync(summary.Resume);
        await reloading;

        Assert.Equal([Address(kind, 1)], client.Reads);
        Assert.Equal(reportsAfterDisposal, summary.Reports.Count);
    }

    [Theory]
    [InlineData(DiscoveryMetadataTargetKind.Resource)]
    [InlineData(DiscoveryMetadataTargetKind.Service)]
    public async Task DisposalDuringTransportLeavesTheClearedSummaryAlone(DiscoveryMetadataTargetKind kind)
    {
        using var context = new BunitContext();
        var client = RegisterClient(context);
        var summary = new PendingSummary();
        var page = Render(context, kind, summary);
        var saving = page.Find("[data-discovery-save]").ClickAsync(new());
        await page.InvokeAsync(async () => await page.Instance.DisposeAsync());
        var reportsAfterDisposal = summary.Reports.Count;

        await CompleteSave(page, client, 0);
        await saving;

        Assert.Equal(reportsAfterDisposal, summary.Reports.Count);
        Assert.Equal(0, summary.Reports[^1].PendingOperationCount);
    }

    private static IRenderedComponent<DiscoveryMetadataEditor> Render(
        BunitContext context, DiscoveryMetadataTargetKind kind, PendingSummary? summary = null) =>
        context.Render<DiscoveryMetadataEditor>(p => p
            .Add(c => c.Address, Address(kind, 1))
            .Add(c => c.UnsavedChanged, summary is null ? _ => Task.CompletedTask : summary.ReportAsync));

    private static DelayedMetadataClient RegisterClient(BunitContext context)
    {
        var client = new DelayedMetadataClient();
        context.Services.AddSingleton<IHonuaDiscoveryMetadataClient>(client);
        return client;
    }

    private static string? Title(IRenderedComponent<DiscoveryMetadataEditor> page) =>
        page.Find("[data-discovery-title]").GetAttribute("value");

    private static bool IsSaving(IRenderedComponent<DiscoveryMetadataEditor> page) =>
        page.Find("fieldset").HasAttribute("disabled");

    private static DiscoveryMetadataAddress Address(DiscoveryMetadataTargetKind kind, int id) =>
        kind == DiscoveryMetadataTargetKind.Resource
            ? DiscoveryMetadataAddress.ForLayer(id)
            : DiscoveryMetadataAddress.ForService($"service-{id}");

    private static Task CompleteSave(IRenderedComponent<DiscoveryMetadataEditor> page, DelayedMetadataClient client, int index) =>
        page.InvokeAsync(() => client.Complete(index));

    private sealed class PendingSummary
    {
        private TaskCompletionSource? _pending;
        private TaskCompletionSource? _paused;
        public List<ConsoleAgentUnsavedSummary> Reports { get; } = [];

        public void PauseNext() => _pending = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task ReportAsync(ConsoleAgentUnsavedSummary summary)
        {
            Reports.Add(summary);
            if (_pending is not { } pending) return Task.CompletedTask;
            _pending = null;
            _paused = pending;
            return pending.Task;
        }

        public void Resume() => _paused!.SetResult();
    }

    private sealed class DelayedMetadataClient : IHonuaDiscoveryMetadataClient
    {
        public bool CompleteImmediately { get; set; }
        public List<DiscoveryMetadataAddress> Reads { get; } = [];
        public List<(DiscoveryMetadataAddress Address, DiscoveryMetadataUpdateRequest Request)> Writes { get; } = [];
        private readonly List<TaskCompletionSource<HonuaAdminEndpointResult<DiscoveryMetadataResponse>>> _saves = [];

        public Task<HonuaAdminEndpointResult<DiscoveryMetadataResponse>> ReadDiscoveryAsync(
            DiscoveryMetadataAddress address, CancellationToken cancellationToken = default)
        {
            Reads.Add(address);
            var id = address == Address(address.Kind, 1) ? 1 : 2;
            return Task.FromResult(HonuaAdminEndpointResult<DiscoveryMetadataResponse>.FromData(new()
            {
                EntityId = $"resource-{id}",
                Etag = $"etag-{id}",
                Title = $"Resource {id}"
            }));
        }

        public Task<HonuaAdminEndpointResult<DiscoveryMetadataResponse>> UpdateDiscoveryAsync(
            DiscoveryMetadataAddress address, DiscoveryMetadataUpdateRequest request, CancellationToken cancellationToken = default)
        {
            Writes.Add((address, request));
            var completion = new TaskCompletionSource<HonuaAdminEndpointResult<DiscoveryMetadataResponse>>(TaskCreationOptions.RunContinuationsAsynchronously);
            _saves.Add(completion);
            if (CompleteImmediately) Complete(Writes.Count - 1);
            return completion.Task;
        }

        public void Complete(int index)
        {
            var write = Writes[index];
            _saves[index].SetResult(HonuaAdminEndpointResult<DiscoveryMetadataResponse>.FromData(new()
            {
                EntityId = "stored",
                Etag = "stored-etag",
                Title = write.Request.Title
            }));
        }
    }
}
