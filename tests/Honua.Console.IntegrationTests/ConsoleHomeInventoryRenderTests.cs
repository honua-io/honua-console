using Bunit;
using Honua.Console.Contracts;
using Honua.Console.Shell.Components;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Pages;
using Honua.Console.Shell.Services;
using Honua.Sdk.Admin.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;

namespace Honua.Console.IntegrationTests;

public sealed class ConsoleHomeInventoryRenderTests
{
    [Fact]
    public void InventoryUsesServerTotalsRatherThanTheFirstPageAndExcludesDeletedFolders()
    {
        using var context = new BunitContext();
        var source = new PublicationNavigationSource();
        context.Services.AddSingleton<IConsolePublicationNavigationSource>(source);
        context.Services.AddSingleton<IConsoleFolderClient>(new FolderReads());
        var inventory = context.Render<ConsoleHomeInventory>();
        inventory.WaitForAssertion(() => Assert.Equal("500", inventory.Find(".console-metric-value").TextContent.Trim()));
        Assert.Equal("5,000", inventory.Find("[data-home-layer-count]").TextContent.Trim());
        Assert.Equal("1", inventory.Find("[data-home-folder-count]").TextContent.Trim());
        Assert.Equal("/operate/services", inventory.Find("[data-home-services]").GetAttribute("href"));
        Assert.Equal(1, source.ServiceReads);
        Assert.Equal(0, source.ChildReads);
        Assert.Equal(0, source.ConnectionReads);
        CatalogAdminPreview.Capture("home-inventory", inventory.Markup);
    }

    [Theory]
    [InlineData(OperateSectionStatus.Forbidden)]
    [InlineData(OperateSectionStatus.Missing)]
    [InlineData(OperateSectionStatus.Unsupported)]
    public void DeniedInventoryPreservesTheReasonAndDoesNotClaimZero(OperateSectionStatus status)
    {
        using var context = new BunitContext();
        context.Services.AddSingleton<IConsolePublicationNavigationSource>(new PublicationNavigationSource
        {
            FailNext = OperateSectionResult<HonuaAdminServicePage>.Denied(status, "Services cannot be read by this user.")
        });
        var inventory = context.Render<ConsoleHomeInventory>();
        inventory.WaitForAssertion(() => Assert.Contains("Services cannot be read by this user.", inventory.Markup));
        Assert.Equal("Not available", inventory.Find(".console-metric-value").TextContent.Trim());
        Assert.Equal("Not available", inventory.Find("[data-home-layer-count]").TextContent.Trim());
        Assert.Equal("Not available", inventory.Find("[data-home-folder-count]").TextContent.Trim());
        Assert.Empty(inventory.FindAll("button"));
    }

    [Fact]
    public void PartialTotalsAreLabelledAsIncomplete()
    {
        using var context = new BunitContext();
        context.Services.AddSingleton<IConsolePublicationNavigationSource>(new PublicationNavigationSource
        {
            FailNext = new()
            {
                Status = OperateSectionStatus.Allowed,
                PartialResult = true,
                Value = new() { Total = 7, LayerTotal = 42 }
            }
        });
        var inventory = context.Render<ConsoleHomeInventory>();
        inventory.WaitForAssertion(() => Assert.Contains("Counts may be incomplete", inventory.Markup));
        Assert.Contains("Services in returned sources", inventory.Markup);
        Assert.Contains("Layers in returned sources", inventory.Markup);
        Assert.Equal("7", inventory.Find(".console-metric-value").TextContent.Trim());
    }

    [Fact]
    public void HungReadExpiresAndRetryRecoversWithoutWaitingForTheOldRead()
    {
        using var context = new BunitContext();
        var source = new PublicationNavigationSource { HoldCount = 1 };
        context.Services.AddSingleton<IConsolePublicationNavigationSource>(source);
        var inventory = context.Render<ConsoleHomeInventory>(parameters => parameters
            .Add(component => component.LoadBudget, TimeSpan.FromMilliseconds(25)));
        inventory.WaitForAssertion(() => Assert.Contains("did not respond in time", inventory.Markup));
        inventory.Find("button").Click();
        inventory.WaitForAssertion(() => Assert.Equal("500", inventory.Find(".console-metric-value").TextContent.Trim()));
        Assert.Equal(2, source.ServiceReads);
    }

