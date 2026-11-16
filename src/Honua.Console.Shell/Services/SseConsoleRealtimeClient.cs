using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Honua.Console.Contracts;
using Microsoft.Extensions.Logging;

namespace Honua.Console.Shell.Services;

public abstract class SseConsoleRealtimeClient : IConsoleRealtimeCapabilityClient, IConsoleRealtimeRefreshClient
{
    private readonly HttpClient _http;
    private readonly IConsoleEnvironmentProfileStore _profiles;
    private readonly IConsoleAuthorityBindingValidator? _authority;
    private readonly ConsoleRealtimeFallbackTracker _tracker;
    private readonly string _topic;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private CancellationTokenSource? _lifetime;
    private Task? _pump;

    protected SseConsoleRealtimeClient(HttpClient http, IConsoleEnvironmentProfileStore profiles,
        IConsoleAuthorityBindingValidator? authority, ILogger logger, string topic)
    {
        _http = http;
        _profiles = profiles;
        _authority = authority;
        _topic = topic;
        _tracker = new ConsoleRealtimeFallbackTracker(logger, "api/v1/admin/live");
    }

    public ConsoleRealtimeConnectionState ConnectionState => _tracker.State;
    public bool IsConnected => ConnectionState == ConsoleRealtimeConnectionState.Connected;
    public bool IsFallbackEngaged => ConnectionState == ConsoleRealtimeConnectionState.FallbackEngaged;
    public event Action<ConsoleRealtimeConnectionState>? ConnectionStateChanged
    {
        add => _tracker.StateChanged += value;
        remove => _tracker.StateChanged -= value;
    }
    public event Func<Task>? RefreshRequired;

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_pump is { IsCompleted: false }) return;
            _lifetime?.Dispose();
            _lifetime = new CancellationTokenSource();
            var profile = await _profiles.GetActiveProfileAsync(cancellationToken).ConfigureAwait(false);
            var binding = _authority is null ? null : await _authority.CaptureAsync(cancellationToken).ConfigureAwait(false);
            if (profile is null || !profile.TransportCapabilities.BrowserRealtime || binding is null)
            {
                _tracker.MarkNotConfigured();
                return;
            }
            _pump = PumpAsync(profile.Id, profile.ServerBaseUri, binding, _lifetime.Token);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _tracker.MarkFallbackEngaged("Admin events could not resolve the current session.");
        }
        finally { _gate.Release(); }
    }

    private async Task PumpAsync(string profileId, Uri server, string binding, CancellationToken token)
    {
        using var authorityLifetime = CancellationTokenSource.CreateLinkedTokenSource(token);
        void AuthorityChanged() => authorityLifetime.Cancel();
        var changes = _authority as IConsoleAuthorityChangeSource;
        if (changes is not null) changes.Changed += AuthorityChanged;
        _profiles.ActiveProfileChanged += AuthorityChanged;
        var stop = authorityLifetime.Token;
        long? cursor = null;
        var delay = 1;
        try
        {
            while (!stop.IsCancellationRequested)
            {
                try
                {
                    await EnsureCurrentAsync(profileId, server, binding, stop).ConfigureAwait(false);
                    _tracker.MarkConnecting();
                    using var request = new HttpRequestMessage(HttpMethod.Get,
                        ConsoleServerHttp.BuildUri(server, $"api/v1/admin/live?topics={_topic}"));
                    request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
                    request.Options.Set(ConsoleAuthorityRequestOptions.Expected, binding);
                    if (cursor is { } after)
                        request.Headers.Add("Last-Event-ID", "admin:" + after.ToString(CultureInfo.InvariantCulture));
                    using var headersDeadline = CancellationTokenSource.CreateLinkedTokenSource(stop);
                    headersDeadline.CancelAfter(TimeSpan.FromSeconds(45));
                    using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, headersDeadline.Token).ConfigureAwait(false);
                    headersDeadline.CancelAfter(Timeout.InfiniteTimeSpan);
                    if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                        throw new InvalidDataException("The current session cannot subscribe to admin events.");
                    response.EnsureSuccessStatusCode();
                    if (response.Content.Headers.ContentType?.MediaType != "text/event-stream")
                        throw new InvalidDataException("The admin stream returned an unexpected response.");
                    var reset = false;
                    await using var stream = await response.Content.ReadAsStreamAsync(stop).ConfigureAwait(false);
                    await foreach (var frame in ConsoleAdminLiveFrame.ReadAsync(stream, stop).ConfigureAwait(false))
                    {
                        await EnsureCurrentAsync(profileId, server, binding, stop).ConfigureAwait(false);
                        if (frame.Kind == "reset")
                        {
                            using var payload = JsonDocument.Parse(frame.Data);
                            var head = payload.RootElement.GetProperty("head").GetInt64();
                            if (head < 0 || RefreshRequired is not { } refresh)
                                throw new InvalidDataException("A reset requires a mounted REST projection.");
                            _tracker.MarkConnecting();
                            foreach (Func<Task> handler in refresh.GetInvocationList()) await handler().WaitAsync(stop).ConfigureAwait(false);
                            await EnsureCurrentAsync(profileId, server, binding, stop).ConfigureAwait(false);
                            cursor = head;
                            reset = true;
                            break; // Reconnect only after REST reconciliation, using the supplied reset head.
                        }
                        if (frame.Position is not { } position)
                            throw new InvalidDataException("An admin event has no cursor.");
                        if (frame.Kind == "status")
                        {
                            cursor ??= position;
                            _tracker.MarkConnected();
                            delay = 1;
                            continue;
                        }
                        if (cursor is { } previous && position <= previous) continue;
                        Dispatch(frame.Kind, frame.Data);
                        cursor = position;
                    }
                    if (!reset) _tracker.MarkFallbackEngaged("The admin stream disconnected; REST refresh remains available.");
                }
                catch (Exception exception) when (exception is HttpRequestException or OperationCanceledException or IOException)
                {
                    if (stop.IsCancellationRequested) break;
                    _tracker.MarkFallbackEngaged("The admin stream disconnected; REST refresh remains available.");
                }
                if (!stop.IsCancellationRequested)
                {
                    await Task.Delay(TimeSpan.FromSeconds(delay), stop).ConfigureAwait(false);
                    delay = Math.Min(delay * 2, 30);
                }
            }
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _tracker.MarkFallbackEngaged("Admin events require a fresh session or REST refresh.");
        }
        finally
        {
            if (changes is not null) changes.Changed -= AuthorityChanged;
            _profiles.ActiveProfileChanged -= AuthorityChanged;
            if (stop.IsCancellationRequested) _tracker.MarkNotConfigured();
        }
    }

    private async Task EnsureCurrentAsync(string profileId, Uri server, string binding, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var profile = await _profiles.GetActiveProfileAsync(token).ConfigureAwait(false);
        if (profile?.Id != profileId || profile.ServerBaseUri != server
            || !await _authority!.IsCurrentAsync(binding, token).ConfigureAwait(false))
            throw new InvalidDataException("The admin stream authority changed.");
    }

    protected abstract void Dispatch(string kind, string data);

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_lifetime is not null) await _lifetime.CancelAsync().ConfigureAwait(false);
            if (_pump is not null) await _pump.ConfigureAwait(false);
            _pump = null;
            _tracker.MarkNotConfigured();
        }
        finally { _gate.Release(); }
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
        _lifetime?.Dispose();
        _http.Dispose();
        _gate.Dispose();
    }
}
