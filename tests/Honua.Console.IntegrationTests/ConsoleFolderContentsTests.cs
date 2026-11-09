using Bunit;
using Honua.Console.Contracts;
using Honua.Console.Shell.Components;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Pages;
using Honua.Console.Shell.Services;
using Honua.Sdk.Admin.Models;
using Microsoft.Extensions.DependencyInjection;

namespace Honua.Console.IntegrationTests;

public sealed class ConsoleFolderContentsTests
{
    [Fact]
    public void DisabledFlagKeepsExistingFolderManagementLinkWithoutReads()
    {
        using var context = new BunitContext();
        var source = new NavigationSource();
        context.Services.AddSingleton<IConsolePublicationNavigationSource>(source);
        var page = context.Render<OperateFolderPage>(parameters => parameters.Add(component => component.FolderId, "transport"));
        Assert.Equal("/operate/services#folders", page.Find("a").GetAttribute("href"));
        Assert.Empty(source.Reads);
        CatalogAdminPreview.Capture("folder-legacy", $"<div class=\"console-shell\" data-console-v4=\"false\">{page.Markup}</div>");
    }

    [Fact]
    public void IncompleteCatalogUsesReturnedFolderServicesAndExplicitlyPartialCounts()
    {
        using var context = new BunitContext();
        var source = new NavigationSource();
        context.Services.AddSingleton<IConsolePublicationNavigationSource>(source);
        context.Services.AddSingleton<IConsoleFolderClient>(new FolderClient());
        var page = context.Render<ConsoleFolderContents>(parameters => parameters.Add(component => component.FolderId, "transport"));
        Assert.Contains("1 Services · 3 Layers in pages read", page.Find("[data-folder-count]").TextContent);
        Assert.Contains("incomplete", page.Markup);
        Assert.DoesNotContain("other-service", page.Markup);
        Assert.Equal("Transport", page.Find("h1").TextContent);
        Assert.Single(source.Reads);
        page.Find("button").Click();
        Assert.Equal([1, 2], source.Reads.Select(query => query.Page));
        Assert.Contains("2 Services · 7 Layers", page.Find("[data-folder-count]").TextContent);
        Assert.DoesNotContain("in pages read", page.Find("[data-folder-count]").TextContent);
        Assert.Empty(page.FindAll("button"));
        CatalogAdminPreview.Capture("folder-v4", $"<div class=\"console-shell\" data-console-v4=\"true\"><section class=\"console-page console-folder-page\">{page.Markup}</section></div>");
    }

    [Fact]
    public void PartialEarlierPageCannotBecomeCompleteWhenTheLastPageIsNotPartial()
    {
        using var context = new BunitContext();
        context.Services.AddSingleton<IConsolePublicationNavigationSource>(new NavigationSource { PartialPage = 1 });
        context.Services.AddSingleton<IConsoleFolderClient>(new FolderClient());
        var page = context.Render<ConsoleFolderContents>(parameters => parameters.Add(component => component.FolderId, "transport"));
        page.Find("button").Click();
        Assert.Contains("2 Services · 7 Layers in pages read", page.Find("[data-folder-count]").TextContent);
        Assert.Contains("incomplete", page.Markup);
    }

    [Fact]
    public void EmptyPageBeforeReportedTotalDoesNotClaimCompleteCounts()
    {
        using var context = new BunitContext();
        context.Services.AddSingleton<IConsolePublicationNavigationSource>(new NavigationSource { EmptyPage = 2 });
        context.Services.AddSingleton<IConsoleFolderClient>(new FolderClient());
        var page = context.Render<ConsoleFolderContents>(parameters => parameters.Add(component => component.FolderId, "transport"));
        page.Find("button").Click();
        Assert.Contains("1 Services · 3 Layers in pages read", page.Find("[data-folder-count]").TextContent);
        Assert.Contains("incomplete", page.Markup);
        Assert.Empty(page.FindAll("button"));
    }

