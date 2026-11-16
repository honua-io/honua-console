using Bunit;
using Honua.Console.Shell.Components.Operate;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Services;
using Honua.Sdk.Admin.Models;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;

namespace Honua.Console.Shell.Tests;

public sealed class FolderPublishSelectionTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("transport")]
    public void RefreshRetainsTheReviewedBindingUntilTheSameDestinationIsConfirmed(string? folderId)
    {
        using var context = new BunitContext();
        var source = new FolderSource();
        context.Services.AddSingleton<IConsoleFolderClient>(source);
        var bindings = new List<string?>();
        var selection = new OptionalString(folderId);
        var picker = context.Render<FolderPublishSelection>(parameters => parameters
            .Add(component => component.Value, selection)
            .Add(component => component.ValueChanged, value => selection = value)
            .Add(component => component.AuthorityBindingChanged, binding => bindings.Add(binding)));
        picker.Find("[data-publish-folder-review]").Click();
        Assert.Equal("original", Assert.Single(bindings));

        source.Binding = "refreshed";
        picker.Find("[data-publish-folder-refresh]").Click();

        Assert.Single(bindings);
        Assert.Equal(folderId is null ? "root:" : "folder:transport", picker.Find("select").GetAttribute("value"));
        Assert.Contains("Refreshing keeps your previous selection and review", picker.Markup);
        picker.Find("[data-publish-folder-review]").Click();
        Assert.Equal(new[] { "original", "refreshed" }, bindings);
        Assert.True(selection.IsSpecified);
        Assert.Equal(folderId, selection.Value);
        Assert.Equal(2, source.Reads);
        Assert.Equal(0, source.Writes);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public void AReadWithoutAuthorityCannotConfirmRootOrFolder(string? binding)
    {
        using var context = new BunitContext();
        context.Services.AddSingleton<IConsoleFolderClient>(new FolderSource { Binding = binding! });
        var bindings = new List<string?>();
        var values = new List<OptionalString>();
        var picker = context.Render<FolderPublishSelection>(parameters => parameters
            .Add(component => component.Value, new OptionalString("transport"))
            .Add(component => component.ValueChanged, value => values.Add(value))
            .Add(component => component.AuthorityBindingChanged, value => bindings.Add(value)));

        Assert.True(picker.Find("[data-publish-folder-review]").HasAttribute("disabled"));
        picker.Find("select").Change("root:");
        picker.Find("select").Change("folder:transport");
        Assert.Empty(bindings);
        Assert.Empty(values);
        Assert.Contains("cannot be reviewed from this catalog", picker.Markup);
        picker.Find("select").Change("preserve:");
        Assert.Null(Assert.Single(bindings));
        Assert.False(Assert.Single(values).IsSpecified);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MissingOrDeletedFolderRemainsVisibleWithoutBeingConfirmed(bool deleted)
    {
        using var context = new BunitContext();
        var source = new FolderSource();
        context.Services.AddSingleton<IConsoleFolderClient>(source);
        var bindings = new List<string?>();
        var picker = context.Render<FolderPublishSelection>(parameters => parameters
            .Add(component => component.Value, new OptionalString("transport"))
            .Add(component => component.AuthorityBindingChanged, value => bindings.Add(value)));
        picker.Find("[data-publish-folder-review]").Click();

        source.Folders = deleted
            ? [new() { Metadata = new() { Id = "transport", Name = "Transport" }, Deleted = true }]
            : [];
        picker.Find("[data-publish-folder-refresh]").Click();

        var retained = picker.Find("option[value='folder:transport']");
        Assert.True(retained.HasAttribute("disabled"));
        Assert.Contains("Selected Folder unavailable", retained.TextContent);
        Assert.True(picker.Find("[data-publish-folder-review]").HasAttribute("disabled"));
        picker.Find("select").Change("folder:transport");
        Assert.Single(bindings);
        Assert.Contains("choose an available Folder or Root", picker.Markup);
        Assert.Equal(0, source.Writes);
    }

    [Fact]
    public void RefusedRefreshCannotRebindAnExistingRootSelection()
    {
        using var context = new BunitContext();
        var source = new FolderSource();
        context.Services.AddSingleton<IConsoleFolderClient>(source);
        var bindings = new List<string?>();
        var picker = context.Render<FolderPublishSelection>(parameters => parameters
            .Add(component => component.Value, new OptionalString(null))
            .Add(component => component.AuthorityBindingChanged, value => bindings.Add(value)));
        picker.Find("[data-publish-folder-review]").Click();
        source.Denied = true;
        picker.Find("[data-publish-folder-refresh]").Click();

        Assert.Contains("Catalog refused", picker.Markup);
        Assert.True(picker.Find("[data-publish-folder-review]").HasAttribute("disabled"));
        picker.Find("select").Change("root:");
        Assert.Equal("original", Assert.Single(bindings));
        Assert.Equal(0, source.Writes);
    }

    [Fact]
    public async Task PendingRefreshCannotConfirmThePreviousCatalog()
    {
        using var context = new BunitContext();
        var source = new FolderSource();
        context.Services.AddSingleton<IConsoleFolderClient>(source);
        var bindings = new List<string?>();
        var picker = context.Render<FolderPublishSelection>(parameters => parameters
            .Add(component => component.Value, new OptionalString("transport"))
            .Add(component => component.AuthorityBindingChanged, value => bindings.Add(value)));
        source.Pending = new();
        var refresh = picker.Find("[data-publish-folder-refresh]").ClickAsync(new MouseEventArgs());
        Assert.True(picker.Find("[data-publish-folder-review]").HasAttribute("disabled"));
        picker.Find("select").Change("root:");
        Assert.Empty(bindings);
        source.Pending.SetResult(OperateSectionResult<ConsoleFolderCatalog>.Allowed(new(source.Folders, "etag", "refreshed")));
        await refresh;
        Assert.Empty(bindings);
        picker.Find("[data-publish-folder-review]").Click();
        Assert.Equal("refreshed", Assert.Single(bindings));
    }

    private sealed class FolderSource : IConsoleFolderClient
    {
        public string Binding { get; set; } = "original";
        public bool Denied { get; set; }
        public IReadOnlyList<Folder> Folders { get; set; } = [new() { Metadata = new() { Id = "transport", Name = "Transport" } }];
        public TaskCompletionSource<OperateSectionResult<ConsoleFolderCatalog>>? Pending { get; set; }
        public int Reads { get; private set; }
        public int Writes { get; private set; }
        public Task<OperateSectionResult<ConsoleFolderCatalog>> ReadAsync(CancellationToken cancellationToken = default)
        {
            Reads++;
            return Pending?.Task ?? Task.FromResult(Denied
                ? OperateSectionResult<ConsoleFolderCatalog>.Denied(OperateSectionStatus.Forbidden, "Catalog refused")
                : OperateSectionResult<ConsoleFolderCatalog>.Allowed(new(Folders, "etag", Binding)));
        }
        public Task<ConsoleFolderMutationResult> CreateAsync(ConsoleFolderCatalog catalog, CreateFolderRequest request, CancellationToken cancellationToken = default) => Write();
        public Task<ConsoleFolderMutationResult> UpdateAsync(ConsoleFolderCatalog catalog, string folderId, UpdateFolderRequest request, CancellationToken cancellationToken = default) => Write();
        public Task<ConsoleFolderMutationResult> DeleteAsync(ConsoleFolderCatalog catalog, string folderId, CancellationToken cancellationToken = default) => Write();
        public Task<ConsoleFolderMutationResult> MoveAsync(ConsoleFolderCatalog catalog, string serviceName, MoveServiceRequest request, CancellationToken cancellationToken = default) => Write();
        private Task<ConsoleFolderMutationResult> Write()
        {
            Writes++;
            throw new InvalidOperationException("Destination review must never mutate the catalog.");
        }
    }
}
