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
    string ServerBaseUri)
{
    public string ServerOrigin => ConsoleServerSessionClientStore.NormalizeOrigin(new Uri(ServerBaseUri)).AbsoluteUri;
}

internal sealed record ConsoleServerSignInTarget(
    string ProfileId, ConsoleServerAuthority Authority, string? ProviderKey);

internal sealed record ConsoleServerSignInQuery(
    string ProfileId, string? Provider, string? ReturnTo, ConsoleServerAuthority? Authority);

internal sealed record ConsoleServerAuthPendingFlow(
    string State,
    string OperatorKey,
    string ProfileId,
    string ProviderKey,
    Uri ServerBaseUri,
    string ReturnTo,
    DateTimeOffset ExpiresAt)
{
    public ConsoleServerAuthority Authority { get; init; }
    public string? ProfileBinding { get; init; }
}

internal sealed class ConsoleServerSessionPartition : IDisposable
{
    private long _lastAccessUnixMilliseconds;
    private readonly SemaphoreSlim _authenticationGate = new(1, 1);

    public ConsoleServerSessionPartition(
        ConsoleServerSessionPartitionKey key,
        CookieContainer cookies,
        HttpClient client,
        DateTimeOffset createdAt,
        TimeProvider timeProvider)
    {
        Key = key;
        Cookies = cookies;
        Client = client;
        ProposalReviews = new(timeProvider);
        Touch(createdAt);
    }

    public ConsoleServerSessionPartitionKey Key { get; }

    public CookieContainer Cookies { get; }

    public string? AuthenticatedProviderKey { get; set; }

    public HttpClient Client { get; }

    public ConsoleProposalReviewCache ProposalReviews { get; }

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

    public void Dispose()
    {
        ProposalReviews.Clear();
        Client.Dispose();
    }

    private sealed class AuthenticationLease(SemaphoreSlim gate) : IDisposable
    {
        public void Dispose() => gate.Release();
    }
}

