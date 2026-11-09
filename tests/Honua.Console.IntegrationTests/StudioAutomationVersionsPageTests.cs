using Bunit;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Pages;
using Honua.Console.Shell.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Honua.Console.IntegrationTests;

public sealed class StudioAutomationVersionsPageTests
{
    [Theory]
    [InlineData(ConsolePresentationMode.Legacy)]
    [InlineData(ConsolePresentationMode.V4)]
    public void UnboundRuntime_RendersMissingBindingState(ConsolePresentationMode presentation)
    {
        using var ctx = new BunitContext();
        ctx.Services.AddSingleton(new ConsoleV4Options { Enabled = presentation == ConsolePresentationMode.V4 });
        ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        ctx.Services.AddSingleton<ICollectAutomationClient>(new UnsupportedCollectAutomationClient());

        var page = ctx.Render<StudioAutomationVersionsPage>(
            parameters => parameters.Add(p => p.DraftId, InMemoryCollectAutomationClient.SeedDraftId));

        page.WaitForAssertion(
            () => Assert.NotEmpty(page.FindAll("section.console-state-error")),
            TimeSpan.FromSeconds(5));
        Assert.Contains("Automation versioning is not bound", page.Markup, StringComparison.Ordinal);
        Assert.Equal(presentation == ConsolePresentationMode.V4, page.FindAll(".console-page-heading-v4").Count == 1);
        if (presentation == ConsolePresentationMode.V4)
        {
            Assert.Single(page.FindAll("h1"));
            CatalogAdminPreview.Capture("automation-versions-unbound", $"<div class=\"console-shell\" data-console-v4=\"true\">{page.Markup}</div>");
        }
    }

    [Theory]
    [InlineData(ConsolePresentationMode.Legacy)]
    [InlineData(ConsolePresentationMode.V4)]
    public void BoundRuntime_ListsAppendOnlyHistoryWithCurrentMarker(ConsolePresentationMode presentation)
    {
        using var ctx = new BunitContext();
        ctx.Services.AddSingleton(new ConsoleV4Options { Enabled = presentation == ConsolePresentationMode.V4 });
        ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        ctx.Services.AddSingleton<ICollectAutomationClient>(InMemoryCollectAutomationClient.CreateSeeded());

        var page = ctx.Render<StudioAutomationVersionsPage>(
            parameters => parameters.Add(p => p.DraftId, InMemoryCollectAutomationClient.SeedDraftId));

        page.WaitForAssertion(
            () => Assert.NotEmpty(page.FindAll("li.automation-version-item")),
            TimeSpan.FromSeconds(5));
        Assert.NotEmpty(page.FindAll("li.automation-version-current"));
        Assert.Contains("Initial version.", page.Markup, StringComparison.Ordinal);
        Assert.Equal("v1", Assert.Single(page.FindAll(".automation-version-item h2")).TextContent);
        Assert.Equal(presentation == ConsolePresentationMode.V4, page.FindAll(".console-page-heading-v4").Count == 1);
        if (presentation == ConsolePresentationMode.V4)
        {
            Assert.Single(page.FindAll("h1"));
            CatalogAdminPreview.Capture("automation-versions-seeded", $"<div class=\"console-shell\" data-console-v4=\"true\">{page.Markup}</div>");
        }
    }

    [Theory]
    [InlineData(ConsolePresentationMode.Legacy)]
    [InlineData(ConsolePresentationMode.V4)]
    public void ViewBody_TogglesTheVersionPreview(ConsolePresentationMode presentation)
    {
        using var ctx = new BunitContext();
        ctx.Services.AddSingleton(new ConsoleV4Options { Enabled = presentation == ConsolePresentationMode.V4 });
        ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        ctx.Services.AddSingleton<ICollectAutomationClient>(InMemoryCollectAutomationClient.CreateSeeded());

        var page = ctx.Render<StudioAutomationVersionsPage>(
            parameters => parameters.Add(p => p.DraftId, InMemoryCollectAutomationClient.SeedDraftId));

        page.WaitForAssertion(
            () => Assert.NotEmpty(page.FindAll("li.automation-version-item")),
            TimeSpan.FromSeconds(5));

        page.FindAll("button").First(b => b.TextContent.Contains("View body", StringComparison.Ordinal)).Click();

        page.WaitForAssertion(
            () => Assert.NotEmpty(page.FindAll(".automation-version-preview")),
            TimeSpan.FromSeconds(5));

        // Toggling the same version again collapses the preview. Re-find the button: the prior click
        // re-rendered the tree, invalidating the earlier element's event-handler id.
        page.FindAll("button").First(b => b.TextContent.Contains("View body", StringComparison.Ordinal)).Click();
        page.WaitForAssertion(
            () => Assert.Empty(page.FindAll(".automation-version-preview")),
            TimeSpan.FromSeconds(5));
    }

    [Theory]
    [InlineData(ConsolePresentationMode.Legacy)]
    [InlineData(ConsolePresentationMode.V4)]
    public async Task ConfirmedRestore_CommitsANewVersionAndGrowsHistory(ConsolePresentationMode presentation)
    {
        var client = InMemoryCollectAutomationClient.CreateSeeded();

        // Save a second version so the seeded v1 becomes a restorable (non-current) prior version.
        var draft = (await client.OpenEditorAsync(InMemoryCollectAutomationClient.SeedDraftId)).Draft!;
        draft.Description = "edited body for v2";
        await client.SaveVersionAsync(draft, "v2");

        using var ctx = new BunitContext();
        ctx.Services.AddSingleton(new ConsoleV4Options { Enabled = presentation == ConsolePresentationMode.V4 });
        ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        ctx.JSInterop.Setup<bool>("confirm", _ => true).SetResult(true);
        ctx.Services.AddSingleton<ICollectAutomationClient>(client);

        var page = ctx.Render<StudioAutomationVersionsPage>(
            parameters => parameters.Add(p => p.DraftId, InMemoryCollectAutomationClient.SeedDraftId));

        page.WaitForAssertion(
            () => Assert.Equal(2, page.FindAll("li.automation-version-item").Count),
            TimeSpan.FromSeconds(5));

        // Restore the older (non-current) version: a Restore button only renders on non-current rows.
        var restoreButton = page.FindAll("button").First(b => b.TextContent.Trim() == "Restore");
        restoreButton.Click();

        page.WaitForAssertion(
            () =>
            {
                Assert.Contains("Restored v", page.Markup, StringComparison.Ordinal);
                // Append-only: a third immutable version now exists.
                Assert.Equal(3, page.FindAll("li.automation-version-item").Count);
            },
            TimeSpan.FromSeconds(5));
    }
}
