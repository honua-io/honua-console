using Microsoft.AspNetCore.Components.Authorization;

namespace Honua.Console.Shell.Services;

internal sealed class ConsoleEnvironmentSubscription : IDisposable
{
    private readonly IConsoleEnvironmentProfileStore? _profiles;
    private readonly AuthenticationStateProvider? _authentication;
    private readonly Action _refresh;
    private bool _disposed;

    public ConsoleEnvironmentSubscription(IConsoleEnvironmentProfileStore? profiles,
        AuthenticationStateProvider? authentication, Action refresh)
    {
        _profiles = profiles;
        _authentication = authentication;
        _refresh = refresh;
        if (_profiles is not null) _profiles.ActiveProfileChanged += Refresh;
        if (_authentication is not null) _authentication.AuthenticationStateChanged += OnAuthenticationChanged;
    }

    private void Refresh()
    {
        if (!_disposed) _refresh();
    }

    private void OnAuthenticationChanged(Task<AuthenticationState> state)
    {
        if (_disposed) return;
        if (_profiles is not null)
        {
            // The operator-scoped store binds its current principal when a handler is added.
            _profiles.ActiveProfileChanged -= Refresh;
            _profiles.ActiveProfileChanged += Refresh;
        }
        Refresh();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_profiles is not null) _profiles.ActiveProfileChanged -= Refresh;
        if (_authentication is not null) _authentication.AuthenticationStateChanged -= OnAuthenticationChanged;
    }
}
