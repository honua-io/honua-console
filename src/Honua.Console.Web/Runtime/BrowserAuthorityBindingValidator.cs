using Honua.Console.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace Honua.Console.Web.Runtime;

internal sealed class BrowserAuthorityBindingValidator : IConsoleAuthorityBindingValidator, IConsoleAuthorityChangeSource, IDisposable
{
    private readonly ConsoleBrowserSession _session;
    private readonly Uri _apiBaseUri;
    private readonly Timer _expiryTimer;
    private readonly object _expiryLock = new();
    private bool _disposed;

    public BrowserAuthorityBindingValidator(ConsoleBrowserSession session, Uri apiBaseUri)
    {
        _session = session;
        _apiBaseUri = apiBaseUri;
        _expiryTimer = new Timer(_ => CheckExpiry());
        _session.Changed += SessionChanged;
        ScheduleExpiry();
    }

    public event Action? Changed;

    public Task<string?> CaptureAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(_session.CaptureAuthority(_apiBaseUri));

    public Task<bool> IsCurrentAsync(string binding, CancellationToken cancellationToken = default) =>
        Task.FromResult(string.Equals(binding, _session.CaptureAuthority(_apiBaseUri), StringComparison.Ordinal));

    private void SessionChanged(ConsoleBrowserSessionSnapshot snapshot)
    {
        ScheduleExpiry();
        Changed?.Invoke();
    }

    private void ScheduleExpiry()
    {
        var delay = _session.ExpiresAt is { } expiry ? expiry - DateTimeOffset.UtcNow : Timeout.InfiniteTimeSpan;
        if (_session.ExpiresAt is not null) delay = TimeSpan.FromMilliseconds(Math.Clamp(delay.TotalMilliseconds, 0, TimeSpan.FromDays(1).TotalMilliseconds));
        lock (_expiryLock)
        {
            if (!_disposed) _expiryTimer.Change(delay, Timeout.InfiniteTimeSpan);
        }
    }

    private void CheckExpiry()
    {
        if (_session.IsAuthenticated) ScheduleExpiry();
        else Changed?.Invoke();
    }

    public void Dispose()
    {
        _session.Changed -= SessionChanged;
        lock (_expiryLock)
        {
            _disposed = true;
            _expiryTimer.Dispose();
        }
    }
}

internal static class BrowserAuthorityServices
{
    internal static IServiceCollection AddBrowserAuthorityBinding(this IServiceCollection services, Uri apiBaseUri) =>
        services.AddSingleton<IConsoleAuthorityBindingValidator>(provider =>
            new BrowserAuthorityBindingValidator(provider.GetRequiredService<ConsoleBrowserSession>(), apiBaseUri));
}
