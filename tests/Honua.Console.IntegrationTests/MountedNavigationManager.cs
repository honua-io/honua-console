using Microsoft.AspNetCore.Components;

namespace Honua.Console.IntegrationTests;

internal sealed class MountedNavigationManager : NavigationManager
{
    public MountedNavigationManager(string applicationPath) =>
        Initialize("http://localhost" + applicationPath, "http://localhost" + applicationPath);

    protected override void NavigateToCore(string uri, bool forceLoad)
    {
        Uri = ToAbsoluteUri(uri).AbsoluteUri;
        NotifyLocationChanged(false);
    }
}