// A process restart discards these sessions, requiring a fresh server sign-in.
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
        created = false;
        ArgumentException.ThrowIfNullOrWhiteSpace(operatorKey);
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentException.ThrowIfNullOrWhiteSpace(profile.Id);

        var now = _timeProvider.GetUtcNow();
        var origin = NormalizeOrigin(profile.ServerBaseUri);
        var key = new ConsoleServerSessionPartitionKey(operatorKey, profile.Id, profile.ServerBaseUri.AbsoluteUri);
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
                BaseAddress = profile.ServerBaseUri,
                Timeout = TimeSpan.FromSeconds(30)
            };
            var partition = new ConsoleServerSessionPartition(key, cookies, client, now, _timeProvider);
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
            profile.ServerBaseUri.AbsoluteUri);
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
            if (_pending.TryGetValue(pending.State, out var existing)
                && (existing.OperatorKey != pending.OperatorKey || existing.ProfileId != pending.ProfileId
                    || existing.ServerBaseUri != pending.ServerBaseUri))
                throw new InvalidOperationException("The server sign-in state conflicts with another authority.");
            foreach (var state in _pending
                         .Where(item => string.Equals(item.Value.OperatorKey, pending.OperatorKey, StringComparison.Ordinal)
                             && string.Equals(item.Value.ProfileId, pending.ProfileId, StringComparison.Ordinal)
                             && item.Value.ServerBaseUri == pending.ServerBaseUri)
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
            profile.ServerBaseUri.AbsoluteUri);
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

    internal static Uri NormalizeOrigin(Uri serverBaseUri)
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
    private readonly IConsoleManagerEndpoint? _management;

    public ConsoleServerSessionBffCoordinator(
        IConsoleOperatorContext operatorContext,
        IConsoleEnvironmentProfileStore profiles,
        IConsoleAccountSessionStore sessions,
        ConsoleServerSessionClientStore serverSessions,
        TimeProvider timeProvider,
        ConsoleServerSessionIdentityBinding? identityBinding = null,
        IConsoleManagerEndpoint? management = null)
    {
        _management = management;
        _identityBinding = identityBinding;
        _operatorContext = operatorContext ?? throw new ArgumentNullException(nameof(operatorContext));
        _profiles = profiles ?? throw new ArgumentNullException(nameof(profiles));
        _sessions = sessions ?? throw new ArgumentNullException(nameof(sessions));
        _serverSessions = serverSessions ?? throw new ArgumentNullException(nameof(serverSessions));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    }

    public Task<ConsoleServerSignInResult> BeginSignInAsync(
        string profileId,
        string? providerKey,
        string? returnTo,
        CancellationToken cancellationToken = default) => BeginAuthoritySignInAsync(
            new ConsoleServerSignInTarget(profileId, ConsoleServerAuthority.Serving, providerKey), returnTo, cancellationToken);

    public async Task<ConsoleServerSignInResult> BeginAuthoritySignInAsync(
        ConsoleServerSignInTarget target, string? returnTo,
        CancellationToken cancellationToken = default)
    {
        var (profileId, authority, providerKey) = target;
        var operatorKey = _operatorContext.RequireOperatorKey();
        var selected = await _profiles.GetProfileAsync(profileId, cancellationToken).ConfigureAwait(false);
        var profile = selected is null ? null : ResolveAuthorityProfile(selected, authority);
        if (profile is null || profile.Account.AuthMode != ConsoleAccountAuthMode.AccountRbac)
        {
            return Unavailable("The selected environment is not configured for an operator account.", returnTo);
        }

        var profileBinding = ConsoleServerProfileBinding.Capture(selected!);

        var created = false;
        var retainPartition = false;
        ConsoleServerSessionPartition? partition = null;
        try
        {
            partition = _serverSessions.GetOrCreate(operatorKey, profile, out created);
            using var configResponse = await partition.Client
                .GetAsync(ConsoleServerHttp.BuildUri(profile.ServerBaseUri, "api/v1/admin/auth/config"), cancellationToken)
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

            using var authorizeResponse = await partition.Client.PostAsJsonAsync(
                ConsoleServerHttp.BuildUri(
                    profile.ServerBaseUri,
                    $"api/v1/admin/auth/providers/{Uri.EscapeDataString(provider.Key)}/authorize-url"),
                new ConsoleServerAuthAuthorizeRequestWire(),
                ConsoleServerSessionBffJsonContext.Default.ConsoleServerAuthAuthorizeRequestWire,
                cancellationToken).ConfigureAwait(false);
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
                profile.Id,
                provider.Key,
                profile.ServerBaseUri,
                ConsoleReturnUrl.Sanitize(returnTo),
                _timeProvider.GetUtcNow().Add(PendingLifetime))
            {
                Authority = authority,
                ProfileBinding = profileBinding
            });
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

    private ConsoleEnvironmentProfile? ResolveAuthorityProfile(ConsoleEnvironmentProfile selected, ConsoleServerAuthority authority)
        => ConsoleManagementBinding.Resolve(_management, selected.ServerBaseUri).ForAuthority(selected, authority);

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

        var selected = await _profiles.GetProfileAsync(pending.ProfileId, cancellationToken).ConfigureAwait(false);
        var profile = selected is null ? null : ResolveAuthorityProfile(selected, pending.Authority);
        if (profile is null || !SameServer(profile.ServerBaseUri, pending.ServerBaseUri)
            || pending.ProfileBinding != ConsoleServerProfileBinding.Capture(selected!))
        {
            return Denied("The environment profile changed while server sign-in was in progress.", pending.ReturnTo);
        }

        try
        {
            var partition = _serverSessions.GetOrCreate(operatorKey, profile);
            using var authentication = await partition.EnterAuthenticationAsync(cancellationToken).ConfigureAwait(false);
            var snapshots = _sessions as IConsoleAccountSessionSnapshotStore;
            var snapshot = snapshots is null ? null
                : await snapshots.CaptureSessionAsync(profile.Id, cancellationToken).ConfigureAwait(false);
            var existing = snapshot is not null ? snapshot.Session
                : await _sessions.GetSessionAsync(profile.Id, cancellationToken).ConfigureAwait(false);
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

            var identityRequest = new ConsoleServerSessionIdentityRequest
            {
                Partition = partition, Profile = profile,
                OperatorKey = operatorKey, ProviderKey = pending.ProviderKey,
                Authority = pending.Authority,
            };
            var identityMatches = _identityBinding is null || (pending.Authority == ConsoleServerAuthority.Management
                ? await _identityBinding.EnsureHumanSessionAsync(identityRequest, cancellationToken).ConfigureAwait(false)
                : await _identityBinding.EnsureAsync(identityRequest, cancellationToken).ConfigureAwait(false));
            if (!identityMatches)
            {
                return Denied("The server session does not match the authenticated Console operator.", pending.ReturnTo);
            }

            partition.AuthenticatedProviderKey = pending.ProviderKey;
            partition.ProposalReviews.Clear();
            if (pending.Authority == ConsoleServerAuthority.Management)
                return new ConsoleServerSignInResult { Status = ConsoleServerSignInStatus.Redirect, RedirectUri = pending.ReturnTo };
            var exchange = await new HttpConsoleOperatorBearerExchange(partition.Client)
                .ExchangeAsync(profile, cancellationToken).ConfigureAwait(false);
            if (exchange.Status != ConsoleOperatorBearerExchangeStatus.Issued
                || string.IsNullOrWhiteSpace(exchange.AccessToken)
                || exchange.ExpiresAt is null)
            {
                return exchange.Status == ConsoleOperatorBearerExchangeStatus.Denied
                    ? Denied(exchange.Message, pending.ReturnTo)
                    : Unavailable(exchange.Message, pending.ReturnTo);
            }

            var updatedSession = CreateBoundAccountSession(identityRequest, existing, exchange);
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

    private static ConsoleAccountSession CreateBoundAccountSession(
        ConsoleServerSessionIdentityRequest identity, ConsoleAccountSession? existing,
        ConsoleOperatorBearerExchangeResult exchange)
    {
        var profile = identity.Profile;
        return (existing ?? new ConsoleAccountSession
        {
            ProfileId = profile.Id,
            AccountId = identity.OperatorKey,
            DisplayName = profile.Account.DisplayName,
            TenantId = string.IsNullOrWhiteSpace(profile.Account.TenantId)
                ? profile.TenantId
                : profile.Account.TenantId,
        }) with
        {
            AccessToken = exchange.AccessToken,
            ServerBaseUri = profile.ServerBaseUri,
            AccessTokenExpiresAt = exchange.ExpiresAt,
        };
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
            return await EnterPrivateSessionAsync(profile, cancellationToken).ConfigureAwait(false)
                ?? (IConsoleOperatorBearerExchangeLease)new UnavailableLease(ConsoleOperatorBearerExchangeResult.Denied(
                    "No honua-server session is available. Sign in to the server again."));
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            return new UnavailableLease(ConsoleOperatorBearerExchangeResult.Unavailable(
                "The per-operator honua-server session is unavailable. Sign in to the server again."));
        }
    }

    internal async Task<ConsoleAuthenticatedServerSession?> EnterPrivateSessionAsync(
        ConsoleEnvironmentProfile profile, CancellationToken cancellationToken) =>
        await EnterPrivateSessionAsync(profile, ConsoleServerAuthority.Serving, cancellationToken).ConfigureAwait(false);

    internal async Task<ConsoleAuthenticatedServerSession?> EnterPrivateSessionAsync(
        ConsoleEnvironmentProfile profile, ConsoleServerAuthority authority, CancellationToken cancellationToken)
    {
        var operatorKey = _operatorContext.RequireOperatorKey();
        if (!_serverSessions.TryGet(operatorKey, profile, out var partition) || partition is null) return null;
        var authentication = await partition.EnterAuthenticationAsync(cancellationToken).ConfigureAwait(false);
        var request = new ConsoleServerSessionIdentityRequest
        {
            Partition = partition, Profile = profile, OperatorKey = operatorKey,
            ProviderKey = partition.AuthenticatedProviderKey, Authority = authority
        };
        return new ConsoleAuthenticatedServerSession(request, _identityBinding, authentication);
    }

    private sealed class UnavailableLease(ConsoleOperatorBearerExchangeResult result) : IConsoleOperatorBearerExchangeLease
    {
        public Task<ConsoleOperatorBearerExchangeResult> ExchangeAsync(
            ConsoleEnvironmentProfile profile, CancellationToken cancellationToken = default) => Task.FromResult(result);

        public void Dispose() { }
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
        builder.Services.TryAddSingleton<ConsoleServerSessionBffCoordinator>();
        builder.Services.TryAddSingleton<PartitionedConsoleOperatorBearerExchange>();
        builder.Services.TryAddSingleton<ConsoleHumanProposalSession>();
        builder.Services.TryAddScoped<IConsoleHumanProposalSession, BrowserHumanProposalSession>();
        builder.Services.Replace(ServiceDescriptor.Singleton<IConsoleOperatorBearerExchange>(
            services => services.GetRequiredService<PartitionedConsoleOperatorBearerExchange>()));
        return builder;
    }

    public static WebApplication MapConsoleServerSessionBff(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        app.MapGet("/auth/server/login", async (
            [AsParameters] ConsoleServerSignInQuery query,
            ConsoleServerSessionBffCoordinator coordinator,
            CancellationToken cancellationToken) =>
        {
            var target = new ConsoleServerSignInTarget(
                query.ProfileId, query.Authority ?? ConsoleServerAuthority.Serving, query.Provider);
            var result = await coordinator.BeginAuthoritySignInAsync(
                target,
                query.ReturnTo,
                cancellationToken).ConfigureAwait(false);
            return ToHttpResult(result, target, query.ReturnTo);
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
        ConsoleServerSignInResult result, ConsoleServerSignInTarget target, string? returnTo) => result.Status switch
        {
            ConsoleServerSignInStatus.Redirect => Results.Redirect(result.RedirectUri),
            ConsoleServerSignInStatus.SelectProvider => Results.Content(
                BuildProviderSelection(result, target, returnTo),
                "text/html; charset=utf-8"),
            ConsoleServerSignInStatus.Denied => Results.Redirect(BuildFailureUri(result)),
            _ => Results.Redirect(BuildFailureUri(result))
        };

    private static string BuildProviderSelection(
        ConsoleServerSignInResult result, ConsoleServerSignInTarget target, string? returnTo)
    {
        var encoder = HtmlEncoder.Default;
        var links = string.Join(
            string.Empty,
            result.Providers.Select(provider =>
            {
                var href = QueryHelpers.AddQueryString("/auth/server/login", new Dictionary<string, string?>
                {
                    ["profileId"] = target.ProfileId,
                    ["authority"] = target.Authority.ToString(),
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
