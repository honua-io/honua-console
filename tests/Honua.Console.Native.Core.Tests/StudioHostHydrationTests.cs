using Honua.Console.Shell.Pages;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.JSInterop;

namespace Honua.Console.Native.Core.Tests;

public sealed class StudioHostHydrationTests
{
    [Fact]
    public async Task StaticPrerenderDoesNotExposeTheMutableStudioMount()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        services.AddSingleton<NavigationManager>(new TestNavigation());
        services.AddSingleton<IJSRuntime, NoJs>();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?> { ["Studio:EmbedJsStudio"] = "true" }).Build());
        await using var provider = services.BuildServiceProvider();
        await using var renderer = new HtmlRenderer(provider, provider.GetRequiredService<ILoggerFactory>());
        var html = await renderer.Dispatcher.InvokeAsync(async () =>
            (await renderer.RenderComponentAsync<StudioPage>(ParameterView.Empty)).ToHtmlString());
        Assert.DoesNotContain("data-studio-embed", html, StringComparison.Ordinal);
        Assert.Contains("Connecting", html, StringComparison.Ordinal);
    }

    private sealed class TestNavigation : NavigationManager
    {
        public TestNavigation() => Initialize("http://localhost/", "http://localhost/studio");
        protected override void NavigateToCore(string uri, bool forceLoad) => throw new NotSupportedException();
        protected override void SetNavigationLockState(bool value) { }
    }

    private sealed class NoJs : IJSRuntime
    {
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) => throw new NotSupportedException();
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args) => throw new NotSupportedException();
    }
}
