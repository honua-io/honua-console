using Microsoft.AspNetCore.Components;

namespace Honua.Console.IntegrationTests;

internal sealed class MountedNavigationManager : NavigationManager
{
    public MountedNavigationManager(string applicationPath, string? route = null) =>
        Initialize("http://localhost" + applicationPath, "http://localhost" + applicationPath + (route ?? "").TrimStart('/'));

    protected override void NavigateToCore(string uri, bool forceLoad)
    {
        Uri = ToAbsoluteUri(uri).AbsoluteUri;
        NotifyLocationChanged(false);
    }
}
