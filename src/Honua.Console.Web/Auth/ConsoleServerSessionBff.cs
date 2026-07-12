using System.Net;
using System.Net.Http.Json;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Security;
using Honua.Console.Shell.Services;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Honua.Console.Web.Auth;

internal sealed record ConsoleServerSessionPartitionKey(
    string OperatorKey,
    string ProfileId,
    string ServerOrigin);

internal sealed record ConsoleServerAuthPendingFlow(
    string State,
    string OperatorKey,
    string ProfileId,
    string ProviderKey,
    Uri ServerBaseUri,
    string ReturnTo,
    DateTimeOffset ExpiresAt);

internal sealed class ConsoleServerSessionPartition : IDisposable
{
    private long _lastAccessUnixMilliseconds;

    public ConsoleServerSessionPartition(
        ConsoleServerSessionPartitionKey key,
        CookieContainer cookies,
        HttpClient client,
        DateTimeOffset createdAt)
    {
        Key = key;
        Cookies = cookies;
        Client = client;
        Touch(createdAt);
    }

    public ConsoleServerSessionPartitionKey Key { get; }

    public CookieContainer Cookies { get; }

    public HttpClient Client { get; }

    public DateTimeOffset LastAccess => DateTimeOffset.FromUnixTimeMilliseconds(
        Interlocked.Read(ref _lastAccessUnixMilliseconds));

    public void Touch(DateTimeOffset now) => Interlocked.Exchange(
        ref _lastAccessUnixMilliseconds,
        now.ToUnixTimeMilliseconds());

    public void Dispose() => Client.Dispose();
}

/// <summary>
/// Owns bounded, process-local honua-server cookie sessions partitioned by Console
/// operator, environment profile, and server origin. Cookie state is intentionally
/// discarded on process restart so a host restart requires server reauthentication.
/// </summary>
internal sealed class ConsoleServerSessionClientStore : IAsyncDisposable
{
    private const int MaximumPartitions = 2_048;
    private static readonly TimeSpan IdleLifetime = TimeSpan.FromHours(8);
    private readonly object _gate = new();
    private readonly Dictionary<ConsoleServerSessionPartitionKey, ConsoleServerSessionPartition> _partitions = [];
    private readonly Dictionary<string, ConsoleServerAuthPendingFlow> _pending = new(StringComparer.Ordinal);
    private readonly TimeProvider _timeProvider;
    private readonly Func<ConsoleServerSessionPartitionKey, CookieContainer, HttpMessageHandler> _handlerFactory;

    public ConsoleServerSessionClientStore(
        TimeProvider timeProvider,
        Func<ConsoleServerSessionPartitionKey, CookieContainer, HttpMessageHandler>? handlerFactory = null)
    {
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _handlerFactory = handlerFactory ?? CreateHandler;
    }

    public ConsoleServerSessionPartition GetOrCreate(
        string operatorKey,
        ConsoleEnvironmentProfile profile)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(operatorKey);
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentException.ThrowIfNullOrWhiteSpace(profile.Id);

        var now = _timeProvider.GetUtcNow();
        var origin = NormalizeOrigin(profile.ServerBaseUri);
        var key = new ConsoleServerSessionPartitionKey(operatorKey, profile.Id, origin.AbsoluteUri);
        List<ConsoleServerSessionPartition>? expired = null;

