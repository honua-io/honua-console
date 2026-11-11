using Microsoft.AspNetCore.Components;

namespace Honua.Console.Shell.Tests;

internal sealed class TestPageNavigationManager : NavigationManager
{
    public TestPageNavigationManager(string route) => Initialize("http://localhost/", $"http://localhost/{route}");

    protected override void NavigateToCore(string uri, bool forceLoad) => throw new NotSupportedException();
}