    [Fact]
    public async Task EnvironmentChangeDiscardsThePreviousInventory()
    {
        using var context = HomeContext();
        var source = new PublicationNavigationSource { HoldCount = 1 };
        context.Services.AddSingleton<IConsolePublicationNavigationSource>(source);
        var profiles = context.Services.GetRequiredService<IConsoleEnvironmentProfileStore>();
        var home = context.Render<ConsoleHomePage>();
        home.WaitForAssertion(() => Assert.Equal(1, source.ServiceReads));
        await profiles.ActivateProfileAsync("second");
        home.WaitForAssertion(() => Assert.Equal("500", home.Find(".console-home-inventory .console-metric-value").TextContent.Trim()));
        source.ReleaseNext(OperateSectionResult<HonuaAdminServicePage>.Allowed(new() { Total = 99 }));
        home.WaitForAssertion(() => Assert.Equal("500", home.Find(".console-home-inventory .console-metric-value").TextContent.Trim()));
        Assert.Equal(2, source.ServiceReads);
    }

    [Fact]
    public void V4HomeShowsInventoryBeforeTheApprovalQueue()
    {
        using var context = HomeContext();
        context.Services.AddSingleton<IConsolePublicationNavigationSource>(new PublicationNavigationSource());
        context.Services.AddSingleton<IConsoleFolderClient>(new FolderReads());
        var home = context.Render<ConsoleHomePage>();
        home.WaitForAssertion(() => Assert.Equal("500", home.Find(".console-home-inventory .console-metric-value").TextContent.Trim()));
        Assert.True(home.Markup.IndexOf("home-inventory-heading", StringComparison.Ordinal)
            < home.Markup.IndexOf("data-open-inbox", StringComparison.Ordinal));
        CatalogAdminPreview.Capture("home-inventory-v4", home.Markup);
    }

    [Fact]
    public void LegacyHomeDoesNotReadInventory()
    {
        using var context = HomeContext();
        context.Services.AddSingleton(new ConsoleV4Options { Enabled = false });
        var source = new PublicationNavigationSource();
        context.Services.AddSingleton<IConsolePublicationNavigationSource>(source);
        var home = context.Render<ConsoleHomePage>();
        home.WaitForAssertion(() => Assert.Equal("Welcome", home.Find("h1").TextContent.Trim()));
        Assert.Empty(home.FindAll(".console-home-inventory"));
        Assert.Equal(0, source.ServiceReads);
        CatalogAdminPreview.Capture("home-inventory-legacy", home.Markup);
    }

    private static BunitContext HomeContext()
    {
        var context = new BunitContext();
        context.Services.AddSingleton(new ConsoleV4Options { Enabled = true });
        context.Services.AddSingleton(new CatalogAdminOptions());
        context.Services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        var proposals = new FakeConsoleProposalsClient(proposals: []);
        context.Services.AddSingleton<IConsoleProposalsClient>(proposals);
        context.Services.AddSingleton<IConsoleApprovalInboxClient>(new ConsoleApprovalInboxClient(proposals));
        context.Services.AddSingleton(ConsoleCapabilityTestManifest.All);
        context.Services.AddSingleton<IConsoleEnvironmentProfileStore>(new InMemoryConsoleEnvironmentProfileStore([
            new() { Id = "first", DisplayName = "Primary server" },
            new() { Id = "second", DisplayName = "Second server" }
        ], activeProfileId: "first"));
        return context;
    }

    private sealed class FolderReads : IConsoleFolderClient
    {
        public Task<OperateSectionResult<ConsoleFolderCatalog>> ReadAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(OperateSectionResult<ConsoleFolderCatalog>.Allowed(new([
                new() { Metadata = new() { Id = "transport", Name = "Transport" } },
                new() { Metadata = new() { Id = "old", Name = "Retired" }, Deleted = true },
                new() { Metadata = new() { Id = "", Name = "Unidentified" } }
            ], null, "synthetic-authority")));
        public Task<ConsoleFolderMutationResult> CreateAsync(ConsoleFolderCatalog catalog, CreateFolderRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ConsoleFolderMutationResult> UpdateAsync(ConsoleFolderCatalog catalog, string folderId, UpdateFolderRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ConsoleFolderMutationResult> DeleteAsync(ConsoleFolderCatalog catalog, string folderId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ConsoleFolderMutationResult> MoveAsync(ConsoleFolderCatalog catalog, string serviceName, MoveServiceRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
