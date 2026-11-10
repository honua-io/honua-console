using Honua.Console.Shell.Services;
using Microsoft.AspNetCore.Components;

namespace Honua.Console.Shell.Tests;

public sealed class ConsoleNavigationPathsTests
{
    private const string BaseUri = "https://honua.example/console/";

    [Theory]
    [InlineData("/operate/jobs/job%201?view=log#events", "./operate/jobs/job%201?view=log#events")]
    [InlineData("/data/services/service%3Aone/layers/0", "./data/services/service%3Aone/layers/0")]
    [InlineData("/data/services/service%2Fone/layers/0", "./data/services/service%2Fone/layers/0")]
    [InlineData("./operate/jobs/job-1", "./operate/jobs/job-1")]
    [InlineData("operate/jobs/job-1", "./operate/jobs/job-1")]
    [InlineData("/console/operate/jobs/job-1", "./operate/jobs/job-1")]
    [InlineData("https://honua.example/console/inbox?proposalId=p1", "./inbox?proposalId=p1")]
    [InlineData("/", "./")]
    [InlineData("/console/", "./")]
    [InlineData("?tab=jobs#events", "./?tab=jobs#events")]
    public void LocalResultsStayUnderConsoleMount(string target, string expected)
    {
        var href = new HostedNavigationManager(BaseUri).GetConsoleHref(target);
        Assert.NotNull(href);
        Assert.Equal(expected, href);
        Assert.StartsWith(BaseUri, new Uri(new Uri(BaseUri), href).AbsoluteUri);
    }

    [Theory]
    [InlineData("https://external.example/console/operate")]
    [InlineData("https://honua.example/operate")]
    [InlineData("//external.example/console/")]
    [InlineData("javascript:alert(1)")]
    [InlineData("../operate")]
    [InlineData("/operate/../../auth")]
    [InlineData("%2e%2e/operate")]
    [InlineData("operate%2F..%2F..%2Fauth")]
    [InlineData("operate%2F%2e%2e%2Fauth")]
    [InlineData("%2f%2fexternal.example")]
    [InlineData("operate%5c..%5c..%5cauth")]
    [InlineData("operate\n/jobs")]
    [InlineData("")]
    [InlineData(null)]
    public void UnsafeResultsDoNotProduceNavigation(string? target)
    {
        Assert.Null(new HostedNavigationManager(BaseUri).GetConsoleHref(target));
    }

    [Fact]
    public void KnownLogicalRoutesPreserveEscapedResourceIdentifiers()
    {
        Assert.Equal("/console/data/services/service%3Aone/layers/0",
            new HostedNavigationManager(BaseUri).ToConsoleHref("/data/services/service%3Aone/layers/0"));
    }

    [Fact]
    public void RootHostedConsoleRemainsSupported()
    {
        var href = new HostedNavigationManager("https://honua.example/").GetConsoleHref("/operate/jobs/j1");
        Assert.Equal("./operate/jobs/j1", href);
    }
    [Theory]
    [InlineData("/catalog/item", "/console/catalog/item")]
    [InlineData("/studio?tab=flows#new", "/console/studio?tab=flows#new")]
    [InlineData("/auth/signin?returnTo=%2F", "/console/auth/signin?returnTo=%2F")]
    [InlineData("/console/operate", "/console/operate")]
    [InlineData("/api/v1/items/1", "/api/v1/items/1")]
    [InlineData("/rest/services/a", "/rest/services/a")]
    [InlineData("/admin/auth/callback", "/admin/auth/callback")]
    [InlineData("/auth/logout", "/auth/logout")]
    [InlineData("https://external.example/studio", "https://external.example/studio")]
    [InlineData("#details", "#details")]
    [InlineData("?tab=jobs", "?tab=jobs")]
    public void MixedLinksKeepServerAndExternalDestinations(string target, string expected)
    {
        Assert.Equal(expected, new HostedNavigationManager(BaseUri).ToConsoleLinkHref(target));
    }

    [Fact]
    public void EveryRoutedPageUsesTheConsoleMountInMixedLinks()
    {
        var navigation = new HostedNavigationManager(BaseUri);
        var routes = typeof(ConsoleNavigationPaths).Assembly.GetTypes()
            .SelectMany(type => type.GetCustomAttributes(typeof(RouteAttribute), inherit: false))
            .Cast<RouteAttribute>()
            .Select(attribute => attribute.Template);

        foreach (var route in routes)
            Assert.Equal(navigation.ToConsoleHref(route), navigation.ToConsoleLinkHref(route));
    }

    private sealed class HostedNavigationManager : NavigationManager
    {
        public HostedNavigationManager(string baseUri) => Initialize(baseUri, baseUri);
    }
}