        lock (_gate)
        {
            expired = PruneExpiredLocked(now);
            if (_partitions.TryGetValue(key, out var existing))
            {
                existing.Touch(now);
                DisposeAll(expired);
                return existing;
            }

            if (_partitions.Count >= MaximumPartitions)
            {
                DisposeAll(expired);
                throw new InvalidOperationException(
                    "The Console server-session capacity is exhausted. Sign out inactive operators before retrying.");
            }

            var cookies = new CookieContainer();
            var client = new HttpClient(_handlerFactory(key, cookies), disposeHandler: true)
            {
                BaseAddress = origin,
                Timeout = TimeSpan.FromSeconds(30)
            };
            var created = new ConsoleServerSessionPartition(key, cookies, client, now);
            _partitions.Add(key, created);
            DisposeAll(expired);
            return created;
        }
    }

    public void RegisterPending(ConsoleServerAuthPendingFlow pending)
    {
        ArgumentNullException.ThrowIfNull(pending);
        var now = _timeProvider.GetUtcNow();
        lock (_gate)
        {
            RemoveExpiredPendingLocked(now);
            foreach (var state in _pending
                         .Where(item => string.Equals(item.Value.OperatorKey, pending.OperatorKey, StringComparison.Ordinal)
                             && string.Equals(item.Value.ProfileId, pending.ProfileId, StringComparison.Ordinal))
                         .Select(static item => item.Key)
                         .ToArray())
            {
                _pending.Remove(state);
            }

            _pending[pending.State] = pending;
        }
    }

    public bool TryGet(
        string operatorKey,
        ConsoleEnvironmentProfile profile,
        out ConsoleServerSessionPartition? partition)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(operatorKey);
        ArgumentNullException.ThrowIfNull(profile);
        var key = new ConsoleServerSessionPartitionKey(
            operatorKey,
            profile.Id,
            NormalizeOrigin(profile.ServerBaseUri).AbsoluteUri);
        lock (_gate)
        {
            if (!_partitions.TryGetValue(key, out partition))
            {
                return false;
            }

            partition.Touch(_timeProvider.GetUtcNow());
            return true;
        }
    }

    public bool TryConsumePending(
        string state,
        string operatorKey,
        out ConsoleServerAuthPendingFlow? pending)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(state);
        ArgumentException.ThrowIfNullOrWhiteSpace(operatorKey);
        var now = _timeProvider.GetUtcNow();
        lock (_gate)
        {
            RemoveExpiredPendingLocked(now);
            if (!_pending.TryGetValue(state, out var candidate)
                || !string.Equals(candidate.OperatorKey, operatorKey, StringComparison.Ordinal))
            {
                pending = null;
                return false;
            }

            _pending.Remove(state);
            pending = candidate;
            return true;
        }
    }

    public void ClearOperator(string operatorKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(operatorKey);
        List<ConsoleServerSessionPartition> removed;
        lock (_gate)
        {
            var keys = _partitions.Keys
                .Where(key => string.Equals(key.OperatorKey, operatorKey, StringComparison.Ordinal))
                .ToArray();
            removed = new List<ConsoleServerSessionPartition>(keys.Length);
            foreach (var key in keys)
            {
                removed.Add(_partitions[key]);
                _partitions.Remove(key);
            }

            foreach (var state in _pending
                         .Where(item => string.Equals(item.Value.OperatorKey, operatorKey, StringComparison.Ordinal))
                         .Select(static item => item.Key)
                         .ToArray())
            {
                _pending.Remove(state);
            }
        }

        DisposeAll(removed);
    }

    public ValueTask DisposeAsync()
    {
        List<ConsoleServerSessionPartition> removed;
        lock (_gate)
        {
            removed = [.. _partitions.Values];
            _partitions.Clear();
            _pending.Clear();
        }

        DisposeAll(removed);
        return ValueTask.CompletedTask;
    }

    private List<ConsoleServerSessionPartition> PruneExpiredLocked(DateTimeOffset now)
    {
        var removed = new List<ConsoleServerSessionPartition>();
        foreach (var key in _partitions
                     .Where(item => now - item.Value.LastAccess >= IdleLifetime)
                     .Select(static item => item.Key)
                     .ToArray())
        {
            removed.Add(_partitions[key]);
            _partitions.Remove(key);
        }

        RemoveExpiredPendingLocked(now);
        return removed;
    }

    private void RemoveExpiredPendingLocked(DateTimeOffset now)
    {
        foreach (var state in _pending
                     .Where(item => item.Value.ExpiresAt <= now)
                     .Select(static item => item.Key)
                     .ToArray())
        {
            _pending.Remove(state);
        }
    }

    private static Uri NormalizeOrigin(Uri serverBaseUri)
    {
        ArgumentNullException.ThrowIfNull(serverBaseUri);
        if (!serverBaseUri.IsAbsoluteUri
            || (serverBaseUri.Scheme != Uri.UriSchemeHttp && serverBaseUri.Scheme != Uri.UriSchemeHttps))
        {
            throw new ArgumentException("The server base URI must be an absolute HTTP(S) URI.", nameof(serverBaseUri));
        }

        return new Uri(serverBaseUri.GetLeftPart(UriPartial.Authority) + "/", UriKind.Absolute);
    }

    private static HttpMessageHandler CreateHandler(
        ConsoleServerSessionPartitionKey _,
        CookieContainer cookies) => new HttpClientHandler
        {
            AllowAutoRedirect = false,
            CookieContainer = cookies,
            UseCookies = true
        };

    private static void DisposeAll(IEnumerable<ConsoleServerSessionPartition>? partitions)
    {
        if (partitions is null)
        {
            return;
        }

        foreach (var partition in partitions)
        {
            partition.Dispose();
        }
    }
}

