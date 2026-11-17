using Bunit;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Pages;
using Honua.Console.Shell.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

namespace Honua.Console.IntegrationTests;

public sealed partial class StudioAutomationEditorPageTests
{
    [Theory]
    [InlineData(ConsolePresentationMode.Legacy)]
    [InlineData(ConsolePresentationMode.V4)]
    public async Task DisposedEditor_SaveCompletionCannotNavigateBack(ConsolePresentationMode presentation)
    {
        var completion = new TaskCompletionSource<CollectAutomationSaveResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var client = new RecordingAutomationClient { PendingSave = completion.Task };
        using var ctx = EditorContext(client, presentation);
        var navigation = ctx.Services.GetRequiredService<NavigationManager>();
        var page = ctx.Render<StudioAutomationEditorPage>();
        var saving = SaveButton(page).ClickAsync(new());
        page.WaitForAssertion(() => Assert.Equal(1, client.SaveCalls));
        await ctx.DisposeComponentsAsync();
        navigation.NavigateTo("/activity");
        var destination = navigation.Uri;

        completion.SetResult(new() { ContentItemId = "saved-automation", VersionId = "v1" });
        await saving;

        Assert.Equal(destination, navigation.Uri);
        Assert.Equal(1, client.SaveCalls);
    }
}
