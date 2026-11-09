using Bunit;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Pages;
using Honua.Console.Shell.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

namespace Honua.Console.IntegrationTests;

public sealed class OperateEventsPageRenderTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task JobQueryPreservesOrderedRunEvidence(bool enabled)
    {
        using var context = CreateContext(enabled);
        var client = InMemoryStudioWorkflowPackageClient.CreateSeeded();
        var draft = await client.GetDraftAsync(InMemoryStudioWorkflowPackageClient.SeedDraftId);
        var run = await client.DryRunAsync(draft!);
        var evidence = await client.GetJobEvidenceAsync(run.JobId!);
        context.Services.AddSingleton<IStudioWorkflowPackageClient>(client);
        context.Services.GetRequiredService<NavigationManager>().NavigateTo($"/operate/events?jobId={run.JobId}");

        var page = context.Render<OperateEventsPage>();

        AssertPresentation(page, enabled);
        Assert.Equal(run.JobId, page.Find("h2").TextContent);
        Assert.Equal(run.Status, page.Find(".console-status").TextContent);
        Assert.Equal(run.Logs, page.FindAll(".console-event-list strong").Select(node => node.TextContent));
        Assert.All(page.FindAll(".console-event-list small"), node => Assert.Equal(evidence!.VersionId, node.TextContent));
        Assert.All(page.FindAll(".console-event-list span"), node => Assert.Equal(evidence!.CreatedAt.ToString("HH:mm:ss"), node.TextContent));
        Assert.Empty(page.FindAll("[data-events-dead-end]"));
        if (enabled) Capture("events-run", page.Markup);
    }

    [Theory]
    [InlineData(false, "/operate/events")]
    [InlineData(true, "/operate/events")]
    [InlineData(false, "/operate/events?jobId=missing-job")]
    [InlineData(true, "/operate/events?jobId=missing-job")]
    public void MissingRunPreservesDiagnosticsAndJobsExit(bool enabled, string path)
    {
        using var context = CreateContext(enabled);
        context.Services.AddSingleton<IStudioWorkflowPackageClient>(InMemoryStudioWorkflowPackageClient.CreateSeeded());
        context.Services.GetRequiredService<NavigationManager>().NavigateTo(path);

        var page = context.Render<OperateEventsPage>();

        AssertPresentation(page, enabled);
        Assert.Single(page.FindAll("[data-events-dead-end]"));
        Assert.Equal("/operate/observability", page.Find("[data-events-exit-jobs]").GetAttribute("href"));
        Assert.Empty(page.FindAll(".console-event-list"));
        Assert.Contains("GET /api/v1/admin/jobs/{jobId}", page.Markup, StringComparison.Ordinal);
        if (enabled) Capture("events-missing", page.Markup);
    }

    private static BunitContext CreateContext(bool enabled)
    {
        var context = new BunitContext();
        context.Services.AddSingleton(new ConsoleV4Options { Enabled = enabled });
        return context;
    }

    private static void AssertPresentation(IRenderedComponent<OperateEventsPage> page, bool enabled)
    {
        Assert.Equal(enabled ? "Workflow events" : "Workflow Event Evidence", Assert.Single(page.FindAll("h1")).TextContent);
        Assert.Equal(enabled ? 1 : 0, page.FindAll(".console-page-heading-v4").Count);
    }

    private static void Capture(string name, string markup) =>
        CatalogAdminPreview.Capture(name, $"<div class=\"console-shell\" data-console-v4=\"true\">{markup}</div>");
}
