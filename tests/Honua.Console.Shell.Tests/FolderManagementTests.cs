using Bunit;
using Honua.Console.Shell.Components.Operate;
using Honua.Console.Shell.Services;
using Honua.Console.Shell.Models;
using Honua.Sdk.Admin.Models;
using Microsoft.Extensions.DependencyInjection;

namespace Honua.Console.Shell.Tests;

public sealed class FolderManagementTests
{
    [Fact]
    public void AgentSummaryTracksSelectionEditsAndSaveWithoutFormValues()
    {
        using var context = new BunitContext();
        context.Services.AddSingleton<IConsoleFolderClient>(new FolderClient());
        var summaries = new List<ConsoleAgentUnsavedSummary>();
        ConsoleAgentEntity? selected = null;
        var panel = context.Render<FolderManagement>(p => p
            .Add(c => c.UnsavedChanged, summary => summaries.Add(summary))
            .Add(c => c.SelectionChanged, entity => selected = entity));
        Assert.Equal(0, summaries[^1].ChangedFieldCount);
        Assert.Contains(summaries, summary => summary.PendingOperationCount == 1);
        panel.Find("[data-folder-select]").Change("transport");
        Assert.Equal(ConsoleAgentEntityKind.Folder, selected!.Kind);
        Assert.Equal("transport", selected.Id);
        Assert.Equal("/operate/folders/transport", ConsoleAgentRoutes.ForEntity(selected));
        panel.Find("[data-folder-title]").Change("Private draft title");
        Assert.Equal(1, summaries[^1].ChangedFieldCount);
        Assert.DoesNotContain("Private draft title", System.Text.Json.JsonSerializer.Serialize(summaries));
        panel.Find("[data-folder-save]").Click();
        Assert.Equal(0, summaries[^1].ChangedFieldCount);
        Assert.Equal(0, summaries[^1].PendingOperationCount);
        Assert.Null(selected);
    }

    [Fact]
    public void EmptyFolderCanBeRenamedAndItsDisplayTitleCleared()
    {
        using var context = new BunitContext();
        var client = new FolderClient();
        context.Services.AddSingleton<IConsoleFolderClient>(client);
        var panel = context.Render<FolderManagement>();
        Assert.Contains("Root", panel.Markup);
        panel.Find("[data-folder-select]").Change("transport");
        Assert.Equal("Transport", panel.Find("[data-folder-name]").GetAttribute("value"));
        panel.Find("[data-folder-name]").Change("Roads");
        panel.Find("[data-folder-title]").Change("");
        panel.Find("[data-folder-save]").Click();
        Assert.Equal("Roads", client.Updated!.Name);
        Assert.True(client.Updated.Title.IsSpecified);
        Assert.Null(client.Updated.Title.Value);
        Assert.Equal("\"catalog-1\"", client.MutatedCatalog!.ETag);
        Assert.Contains("Saved", panel.Markup);
    }

    [Fact]
    public void StaleSnapshotRetainsEditsAndRequiresAnExplicitRefreshBeforeAnotherAction()
    {
        using var context = new BunitContext();
        var client = new FolderClient { Result = new() { State = "Snapshot changed", Detail = "The catalog changed. Review it again." } };
        context.Services.AddSingleton<IConsoleFolderClient>(client);
        var panel = context.Render<FolderManagement>();
        panel.Find("[data-folder-name]").Change("New-path");
        panel.Find("[data-folder-title]").Change("My edits");
        panel.Find("[data-folder-save]").Click();
        Assert.Equal(1, client.Mutations);
        Assert.Contains("The catalog changed", panel.Markup);
        Assert.True(panel.Find("[data-folder-save]").HasAttribute("disabled"));
        Assert.Equal("My edits", panel.Find("[data-folder-title]").GetAttribute("value"));
        client.ETag = "\"catalog-2\"";
        panel.Find("[data-folder-refresh]").Click();
        Assert.Equal(1, client.Mutations);
        Assert.Equal("New-path", panel.Find("[data-folder-name]").GetAttribute("value"));
        Assert.False(panel.Find("[data-folder-save]").HasAttribute("disabled"));
        panel.Find("[data-folder-save]").Click();
        Assert.Equal("\"catalog-2\"", client.MutatedCatalog!.ETag);
        Assert.Equal(2, client.Mutations);
    }

    [Fact]
    public void DeleteRequiresSelectionAndDeliberateConfirmation()
    {
        using var context = new BunitContext();
        var client = new FolderClient();
        context.Services.AddSingleton<IConsoleFolderClient>(client);
        var panel = context.Render<FolderManagement>();
        Assert.Empty(panel.FindAll("[data-folder-delete]"));
        panel.Find("[data-folder-select]").Change("transport");
        Assert.True(panel.Find("[data-folder-delete]").HasAttribute("disabled"));
        panel.Find("[data-folder-delete-confirm]").Change(true);
        panel.Find("[data-folder-delete]").Click();
        Assert.Equal("transport", client.Deleted);
        Assert.Equal(1, client.Mutations);
    }