internal enum ConsoleServerSignInStatus
{
    Redirect,
    SelectProvider,
    Denied,
    Unavailable
}

internal sealed record ConsoleServerAuthProvider(string Key, string DisplayName);

internal sealed record ConsoleServerSignInResult
{
    public required ConsoleServerSignInStatus Status { get; init; }

    public string RedirectUri { get; init; } = string.Empty;

    public string Message { get; init; } = string.Empty;

    public IReadOnlyList<ConsoleServerAuthProvider> Providers { get; init; } = [];
}

internal sealed class ConsoleServerSessionBffCoordinator
{
    private static readonly TimeSpan PendingLifetime = TimeSpan.FromMinutes(10);
    private readonly IConsoleOperatorContext _operatorContext;
    private readonly IConsoleEnvironmentProfileStore _profiles;
    private readonly IConsoleAccountSessionStore _sessions;
    private readonly ConsoleServerSessionClientStore _serverSessions;
    private readonly TimeProvider _timeProvider;

    public ConsoleServerSessionBffCoordinator(
        IConsoleOperatorContext operatorContext,
        IConsoleEnvironmentProfileStore profiles,
        IConsoleAccountSessionStore sessions,
        ConsoleServerSessionClientStore serverSessions,
        TimeProvider timeProvider)
    {
        _operatorContext = operatorContext ?? throw new ArgumentNullException(nameof(operatorContext));
        _profiles = profiles ?? throw new ArgumentNullException(nameof(profiles));
        _sessions = sessions ?? throw new ArgumentNullException(nameof(sessions));
        _serverSessions = serverSessions ?? throw new ArgumentNullException(nameof(serverSessions));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    }

    public async Task<ConsoleServerSignInResult> BeginSignInAsync(
        string profileId,
        string? providerKey,
        string? returnTo,
        CancellationToken cancellationToken = default)
    {
        var operatorKey = _operatorContext.RequireOperatorKey();
        var profile = await _profiles.GetProfileAsync(profileId, cancellationToken).ConfigureAwait(false);
        if (profile is null || profile.Account.AuthMode != ConsoleAccountAuthMode.AccountRbac)
        {
            return Unavailable("The selected environment is not configured for an operator account.", returnTo);
        }

        try
        {
            var partition = _serverSessions.GetOrCreate(operatorKey, profile);
            using var configResponse = await partition.Client
                .GetAsync(ConsoleServerHttp.BuildUri(profile.ServerBaseUri, "api/v1/admin/auth/config"), cancellationToken)
                .ConfigureAwait(false);
            if (!configResponse.IsSuccessStatusCode)
            {
                return Unavailable("The honua-server sign-in configuration is unavailable.", returnTo);
            }

            var config = await configResponse.Content.ReadFromJsonAsync(
                ConsoleServerSessionBffJsonContext.Default.ConsoleServerAuthConfigWire,
                cancellationToken).ConfigureAwait(false);
            var providers = config?.Providers
                .Where(static provider => !string.IsNullOrWhiteSpace(provider.Key))
                .Select(static provider => new ConsoleServerAuthProvider(
                    provider.Key,
                    string.IsNullOrWhiteSpace(provider.DisplayName) ? provider.Key : provider.DisplayName))
                .ToArray() ?? [];
            if (config?.OidcEnabled != true || providers.Length == 0)
            {
                return Unavailable(
                    "This honua-server has no browser operator identity provider configured.",
                    returnTo);
            }

            var provider = string.IsNullOrWhiteSpace(providerKey)
                ? providers.Length == 1 ? providers[0] : null
                : providers.FirstOrDefault(item => string.Equals(item.Key, providerKey, StringComparison.Ordinal));
            if (provider is null)
            {
                return new ConsoleServerSignInResult
                {
                    Status = ConsoleServerSignInStatus.SelectProvider,
                    Providers = providers,
                    RedirectUri = ConsoleReturnUrl.Sanitize(returnTo)
                };
            }

            using var authorizeResponse = await partition.Client.PostAsJsonAsync(
                ConsoleServerHttp.BuildUri(
                    profile.ServerBaseUri,
                    $"api/v1/admin/auth/providers/{Uri.EscapeDataString(provider.Key)}/authorize-url"),
                new ConsoleServerAuthAuthorizeRequestWire(),
                ConsoleServerSessionBffJsonContext.Default.ConsoleServerAuthAuthorizeRequestWire,
                cancellationToken).ConfigureAwait(false);
            if (!authorizeResponse.IsSuccessStatusCode)
            {
                return Unavailable("The honua-server operator sign-in flow could not be started.", returnTo);
            }

            var authorize = await authorizeResponse.Content.ReadFromJsonAsync(
                ConsoleServerSessionBffJsonContext.Default.ConsoleServerAuthAuthorizeResponseWire,
                cancellationToken).ConfigureAwait(false);
            if (!TryReadAuthorizeState(authorize?.AuthorizeUrl, out var authorizeUri, out var state))
            {
                return Unavailable("The honua-server sign-in response was invalid.", returnTo);
            }

            _serverSessions.RegisterPending(new ConsoleServerAuthPendingFlow(
                state,
                operatorKey,
                profile.Id,
                provider.Key,
                profile.ServerBaseUri,
                ConsoleReturnUrl.Sanitize(returnTo),
                _timeProvider.GetUtcNow().Add(PendingLifetime)));
            return new ConsoleServerSignInResult
            {
                Status = ConsoleServerSignInStatus.Redirect,
                RedirectUri = authorizeUri.AbsoluteUri
            };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or InvalidOperationException or ArgumentException)
        {
            return Unavailable("The honua-server operator sign-in flow is unavailable.", returnTo);
        }
    }

