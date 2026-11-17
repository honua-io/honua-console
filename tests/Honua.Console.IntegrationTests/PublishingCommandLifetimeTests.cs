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
    [InlineData(ConsolePresentationMode.Legacy)]
    [InlineData(ConsolePresentationMode.V4)]
    public async Task EarlierRepublishDoesNotLockNewPublication(ConsolePresentationMode presentation)
    {
        using var context = CreateContext(presentation == ConsolePresentationMode.V4);
        var source = new PublishingSource();
        var nextRepublish = new TaskCompletionSource<PublishingLookupResult>();
        source.PendingRepublishes.Add("third", nextRepublish);
        context.Services.AddSingleton<IPublishingWorkspaceDataSource>(source);
        var navigation = context.Services.GetRequiredService<NavigationManager>();
        navigation.NavigateTo("/operate/publishing?publicationId=second");
        var page = context.Render<OperatePublishingPage>();
        page.FindAll("[data-publication-lookup] button.console-button")
            .Single(button => button.TextContent.Trim() == "Republish").Click();
        page.Find("[data-console-confirm-accept]").Click();
        Assert.Equal(new[] { "second" }, source.Writes);

        await page.InvokeAsync(() => navigation.NavigateTo("/operate/publishing?publicationId=third"));

        page.WaitForAssertion(() => Assert.Contains("Release third", page.Markup));
        Assert.False(page.Find("input[aria-label='Republish title']").HasAttribute("disabled"));
        page.FindAll("[data-publication-lookup] button.console-button")
            .Single(button => button.TextContent.Trim() == "Republish").Click();
        page.Find("[data-console-confirm-accept]").Click();
        Assert.Equal(new[] { "second", "third" }, source.Writes);

        await page.InvokeAsync(() => source.Republish.SetResult(Result("old-write")));
        Assert.Contains("Release third", page.Markup);
        Assert.DoesNotContain("Release old-write", page.Markup);
        Assert.True(page.Find("input[aria-label='Republish title']").HasAttribute("disabled"));

        await page.InvokeAsync(() => nextRepublish.SetResult(Result("third-updated")));
        page.WaitForAssertion(() => Assert.Contains("Release third-updated", page.Markup));
        Assert.False(page.Find("input[aria-label='Republish title']").HasAttribute("disabled"));
    }
}
