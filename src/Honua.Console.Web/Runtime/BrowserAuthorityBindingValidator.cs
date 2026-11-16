using System.Text.Json;
using Honua.Console.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace Honua.Console.Web.Runtime;

internal sealed class BrowserAuthorityBindingValidator : IConsoleAuthorityBindingValidator, IConsoleAuthorityChangeSource, IDisposable
{
    private readonly ConsoleBrowserSession _session;
    private readonly Uri _apiBaseUri;
    private readonly ConsoleApiClient? _api;
    private readonly Timer _expiryTimer;
    private readonly object _expiryLock = new();
    private bool _disposed;

    public BrowserAuthorityBindingValidator(ConsoleBrowserSession session, Uri apiBaseUri, ConsoleApiClient? api = null)
    {
        _session = session;
        _apiBaseUri = apiBaseUri;
        _api = api;
        _expiryTimer = new Timer(_ => CheckExpiry());
        _session.Changed += SessionChanged;
        ScheduleExpiry();
    }

    public event Action? Changed;

    public Task<string?> CaptureAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(_session.CaptureAuthority(_apiBaseUri));

    public Task<bool> IsCurrentAsync(string binding, CancellationToken cancellationToken = default) =>
        Task.FromResult(!string.IsNullOrWhiteSpace(binding)
            && string.Equals(binding, _session.CaptureAuthority(_apiBaseUri), StringComparison.Ordinal));

    public async Task<bool> RevalidateAsync(string binding, CancellationToken cancellationToken = default)
    {
        if (_api is null || !await IsCurrentAsync(binding, cancellationToken)) return false;
        var claims = ClaimValues(_session.Principal);
        try
        {
            var fresh = await _api.GetSessionAsync(cancellationToken);
            return fresh.Principal.Identity?.IsAuthenticated == true
                && (fresh.Credentials?.ExpiresAt is not { } expiry || expiry > DateTimeOffset.UtcNow)
                && claims.SequenceEqual(ClaimValues(fresh.Principal))
                && await IsCurrentAsync(binding, cancellationToken);
        }
        catch (Exception exception) when (exception is HttpRequestException or OperationCanceledException or JsonException
            or InvalidOperationException or KeyNotFoundException or ArgumentException or FormatException)
        {
            return false;
        }
    }

    private static (string Type, string Value)[] ClaimValues(System.Security.Claims.ClaimsPrincipal principal) =>
        principal.Claims.Select(claim => (claim.Type, claim.Value))
            .OrderBy(claim => claim.Type, StringComparer.Ordinal)
            .ThenBy(claim => claim.Value, StringComparer.Ordinal).ToArray();

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
            new BrowserAuthorityBindingValidator(provider.GetRequiredService<ConsoleBrowserSession>(), apiBaseUri, provider.GetService<ConsoleApiClient>()));
}