    public async Task<ConsoleServerSignInResult> CompleteSignInAsync(
        string? code,
        string? state,
        string? error,
        CancellationToken cancellationToken = default)
    {
        var operatorKey = _operatorContext.RequireOperatorKey();
        if (string.IsNullOrWhiteSpace(state)
            || !_serverSessions.TryConsumePending(state, operatorKey, out var pending)
            || pending is null)
        {
            return Denied("The server sign-in session is invalid or expired.");
        }

        if (!string.IsNullOrWhiteSpace(error) || string.IsNullOrWhiteSpace(code))
        {
            return Denied("The identity provider did not complete server sign-in.", pending.ReturnTo);
        }

        var profile = await _profiles.GetProfileAsync(pending.ProfileId, cancellationToken).ConfigureAwait(false);
        if (profile is null || !SameServer(profile.ServerBaseUri, pending.ServerBaseUri))
        {
            return Denied("The environment profile changed while server sign-in was in progress.", pending.ReturnTo);
        }

        try
        {
            var partition = _serverSessions.GetOrCreate(operatorKey, profile);
            using var tokenResponse = await partition.Client.PostAsJsonAsync(
                ConsoleServerHttp.BuildUri(
                    profile.ServerBaseUri,
                    $"api/v1/admin/auth/providers/{Uri.EscapeDataString(pending.ProviderKey)}/token"),
                new ConsoleServerAuthTokenRequestWire
                {
                    GrantType = "authorization_code",
                    Code = code,
                    State = state
                },
                ConsoleServerSessionBffJsonContext.Default.ConsoleServerAuthTokenRequestWire,
                cancellationToken).ConfigureAwait(false);
            if (!tokenResponse.IsSuccessStatusCode)
            {
                return Denied("honua-server rejected the operator sign-in response.", pending.ReturnTo);
            }

            var exchange = await new HttpConsoleOperatorBearerExchange(partition.Client)
                .ExchangeAsync(profile, cancellationToken).ConfigureAwait(false);
            if (exchange.Status != ConsoleOperatorBearerExchangeStatus.Issued
                || string.IsNullOrWhiteSpace(exchange.AccessToken)
                || exchange.ExpiresAt is not { } expiresAt)
            {
                return exchange.Status == ConsoleOperatorBearerExchangeStatus.Denied
                    ? Denied(exchange.Message, pending.ReturnTo)
                    : Unavailable(exchange.Message, pending.ReturnTo);
            }

            var existing = await _sessions.GetSessionAsync(profile.Id, cancellationToken).ConfigureAwait(false);
            await _sessions.SaveSessionAsync((existing ?? new ConsoleAccountSession
            {
                ProfileId = profile.Id,
                AccountId = operatorKey,
                DisplayName = profile.Account.DisplayName,
                TenantId = string.IsNullOrWhiteSpace(profile.Account.TenantId)
                    ? profile.TenantId
                    : profile.Account.TenantId
            }) with
            {
                AccessToken = exchange.AccessToken,
                AccessTokenExpiresAt = expiresAt
            }, cancellationToken).ConfigureAwait(false);

            return new ConsoleServerSignInResult
            {
                Status = ConsoleServerSignInStatus.Redirect,
                RedirectUri = pending.ReturnTo
            };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or InvalidOperationException or ArgumentException)
        {
            return Unavailable("The server sign-in callback could not be completed.", pending.ReturnTo);
        }
    }