    [Fact]
    public void MissingFolderDoesNotReadServicesOrClaimAnEmptyFolder()
    {
        using var context = new BunitContext();
        var source = new NavigationSource();
        context.Services.AddSingleton<IConsolePublicationNavigationSource>(source);
        context.Services.AddSingleton<IConsoleFolderClient>(new FolderClient());
        var page = context.Render<ConsoleFolderContents>(parameters => parameters.Add(component => component.FolderId, "absent"));
        Assert.Empty(source.Reads);
        Assert.Contains("not available in the selected environment", page.Markup);
        Assert.DoesNotContain("no Services", page.Markup);
    }

    [Fact]
    public void DeniedServicesKeepTheirStateWithoutZeroCounts()
    {
        using var context = new BunitContext();
        context.Services.AddSingleton<IConsolePublicationNavigationSource>(new NavigationSource { Status = OperateSectionStatus.Forbidden });
        context.Services.AddSingleton<IConsoleFolderClient>(new FolderClient());
        var page = context.Render<ConsoleFolderContents>(parameters => parameters.Add(component => component.FolderId, "transport"));
        Assert.Contains("Access denied", page.Markup);
        Assert.Empty(page.FindAll("[data-folder-count]"));
        Assert.Empty(page.FindAll("button"));
    }

    private sealed class NavigationSource : IConsolePublicationNavigationSource
    {
        public List<HonuaAdminCatalogQuery> Reads { get; } = [];
        public OperateSectionStatus? Status { get; init; }
        public int? PartialPage { get; init; }
        public int? EmptyPage { get; init; }
        public Task<OperateSectionResult<HonuaAdminServicePage>> ReadServicesAsync(HonuaAdminCatalogQuery query, CancellationToken cancellationToken)
        {
            Reads.Add(query);
            if (Status is { } status) return Task.FromResult(OperateSectionResult<HonuaAdminServicePage>.Denied(status, "Access denied"));
            ServiceSummary[] items = query.Page == 1
                ? [new() { ServiceName = "roads", FolderId = "transport", LayerCount = 3 }, new() { ServiceName = "other-service", FolderId = "other", LayerCount = 90 }]
                : [new() { ServiceName = "bridges", FolderId = "transport", LayerCount = 4 }];
            if (query.Page == EmptyPage) items = [];
            return Task.FromResult(OperateSectionResult<HonuaAdminServicePage>.Allowed(new() { Items = items, Page = query.Page, PageSize = 2, Total = 3, LayerTotal = 97 }, partialResult: query.Page == PartialPage));
        }

        public Task<OperateSectionResult<IReadOnlyList<string>>> ReadConnectionsAsync(CancellationToken cancellationToken) => throw new InvalidOperationException();
        public Task<OperateSectionResult<IReadOnlyList<ConsolePublicationLayer>>> ReadLayersAsync(string serviceName, IReadOnlyList<string> connectionKeys, CancellationToken cancellationToken) => throw new InvalidOperationException();
    }

    private sealed class FolderClient : IConsoleFolderClient
    {
        public Task<OperateSectionResult<ConsoleFolderCatalog>> ReadAsync(CancellationToken cancellationToken = default) => Task.FromResult(
            OperateSectionResult<ConsoleFolderCatalog>.Allowed(new([new() { Metadata = new() { Id = "transport", Name = "transport", Title = "Transport" } }], "etag", "authority")));
        public Task<ConsoleFolderMutationResult> CreateAsync(ConsoleFolderCatalog catalog, CreateFolderRequest request, CancellationToken cancellationToken = default) => throw new InvalidOperationException();
        public Task<ConsoleFolderMutationResult> UpdateAsync(ConsoleFolderCatalog catalog, string folderId, UpdateFolderRequest request, CancellationToken cancellationToken = default) => throw new InvalidOperationException();
        public Task<ConsoleFolderMutationResult> DeleteAsync(ConsoleFolderCatalog catalog, string folderId, CancellationToken cancellationToken = default) => throw new InvalidOperationException();
        public Task<ConsoleFolderMutationResult> MoveAsync(ConsoleFolderCatalog catalog, string serviceName, MoveServiceRequest request, CancellationToken cancellationToken = default) => throw new InvalidOperationException();
    }
}
