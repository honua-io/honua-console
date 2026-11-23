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
    public void FolderPageReadsOnlyItsServicesWithTheServerFolderFilter()
    {
        using var context = new BunitContext();
        var source = new NavigationSource();
        context.Services.AddSingleton<IConsolePublicationNavigationSource>(source);
        context.Services.AddSingleton<IConsoleFolderClient>(new FolderClient());
        var page = context.Render<ConsoleFolderContents>(parameters => parameters.Add(component => component.FolderId, "transport"));
        var query = Assert.Single(source.Reads);
        Assert.Equal(("transport", 25, 1), (query.FolderId, query.PageSize, query.Page));
        Assert.Equal("Transport", page.Find("h1").TextContent);
        Assert.Equal("Folder transport · 2 services · 7 layers", page.Find(".console-folder-summary").TextContent);
        Assert.Equal(["roads", "bridges"], page.FindAll("tbody th a").Select(link => link.TextContent));
        Assert.Equal("transport/roads", page.Find("tbody .console-folder-service-path").TextContent);
        Assert.Equal(["3", "2"], page.FindAll("tbody tr:first-child td.console-number").Select(cell => cell.TextContent));
        Assert.Equal("/operate/data/new", page.Find(".console-folder-actions a").GetAttribute("href"));
        Assert.Equal("Results 1 to 2 (out of 2 services)", page.Find("[data-list-pager] [role='status']").TextContent);
        Assert.DoesNotContain("other-service", page.Markup);
        Assert.DoesNotContain("Administration", page.Markup);
        CatalogAdminPreview.Capture("folder-v4", $"<div class=\"console-shell\" data-console-v4=\"true\"><section class=\"console-page console-folder-page\">{page.Markup}</section></div>");
    }

    [Fact]
    public void FolderPagerReadsTheRequestedPageAndSearchKeepsTheFolderTotals()
    {
        using var context = new BunitContext();
        var source = new NavigationSource { TransportServices = 30 };
        context.Services.AddSingleton<IConsolePublicationNavigationSource>(source);
        context.Services.AddSingleton<IConsoleFolderClient>(new FolderClient());
        var page = context.Render<ConsoleFolderContents>(parameters => parameters.Add(component => component.FolderId, "transport"));
        Assert.Equal("Results 1 to 25 (out of 30 services)", page.Find("[data-list-pager] [role='status']").TextContent);
        Assert.Equal(25, page.FindAll("tbody tr").Count);
        page.Find("[aria-label='Next page']").Click();
        page.WaitForAssertion(() => Assert.Equal("Results 26 to 30 (out of 30 services)", page.Find("[data-list-pager] [role='status']").TextContent));
        Assert.Equal(5, page.FindAll("tbody tr").Count);
        Assert.Equal("page", page.Find("[aria-label='Page 2']").GetAttribute("aria-current"));

        page.Find("[data-folder-search]").Change(" route-01 ");
        page.WaitForAssertion(() => Assert.Equal(("route-01", 1), (source.Reads[^1].NameQuery, source.Reads[^1].Page)));
        Assert.Equal(["route-01"], page.FindAll("tbody th a").Select(link => link.TextContent));
        Assert.Equal("Folder transport · 30 services · 30 layers", page.Find(".console-folder-summary").TextContent);
        page.Find("[data-folder-search]").Change("absent");
        page.WaitForAssertion(() => Assert.Contains("No services in this Folder match this search.", page.Markup));
    }

    [Fact]
    public void ServerThatIgnoresTheFolderFilterShowsOnlyFolderRowsWithoutClaimingFolderCounts()
    {
        using var context = new BunitContext();
        context.Services.AddSingleton<IConsolePublicationNavigationSource>(new NavigationSource { IgnoreFolder = true });
        context.Services.AddSingleton<IConsoleFolderClient>(new FolderClient());
        var page = context.Render<ConsoleFolderContents>(parameters => parameters.Add(component => component.FolderId, "transport"));
        Assert.Equal(["roads", "bridges"], page.FindAll("tbody th a").Select(link => link.TextContent));
        Assert.Contains("does not filter services by Folder", page.Find("[role='note']").TextContent);
        Assert.Empty(page.FindAll("[data-folder-count]"));
        Assert.Equal("Results 1 to 3 (out of 3 catalog services)", page.Find("[data-list-pager] [role='status']").TextContent);
    }

    [Fact]
    public void IgnoredFolderFilterFoundOnALaterPageWithdrawsTheFolderCounts()
    {
        using var context = new BunitContext();
        context.Services.AddSingleton<IConsolePublicationNavigationSource>(new NavigationSource { TransportServices = 30, OtherServicesAfterTransport = 5, IgnoreFolder = true });
        context.Services.AddSingleton<IConsoleFolderClient>(new FolderClient());
        var page = context.Render<ConsoleFolderContents>(parameters => parameters.Add(component => component.FolderId, "transport"));
        Assert.Single(page.FindAll("[data-folder-count]"));
        page.Find("[aria-label='Next page']").Click();
        page.WaitForAssertion(() => Assert.Contains("does not filter services by Folder", page.Find("[role='note']").TextContent));
        Assert.Empty(page.FindAll("[data-folder-count]"));
        page.Find("[aria-label='Previous page']").Click();
        page.WaitForAssertion(() => Assert.Equal(25, page.FindAll("tbody tr").Count));
        Assert.Empty(page.FindAll("[data-folder-count]"));
        Assert.Single(page.FindAll("[role='note']"));
    }

    [Fact]
    public async Task PagingDuringAPendingSearchKeepsTheSearch()
    {
        using var context = new BunitContext();
        var source = new NavigationSource { TransportServices = 30, HoldSearch = new(TaskCreationOptions.RunContinuationsAsynchronously) };
        context.Services.AddSingleton<IConsolePublicationNavigationSource>(source);
        context.Services.AddSingleton<IConsoleFolderClient>(new FolderClient());
        var page = context.Render<ConsoleFolderContents>(parameters => parameters.Add(component => component.FolderId, "transport"));
        page.Find("[data-folder-search]").Change("route-2");
        page.Find("[aria-label='Next page']").Click();
        page.WaitForAssertion(() => Assert.Equal(("route-2", 2), (source.Reads[^1].NameQuery, source.Reads[^1].Page)));
        await page.InvokeAsync(() => source.HoldSearch!.SetResult());
        page.WaitForAssertion(() => Assert.Contains("No services in this Folder match this search.", page.Markup));
        Assert.Empty(page.FindAll("tbody tr"));
    }

    [Fact]
    public void PartialOrMismatchedPageIsMarkedIncomplete()
    {
        using var context = new BunitContext();
        context.Services.AddSingleton<IConsolePublicationNavigationSource>(new NavigationSource { TransportServices = 30, MismatchedSecondPage = true });
        context.Services.AddSingleton<IConsoleFolderClient>(new FolderClient());
        var page = context.Render<ConsoleFolderContents>(parameters => parameters.Add(component => component.FolderId, "transport"));
        Assert.Empty(page.FindAll("[role='note']"));
        page.Find("[aria-label='Next page']").Click();
        page.WaitForAssertion(() => Assert.Contains("incomplete", page.Find("[role='note']").TextContent));
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
        Assert.DoesNotContain("no services", page.Markup);
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
    public void DeniedLaterPageHidesTheFolderCountsAndThePager()
    {
        using var context = new BunitContext();
        context.Services.AddSingleton<IConsolePublicationNavigationSource>(new NavigationSource { TransportServices = 30, Status = OperateSectionStatus.Forbidden, StatusPage = 2 });
        context.Services.AddSingleton<IConsoleFolderClient>(new FolderClient());
        var page = context.Render<ConsoleFolderContents>(parameters => parameters.Add(component => component.FolderId, "transport"));
        page.Find("[aria-label='Next page']").Click();
        page.WaitForAssertion(() => Assert.Contains("Access denied", page.Markup));
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
        page.WaitForAssertion(() => Assert.Equal(" · 2 services · 7 layers", page.Find("[data-folder-count]").TextContent));
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
        page.WaitForAssertion(() => Assert.Equal(" · 2 services · 7 layers", page.Find("[data-folder-count]").TextContent));
        source.HeldRead.SetResult(StaleRead("stale"));
        page.WaitForAssertion(() => Assert.DoesNotContain("stale", page.Markup));
        Assert.Equal(2, source.Reads.Count);
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
        source.HeldRead.SetResult(StaleRead("stale-operator"));
        page.WaitForAssertion(() => Assert.Equal(" · 2 services · 7 layers", page.Find("[data-folder-count]").TextContent));
        Assert.DoesNotContain("stale-operator", page.Markup);
    }

    private static OperateSectionResult<HonuaAdminServicePage> StaleRead(string serviceName) =>
        OperateSectionResult<HonuaAdminServicePage>.Allowed(new()
        {
            Items = [new() { ServiceName = serviceName, FolderId = "transport", LayerCount = 999 }],
            Page = 1,
            Total = 1
        });

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
        public bool IgnoreFolder { get; init; }
        public bool MismatchedSecondPage { get; init; }
        public int? TransportServices { get; init; }
        public TaskCompletionSource? HoldSearch { get; init; }
        public int OtherServicesAfterTransport { get; init; }
        public TaskCompletionSource<OperateSectionResult<HonuaAdminServicePage>> HeldRead { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        private ServiceSummary[] Catalog => TransportServices is { } count
            ? Enumerable.Range(0, count).Select(index => new ServiceSummary { ServiceName = $"route-{index:D2}", FolderId = "transport", LayerCount = 1 })
                .Concat(Enumerable.Range(0, OtherServicesAfterTransport).Select(index => new ServiceSummary { ServiceName = $"zone-{index:D2}", FolderId = "other", LayerCount = 1 })).ToArray()
            : [new() { ServiceName = "roads", FolderId = "transport", LayerCount = 3, EnabledProtocols = ["wms", "wfs"] },
               new() { ServiceName = "other-service", FolderId = "other", LayerCount = 90 },
               new() { ServiceName = "bridges", FolderId = "transport", LayerCount = 4 }];

        public Task<OperateSectionResult<HonuaAdminServicePage>> ReadServicesAsync(HonuaAdminCatalogQuery query, CancellationToken cancellationToken)
        {
            Reads.Add(query);
            if ((HoldFirstRead && Reads.Count == 1) || Reads.Count == HoldReadNumber) return HeldRead.Task;
            if (HoldSearch is { } hold && query.NameQuery is not null && query.Page == 1) return ReadAfterAsync(hold.Task, query);
            if (Status is { } status && (StatusPage is null || StatusPage == query.Page)) return Task.FromResult(OperateSectionResult<HonuaAdminServicePage>.Denied(status, "Access denied"));
            return Task.FromResult(Page(query));
        }

        private OperateSectionResult<HonuaAdminServicePage> Page(HonuaAdminCatalogQuery query)
        {
            var matches = Catalog.Where(service => IgnoreFolder || service.FolderId == query.FolderId)
                .Where(service => query.NameQuery is null || service.ServiceName.Contains(query.NameQuery, StringComparison.OrdinalIgnoreCase)).ToArray();
            var items = matches.Skip((query.Page - 1) * query.PageSize).Take(query.PageSize).ToArray();
            var page = query.Page == 2 && MismatchedSecondPage ? 1 : query.Page;
            return OperateSectionResult<HonuaAdminServicePage>.Allowed(new()
            {
                Items = items,
                Page = page,
                PageSize = query.PageSize,
                Total = matches.Length,
                LayerTotal = matches.Sum(service => service.LayerCount)
            });
        }

        private async Task<OperateSectionResult<HonuaAdminServicePage>> ReadAfterAsync(Task gate, HonuaAdminCatalogQuery query)
        {
            await gate;
            return Page(query);
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