    public async Task SignOutAsync(CancellationToken cancellationToken = default)
    {
        var operatorKey = _operatorContext.RequireOperatorKey();
        var profiles = await _profiles.ListProfilesAsync(CancellationToken.None).ConfigureAwait(false);
        foreach (var profile in profiles)
        {
            if (_serverSessions.TryGet(operatorKey, profile, out var partition) && partition is not null)
            {
                try
                {
                    using var response = await partition.Client.PostAsync(
                        ConsoleServerHttp.BuildUri(profile.ServerBaseUri, "api/v1/admin/auth/logout"),
                        content: null,
                        cancellationToken).ConfigureAwait(false);
                }
                catch (HttpRequestException)
                {
                    // Local sign-out remains authoritative for the Console partition. The
                    // upstream session is short-lived and becomes unreachable after the jar
                    // is discarded, so a network failure must not retain local bearer state.
                }
                catch (OperationCanceledException)
                {
                    // Local sign-out remains authoritative for the Console partition. The
                    // upstream session is short-lived and becomes unreachable after the jar
                    // is discarded, so a network failure must not retain local bearer state.
                }
            }

            await _sessions.ClearSessionAsync(profile.Id, CancellationToken.None).ConfigureAwait(false);
        }

        _serverSessions.ClearOperator(operatorKey);
    }

    private static bool TryReadAuthorizeState(
        string? value,
        out Uri authorizeUri,
        out string state)
    {
        if (Uri.TryCreate(value, UriKind.Absolute, out authorizeUri!)
            && (authorizeUri.Scheme == Uri.UriSchemeHttps || authorizeUri.Scheme == Uri.UriSchemeHttp))
        {
            state = QueryHelpers.ParseQuery(authorizeUri.Query)["state"].ToString();
            if (!string.IsNullOrWhiteSpace(state))
            {
                return true;
            }
        }

        authorizeUri = null!;
        state = string.Empty;
        return false;
    }

    private static bool SameServer(Uri left, Uri right) => string.Equals(
        NormalizeBase(left),
        NormalizeBase(right),
        StringComparison.OrdinalIgnoreCase);

    private static string NormalizeBase(Uri value) => value.AbsoluteUri.EndsWith('/')
        ? value.AbsoluteUri
        : value.AbsoluteUri + "/";

    private static ConsoleServerSignInResult Denied(string message, string? returnTo = null) => new()
    {
        Status = ConsoleServerSignInStatus.Denied,
        Message = message,
        RedirectUri = ConsoleReturnUrl.Sanitize(returnTo)
    };

    private static ConsoleServerSignInResult Unavailable(string message, string? returnTo = null) => new()
    {
        Status = ConsoleServerSignInStatus.Unavailable,
        Message = message,
        RedirectUri = ConsoleReturnUrl.Sanitize(returnTo)
    };
}

internal sealed class PartitionedConsoleOperatorBearerExchange : IConsoleOperatorBearerExchange
{
    private readonly IConsoleOperatorContext _operatorContext;
    private readonly ConsoleServerSessionClientStore _serverSessions;

    public PartitionedConsoleOperatorBearerExchange(
        IConsoleOperatorContext operatorContext,
        ConsoleServerSessionClientStore serverSessions)
    {
        _operatorContext = operatorContext ?? throw new ArgumentNullException(nameof(operatorContext));
        _serverSessions = serverSessions ?? throw new ArgumentNullException(nameof(serverSessions));
    }

    public Task<ConsoleOperatorBearerExchangeResult> ExchangeAsync(
        ConsoleEnvironmentProfile profile,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var partition = _serverSessions.GetOrCreate(_operatorContext.RequireOperatorKey(), profile);
            return new HttpConsoleOperatorBearerExchange(partition.Client)
                .ExchangeAsync(profile, cancellationToken);
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            return Task.FromResult(ConsoleOperatorBearerExchangeResult.Unavailable(
                "The per-operator honua-server session is unavailable. Sign in to the server again."));
        }
    }
}

