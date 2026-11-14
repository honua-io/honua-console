using Bunit;
using Honua.Console.Shell.Pages;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;

namespace Honua.Console.IntegrationTests;

// The static bundle's audience viewer page: it mounts the native viewer module and releases it.
public sealed class PublishedMapPageRenderTests
{
    private const string ViewerModule = "./js/published-map-viewer.mjs";

    [Fact]
    public async Task ValidRouteMountsTheViewerOnceAndDisposesItWithThePage()
    {
        using var context = new BunitContext();
        var module = context.JSInterop.SetupModule(ViewerModule);
        var viewer = module.SetupModule(invocation => invocation.Identifier == "mountPublishedMap");
        viewer.SetupVoid("dispose").SetVoidResult();

        var page = context.Render<PublishedMapPage>(p => p.Add(c => c.Route, "maps/parcels"));

        var host = page.Find("[data-published-map]");
        Assert.Equal("maps/parcels", host.GetAttribute("data-published-route"));
        Assert.NotNull(page.Find("[data-map-canvas]"));
        Assert.Contains("Loading map", page.Find("[data-map-status]").TextContent, StringComparison.Ordinal);
        page.WaitForAssertion(() => Assert.Single(module.Invocations["mountPublishedMap"]));

        page.Render(p => p.Add(c => c.Route, "maps/parcels"));
        Assert.Single(module.Invocations["mountPublishedMap"]);

        await page.Instance.DisposeAsync();
        Assert.Single(viewer.Invocations["dispose"]);
    }

    [Fact]
    public void ChangingTheRouteReleasesTheOldViewerBeforeMountingTheNext()
    {
        using var context = new BunitContext();
        var module = context.JSInterop.SetupModule(ViewerModule);
        var viewer = module.SetupModule(invocation => invocation.Identifier == "mountPublishedMap");
        viewer.SetupVoid("dispose").SetVoidResult();

        var page = context.Render<PublishedMapPage>(p => p.Add(c => c.Route, "maps/first"));
        page.WaitForAssertion(() => Assert.Single(module.Invocations["mountPublishedMap"]));

        page.Render(p => p.Add(c => c.Route, "maps/second"));

        page.WaitForAssertion(() => Assert.Equal(2, module.Invocations["mountPublishedMap"].Count));
        Assert.Single(viewer.Invocations["dispose"]);
        Assert.Equal("maps/second", page.Find("[data-published-map]").GetAttribute("data-published-route"));
    }

    [Theory]
    [InlineData("maps/Hawai%CA%BBi%20%231", "maps/Hawaiʻi #1")]
    [InlineData("maps/Hawaiʻi #1", "maps/Hawaiʻi #1")]
    public void EncodedAndDecodedRoutesMountTheSamePublication(string route, string expected)
    {
        using var context = new BunitContext();
        var module = context.JSInterop.SetupModule(ViewerModule);
        module.SetupModule(invocation => invocation.Identifier == "mountPublishedMap");

        var page = context.Render<PublishedMapPage>(p => p.Add(c => c.Route, route));

        Assert.Equal(expected, page.Find("[data-published-map]").GetAttribute("data-published-route"));
        page.WaitForAssertion(() => Assert.Single(module.Invocations["mountPublishedMap"]));
    }

    [Theory]
    [InlineData("maps/%25252e%25252e")]
    [InlineData("maps/%2e%2e/admin")]
    [InlineData("")]
    [InlineData("maps/../admin")]
    [InlineData("maps/%2fadmin")]
    [InlineData("maps//parcels")]
    public void InvalidRouteShowsAMissingStateWithoutLoadingTheViewer(string route)
    {
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Strict;

        var page = context.Render<PublishedMapPage>(p => p.Add(c => c.Route, route));

        Assert.Empty(page.FindAll("[data-published-map]"));
        Assert.Contains("Published map not found", page.Markup, StringComparison.Ordinal);
        Assert.Empty(context.JSInterop.Invocations);
    }

    [Fact]
    public void AViewerThatCannotStartSaysSoInsteadOfLoadingForever()
    {
        using var context = new BunitContext();
        var runtime = new UnavailableModuleRuntime();
        context.Services.AddSingleton<IJSRuntime>(runtime);

        var page = context.Render<PublishedMapPage>(p => p.Add(c => c.Route, "maps/parcels"));

        page.WaitForAssertion(() => Assert.Contains("could not start",
            page.Find("[data-map-status]").TextContent, StringComparison.Ordinal));
        Assert.Equal(["import"], runtime.Identifiers);
    }

    private sealed class UnavailableModuleRuntime : IJSRuntime
    {
        public List<string> Identifiers { get; } = [];

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args)
        {
            Identifiers.Add(identifier);
            throw new JSException("module unavailable");
        }

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args)
            => InvokeAsync<TValue>(identifier, args);
    }
}
