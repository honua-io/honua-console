using Bunit;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Pages;
using Honua.Console.Shell.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Honua.Console.IntegrationTests;

/// <summary>
/// Docker-free render regression for the Collect automation versions page
/// (<see cref="StudioAutomationVersionsPage"/>, honua-console#219). Covers the missing-binding surface, the
/// append-only history listing, version-body preview toggling, and a confirmed restore that commits a NEW
/// version (history grows; the prior version is never rewritten).
/// </summary>
public sealed class StudioAutomationVersionsPageTests
{
    [Fact]
    public void UnboundRuntime_RendersMissingBindingState()
    {
        using var ctx = new BunitContext();
        ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        ctx.Services.AddSingleton<ICollectAutomationClient>(new UnsupportedCollectAutomationClient());

        var page = ctx.Render<StudioAutomationVersionsPage>(
            parameters => parameters.Add(p => p.DraftId, InMemoryCollectAutomationClient.SeedDraftId));

        page.WaitForAssertion(
            () => Assert.NotEmpty(page.FindAll("section.console-state-error")),
            TimeSpan.FromSeconds(5));
        Assert.Contains("Automation versioning is not bound", page.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void BoundRuntime_ListsAppendOnlyHistoryWithCurrentMarker()
    {
        using var ctx = new BunitContext();
        ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        ctx.Services.AddSingleton<ICollectAutomationClient>(InMemoryCollectAutomationClient.CreateSeeded());

        var page = ctx.Render<StudioAutomationVersionsPage>(
            parameters => parameters.Add(p => p.DraftId, InMemoryCollectAutomationClient.SeedDraftId));

        page.WaitForAssertion(
            () => Assert.NotEmpty(page.FindAll("li.automation-version-item")),
            TimeSpan.FromSeconds(5));
        Assert.NotEmpty(page.FindAll("li.automation-version-current"));
        Assert.Contains("Initial version.", page.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void ViewBody_TogglesTheVersionPreview()
    {
        using var ctx = new BunitContext();
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

    [Fact]
    public async Task ConfirmedRestore_CommitsANewVersionAndGrowsHistory()
    {
        var client = InMemoryCollectAutomationClient.CreateSeeded();

        // Save a second version so the seeded v1 becomes a restorable (non-current) prior version.
        var draft = (await client.OpenEditorAsync(InMemoryCollectAutomationClient.SeedDraftId)).Draft!;
        draft.Description = "edited body for v2";
        await client.SaveVersionAsync(draft, "v2");

        using var ctx = new BunitContext();
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
