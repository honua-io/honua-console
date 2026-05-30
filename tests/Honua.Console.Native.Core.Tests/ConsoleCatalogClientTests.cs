using Honua.Console.Contracts;
using Honua.Console.Shell.DependencyInjection;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Pages;
using Honua.Console.Shell.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Honua.Console.Native.Core.Tests;

public sealed class ConsoleCatalogClientTests
{
    [Fact]
    public void RuntimeShellRegistrationDoesNotUseSeededInMemoryCatalog()
    {
        var services = new ServiceCollection();

        services.AddHonuaConsoleShell();

        using var provider = services.BuildServiceProvider();
        var catalog = provider.GetRequiredService<IConsoleCatalogClient>();

        Assert.IsNotType<InMemoryConsoleCatalogClient>(catalog);
        Assert.IsType<UnsupportedConsoleCatalogClient>(catalog);
    }

    [Fact]
    public void DemoCatalogContentRegistrationRestoresInMemorySource()
    {
        var services = new ServiceCollection();

        services.AddHonuaConsoleShell();
        services.AddHonuaConsoleDemoCatalogContent();

        using var provider = services.BuildServiceProvider();
        var catalog = provider.GetRequiredService<IConsoleCatalogClient>();

        Assert.IsType<InMemoryConsoleCatalogClient>(catalog);
    }

