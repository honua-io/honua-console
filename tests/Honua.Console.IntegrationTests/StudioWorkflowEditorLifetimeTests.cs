using Bunit;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Pages;
using Honua.Console.Shell.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

namespace Honua.Console.IntegrationTests;

public sealed partial class StudioWorkflowEditorPageTests
{
    [Theory]
    [InlineData(ConsolePresentationMode.Legacy, "Save draft")]
    [InlineData(ConsolePresentationMode.V4, "Save draft")]
    [InlineData(ConsolePresentationMode.Legacy, "Run once")]
    [InlineData(ConsolePresentationMode.V4, "Run once")]
    [InlineData(ConsolePresentationMode.Legacy, "Publish")]
    [InlineData(ConsolePresentationMode.V4, "Publish")]
    public async Task DisposedEditor_SaveCompletionCannotNavigateOrDispatchDependentCommand(
        ConsolePresentationMode presentation, string command)
    {
        var completion = new TaskCompletionSource<StudioWorkflowSaveResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        StudioWorkflowPackageDraft? savingDraft = null;
        var client = new RecordingWorkflowPackageClient
        {
            SaveHandler = draft =>
            {
                savingDraft = draft;
                return completion.Task;
            }
        };
        using var ctx = EditorContext(client, presentation);
        var navigation = ctx.Services.GetRequiredService<NavigationManager>();
        var page = ctx.Render<StudioWorkflowEditorPage>(p => p.Add(x => x.DraftId, "earlier"));
        TitleInput(page).Input("Unsaved changes before leaving");
        var running = FindButton(page, command).ClickAsync(new());
        page.WaitForAssertion(() => Assert.Equal(1, client.SaveCalls));
        await ctx.DisposeComponentsAsync();
        navigation.NavigateTo("/activity");
        var destination = navigation.Uri;

        Assert.NotNull(savingDraft);
        savingDraft.ContentItemId = "saved-workflow";
        completion.SetResult(new() { ContentItemId = "saved-workflow", VersionId = "v2" });
        await running;

        Assert.Equal(destination, navigation.Uri);
        Assert.Equal(1, client.SaveCalls);
        Assert.Equal(0, client.DryRunCalls);
        Assert.Equal(0, client.PublishCalls);
    }
}
