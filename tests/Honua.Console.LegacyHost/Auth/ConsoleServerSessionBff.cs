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
    private readonly SemaphoreSlim _authenticationGate = new(1, 1);

    public ConsoleServerSessionPartition(
        ConsoleServerSessionPartitionKey key,
        Uri resourceBaseUri,
        CookieContainer cookies,
        HttpClient client,
        DateTimeOffset createdAt)
    {
        Key = key;
        ResourceBaseUri = resourceBaseUri;
        Cookies = cookies;
        Client = client;
        Touch(createdAt);
    }

    public ConsoleServerSessionPartitionKey Key { get; }

    // The URI this jar signs in to. A management partition uses its own base, not the serving profile.
    public Uri ResourceBaseUri { get; }

    public CookieContainer Cookies { get; }

    public string? AuthenticatedProviderKey { get; set; }

    public HttpClient Client { get; }

    // Other sign-in attempts that observed this partition. The creator does not count, and only the store lock mutates it.
    public int Observers { get; private set; }

    public DateTimeOffset LastAccess => DateTimeOffset.FromUnixTimeMilliseconds(
        Interlocked.Read(ref _lastAccessUnixMilliseconds));

    public void Touch(DateTimeOffset now) => Interlocked.Exchange(
        ref _lastAccessUnixMilliseconds,
        now.ToUnixTimeMilliseconds());

    public void NoteObserver() => Observers++;

    public async Task<IDisposable> EnterAuthenticationAsync(CancellationToken cancellationToken)
    {
        await _authenticationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        return new AuthenticationLease(_authenticationGate);
    }

    public void Dispose() => Client.Dispose();

    private sealed class AuthenticationLease(SemaphoreSlim gate) : IDisposable
    {
        public void Dispose() => gate.Release();
    }
}

// Cookie sessions are partitioned by operator, profile, and origin, and a process restart discards them so the server must authenticate again.
internal sealed class ConsoleServerSessionClientStore : IAsyncDisposable
{
    private const int MaximumPartitions = 2_048;
    private const int MaximumPartitionsPerOperator = 32;
    private static readonly TimeSpan IdleLifetime = TimeSpan.FromHours(8);
    private readonly object _gate = new();
    private readonly Dictionary<ConsoleServerSessionPartitionKey, ConsoleServerSessionPartition> _partitions = [];
    private readonly Dictionary<string, ConsoleServerAuthPendingFlow> _pending = new(StringComparer.Ordinal);
    private readonly TimeProvider _timeProvider;
    private readonly Func<ConsoleServerSessionPartitionKey, CookieContainer, HttpMessageHandler> _handlerFactory;
    private readonly int _maximumPartitions;
    private readonly int _maximumPartitionsPerOperator;

