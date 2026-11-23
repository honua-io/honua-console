using System.Globalization;
using Bunit;
using Honua.Console.Shell.Layout;
using Honua.Console.Shell.Security;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

namespace Honua.Console.IntegrationTests;

public sealed class ConsolePendingReturnTests
{
    private const string StorageKey = "honua.console.pendingReturn";
    private static readonly DateTimeOffset Now = new(2026, 11, 23, 12, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(0, "/operate/settings?tab=policy", "/operate/settings?tab=policy")]
    [InlineData(11, "/operate/settings", null)]
    [InlineData(-1, "/operate/settings", null)]
    [InlineData(0, "//outside.invalid/path", null)]
    [InlineData(0, "/", null)]
    public async Task TakeReturnsOnlyAFreshSafeRouteAndAlwaysClearsIt(int minutesOld, string route, string? expected)
    {
        using var context = new BunitContext();
        context.JSInterop.Setup<string?>("sessionStorage.getItem", StorageKey).SetResult(Saved(minutesOld, route));
        var removal = context.JSInterop.SetupVoid("sessionStorage.removeItem", StorageKey);
        removal.SetVoidResult();

        var taken = await ConsolePendingReturn.TakeAsync(context.JSInterop.JSRuntime, Now);

        Assert.Equal(expected, taken);
        Assert.Single(removal.Invocations);
    }

    [Fact]
    public async Task SaveWithBlockedStorageLetsSignInContinue()
    {
        using var context = new BunitContext();
        context.JSInterop.SetupVoid("sessionStorage.setItem", _ => true)
            .SetException(new Microsoft.JSInterop.JSException("The quota has been exceeded."));

        await ConsolePendingReturn.SaveAsync(context.JSInterop.JSRuntime, "/operate/settings", Now);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TakeWithBlockedStorageReturnsNothing(bool removalBlocked)
    {
        using var context = new BunitContext();
        var read = context.JSInterop.Setup<string?>("sessionStorage.getItem", StorageKey);
        if (removalBlocked)
        {
            read.SetResult(Saved(0, "/operate/settings"));
            context.JSInterop.SetupVoid("sessionStorage.removeItem", StorageKey)
                .SetException(new Microsoft.JSInterop.JSException("Storage is unavailable."));
        }
        else
        {
            read.SetException(new Microsoft.JSInterop.JSException("Storage is unavailable."));
        }

        Assert.Null(await ConsolePendingReturn.TakeAsync(context.JSInterop.JSRuntime, Now));
    }

    [Fact]
    public async Task TakeWithNothingSavedReturnsNothing()
    {
        using var context = new BunitContext();
        context.JSInterop.Setup<string?>("sessionStorage.getItem", StorageKey).SetResult(null);

        Assert.Null(await ConsolePendingReturn.TakeAsync(context.JSInterop.JSRuntime, Now));
    }

    [Theory]
    [InlineData("http://localhost/console/", "http://localhost/console/operate/settings")]
    [InlineData("http://localhost/console/operate/connections", "http://localhost/console/operate/connections")]
    public void NavigatorOpensTheSavedPageOnlyFromHome(string landing, string expected)
    {
        using var context = new BunitContext();
        var navigation = new MountedNavigation(landing);
        context.Services.AddSingleton<NavigationManager>(navigation);
        context.JSInterop.Setup<string?>("sessionStorage.getItem", StorageKey)
            .SetResult(Saved(0, "/operate/settings", DateTimeOffset.UtcNow));
        context.JSInterop.SetupVoid("sessionStorage.removeItem", StorageKey).SetVoidResult();

        context.Render<ConsolePendingReturnNavigator>();

        Assert.Equal(expected, navigation.Uri);
        Assert.Equal(landing != expected, navigation.Replaced);
    }

    [Theory]
    [InlineData("99999999999999999|/operate/settings")]
    [InlineData("{now}|/%2e%2e/outside")]
    public void NavigatorIgnoresAMalformedSavedPage(string template)
    {
        var saved = template.Replace("{now}",
            DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal);
        using var context = new BunitContext();
        var navigation = new MountedNavigation("http://localhost/console/");
        context.Services.AddSingleton<NavigationManager>(navigation);
        context.JSInterop.Setup<string?>("sessionStorage.getItem", StorageKey).SetResult(saved);
        context.JSInterop.SetupVoid("sessionStorage.removeItem", StorageKey).SetVoidResult();

        context.Render<ConsolePendingReturnNavigator>();

        Assert.Equal("http://localhost/console/", navigation.Uri);
    }

    private static string Saved(int minutesOld, string route, DateTimeOffset? now = null) =>
        string.Create(CultureInfo.InvariantCulture,
            $"{((now ?? Now) - TimeSpan.FromMinutes(minutesOld)).ToUnixTimeMilliseconds()}|{route}");

    private sealed class MountedNavigation : NavigationManager
    {
        public MountedNavigation(string uri) => Initialize("http://localhost/console/", uri);

        public bool Replaced { get; private set; }

        protected override void NavigateToCore(string uri, NavigationOptions options)
        {
            Uri = ToAbsoluteUri(uri).AbsoluteUri;
            Replaced = options.ReplaceHistoryEntry;
        }
    }
}
