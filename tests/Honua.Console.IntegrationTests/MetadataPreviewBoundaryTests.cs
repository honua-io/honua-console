using Bunit;
using Honua.Console.Shell.DependencyInjection;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Pages;
using Honua.Console.Shell.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Components;

namespace Honua.Console.IntegrationTests;

public sealed class MetadataPreviewBoundaryTests
{
    [Theory]
    [InlineData(null, "missing")]
    [InlineData("https://server.example.test/", "unsupported")]
    public void ProductionBinding_NeverSeedsFixtures(string? serverUrl, string state)
    {
        using var context = new BunitContext();
        context.Services.AddHonuaConsoleShell(new HonuaConsoleShellOptions { CatalogAdminEnabled = true, ServerBaseUrl = serverUrl });
        var page = context.Render<MetadataPreviewPage>(p => p.Add(view => view.DatasetId, "flood"));
        Assert.Single(page.FindAll(".console-state-" + state));
        Assert.DoesNotContain("Fixture preview", page.Markup);
        Assert.Empty(page.FindAll("textarea, input, button"));
    }

    [Theory]
    [InlineData("unsupported", "unsupported")]
    [InlineData("missing", "missing")]
    [InlineData("forbidden", "denied")]
    [InlineData("unavailable", "denied")]
    [InlineData("ready", "unsupported")]
    [InlineData("future-state", "unsupported")]
    public void NonFixtureState_CannotExposeOrEditSuppliedValues(string state, string surface)
    {
        var source = new MetadataFixtureSource { Read = MetadataFixtureSource.Example() with { State = state } };
        using var context = MetadataFixtureSource.Context(source);
        var page = context.Render<MetadataPreviewPage>(p => p.Add(view => view.DatasetId, "flood"));
        Assert.Single(page.FindAll(".console-state-" + surface));
        Assert.DoesNotContain("Flood hazard areas", page.Markup);
        Assert.Empty(page.FindAll("textarea, input"));
    }

    [Theory]
    [InlineData("other", null)]
    [InlineData("flood", "different-service")]
    public void WrongIdentity_DoesNotExposeDatasetOrImport(string dataset, string? service)
    {
        var source = new MetadataFixtureSource();
        using var context = MetadataFixtureSource.Context(source);
        var query = service is null ? string.Empty : "?service=" + Uri.EscapeDataString(service);
        context.Services.GetRequiredService<NavigationManager>().NavigateTo("/metadata/datasets/" + dataset + query);
        var page = context.Render<MetadataPreviewPage>(p => p.Add(view => view.DatasetId, dataset));
        Assert.Equal(service, Assert.Single(source.Requests).ServiceId);
        Assert.Contains("No matching dataset", page.Markup);
        Assert.Empty(page.FindAll("textarea, input"));
        page.Render(p => p.Add(view => view.Section, "import"));
        Assert.DoesNotContain("flood-metadata.xml", page.Markup);
        Assert.Contains("No matching dataset", page.Markup);
    }

    [Fact]
    public void ServiceContext_IsPassedThroughAndEncodedInBothLinks()
    {
        var source = new MetadataFixtureSource();
        source.Read = source.Read with { Dataset = source.Read.Dataset! with { ServiceId = "service & 1" } };
        using var context = MetadataFixtureSource.Context(source);
        context.Services.GetRequiredService<NavigationManager>().NavigateTo("/metadata/datasets/flood?service=service%20%26%201");
        var page = context.Render<MetadataPreviewPage>(p => p.Add(view => view.DatasetId, "flood"));
        Assert.Equal(new("flood", "dataset", "service & 1"), Assert.Single(source.Requests));
        Assert.Contains("service=service%20%26%201", page.Find(".metadata-preview-nav a:nth-child(2)").GetAttribute("href"));
        Assert.EndsWith("/import?service=service%20%26%201", page.Find(".metadata-preview-nav a:nth-child(3)").GetAttribute("href"));
    }

    [Fact]
    public async Task LateRead_CannotReplaceNewDataset_OrSurviveDisposal()
    {
        var source = new DeferredSource();
        using var context = MetadataFixtureSource.Context(source);
        var page = context.Render<MetadataPreviewPage>(p => p.Add(view => view.DatasetId, "old"));
        Assert.Contains("Loading metadata preview", page.Markup);
        page.Render(p => p.Add(view => view.DatasetId, "new"));
        page.WaitForAssertion(() => Assert.Equal(2, source.Completions.Count));
        Assert.True(source.Cancellations[0].IsCancellationRequested);
        source.Completions[1].SetResult(new("fixture") { Dataset = new("new", "New dataset") });
        page.WaitForAssertion(() => Assert.Contains("New dataset", page.Markup));
        var renderCount = page.RenderCount;
        source.Completions[0].SetResult(new("fixture") { Dataset = new("old", "Stale private dataset") });
        page.WaitForState(() => page.RenderCount > renderCount);
        Assert.Contains("New dataset", page.Markup);
        Assert.DoesNotContain("Stale private", page.Markup);
        page.Render(p => p.Add(view => view.DatasetId, "pending"));
        page.WaitForAssertion(() => Assert.Equal(3, source.Completions.Count));
        await context.DisposeAsync();
        Assert.True(source.Cancellations[2].IsCancellationRequested);
        source.Completions[2].SetResult(new("fixture") { Dataset = new("pending", "Disposed dataset") });
    }

    [Fact]
    public void ReadException_ShowsRecoveryWithoutLeakingExceptionText()
    {
        using var context = MetadataFixtureSource.Context(new ThrowingSource());
        var page = context.Render<MetadataPreviewPage>();
        Assert.Contains("Refresh to try again", page.Markup);
        Assert.DoesNotContain("private exception", page.Markup);
        Assert.Single(page.FindAll(".console-state-denied"));
    }

    [Fact]
    public void MetadataRoutes_RequireAuthentication()
    {
        Assert.NotEmpty(typeof(MetadataPreviewPage).GetCustomAttributes(typeof(Microsoft.AspNetCore.Authorization.AuthorizeAttribute), true));
    }

    private sealed class DeferredSource : IMetadataPreviewSource
    {
        public List<TaskCompletionSource<MetadataPreviewRead>> Completions { get; } = [];
        public List<CancellationToken> Cancellations { get; } = [];
        public Task<MetadataPreviewRead> ReadAsync(MetadataPreviewRequest request, CancellationToken cancellationToken)
        {
            var completion = new TaskCompletionSource<MetadataPreviewRead>(TaskCreationOptions.RunContinuationsAsynchronously);
            Completions.Add(completion);
            Cancellations.Add(cancellationToken);
            return completion.Task;
        }
    }

    private sealed class ThrowingSource : IMetadataPreviewSource
    {
        public Task<MetadataPreviewRead> ReadAsync(MetadataPreviewRequest request, CancellationToken cancellationToken) =>
            Task.FromException<MetadataPreviewRead>(new InvalidOperationException("private exception"));
    }
}