    public ConsoleServerSessionClientStore(
        TimeProvider timeProvider,
        Func<ConsoleServerSessionPartitionKey, CookieContainer, HttpMessageHandler>? handlerFactory = null,
        int maximumPartitions = MaximumPartitions,
        int maximumPartitionsPerOperator = MaximumPartitionsPerOperator)
    {
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _handlerFactory = handlerFactory ?? CreateHandler;
        if (maximumPartitions < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumPartitions));
        }

        if (maximumPartitionsPerOperator < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumPartitionsPerOperator));
        }

        _maximumPartitions = maximumPartitions;
        _maximumPartitionsPerOperator = maximumPartitionsPerOperator;
    }

    public ConsoleServerSessionPartition GetOrCreate(
        string operatorKey,
        ConsoleEnvironmentProfile profile) =>
        GetOrCreate(operatorKey, profile, out _);

    public ConsoleServerSessionPartition GetOrCreate(
        string operatorKey,
        ConsoleEnvironmentProfile profile,
        out bool created)
    {
        ArgumentNullException.ThrowIfNull(profile);
        return GetOrCreate(operatorKey, profile.Id, profile.ServerBaseUri, out created);
    }

    // A separate management endpoint has its own origin and its own session cookie.
    public ConsoleServerSessionPartition GetOrCreate(
        string operatorKey,
        string profileId,
        Uri serverBaseUri,
        out bool created)
    {
        created = false;
        ArgumentException.ThrowIfNullOrWhiteSpace(operatorKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(profileId);
        ArgumentNullException.ThrowIfNull(serverBaseUri);

        var now = _timeProvider.GetUtcNow();
        var origin = NormalizeOrigin(serverBaseUri);
        var key = new ConsoleServerSessionPartitionKey(operatorKey, profileId, ConsoleServerSessionBffCoordinator.NormalizeBase(serverBaseUri));
        List<ConsoleServerSessionPartition>? discarded = null;

        lock (_gate)
        {
            discarded = PruneExpiredLocked(now);
            if (_partitions.TryGetValue(key, out var existing))
            {
                existing.Touch(now);
                existing.NoteObserver();
                created = false;
                DisposeAll(discarded);
                return existing;
            }

            // One operator must not be able to occupy the process-wide table. Evict that operator's
            // least-recently-used partition before refusing, and leave every other operator's slots.
            while (CountForOperatorLocked(operatorKey) >= _maximumPartitionsPerOperator)
            {
                var victim = OldestForOperatorLocked(operatorKey);
                _partitions.Remove(victim.Key);
                discarded.Add(victim);
            }

            if (_partitions.Count >= _maximumPartitions)
            {
                created = false;
                DisposeAll(discarded);
                throw new InvalidOperationException(
                    "The Console server-session capacity is exhausted. Sign out inactive operators before retrying.");
            }

            var cookies = new CookieContainer();
            var client = new HttpClient(_handlerFactory(key, cookies), disposeHandler: true)
            {
                BaseAddress = origin,
                Timeout = TimeSpan.FromSeconds(30)
            };
            var partition = new ConsoleServerSessionPartition(key, serverBaseUri, cookies, client, now);
            _partitions.Add(key, partition);
            created = true;
            DisposeAll(discarded);
            return partition;
        }
    }

    public void Remove(string operatorKey, ConsoleEnvironmentProfile profile)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(operatorKey);
        ArgumentNullException.ThrowIfNull(profile);
        var key = new ConsoleServerSessionPartitionKey(
            operatorKey,
            profile.Id,
            ConsoleServerSessionBffCoordinator.NormalizeBase(profile.ServerBaseUri));
        ConsoleServerSessionPartition? removed = null;
        lock (_gate)
        {
            if (_partitions.Remove(key, out var partition))
            {
                removed = partition;
            }
        }

        removed?.Dispose();
    }

    // Removes only this instance, so a replacement partition for the same key survives.
    public bool TryRemove(ConsoleServerSessionPartition partition)
    {
        ArgumentNullException.ThrowIfNull(partition);
        lock (_gate)
        {
            if (!_partitions.TryGetValue(partition.Key, out var current)
                || !ReferenceEquals(current, partition) || !_partitions.Remove(partition.Key))
                return false;
        }
        partition.Dispose();
        return true;
    }

    // A failed configuration read drops its unadopted probe immediately so the slot is not held until the idle lifetime, and a partition another sign-in adopted stays.
    public bool TryRemoveIfUnadopted(ConsoleServerSessionPartition partition)
    {
        ArgumentNullException.ThrowIfNull(partition);
        ConsoleServerSessionPartition? removed = null;
        lock (_gate)
        {
            if (!_partitions.TryGetValue(partition.Key, out var current)
                || !ReferenceEquals(current, partition)
                || current.Observers > 0
                || !_partitions.Remove(partition.Key))
            {
                return false;
            }

            removed = current;
        }

        removed.Dispose();
        return true;
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
        ArgumentNullException.ThrowIfNull(profile);
        return TryGet(operatorKey, profile.Id, profile.ServerBaseUri, out partition);
    }

    public bool TryGet(
        string operatorKey,
        string profileId,
        Uri serverBaseUri,
        out ConsoleServerSessionPartition? partition)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(operatorKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(profileId);
        ArgumentNullException.ThrowIfNull(serverBaseUri);
        var key = new ConsoleServerSessionPartitionKey(
            operatorKey,
            profileId,
            ConsoleServerSessionBffCoordinator.NormalizeBase(serverBaseUri));
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

    public IReadOnlyList<ConsoleServerSessionPartition> CopyForOperator(string operatorKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(operatorKey);
        lock (_gate)
        {
            return _partitions
                .Where(item => string.Equals(item.Key.OperatorKey, operatorKey, StringComparison.Ordinal))
                .Select(static item => item.Value)
                .ToArray();
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

    private int CountForOperatorLocked(string operatorKey) =>
        _partitions.Keys.Count(key => string.Equals(key.OperatorKey, operatorKey, StringComparison.Ordinal));

    private ConsoleServerSessionPartition OldestForOperatorLocked(string operatorKey) =>
        _partitions
            .Where(item => string.Equals(item.Key.OperatorKey, operatorKey, StringComparison.Ordinal))
            .Select(static item => item.Value)
            .OrderBy(static item => item.LastAccess)
            .ThenBy(static item => item.Key.ProfileId, StringComparer.Ordinal)
            .First();

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
    private readonly ConsoleServerSessionIdentityBinding? _identityBinding;
    private readonly IConsoleManagerEndpoint? _managerEndpoint;

    public ConsoleServerSessionBffCoordinator(
        IConsoleOperatorContext operatorContext,
        IConsoleEnvironmentProfileStore profiles,
        IConsoleAccountSessionStore sessions,
        ConsoleServerSessionClientStore serverSessions,
        TimeProvider timeProvider,
        ConsoleServerSessionIdentityBinding? identityBinding = null,
        IConsoleManagerEndpoint? managerEndpoint = null)
    {
        _identityBinding = identityBinding;
        _managerEndpoint = managerEndpoint;
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

        return await StartServerSignInAsync(
            operatorKey,
            profile.Id,
            profile.ServerBaseUri,
            providerKey,
            returnTo,
            cancellationToken).ConfigureAwait(false);
    }

    private async Task<ConsoleServerSignInResult> StartServerSignInAsync(
        string operatorKey,
        string profileId,
        Uri serverBaseUri,
        string? providerKey,
        string? returnTo,
        CancellationToken cancellationToken)
    {
        var created = false;
        var retainPartition = false;
        ConsoleServerSessionPartition? partition = null;
        try
        {
            partition = _serverSessions.GetOrCreate(operatorKey, profileId, serverBaseUri, out created);
            using var configResponse = await partition.Client
                .GetAsync(ConsoleServerHttp.BuildUri(serverBaseUri, "api/v1/admin/auth/config"), cancellationToken)
                .ConfigureAwait(false);
            if (!configResponse.IsSuccessStatusCode)
            {
                configResponse.Dispose();
                if (created)
                {
                    _serverSessions.TryRemoveIfUnadopted(partition);
                }

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
                configResponse.Dispose();
                if (created)
                {
                    _serverSessions.TryRemoveIfUnadopted(partition);
                }

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

            using var authorizeResponse = await new ConsoleServerSessionPost
            {
                Client = partition.Client,
                ServerBaseUri = serverBaseUri,
                RelativePath = $"api/v1/admin/auth/providers/{Uri.EscapeDataString(provider.Key)}/authorize-url",
                Content = JsonContent.Create(
                    new ConsoleServerAuthAuthorizeRequestWire(),
                    ConsoleServerSessionBffJsonContext.Default.ConsoleServerAuthAuthorizeRequestWire)
            }.SendAsync(cancellationToken).ConfigureAwait(false);
            if (!authorizeResponse.IsSuccessStatusCode)
            {
                authorizeResponse.Dispose();
                configResponse.Dispose();
                if (created)
                {
                    _serverSessions.TryRemoveIfUnadopted(partition);
                }

                return Unavailable("The honua-server operator sign-in flow could not be started.", returnTo);
            }

            var authorize = await authorizeResponse.Content.ReadFromJsonAsync(
                ConsoleServerSessionBffJsonContext.Default.ConsoleServerAuthAuthorizeResponseWire,
                cancellationToken).ConfigureAwait(false);
            if (!TryReadAuthorizeState(authorize?.AuthorizeUrl, out var authorizeUri, out var state))
            {
                authorizeResponse.Dispose();
                configResponse.Dispose();
                if (created)
                {
                    _serverSessions.TryRemoveIfUnadopted(partition);
                }

                return Unavailable("The honua-server sign-in response was invalid.", returnTo);
            }

            _serverSessions.RegisterPending(new ConsoleServerAuthPendingFlow(
                state,
                operatorKey,
                profileId,
                provider.Key,
                serverBaseUri,
                ConsoleReturnUrl.Sanitize(returnTo),
                _timeProvider.GetUtcNow().Add(PendingLifetime)));
            retainPartition = true;
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
            if (created && partition is not null && !retainPartition)
            {
                _serverSessions.TryRemoveIfUnadopted(partition);
            }

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
        var manager = profile is null ? null : _managerEndpoint?.Resolve(profile.ServerBaseUri);
        var managerFlow = profile is not null
            && manager is not null
            && !SameServer(manager, profile.ServerBaseUri)
            && SameServer(pending.ServerBaseUri, manager);
        if (profile is null || (!managerFlow && !SameServer(profile.ServerBaseUri, pending.ServerBaseUri)))
        {
            return Denied("The environment profile changed while server sign-in was in progress.", pending.ReturnTo);
        }

        var target = managerFlow ? manager! : profile.ServerBaseUri;
        try
        {
            var partition = managerFlow
                ? _serverSessions.GetOrCreate(operatorKey, profile.Id, target, out _)
                : _serverSessions.GetOrCreate(operatorKey, profile);
            using var authentication = await partition.EnterAuthenticationAsync(cancellationToken).ConfigureAwait(false);
            var snapshots = _sessions as IConsoleAccountSessionSnapshotStore;
            var snapshot = snapshots is null ? null
                : await snapshots.CaptureSessionAsync(profile.Id, cancellationToken).ConfigureAwait(false);
            var existing = snapshot is not null ? snapshot.Session
                : await _sessions.GetSessionAsync(profile.Id, cancellationToken).ConfigureAwait(false);
            using var tokenResponse = await new ConsoleServerSessionPost
            {
                Client = partition.Client,
                ServerBaseUri = target,
                RelativePath = $"api/v1/admin/auth/providers/{Uri.EscapeDataString(pending.ProviderKey)}/token",
                Content = JsonContent.Create(
                    new ConsoleServerAuthTokenRequestWire { GrantType = "authorization_code", Code = code, State = state },
                    ConsoleServerSessionBffJsonContext.Default.ConsoleServerAuthTokenRequestWire)
            }.SendAsync(cancellationToken).ConfigureAwait(false);
            if (!tokenResponse.IsSuccessStatusCode)
            {
                return Denied("honua-server rejected the operator sign-in response.", pending.ReturnTo);
            }

            if (_identityBinding is not null
                && !await _identityBinding.EnsureAsync(
                    partition, profile, operatorKey, pending.ProviderKey, cancellationToken).ConfigureAwait(false))
            {
                return Denied(
                    managerFlow
                        ? "The management session does not match the authenticated Console operator."
                        : "The server session does not match the authenticated Console operator.",
                    pending.ReturnTo);
            }

            partition.AuthenticatedProviderKey = pending.ProviderKey;
            if (managerFlow)
            {
                // The management cookie is the approval credential. Do not replace the serving bearer.
                return new ConsoleServerSignInResult
                {
                    Status = ConsoleServerSignInStatus.Redirect,
                    RedirectUri = pending.ReturnTo
                };
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

            var updatedSession = (existing ?? new ConsoleAccountSession
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
                ServerBaseUri = profile.ServerBaseUri,
                AccessTokenExpiresAt = expiresAt
            };
            if (snapshots is not null && snapshot is not null)
            {
                if (!await snapshots.TrySaveSessionAsync(snapshot, updatedSession, cancellationToken).ConfigureAwait(false))
                {
                    _serverSessions.TryRemove(partition);
                    return Denied("The Console operator session changed during sign-in. Sign in again.", pending.ReturnTo);
                }
            }
            else
            {
                await _sessions.SaveSessionAsync(updatedSession, cancellationToken).ConfigureAwait(false);
            }

            var chained = await TryChainManagerSignInAsync(
                profile, operatorKey, pending.ReturnTo, cancellationToken).ConfigureAwait(false);
            return chained ?? new ConsoleServerSignInResult
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

    // A separate manager refuses a bearer on proposal approval, so serving sign-in continues
    // there when that endpoint has its own browser identity provider. A manager that cannot
    // start sign-in leaves the serving session in place.
    private async Task<ConsoleServerSignInResult?> TryChainManagerSignInAsync(
        ConsoleEnvironmentProfile profile,
        string operatorKey,
        string returnTo,
        CancellationToken cancellationToken)
    {
        if (_managerEndpoint is null)
        {
            return null;
        }

        var manager = _managerEndpoint.Resolve(profile.ServerBaseUri);
        if (manager is null || SameServer(manager, profile.ServerBaseUri))
        {
            return null;
        }

        if (_serverSessions.TryGet(operatorKey, profile.Id, manager, out var existing)
            && !string.IsNullOrWhiteSpace(existing?.AuthenticatedProviderKey))
        {
            return null;
        }

        var providerKey = _serverSessions.TryGet(operatorKey, profile, out var serving)
            ? serving?.AuthenticatedProviderKey
            : null;
        var started = await StartServerSignInAsync(
            operatorKey, profile.Id, manager, providerKey, returnTo, cancellationToken).ConfigureAwait(false);
        return started.Status == ConsoleServerSignInStatus.Redirect ? started : null;
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
                    using var response = await new ConsoleServerSessionPost
                    {
                        Client = partition.Client,
                        ServerBaseUri = profile.ServerBaseUri,
                        RelativePath = "api/v1/admin/auth/logout"
                    }.SendAsync(cancellationToken).ConfigureAwait(false);
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

        var loggedOut = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var profile in profiles)
        {
            loggedOut.Add(NormalizeBase(profile.ServerBaseUri));
        }

        foreach (var partition in _serverSessions.CopyForOperator(operatorKey))
        {
            if (!loggedOut.Add(NormalizeBase(partition.ResourceBaseUri)))
            {
                continue;
            }

            try
            {
                using var response = await new ConsoleServerSessionPost
                {
                    Client = partition.Client,
                    ServerBaseUri = partition.ResourceBaseUri,
                    RelativePath = "api/v1/admin/auth/logout"
                }.SendAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (HttpRequestException)
            {
                // The local partition is still discarded below.
            }
            catch (OperationCanceledException)
            {
                // The local partition is still discarded below.
            }
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

    internal static bool SameServer(Uri left, Uri right) => string.Equals(
        NormalizeBase(left),
        NormalizeBase(right),
        StringComparison.OrdinalIgnoreCase);

    internal static string NormalizeBase(Uri value)
    {
        var builder = new UriBuilder(value) { Query = string.Empty, Fragment = string.Empty };
        var text = builder.Uri.AbsoluteUri;
        return text.EndsWith('/') ? text : text + "/";
    }

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

internal sealed class PartitionedConsoleOperatorBearerExchange : IConsoleOperatorBearerExchange, IConsoleOperatorBearerExchangeLeaseProvider
{
    private readonly IConsoleOperatorContext _operatorContext;
    private readonly ConsoleServerSessionClientStore _serverSessions;
    private readonly ConsoleServerSessionIdentityBinding? _identityBinding;

    public PartitionedConsoleOperatorBearerExchange(
        IConsoleOperatorContext operatorContext,
        ConsoleServerSessionClientStore serverSessions,
        ConsoleServerSessionIdentityBinding? identityBinding = null)
    {
        _identityBinding = identityBinding;
        _operatorContext = operatorContext ?? throw new ArgumentNullException(nameof(operatorContext));
        _serverSessions = serverSessions ?? throw new ArgumentNullException(nameof(serverSessions));
    }

    public async Task<ConsoleOperatorBearerExchangeResult> ExchangeAsync(
        ConsoleEnvironmentProfile profile,
        CancellationToken cancellationToken = default)
    {
        using var authentication = await EnterSessionAsync(profile, cancellationToken).ConfigureAwait(false);
        return await authentication.ExchangeAsync(profile, cancellationToken).ConfigureAwait(false);
    }

    public async Task<IConsoleOperatorBearerExchangeLease> EnterSessionAsync(
        ConsoleEnvironmentProfile profile,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var operatorKey = _operatorContext.RequireOperatorKey();
            if (!_serverSessions.TryGet(operatorKey, profile, out var partition)
                || partition is null)
            {
                return new UnavailableLease(ConsoleOperatorBearerExchangeResult.Denied(
                    "No honua-server session is available. Sign in to the server again."));
            }

            var authentication = await partition.EnterAuthenticationAsync(cancellationToken).ConfigureAwait(false);
            return new PartitionLease(partition, profile, operatorKey, _identityBinding, authentication);
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            return new UnavailableLease(ConsoleOperatorBearerExchangeResult.Unavailable(
                "The per-operator honua-server session is unavailable. Sign in to the server again."));
        }
    }

    private sealed class UnavailableLease(ConsoleOperatorBearerExchangeResult result) : IConsoleOperatorBearerExchangeLease
    {
        public Task<ConsoleOperatorBearerExchangeResult> ExchangeAsync(
            ConsoleEnvironmentProfile profile, CancellationToken cancellationToken = default) => Task.FromResult(result);

        public void Dispose() { }
    }

    private sealed class PartitionLease(
        ConsoleServerSessionPartition partition,
        ConsoleEnvironmentProfile boundProfile,
        string operatorKey,
        ConsoleServerSessionIdentityBinding? identityBinding,
        IDisposable authentication) : IConsoleOperatorBearerExchangeLease
    {
        private IDisposable? _authentication = authentication;

        public async Task<ConsoleOperatorBearerExchangeResult> ExchangeAsync(
            ConsoleEnvironmentProfile profile, CancellationToken cancellationToken = default)
        {
            if (_authentication is null || profile.Id != boundProfile.Id || profile.ServerBaseUri != boundProfile.ServerBaseUri)
            {
                return ConsoleOperatorBearerExchangeResult.Unavailable("The authenticated environment scope changed.");
            }

            try
            {
                if (identityBinding is not null
                    && !await identityBinding.EnsureAsync(
                        partition, profile, operatorKey, partition.AuthenticatedProviderKey, cancellationToken).ConfigureAwait(false))
                {
                    return ConsoleOperatorBearerExchangeResult.Denied(
                        "The server session does not match the authenticated Console operator. Sign in again.");
                }

                return await new HttpConsoleOperatorBearerExchange(partition.Client)
                    .ExchangeAsync(profile, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
            {
                return ConsoleOperatorBearerExchangeResult.Unavailable(
                    "The per-operator honua-server session is unavailable. Sign in to the server again.");
            }
        }

        public void Dispose() => Interlocked.Exchange(ref _authentication, null)?.Dispose();
    }
}

internal static class ConsoleServerSessionBffExtensions
{
    public static WebApplicationBuilder AddConsoleServerSessionBff(this WebApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Services.TryAddSingleton(TimeProvider.System);
        builder.Services.TryAddSingleton<ConsoleServerSessionClientStore>();
        builder.Services.TryAddSingleton<ConsoleServerSessionIdentityBinding>();
        builder.Services.TryAddSingleton(static provider => new ConsoleServerSessionBffCoordinator(
            provider.GetRequiredService<IConsoleOperatorContext>(),
            provider.GetRequiredService<IConsoleEnvironmentProfileStore>(),
            provider.GetRequiredService<IConsoleAccountSessionStore>(),
            provider.GetRequiredService<ConsoleServerSessionClientStore>(),
            provider.GetRequiredService<TimeProvider>(),
            provider.GetService<ConsoleServerSessionIdentityBinding>(),
            provider.GetService<IConsoleManagerEndpoint>()));
        builder.Services.Replace(ServiceDescriptor.Singleton<IConsoleOperatorBearerExchange,
            PartitionedConsoleOperatorBearerExchange>());
        DecorateProposalsWithSessionApproval(builder.Services);
        return builder;
    }

    private static void DecorateProposalsWithSessionApproval(IServiceCollection services)
    {
        var original = services.LastOrDefault(descriptor => descriptor.ServiceType == typeof(IConsoleProposalsClient));
        if (original?.ImplementationFactory is not { } factory)
        {
            return;
        }

        services.Remove(original);
        services.AddSingleton<IConsoleProposalsClient>(provider =>
            new ConsoleSessionApprovalProposalsClient((IConsoleProposalsClient)factory(provider), provider));
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

// A partition that already holds the server session cookie needs the server's CSRF token on
// unsafe session requests (authorize-url, token, logout); without the cookie the token is harmless.
internal sealed class ConsoleServerSessionPost
{
    public required HttpClient Client { get; init; }

    public required Uri ServerBaseUri { get; init; }

    public required string RelativePath { get; init; }

    public HttpContent? Content { get; init; }

    public async Task<HttpResponseMessage> SendAsync(CancellationToken cancellationToken)
    {
        var token = await ConsoleServerCsrf.TryGetTokenAsync(Client, ServerBaseUri, cancellationToken).ConfigureAwait(false);
        using var request = new HttpRequestMessage(HttpMethod.Post, ConsoleServerHttp.BuildUri(ServerBaseUri, RelativePath))
        {
            Content = Content
        };
        ConsoleServerCsrf.AddToken(request, token);
        return await Client.SendAsync(request, cancellationToken).ConfigureAwait(false);
    }
}