    [Fact]
    public async Task MissingServerBindingReturnsEmptySearchAndTypedUnavailableReads()
    {
        var catalog = new UnsupportedConsoleCatalogClient();

        var search = await catalog.SearchAsync(new CatalogListRequest(), CatalogReadContext.Authenticated);
        var item = await catalog.GetCatalogItemAsync("coastal-flood-service", CatalogReadContext.Authenticated);
        var openData = await catalog.GetOpenDataItemAsync("coastal-flood-service");
        var map = await catalog.GetMapPackageAsync("storm-response-map", CatalogReadContext.Authenticated);
        var draft = await catalog.GetDraftMapAsync("utilities-layer", CatalogReadContext.Authenticated);
        var embed = await catalog.AuthorizeEmbedAsync(
            "storm-response-map",
            EmbedRouteOptions.FromUri("https://console.example/embed/maps/storm-response-map#embedToken=embed-storm-map"));

        Assert.Empty(search.Items);
        Assert.Empty(search.TypeCounts);
        Assert.Equal(CatalogReadStatus.Unavailable, item.Status);
        Assert.Equal(CatalogReadStatus.Unavailable, openData.Status);
        Assert.Equal(CatalogReadStatus.Unavailable, map.Status);
        Assert.Equal(CatalogReadStatus.Unavailable, draft.Status);
        Assert.Equal(CatalogReadStatus.Unavailable, embed.Status);
        Assert.Contains("Honua:Server:BaseUrl", item.Message, StringComparison.Ordinal);
        Assert.Contains("Honua:Server:BaseUrl", map.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CatalogSearchFiltersProtectedSummariesForAnonymousContext()
    {
        var catalog = new InMemoryConsoleCatalogClient();

        var anonymous = await catalog.SearchAsync(
            new CatalogListRequest(),
            CatalogReadContext.AnonymousPublicLink(token: null));
        var authenticated = await catalog.SearchAsync(
            new CatalogListRequest(),
            CatalogReadContext.Authenticated);

        Assert.Contains(anonymous.Items, item => item.Id == "svc-coastal-flood");
        Assert.DoesNotContain(anonymous.Items, item => item.Id == "lyr-utilities");
        Assert.DoesNotContain(anonymous.Items, item => item.Id == "dash-capital-projects");
        Assert.Contains(authenticated.Items, item => item.Id == "lyr-utilities");
        Assert.Contains(authenticated.Items, item => item.Id == "dash-capital-projects");
    }

    [Fact]
    public async Task PublicLinkCatalogReadRequiresMatchingToken()
    {
        var catalog = new InMemoryConsoleCatalogClient();

        var denied = await catalog.GetCatalogItemAsync(
            "utilities-layer",
            CatalogReadContext.AnonymousPublicLink("wrong-token"));
        var allowed = await catalog.GetCatalogItemAsync(
            "utilities-layer",
            CatalogReadContext.AnonymousPublicLink("pl-utilities"));

        Assert.Equal(CatalogReadStatus.Unavailable, denied.Status);
        Assert.Equal(CatalogReadStatus.Allowed, allowed.Status);
        Assert.True(allowed.AnonymousRead);
        Assert.Equal("Utilities Critical Layer", allowed.Item?.Summary.Title);
    }

    [Fact]
    public async Task DraftMapHydrationRequiresAuthenticatedContext()
    {
        var catalog = new InMemoryConsoleCatalogClient();

        var noSession = await catalog.GetDraftMapAsync(
            "utilities-layer",
            CatalogReadContext.AnonymousPublicLink(token: null));
        var publicLink = await catalog.GetDraftMapAsync(
            "utilities-layer",
            CatalogReadContext.AnonymousPublicLink("pl-utilities"));
        var authenticated = await catalog.GetDraftMapAsync(
            "utilities-layer",
            CatalogReadContext.Authenticated);

        Assert.Equal(CatalogReadStatus.Unavailable, noSession.Status);
        Assert.Equal(CatalogReadStatus.Unavailable, publicLink.Status);
        Assert.Equal(CatalogReadStatus.Allowed, authenticated.Status);
        Assert.Equal("Utilities Critical Layer draft map", authenticated.MapPackage?.Summary.Title);
    }

    [Fact]
    public async Task DraftMapHydrationReturnsMissingForUnknownSource()
    {
        var catalog = new InMemoryConsoleCatalogClient();

        var result = await catalog.GetDraftMapAsync(
            "missing-source",
            CatalogReadContext.Authenticated);
        var state = MapDraftPage.ResolveReadState(result.Status, result.Message);

        Assert.Equal(CatalogReadStatus.Missing, result.Status);
        Assert.Null(result.MapPackage);
        Assert.Equal("missing", state.Kind);
    }

    [Fact]
    public async Task DraftMapHydrationRejectsSavedMapSources()
    {
        var catalog = new InMemoryConsoleCatalogClient();

        var result = await catalog.GetDraftMapAsync(
            "storm-response-map",
            CatalogReadContext.Authenticated);

        Assert.Equal(CatalogReadStatus.UnsupportedServiceMetadata, result.Status);
        Assert.Null(result.MapPackage);
    }

    [Fact]
    public async Task PublicLinkMapReadFollowsMapsRouteTokenContract()
    {
        var catalog = new InMemoryConsoleCatalogClient();

        var denied = await catalog.GetMapPackageAsync(
            "storm-response-map",
            CatalogReadContext.AnonymousPublicLink("wrong-token"));
        var allowed = await catalog.GetMapPackageAsync(
            "storm-response-map",
            CatalogReadContext.AnonymousPublicLink("pl-storm-map"));

        Assert.Equal(CatalogReadStatus.Unavailable, denied.Status);
        Assert.Equal(CatalogReadStatus.Allowed, allowed.Status);
        Assert.True(allowed.AnonymousRead);
        Assert.Equal("Storm Response Map", allowed.MapPackage?.Summary.Title);
    }

    [Fact]
    public async Task NoSessionTokenlessReadsUseAnonymousPublicContext()
    {
        var catalog = new InMemoryConsoleCatalogClient();
        var resolver = new ConsoleCatalogReadContextResolver(
            InMemoryConsoleEnvironmentProfileStore.CreateSeeded(),
            new InMemoryConsoleAccountSessionStore());

        var context = await resolver.ResolveAsync(publicLinkToken: null);
        var item = await catalog.GetCatalogItemAsync("coastal-flood-service", context);
        var map = await catalog.GetMapPackageAsync("public-field-map", context);

        Assert.True(context.Anonymous);
        Assert.Equal(CatalogReadStatus.Allowed, item.Status);
        Assert.True(item.AnonymousRead);
        Assert.Equal(CatalogReadStatus.Allowed, map.Status);
        Assert.True(map.AnonymousRead);
        Assert.Equal("Public Field Map", map.MapPackage?.Summary.Title);
        Assert.DoesNotContain(
            ConsoleCatalogActionPolicy.Resolve(item.Item!.Summary, isAuthenticated: !item.AnonymousRead),
            action => action.Id is "studio" or "share");
    }

    [Fact]
    public async Task ActiveSessionTokenlessReadsUseAuthenticatedContext()
    {
        var profiles = InMemoryConsoleEnvironmentProfileStore.CreateSeeded();
        var sessions = new InMemoryConsoleAccountSessionStore();
        await sessions.SaveSessionAsync(new ConsoleAccountSession
        {
            ProfileId = ConsoleEnvironmentProfileDefaults.DevelopmentProfileId,
            AccountId = "operator.dev",
            TenantId = "honua-dev",
            AccessToken = "dev-session-token"
        });
        var resolver = new ConsoleCatalogReadContextResolver(profiles, sessions);
        var catalog = new InMemoryConsoleCatalogClient();

        var context = await resolver.ResolveAsync(publicLinkToken: null);
        var orgItem = await catalog.GetCatalogItemAsync("capital-projects-dashboard", context);

        Assert.False(context.Anonymous);
        Assert.Equal(CatalogReadStatus.Allowed, orgItem.Status);
        Assert.False(orgItem.AnonymousRead);
    }

    [Fact]
    public async Task ActiveSessionTokenizedCatalogAndMapReadsUseAuthenticatedContext()
    {
        var profiles = InMemoryConsoleEnvironmentProfileStore.CreateSeeded();
        var sessions = new InMemoryConsoleAccountSessionStore();
        await sessions.SaveSessionAsync(new ConsoleAccountSession
        {
            ProfileId = ConsoleEnvironmentProfileDefaults.DevelopmentProfileId,
            AccountId = "operator.dev",
            TenantId = "honua-dev",
            AccessToken = "dev-session-token"
        });
        var resolver = new ConsoleCatalogReadContextResolver(profiles, sessions);
        var catalog = new InMemoryConsoleCatalogClient();

        var context = await resolver.ResolveAsync(publicLinkToken: "stale-token");
        var linkItem = await catalog.GetCatalogItemAsync("utilities-layer", context);
        var linkMap = await catalog.GetMapPackageAsync("storm-response-map", context);
        var actions = ConsoleCatalogActionPolicy.Resolve(
            linkItem.Item!.Summary,
            isAuthenticated: !linkItem.AnonymousRead,
            publicLinkToken: "stale-token");

        Assert.False(context.Anonymous);
        Assert.Equal(CatalogReadStatus.Allowed, linkItem.Status);
        Assert.False(linkItem.AnonymousRead);
        Assert.Equal(CatalogReadStatus.Allowed, linkMap.Status);
        Assert.False(linkMap.AnonymousRead);
        Assert.Contains(actions, action => action.Id == "studio");
        Assert.Contains(actions, action => action.Id == "share");
        Assert.DoesNotContain(actions, action => action.Href.Contains("token=", StringComparison.Ordinal));
    }

    [Fact]
    public async Task EmbedAuthorizationRequiresFragmentTokenAndRejectsQueryBearer()
    {
        var catalog = new InMemoryConsoleCatalogClient();

        var queryBearer = await catalog.AuthorizeEmbedAsync(
            "storm-response-map",
            EmbedRouteOptions.FromUri("https://console.example/embed/maps/storm-response-map?embedToken=embed-storm-map"));
        var fragmentBearer = await catalog.AuthorizeEmbedAsync(
            "storm-response-map",
            EmbedRouteOptions.FromUri("https://console.example/embed/maps/storm-response-map#embedToken=embed-storm-map"));

        Assert.Equal(CatalogReadStatus.Unavailable, queryBearer.Status);
        Assert.Equal(CatalogReadStatus.Allowed, fragmentBearer.Status);
        Assert.True(fragmentBearer.AnonymousRead);
    }

    [Fact]
    public async Task UnsupportedStatesUseSharedReadStatuses()
    {
        var catalog = new InMemoryConsoleCatalogClient();

        var legacyService = await catalog.GetCatalogItemAsync(
            "legacy-parcels-service",
            CatalogReadContext.Authenticated);
        var futureMap = await catalog.GetMapPackageAsync(
            "future-response-map",
            CatalogReadContext.Authenticated);

        Assert.Equal(CatalogReadStatus.UnsupportedServiceMetadata, legacyService.Status);
        Assert.Equal(CatalogReadStatus.UnsupportedPackageBinding, futureMap.Status);
    }

    [Fact]
    public async Task OpenDataRouteOnlyExposesEligiblePublicItems()
    {
        var catalog = new InMemoryConsoleCatalogClient();

        var publicService = await catalog.GetOpenDataItemAsync("coastal-flood-service");
        var publicButNotOpenDataMap = await catalog.GetOpenDataItemAsync("future-response-map");
        var publicLinkLayer = await catalog.GetOpenDataItemAsync("utilities-layer");

        Assert.Equal(CatalogReadStatus.Allowed, publicService.Status);
        Assert.True(publicService.AnonymousRead);
        Assert.Equal(CatalogReadStatus.Missing, publicButNotOpenDataMap.Status);
        Assert.Equal(CatalogReadStatus.Missing, publicLinkLayer.Status);
    }

    [Fact]
    public async Task ShareLinksUseCatalogForNonMapsAndMapsForMapItems()
    {
        var catalog = new InMemoryConsoleCatalogClient();
        var layer = await catalog.GetCatalogItemAsync(
            "utilities-layer",
            CatalogReadContext.Authenticated);
        var map = await catalog.GetCatalogItemAsync(
            "storm-response-map",
            CatalogReadContext.Authenticated);

        Assert.Equal(
            "/catalog/utilities-layer?token=pl-utilities",
            ConsoleShareLinkBuilder.BuildRelativeShareLink(layer.Item!.Summary));
        Assert.Equal(
            "/maps/storm-response-map?token=pl-storm-map",
            ConsoleShareLinkBuilder.BuildRelativeShareLink(map.Item!.Summary));
    }

    [Fact]
    public async Task ActionPolicyHidesStudioAndShareActionsForAnonymousReads()
    {
        var catalog = new InMemoryConsoleCatalogClient();
        var result = await catalog.GetCatalogItemAsync(
            "storm-response-map",
            CatalogReadContext.AnonymousPublicLink("pl-storm-map"));

        var anonymousActions = ConsoleCatalogActionPolicy.Resolve(result.Item!.Summary, isAuthenticated: false);
        var authenticatedActions = ConsoleCatalogActionPolicy.Resolve(result.Item!.Summary, isAuthenticated: true);

        Assert.DoesNotContain(anonymousActions, action => action.Id == "studio");
        Assert.DoesNotContain(anonymousActions, action => action.Id == "share");
        Assert.Contains(authenticatedActions, action => action.Id == "studio");
        Assert.Contains(authenticatedActions, action => action.Id == "share");
        Assert.Contains(authenticatedActions, action => action.Id == "viewer" && action.Href == "/maps/storm-response-map");
    }

    [Fact]
    public async Task MapViewerActionsFollowCatalogItemPolicy()
    {
        var catalog = new InMemoryConsoleCatalogClient();
        var viewerMap = await catalog.GetMapPackageAsync(
            "public-field-map",
            CatalogReadContext.Authenticated);
        var ownerMap = await catalog.GetMapPackageAsync(
            "storm-response-map",
            CatalogReadContext.Authenticated);

        var viewerActions = MapViewerPage.ResolveViewerActions(
            viewerMap.MapPackage!.Summary,
            isAuthenticated: true);
        var ownerActions = MapViewerPage.ResolveViewerActions(
            ownerMap.MapPackage!.Summary,
            isAuthenticated: true);
        var anonymousActions = MapViewerPage.ResolveViewerActions(
            ownerMap.MapPackage!.Summary,
            isAuthenticated: false);

        Assert.Contains(viewerActions, action => action.Id == "detail");
        Assert.DoesNotContain(viewerActions, action => action.Id is "studio" or "share");
        Assert.Contains(
            ownerActions,
            action => action.Id == "studio" && action.Href == "/studio?source=map&itemId=map-storm-response");
        Assert.Contains(ownerActions, action => action.Id == "share");
        Assert.DoesNotContain(ownerActions, action => action.Id is "viewer" or "retire");
        Assert.Empty(anonymousActions);
    }

    [Fact]
    public async Task AnonymousReadLabelsDistinguishPublicReadsFromPublicLinkReads()
    {
        var catalog = new InMemoryConsoleCatalogClient();
        var publicItem = await catalog.GetCatalogItemAsync(
            "coastal-flood-service",
            CatalogReadContext.AnonymousPublicLink(token: null));
        var publicMap = await catalog.GetMapPackageAsync(
            "public-field-map",
            CatalogReadContext.AnonymousPublicLink(token: null));
        var publicLinkItem = await catalog.GetCatalogItemAsync(
            "utilities-layer",
            CatalogReadContext.AnonymousPublicLink("pl-utilities"));
        var publicLinkMap = await catalog.GetMapPackageAsync(
            "storm-response-map",
            CatalogReadContext.AnonymousPublicLink("pl-storm-map"));

        Assert.Equal("Public read", ConsoleAnonymousReadLabels.ReadLabel(publicItem.Item!.Summary));
        Assert.Equal("Public read", ConsoleAnonymousReadLabels.ReadLabel(publicMap.MapPackage!.Summary));
        Assert.Equal("Public-link read", ConsoleAnonymousReadLabels.ReadLabel(publicLinkItem.Item!.Summary));
        Assert.Equal("Public-link read", ConsoleAnonymousReadLabels.ReadLabel(publicLinkMap.MapPackage!.Summary));
        Assert.Equal(
            "Anonymous public reads do not expose workspace permission details.",
            ConsoleAnonymousReadLabels.PermissionsHiddenMessage(publicItem.Item.Summary));
        Assert.Equal(
            "Anonymous public-link reads do not expose workspace permission details.",
            ConsoleAnonymousReadLabels.PermissionsHiddenMessage(publicLinkItem.Item.Summary));
    }

    [Fact]
    public async Task PublicLinkAnonymousActionsPreserveTokenForFollowableRoutes()
    {
        var catalog = new InMemoryConsoleCatalogClient();
        var map = await catalog.GetCatalogItemAsync(
            "storm-response-map",
            CatalogReadContext.AnonymousPublicLink("pl-storm-map"));
        var layer = await catalog.GetCatalogItemAsync(
            "utilities-layer",
            CatalogReadContext.AnonymousPublicLink("pl-utilities"));

        var mapActions = ConsoleCatalogActionPolicy.Resolve(
            map.Item!.Summary,
            isAuthenticated: false,
            publicLinkToken: "pl-storm-map");
        var layerActions = ConsoleCatalogActionPolicy.Resolve(
            layer.Item!.Summary,
            isAuthenticated: false,
            publicLinkToken: "pl-utilities");

        Assert.Contains(mapActions, action => action.Id == "detail" && action.Href == "/catalog/storm-response-map?token=pl-storm-map");
        Assert.Contains(mapActions, action => action.Id == "viewer" && action.Href == "/maps/storm-response-map?token=pl-storm-map");
        Assert.Contains(layerActions, action => action.Id == "detail" && action.Href == "/catalog/utilities-layer?token=pl-utilities");
        Assert.DoesNotContain(layerActions, action => action.Id == "viewer");
        Assert.DoesNotContain(mapActions, action => action.Id is "studio" or "share");
        Assert.DoesNotContain(layerActions, action => action.Id is "studio" or "share");
    }
}
