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
    public async Task DisposedWorkspaceDoesNotStartPublicationLookup(ConsolePresentationMode presentation)
    {
        using var context = CreateContext(presentation == ConsolePresentationMode.V4);
        var source = new PublishingSource { Workspace = new() };
        context.Services.AddSingleton<IPublishingWorkspaceDataSource>(source);
        context.Services.GetRequiredService<NavigationManager>().NavigateTo("/operate/publishing?publicationId=second");
        context.Render<OperatePublishingPage>();

        await context.DisposeComponentsAsync();
        await context.Renderer.Dispatcher.InvokeAsync(() =>
            source.Workspace.SetResult(new PublishingWorkspace([], [], [])));

        Assert.Empty(source.Lookups);
        Assert.True(source.WorkspaceToken.IsCancellationRequested);
    }

    [Theory]
    [InlineData(ConsolePresentationMode.Legacy)]
    [InlineData(ConsolePresentationMode.V4)]
    public async Task DisposedPublicationLookupCancelsRead(ConsolePresentationMode presentation)
    {
        using var context = CreateContext(presentation == ConsolePresentationMode.V4);
        var source = new PublishingSource();
        context.Services.AddSingleton<IPublishingWorkspaceDataSource>(source);
        context.Services.GetRequiredService<NavigationManager>().NavigateTo("/operate/publishing?publicationId=first");
        context.Render<OperatePublishingPage>();
        Assert.Equal(new[] { "first" }, source.Lookups);

        await context.DisposeComponentsAsync();
        await context.Renderer.Dispatcher.InvokeAsync(() => source.FirstLookup.SetResult(Result("stale")));

        Assert.True(source.LookupToken.IsCancellationRequested);
    }

}