    [Fact]
    public void MoveUsesCurrentMembershipAndExplicitRootWithoutChangingServiceIdentity()
    {
        using var context = new BunitContext();
        var client = new FolderClient();
        context.Services.AddSingleton<IConsoleFolderClient>(client);
        var panel = context.Render<FolderManagement>(parameters => parameters
            .Add(component => component.ServiceName, "logical/name")
            .Add(component => component.CurrentFolderId, "transport"));
        Assert.Equal("transport", panel.Find("[data-service-folder]").GetAttribute("value"));
        panel.Find("[data-service-folder]").Change("");
        panel.Find("[data-service-move]").Click();
        Assert.Equal("logical/name", client.MovedService);
        Assert.NotNull(client.Move);
        Assert.Null(client.Move.FolderId);
        Assert.Empty(panel.FindAll("[data-folder-name]"));
    }

    [Theory]
    [InlineData(OperateSectionStatus.Forbidden)]
    [InlineData(OperateSectionStatus.Unavailable)]
    [InlineData(OperateSectionStatus.Unsupported)]
    public void DeniedReadsDoNotOfferMutationControls(OperateSectionStatus status)
    {
        using var context = new BunitContext();
        context.Services.AddSingleton<IConsoleFolderClient>(new FolderClient { Denial = status });
        var panel = context.Render<FolderManagement>();
        Assert.Empty(panel.FindAll("[data-folder-save], [data-service-move]"));
        Assert.Contains("Folder read refused", panel.Markup);
        Assert.Single(panel.FindAll("[data-folder-refresh]"));
    }

    [Fact]
    public void UnboundSurfaceDoesNotInventFolders()
    {
        using var context = new BunitContext();
        var panel = context.Render<FolderManagement>();
        Assert.Contains("Connect an environment", panel.Markup);
        Assert.Empty(panel.FindAll("select, input"));
    }

    [Fact]
    public void PublishingSelectionKeepsOmittedRootAndFolderDistinct()
    {
        using var context = new BunitContext();
        context.Services.AddSingleton<IConsoleFolderClient>(new FolderClient());
        OptionalString selected = default;
        var bindings = new List<string?>();
        var picker = context.Render<FolderPublishSelection>(parameters => parameters
            .Add(component => component.ValueChanged, value => selected = value)
            .Add(component => component.AuthorityBindingChanged, binding => bindings.Add(binding)));
        Assert.False(selected.IsSpecified);
        picker.Find("[data-publish-folder]").Change("root:");
        Assert.True(selected.IsSpecified);
        Assert.Null(selected.Value);
        picker.Find("[data-publish-folder]").Change("folder:transport");
        Assert.Equal("transport", selected.Value);
        picker.Find("[data-publish-folder]").Change("preserve:");
        Assert.False(selected.IsSpecified);
        Assert.Equal(new string?[] { "authority", "authority", null }, bindings);
    }

    [Fact]
    public void PublishingPickerDoesNotOfferRetiredFoldersOrBlankLabels()
    {
        using var context = new BunitContext();
        context.Services.AddSingleton<IConsoleFolderClient>(new FolderClient
        {
            Folders = [new() { Metadata = new() { Id = "active", Name = "Survey", Title = " " } },
                new() { Metadata = new() { Id = "retired", Name = "Retired" }, Deleted = true }]
        });
        var picker = context.Render<FolderPublishSelection>();
        Assert.Empty(picker.FindAll("option[value=\"folder:retired\"]"));
        Assert.Equal("Survey", picker.Find("option[value=\"folder:active\"]").TextContent);
    }

    [Fact]
    public void PublishingPickerCanRefreshAfterASuccessfulReadAndReviewANewAuthority()
    {
        using var context = new BunitContext();
        var client = new FolderClient();
        context.Services.AddSingleton<IConsoleFolderClient>(client);
        var bindings = new List<string?>();
        var picker = context.Render<FolderPublishSelection>(parameters => parameters
            .Add(component => component.AuthorityBindingChanged, binding => bindings.Add(binding)));
        picker.Find("[data-publish-folder]").Change("folder:transport");
        Assert.Equal("authority", Assert.Single(bindings));

        client.AuthorityBinding = "new-authority";
        client.Folders = [new() { Metadata = new() { Id = "survey", Name = "Survey" } }];
        picker.Find("[data-publish-folder-refresh]").Click();

        Assert.Equal(2, client.Reads);
        Assert.Empty(picker.FindAll("option[value='folder:transport']"));
        picker.Find("[data-publish-folder]").Change("folder:survey");
        Assert.Equal(new[] { "authority", "new-authority" }, bindings);
    }