internal static class ConsoleServerSessionBffExtensions
{
    public static WebApplicationBuilder AddConsoleServerSessionBff(this WebApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Services.TryAddSingleton(TimeProvider.System);
        builder.Services.TryAddSingleton<ConsoleServerSessionClientStore>();
        builder.Services.TryAddSingleton<ConsoleServerSessionBffCoordinator>();
        builder.Services.Replace(ServiceDescriptor.Singleton<IConsoleOperatorBearerExchange,
            PartitionedConsoleOperatorBearerExchange>());
        return builder;
    }

    public static WebApplication MapConsoleServerSessionBff(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        app.MapGet("/auth/server/login", async (
            string profileId,
            string? provider,
            string? returnTo,
            ConsoleServerSessionBffCoordinator coordinator,
            CancellationToken cancellationToken) =>
        {
            var result = await coordinator.BeginSignInAsync(
                profileId,
                provider,
                returnTo,
                cancellationToken).ConfigureAwait(false);
            return ToHttpResult(result, profileId, returnTo);
        });

        app.MapGet("/admin/auth/callback", async (
            string? code,
            string? state,
            string? error,
            ConsoleServerSessionBffCoordinator coordinator,
            CancellationToken cancellationToken) =>
        {
            var result = await coordinator.CompleteSignInAsync(
                code,
                state,
                error,
                cancellationToken).ConfigureAwait(false);
            return result.Status == ConsoleServerSignInStatus.Redirect
                ? Results.Redirect(result.RedirectUri)
                : Results.Redirect(BuildFailureUri(result));
        });

        return app;
    }

    private static IResult ToHttpResult(
        ConsoleServerSignInResult result,
        string profileId,
        string? returnTo) => result.Status switch
        {
            ConsoleServerSignInStatus.Redirect => Results.Redirect(result.RedirectUri),
            ConsoleServerSignInStatus.SelectProvider => Results.Content(
                BuildProviderSelection(result, profileId, returnTo),
                "text/html; charset=utf-8"),
            ConsoleServerSignInStatus.Denied => Results.Redirect(BuildFailureUri(result)),
            _ => Results.Redirect(BuildFailureUri(result))
        };

    private static string BuildProviderSelection(
        ConsoleServerSignInResult result,
        string profileId,
        string? returnTo)
    {
        var encoder = HtmlEncoder.Default;
        var links = string.Join(
            string.Empty,
            result.Providers.Select(provider =>
            {
                var href = QueryHelpers.AddQueryString("/auth/server/login", new Dictionary<string, string?>
                {
                    ["profileId"] = profileId,
                    ["provider"] = provider.Key,
                    ["returnTo"] = ConsoleReturnUrl.Sanitize(returnTo)
                });
                return $"<li><a href=\"{encoder.Encode(href)}\">{encoder.Encode(provider.DisplayName)}</a></li>";
            }));
        return "<!doctype html><html><head><meta charset=\"utf-8\"><title>Choose sign-in provider</title></head>"
            + "<body><main><h1>Choose sign-in provider</h1><ul>" + links + "</ul></main></body></html>";
    }

    private static string BuildFailureUri(ConsoleServerSignInResult result) => QueryHelpers.AddQueryString(
        "/auth/signin",
        new Dictionary<string, string?>
        {
            ["returnTo"] = ConsoleReturnUrl.Sanitize(result.RedirectUri),
            ["serverAuth"] = result.Status == ConsoleServerSignInStatus.Denied ? "denied" : "unavailable"
        });
}

internal sealed record ConsoleServerAuthConfigWire
{
    public bool OidcEnabled { get; init; }

    public List<ConsoleServerAuthProviderWire> Providers { get; init; } = [];
}

internal sealed record ConsoleServerAuthProviderWire
{
    public string Key { get; init; } = string.Empty;

    public string DisplayName { get; init; } = string.Empty;
}

internal sealed record ConsoleServerAuthAuthorizeRequestWire;

internal sealed record ConsoleServerAuthAuthorizeResponseWire
{
    public string AuthorizeUrl { get; init; } = string.Empty;
}

internal sealed record ConsoleServerAuthTokenRequestWire
{
    public string GrantType { get; init; } = string.Empty;

    public string Code { get; init; } = string.Empty;

    public string State { get; init; } = string.Empty;
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(ConsoleServerAuthConfigWire))]
[JsonSerializable(typeof(ConsoleServerAuthAuthorizeRequestWire))]
[JsonSerializable(typeof(ConsoleServerAuthAuthorizeResponseWire))]
[JsonSerializable(typeof(ConsoleServerAuthTokenRequestWire))]
internal sealed partial class ConsoleServerSessionBffJsonContext : JsonSerializerContext;
