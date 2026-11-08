using Bunit;
using Honua.Console.Shell.Components.Operate;
using Honua.Console.Shell.Services;
using Honua.Sdk.Admin.Models;
using Microsoft.Extensions.DependencyInjection;

namespace Honua.Console.Shell.Tests;

public sealed class FolderManagementTests
{
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
        var picker = context.Render<FolderPublishSelection>(parameters => parameters
            .Add(component => component.ValueChanged, value => selected = value));
        Assert.False(selected.IsSpecified);
        picker.Find("[data-publish-folder]").Change("root:");
        Assert.True(selected.IsSpecified);
        Assert.Null(selected.Value);
        picker.Find("[data-publish-folder]").Change("folder:transport");
        Assert.Equal("transport", selected.Value);
        picker.Find("[data-publish-folder]").Change("preserve:");
        Assert.False(selected.IsSpecified);
    }

    private sealed class FolderClient : IConsoleFolderClient
    {
        public string ETag { get; set; } = "\"catalog-1\"";
        public OperateSectionStatus? Denial { get; init; }
        public int Mutations { get; private set; }
        public UpdateFolderRequest? Updated { get; private set; }
        public string? Deleted { get; private set; }
        public string? MovedService { get; private set; }
        public MoveServiceRequest? Move { get; private set; }
        public ConsoleFolderCatalog? MutatedCatalog { get; private set; }
        public ConsoleFolderMutationResult Result { get; set; } = new() { Succeeded = true, State = "Saved" };

        public Task<OperateSectionResult<ConsoleFolderCatalog>> ReadAsync(CancellationToken cancellationToken = default) => Task.FromResult(
            Denial is { } denied ? OperateSectionResult<ConsoleFolderCatalog>.Denied(denied, "Folder read refused")
                : OperateSectionResult<ConsoleFolderCatalog>.Allowed(new([new Folder { Metadata = new() { Id = "transport", Name = "Transport", Title = "Transport data" } }], ETag, "authority")));
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
            return Task.FromResult(Result);
        }
    }
}
