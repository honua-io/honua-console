using Bunit;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Pages;
using Honua.Console.Shell.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

namespace Honua.Console.IntegrationTests;

public sealed partial class PublishingNavigationTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ChangedQuery_DisplaysLatestReleaseDespiteEarlierPendingLookup(bool enabled)
    {
        using var ctx = CreateContext(enabled);
        var source = new PublishingSource();
        ctx.Services.AddSingleton<IPublishingWorkspaceDataSource>(source);
        var navigation = ctx.Services.GetRequiredService<NavigationManager>();
        navigation.NavigateTo("/operate/publishing?publicationId=first");
        var page = ctx.Render<OperatePublishingPage>();
        await page.InvokeAsync(() => navigation.NavigateTo("/operate/publishing?publicationId=second"));
        page.WaitForAssertion(() => Assert.Contains("Release second", page.Markup));

        var renderCount = page.RenderCount;
        await page.InvokeAsync(() => source.FirstLookup.SetResult(Result("stale")));
        page.WaitForState(() => page.RenderCount > renderCount);

        page.WaitForAssertion(() => Assert.DoesNotContain("Release stale", page.Markup));
        Assert.Contains("Release second", page.Markup);
        Assert.Equal(new[] { "first", "second" }, source.Lookups);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ClearedQuery_RemovesReviewAndRejectsPendingResponse(bool enabled)
    {
        using var ctx = CreateContext(enabled);
        var source = new PublishingSource();
        ctx.Services.AddSingleton<IPublishingWorkspaceDataSource>(source);
        var navigation = ctx.Services.GetRequiredService<NavigationManager>();
        navigation.NavigateTo("/operate/publishing?publicationId=first");
        var page = ctx.Render<OperatePublishingPage>();
        await page.InvokeAsync(() => navigation.NavigateTo("/operate/publishing"));

        var renderCount = page.RenderCount;
        await page.InvokeAsync(() => source.FirstLookup.SetResult(Result("stale")));
        page.WaitForState(() => page.RenderCount > renderCount);

        page.WaitForAssertion(() => Assert.Empty(page.FindAll("input[aria-label='Republish title']")));
        Assert.DoesNotContain("Release stale", page.Markup);
        Assert.Equal(string.Empty, page.Find("[data-publication-lookup] input.console-input").GetAttribute("value"));
        Assert.False(page.Find("[data-publication-lookup] input.console-input").HasAttribute("disabled"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ChangedQuery_ClosesObsoleteConfirmationAndClearsTitle(bool enabled)
    {
        using var ctx = CreateContext(enabled);
        var source = new PublishingSource();
        ctx.Services.AddSingleton<IPublishingWorkspaceDataSource>(source);
        var navigation = ctx.Services.GetRequiredService<NavigationManager>();
        navigation.NavigateTo("/operate/publishing?publicationId=second");
        var page = ctx.Render<OperatePublishingPage>();
        page.Find("input[aria-label='Republish title']").Input("Edited title");
        page.Find("[data-publication-lookup] button.console-button-secondary").Click();
        Assert.Single(page.FindAll("[data-console-confirm-accept]"));

        await page.InvokeAsync(() => navigation.NavigateTo("/operate/publishing?publicationId=third"));

        page.WaitForAssertion(() => Assert.Contains("Release third", page.Markup));
        Assert.Empty(page.FindAll("[data-console-confirm-accept]"));
        Assert.Equal(string.Empty, page.Find("input[aria-label='Republish title']").GetAttribute("value"));
        Assert.Empty(source.Writes);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task EarlierRepublishCompletion_DoesNotReplaceNewQueryReview(bool enabled)
    {
        using var ctx = CreateContext(enabled);
        var source = new PublishingSource();
        ctx.Services.AddSingleton<IPublishingWorkspaceDataSource>(source);
        var navigation = ctx.Services.GetRequiredService<NavigationManager>();
        navigation.NavigateTo("/operate/publishing?publicationId=second");
        var page = ctx.Render<OperatePublishingPage>();
        page.FindAll("[data-publication-lookup] button.console-button")
            .Single(b => b.TextContent.Trim() == "Republish").Click();
        page.Find("[data-console-confirm-accept]").Click();
        Assert.Equal(new[] { "second" }, source.Writes);
        await page.InvokeAsync(() => navigation.NavigateTo("/operate/publishing?publicationId=third"));
        page.WaitForAssertion(() => Assert.Contains("Release third", page.Markup));

        await page.InvokeAsync(() => source.Republish.SetResult(Result("old-write")));

        page.WaitForAssertion(() => Assert.False(page.Find("input[aria-label='Republish title']").HasAttribute("disabled")));
        Assert.Contains("Release third", page.Markup);
        Assert.DoesNotContain("Release old-write", page.Markup);
    }

    private static BunitContext CreateContext(bool enabled)
    {
        var ctx = new BunitContext();
        ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        ctx.AddConsoleNotifications();
        ctx.Services.AddSingleton(new ConsoleV4Options { Enabled = enabled });
        ctx.Services.AddSingleton<ConsoleAgentContextRegistry>();
        ctx.Services.AddSingleton<IServiceLayerPublishOperation>(new UnsupportedServiceLayerPublishOperation());
        ctx.Services.AddSingleton<IOperateTransitionDataSource>(new UnsupportedOperateTransitionDataSource());
        return ctx;
    }

    private static PublishingLookupResult Result(string id) => new(
        new PublishingReview(id, $"Release {id}", PublishingResourceKind.StudioArtifact, "public",
            [new PublishingEndpoint("Published map", "/published/" + id)],
            new PublishingCatalogRegistration(true, id, "public"), "visibility: public", [], "reversible", "operator",
            new PublishingReviewLinks(null, null, null, null)),
        [new PublishingVersion("ver-2", 2, "Current", null, true, "operator", DateTimeOffset.UnixEpoch),
         new PublishingVersion("ver-1", 1, "Previous", null, false, "operator", DateTimeOffset.UnixEpoch)], []);

    private sealed class PublishingSource : IPublishingWorkspaceDataSource
    {
        public TaskCompletionSource<PublishingLookupResult> FirstLookup { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<PublishingWorkspace>? Workspace { get; init; }
        public CancellationToken WorkspaceToken { get; private set; }
        public CancellationToken LookupToken { get; private set; }
        public TaskCompletionSource<PublishingLookupResult> Republish { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Dictionary<string, TaskCompletionSource<PublishingLookupResult>> PendingRepublishes { get; } = [];
        public List<string> Lookups { get; } = [];
        public List<string> Writes { get; } = [];
        public Task<PublishingWorkspace> GetWorkspaceAsync(CancellationToken cancellationToken = default) =>
            GetWorkspace(cancellationToken);
        private Task<PublishingWorkspace> GetWorkspace(CancellationToken token)
        {
            WorkspaceToken = token;
            return Workspace?.Task ?? Task.FromResult(new PublishingWorkspace([], [], []));
        }
        public Task<PublishingLookupResult> LookupAsync(string publicationId, CancellationToken cancellationToken = default)
        {
            LookupToken = cancellationToken;
            Lookups.Add(publicationId);
            return publicationId == "first" ? FirstLookup.Task : Task.FromResult(Result(publicationId));
        }
        public Task<PublishingLookupResult> RepublishAsync(string publicationId, PublishingRepublishCommand command, CancellationToken cancellationToken = default)
        {
            Writes.Add(publicationId);
            return PendingRepublishes.GetValueOrDefault(publicationId, Republish).Task;
        }
        public Task<PublishingLookupResult> RollbackAsync(string publicationId, PublishingRollbackCommand command, CancellationToken cancellationToken = default)
        {
            Writes.Add(publicationId);
            return Task.FromResult(Result(publicationId));
        }
    }
}
