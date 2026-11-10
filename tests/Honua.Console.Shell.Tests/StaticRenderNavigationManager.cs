using Microsoft.AspNetCore.Components;

namespace Honua.Console.Shell.Tests;

internal sealed class StaticRenderNavigationManager : NavigationManager
{
    public StaticRenderNavigationManager()
    {
        Initialize("https://localhost/", "https://localhost/");
    }

    protected override void NavigateToCore(string uri, bool forceLoad) =>
        throw new NotSupportedException("Static HTML rendering does not support navigation.");

    protected override void SetNavigationLockState(bool value)
    {
    }
}
