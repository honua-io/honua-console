using Honua.Console.Contracts;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Services;

namespace Honua.Console.Native.Core.Tests;

public sealed class ConsoleCatalogClientTests
{
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
}