    [Fact]
    public async Task PublishingPickerDisablesRefreshAndSelectionDuringARead()
    {
        using var context = new BunitContext();
        var client = new FolderClient();
        context.Services.AddSingleton<IConsoleFolderClient>(client);
        var picker = context.Render<FolderPublishSelection>();
        client.PendingRead = new();
        var refresh = picker.Find("[data-publish-folder-refresh]").ClickAsync(new Microsoft.AspNetCore.Components.Web.MouseEventArgs());
        picker.WaitForAssertion(() =>
        {
            Assert.True(picker.Find("[data-publish-folder-refresh]").HasAttribute("disabled"));
            Assert.True(picker.Find("[data-publish-folder]").HasAttribute("disabled"));
        });
        client.PendingRead.SetResult(OperateSectionResult<ConsoleFolderCatalog>.Allowed(new(client.Folders, client.ETag, client.AuthorityBinding)));
        await refresh;
        Assert.False(picker.Find("[data-publish-folder-refresh]").HasAttribute("disabled"));
        Assert.False(picker.Find("[data-publish-folder]").HasAttribute("disabled"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NavigatingDuringAMoveRetainsTheOriginalServicesOutcomeAsANotification(bool saved)
    {
        using var context = new BunitContext();
        var pending = new TaskCompletionSource<ConsoleFolderMutationResult>();
        var client = new FolderClient { Pending = pending };
        var notifications = new ConsoleNotificationService();
        var changes = new ConsoleFolderChanges();
        var reloads = 0;
        changes.Changed += () => reloads++;
        context.Services.AddSingleton(changes);
        context.Services.AddSingleton<IConsoleFolderClient>(client);
        context.Services.AddSingleton<IConsoleNotificationService>(notifications);
        var panel = context.Render<FolderManagement>(parameters => parameters.Add(component => component.ServiceName, "original-service"));
        var move = panel.Find("[data-service-move]").ClickAsync(new Microsoft.AspNetCore.Components.Web.MouseEventArgs());
        panel.Render(parameters => parameters.Add(component => component.ServiceName, "other-service"));
        var state = saved ? "Saved" : "Outcome unverified";
        pending.SetResult(new() { Succeeded = saved, State = state, Detail = "Read the original catalog." });
        await move;
        panel.WaitForAssertion(() => Assert.Contains(notifications.Notifications,
            item => item.Message.Contains("original-service") && item.Message.Contains(state)));
        Assert.Equal(saved ? 1 : 0, reloads);
    }

    private sealed class FolderClient : IConsoleFolderClient
    {
        public IReadOnlyList<Folder> Folders { get; set; } = [new() { Metadata = new() { Id = "transport", Name = "Transport", Title = "Transport data" } }];
        public TaskCompletionSource<ConsoleFolderMutationResult>? Pending { get; init; }
        public string ETag { get; set; } = "\"catalog-1\"";
        public OperateSectionStatus? Denial { get; init; }
        public string AuthorityBinding { get; set; } = "authority";
        public int Reads { get; private set; }
        public TaskCompletionSource<OperateSectionResult<ConsoleFolderCatalog>>? PendingRead { get; set; }
        public int Mutations { get; private set; }
        public UpdateFolderRequest? Updated { get; private set; }
        public string? Deleted { get; private set; }
        public string? MovedService { get; private set; }
        public MoveServiceRequest? Move { get; private set; }
        public ConsoleFolderCatalog? MutatedCatalog { get; private set; }
        public ConsoleFolderMutationResult Result { get; set; } = new() { Succeeded = true, State = "Saved" };

        public Task<OperateSectionResult<ConsoleFolderCatalog>> ReadAsync(CancellationToken cancellationToken = default)
        {
            Reads++;
            return PendingRead?.Task ?? Task.FromResult(
                Denial is { } denied ? OperateSectionResult<ConsoleFolderCatalog>.Denied(denied, "Folder read refused")
                    : OperateSectionResult<ConsoleFolderCatalog>.Allowed(new(Folders, ETag, AuthorityBinding)));
        }
        public Task<ConsoleFolderMutationResult> CreateAsync(ConsoleFolderCatalog catalog, CreateFolderRequest request, CancellationToken cancellationToken = default) => Record(catalog);
        public Task<ConsoleFolderMutationResult> UpdateAsync(ConsoleFolderCatalog catalog, string folderId, UpdateFolderRequest request, CancellationToken cancellationToken = default)
        {
            Updated = request;
            return Record(catalog);
        }
        public Task<ConsoleFolderMutationResult> DeleteAsync(ConsoleFolderCatalog catalog, string folderId, CancellationToken cancellationToken = default)
        {
            Deleted = folderId;
            return Record(catalog);
        }
        public Task<ConsoleFolderMutationResult> MoveAsync(ConsoleFolderCatalog catalog, string serviceName, MoveServiceRequest request, CancellationToken cancellationToken = default)
        {
            MovedService = serviceName;
            Move = request;
            return Record(catalog);
        }
        private Task<ConsoleFolderMutationResult> Record(ConsoleFolderCatalog catalog)
        {
            Mutations++;
            MutatedCatalog = catalog;
            return Pending?.Task ?? Task.FromResult(Result);
        }
    }
}
