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
        Assert.True(new HostedNavigationManager(BaseUri).TryGetConsoleHref(target, out var href));
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
    [InlineData("%2f%2fexternal.example")]
    [InlineData("operate%5c..%5c..%5cauth")]
    [InlineData("operate\n/jobs")]
    [InlineData("")]
    [InlineData(null)]
    public void UnsafeResultsDoNotProduceNavigation(string? target)
    {
        Assert.False(new HostedNavigationManager(BaseUri).TryGetConsoleHref(target, out var href));
        Assert.Empty(href);
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
        Assert.True(new HostedNavigationManager("https://honua.example/").TryGetConsoleHref("/operate/jobs/j1", out var href));
        Assert.Equal("./operate/jobs/j1", href);
    }
    private sealed class HostedNavigationManager : NavigationManager
    {
        public HostedNavigationManager(string baseUri) => Initialize(baseUri, baseUri);
    }
}
