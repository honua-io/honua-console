using Bunit;
using System.Security.Claims;
using Honua.Console.Web.Auth;
using Microsoft.AspNetCore.Components.Authorization;
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
        CatalogAdminPreview.Capture("folder-legacy", $"<div class=\"console-shell\">{page.Markup}</div>");
    }

    [Fact]
    public void IncompleteCatalogUsesReturnedFolderServicesAndExplicitlyPartialCounts()
    {
        using var context = new BunitContext();
        var source = new NavigationSource();
        context.Services.AddSingleton<IConsolePublicationNavigationSource>(source);
        context.Services.AddSingleton<IConsoleFolderClient>(new FolderClient());
        var page = context.Render<ConsoleFolderContents>(parameters => parameters.Add(component => component.FolderId, "transport"));
        Assert.Contains("1 Service · 3 Layers in pages read", page.Find("[data-folder-count]").TextContent);
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
        Assert.Contains("1 Service · 3 Layers in pages read", page.Find("[data-folder-count]").TextContent);
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

    [Fact]
    public void DeniedLaterPageHidesPreviouslyReadCountsAndStopsPaging()
    {
        using var context = new BunitContext();
        context.Services.AddSingleton<IConsolePublicationNavigationSource>(new NavigationSource { Status = OperateSectionStatus.Forbidden, StatusPage = 2 });
        context.Services.AddSingleton<IConsoleFolderClient>(new FolderClient());
        var page = context.Render<ConsoleFolderContents>(parameters => parameters.Add(component => component.FolderId, "transport"));
        page.Find("button").Click();
        Assert.Contains("Access denied", page.Markup);
        Assert.Empty(page.FindAll("[data-folder-count]"));
        Assert.Empty(page.FindAll("button"));
    }

    [Fact]
    public void HungServiceReadExpiresAndRetryRecovers()
    {
        using var context = new BunitContext();
        var source = new NavigationSource { HoldFirstRead = true };
        context.Services.AddSingleton<IConsolePublicationNavigationSource>(source);
        context.Services.AddSingleton<IConsoleFolderClient>(new FolderClient());
        var page = context.Render<ConsoleFolderContents>(parameters => parameters
            .Add(component => component.FolderId, "transport")
            .Add(component => component.LoadBudget, TimeSpan.FromMilliseconds(25)));
        page.WaitForAssertion(() => Assert.Contains("did not respond in time", page.Markup));
        page.Find("button").Click();
        page.WaitForAssertion(() => Assert.Contains("1 Service · 3 Layers in pages read", page.Find("[data-folder-count]").TextContent));
        Assert.Equal(2, source.Reads.Count);
    }

    [Fact]
    public async Task EnvironmentChangeDiscardsThePreviousFolderRead()
    {
        using var context = new BunitContext();
        var source = new NavigationSource { HoldFirstRead = true };
        var profiles = new InMemoryConsoleEnvironmentProfileStore([
            new() { Id = "first", DisplayName = "First" },
            new() { Id = "second", DisplayName = "Second" }
        ], activeProfileId: "first");
        context.Services.AddSingleton(new ConsoleV4Options { Enabled = true });
        context.Services.AddSingleton<IConsoleEnvironmentProfileStore>(profiles);
        context.Services.AddSingleton<IConsolePublicationNavigationSource>(source);
        context.Services.AddSingleton<IConsoleFolderClient>(new FolderClient());
        var page = context.Render<OperateFolderPage>(parameters => parameters.Add(component => component.FolderId, "transport"));
        page.WaitForAssertion(() => Assert.Single(source.Reads));
        await profiles.ActivateProfileAsync("second");
        page.WaitForAssertion(() => Assert.Contains("1 Service · 3 Layers in pages read", page.Find("[data-folder-count]").TextContent));
        source.HeldRead.SetResult(OperateSectionResult<HonuaAdminServicePage>.Allowed(new()
        {
            Items = [new() { ServiceName = "stale", FolderId = "transport", LayerCount = 999 }],
            Total = 1
        }));
        page.WaitForAssertion(() => Assert.DoesNotContain("stale", page.Markup));
        Assert.Equal(2, source.Reads.Count);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RepeatedOrMismatchedPagesCannotClaimCompleteCounts(bool mismatchedPage)
    {
        using var context = new BunitContext();
        context.Services.AddSingleton<IConsolePublicationNavigationSource>(new NavigationSource { DuplicateSecondPage = !mismatchedPage, MismatchedSecondPage = mismatchedPage });
        context.Services.AddSingleton<IConsoleFolderClient>(new FolderClient());
        var page = context.Render<ConsoleFolderContents>(parameters => parameters.Add(component => component.FolderId, "transport"));
        page.Find("button").Click();
        Assert.Contains("in pages read", page.Find("[data-folder-count]").TextContent);
        Assert.Contains("incomplete", page.Markup);
    }

    [Fact]
    public async Task AuthenticationChangeRebindsEnvironmentChangesAndDiscardsOldRead()
    {
        using var context = new BunitContext();
        var source = new NavigationSource();
        var identity = new MutableOperator();
        var profiles = new OperatorScopedEnvironmentProfileStore(identity, () => new InMemoryConsoleEnvironmentProfileStore([
            new() { Id = "first", DisplayName = "First" }, new() { Id = "second", DisplayName = "Second" }
        ], activeProfileId: "first"));
        var authentication = new AuthenticationSignals();
        context.Services.AddSingleton(new ConsoleV4Options { Enabled = true });
        context.Services.AddSingleton<IConsoleEnvironmentProfileStore>(profiles);
        context.Services.AddSingleton<AuthenticationStateProvider>(authentication);
        context.Services.AddSingleton<IConsolePublicationNavigationSource>(source);
        context.Services.AddSingleton<IConsoleFolderClient>(new FolderClient());
        var page = context.Render<OperateFolderPage>(parameters => parameters.Add(component => component.FolderId, "transport"));
        Assert.Single(source.Reads);
        identity.CurrentOperatorKey = "second-operator";
        source.HoldReadNumber = 2;
        await page.InvokeAsync(authentication.Publish);
        page.WaitForAssertion(() => Assert.Equal(2, source.Reads.Count));
        await profiles.ActivateProfileAsync("second");
        page.WaitForAssertion(() => Assert.Equal(3, source.Reads.Count));
        source.HeldRead.SetResult(OperateSectionResult<HonuaAdminServicePage>.Allowed(new()
        {
            Items = [new() { ServiceName = "stale-operator", FolderId = "transport", LayerCount = 999 }],
            Total = 1
        }));
        page.WaitForAssertion(() => Assert.Contains("1 Service · 3 Layers", page.Find("[data-folder-count]").TextContent));
        Assert.DoesNotContain("stale-operator", page.Markup);
    }

    private sealed class MutableOperator : IConsoleOperatorContext
    {
        public string CurrentOperatorKey { get; set; } = "first-operator";
        public bool HasOperator => true;
        public string RequireOperatorKey() => CurrentOperatorKey;
    }

    // Test provider publishes its own current state; it does not consume cached state.
#pragma warning disable BL0013
    private sealed class AuthenticationSignals : AuthenticationStateProvider
    {
        public override Task<AuthenticationState> GetAuthenticationStateAsync() => Task.FromResult(new AuthenticationState(new ClaimsPrincipal(new ClaimsIdentity())));
        public void Publish() => NotifyAuthenticationStateChanged(GetAuthenticationStateAsync());
    }

#pragma warning restore BL0013

    private sealed class NavigationSource : IConsolePublicationNavigationSource
    {
        public List<HonuaAdminCatalogQuery> Reads { get; } = [];
        public OperateSectionStatus? Status { get; init; }
        public int? StatusPage { get; init; }
        public bool HoldFirstRead { get; init; }
        public int? HoldReadNumber { get; set; }
        public bool DuplicateSecondPage { get; init; }
        public bool MismatchedSecondPage { get; init; }
        public TaskCompletionSource<OperateSectionResult<HonuaAdminServicePage>> HeldRead { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int? PartialPage { get; init; }
        public int? EmptyPage { get; init; }
        public Task<OperateSectionResult<HonuaAdminServicePage>> ReadServicesAsync(HonuaAdminCatalogQuery query, CancellationToken cancellationToken)
        {
            Reads.Add(query);
            if ((HoldFirstRead && Reads.Count == 1) || Reads.Count == HoldReadNumber) return HeldRead.Task;
            if (Status is { } status && (StatusPage is null || StatusPage == query.Page)) return Task.FromResult(OperateSectionResult<HonuaAdminServicePage>.Denied(status, "Access denied"));
            ServiceSummary[] items = query.Page == 1
                ? [new() { ServiceName = "roads", FolderId = "transport", LayerCount = 3 }, new() { ServiceName = "other-service", FolderId = "other", LayerCount = 90 }]
                : [new() { ServiceName = "bridges", FolderId = "transport", LayerCount = 4 }];
            if (query.Page == 2 && DuplicateSecondPage) items = [new() { ServiceName = "roads", FolderId = "transport", LayerCount = 3 }];
            if (query.Page == EmptyPage) items = [];
            return Task.FromResult(OperateSectionResult<HonuaAdminServicePage>.Allowed(new() { Items = items, Page = query.Page == 2 && MismatchedSecondPage ? 1 : query.Page, PageSize = 2, Total = 3, LayerTotal = 97 }, partialResult: query.Page == PartialPage));
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
