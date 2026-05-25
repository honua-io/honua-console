using System.Collections.Concurrent;
using System.Security.Cryptography.X509Certificates;
using Honua.Console.Contracts;
using Honua.Console.Native.Core.Security;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Services;

namespace Honua.Console.Native.Core.Connections;

/// <summary>
/// Native connection lifecycle and trust gate. Connecting resolves the bound client certificate,
/// observes the server TLS fingerprint, validates the certificate against the real server, and runs
/// the <see cref="ConsoleTrustEvaluator"/>. A changed server identity, changed client certificate, or
/// blocking validation status refuses the connection (no usable transport) and persists a blocking
/// trust state until the operator acknowledges or revalidates (AC#2, AC#3).
/// </summary>
public sealed class ConsoleConnectionManager : IConsoleConnectionManager, IAsyncDisposable
{
    private readonly IConsoleEnvironmentProfileStore _profiles;
    private readonly IClientCertificateResolver _certificateResolver;
    private readonly IConsoleServerCertificateProbe _serverProbe;
    private readonly IConsoleClientCertificateValidationClient _validationClient;
    private readonly ConsoleTrustEvaluator _evaluator;
    private readonly NativeHonuaConnectionFactory _connectionFactory;
    private readonly TimeProvider _timeProvider;

    private readonly ConcurrentDictionary<string, NativeHonuaConnection> _connections =
        new(StringComparer.Ordinal);

    public ConsoleConnectionManager(
        IConsoleEnvironmentProfileStore profiles,
        IClientCertificateResolver certificateResolver,
        IConsoleServerCertificateProbe serverProbe,
        IConsoleClientCertificateValidationClient validationClient,
        ConsoleTrustEvaluator evaluator,
        NativeHonuaConnectionFactory connectionFactory,
        TimeProvider? timeProvider = null)
    {
        _profiles = profiles;
        _certificateResolver = certificateResolver;
        _serverProbe = serverProbe;
        _validationClient = validationClient;
        _evaluator = evaluator;
        _connectionFactory = connectionFactory;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public bool IsConnected(string profileId) =>
        !string.IsNullOrWhiteSpace(profileId) && _connections.ContainsKey(profileId);

    public async Task<ConsoleConnectionOutcome> ConnectAsync(string profileId, CancellationToken cancellationToken = default)
    {
        var profile = await _profiles.GetProfileAsync(profileId, cancellationToken).ConfigureAwait(false);
        if (profile is null)
        {
            return new ConsoleConnectionOutcome
            {
                Status = ConsoleConnectionStatus.Disconnected,
                Message = $"Environment profile '{profileId}' was not found."
            };
        }

        var state = await GetOrCreateStateAsync(profileId, cancellationToken).ConfigureAwait(false);

        // Resolve the bound client certificate exactly once and reuse the same instance for
        // validation and for the transport, so the connection can never attach a different
        // certificate than the trust gate validated. The connection takes ownership on success.
        var certificate = await _certificateResolver.ResolveAsync(profile, cancellationToken).ConfigureAwait(false);
        var certificateOwned = true;
        try
        {
            var (evaluation, unreachable) = await EvaluateAsync(
                    profile,
                    state,
                    certificate,
                    acknowledgeServerCertificate: false,
                    revalidateClientCertificateChange: false,
                    cancellationToken)
                .ConfigureAwait(false);

            if (evaluation.IsBlocked)
            {
                await PersistAsync(state, evaluation, markConnected: false, cancellationToken).ConfigureAwait(false);
                await DisconnectAsync(profileId, cancellationToken).ConfigureAwait(false);
                return new ConsoleConnectionOutcome
                {
                    Status = ConsoleConnectionStatus.Blocked,
                    Trust = evaluation.Trust,
                    Message = evaluation.Trust.SanitizedMessage
                        ?? "The connection is blocked until the certificate change is acknowledged or revalidated."
                };
            }

            if (unreachable)
            {
                return new ConsoleConnectionOutcome
                {
                    Status = ConsoleConnectionStatus.Unreachable,
                    Trust = evaluation.Trust,
                    Message = "The server could not be reached to validate trust."
                };
            }

            var connection = await _connectionFactory
                .CreateAsync(profile, certificate, evaluation.ServerFingerprintToPin, cancellationToken)
                .ConfigureAwait(false);
            certificateOwned = false;
            ReplaceConnection(profileId, connection);
            await PersistAsync(state, evaluation, markConnected: true, cancellationToken).ConfigureAwait(false);

            return new ConsoleConnectionOutcome
            {
                Status = ConsoleConnectionStatus.Connected,
                Trust = evaluation.Trust,
                UsesMutualTls = profile.ClientCertificate.Enabled
                    && !string.IsNullOrEmpty(evaluation.ClientThumbprintToPin),
                Message = "Connected."
            };
        }
        finally
        {
            if (certificateOwned)
            {
                certificate?.Dispose();
            }
        }
    }

    public Task DisconnectAsync(string profileId, CancellationToken cancellationToken = default)
    {
        if (!string.IsNullOrWhiteSpace(profileId) && _connections.TryRemove(profileId, out var connection))
        {
            connection.Dispose();
        }

        return Task.CompletedTask;
    }

    public async Task<ConsoleConnectionOutcome> RevalidateAsync(string profileId, CancellationToken cancellationToken = default)
    {
        var profile = await _profiles.GetProfileAsync(profileId, cancellationToken).ConfigureAwait(false);
        if (profile is null)
        {
            return new ConsoleConnectionOutcome
            {
                Status = ConsoleConnectionStatus.Disconnected,
                Message = $"Environment profile '{profileId}' was not found."
            };
        }

        var state = await GetOrCreateStateAsync(profileId, cancellationToken).ConfigureAwait(false);
        var certificate = await _certificateResolver.ResolveAsync(profile, cancellationToken).ConfigureAwait(false);
        try
        {
            var (evaluation, unreachable) = await EvaluateAsync(
                    profile,
                    state,
                    certificate,
                    acknowledgeServerCertificate: false,
                    revalidateClientCertificateChange: true,
                    cancellationToken)
                .ConfigureAwait(false);
            if (evaluation.IsBlocked || !unreachable)
            {
                await PersistAsync(state, evaluation, markConnected: false, cancellationToken).ConfigureAwait(false);
            }

            if (evaluation.IsBlocked)
            {
                await DisconnectAsync(profileId, cancellationToken).ConfigureAwait(false);
            }

            return BuildLifecycleOutcome(profileId, evaluation, unreachable);
        }
        finally
        {
            certificate?.Dispose();
        }
    }

    public async Task<ConsoleConnectionOutcome> AcknowledgeServerCertificateAsync(string profileId, CancellationToken cancellationToken = default)
    {
        var profile = await _profiles.GetProfileAsync(profileId, cancellationToken).ConfigureAwait(false);
        if (profile is null)
        {
            return new ConsoleConnectionOutcome
            {
                Status = ConsoleConnectionStatus.Disconnected,
                Message = $"Environment profile '{profileId}' was not found."
            };
        }

        var state = await GetOrCreateStateAsync(profileId, cancellationToken).ConfigureAwait(false);
        var certificate = await _certificateResolver.ResolveAsync(profile, cancellationToken).ConfigureAwait(false);
        try
        {
            var (evaluation, unreachable) = await EvaluateAsync(
                    profile,
                    state,
                    certificate,
                    acknowledgeServerCertificate: true,
                    revalidateClientCertificateChange: false,
                    cancellationToken)
                .ConfigureAwait(false);
            if (evaluation.IsBlocked || !unreachable)
            {
                await PersistAsync(state, evaluation, markConnected: false, cancellationToken).ConfigureAwait(false);
            }

            // Terminal-state invariant: a blocked evaluation must not leave a live connection,
            // matching ConnectAsync/RevalidateAsync.
            if (evaluation.IsBlocked)
            {
                await DisconnectAsync(profileId, cancellationToken).ConfigureAwait(false);
            }

            return BuildLifecycleOutcome(profileId, evaluation, unreachable);
        }
        finally
        {
            certificate?.Dispose();
        }
    }

    private ConsoleConnectionOutcome BuildLifecycleOutcome(string profileId, ConsoleTrustEvaluation evaluation, bool unreachable)
    {
        if (evaluation.IsBlocked)
        {
            return new ConsoleConnectionOutcome
            {
                Status = ConsoleConnectionStatus.Blocked,
                Trust = evaluation.Trust,
                Message = evaluation.Trust.SanitizedMessage
                    ?? "The connection is blocked until the certificate change is acknowledged or revalidated."
            };
        }

        if (unreachable)
        {
            return new ConsoleConnectionOutcome
            {
                Status = ConsoleConnectionStatus.Unreachable,
                Trust = evaluation.Trust,
                Message = "The server could not be reached to validate trust."
            };
        }

        return new ConsoleConnectionOutcome
        {
            Status = IsConnected(profileId) ? ConsoleConnectionStatus.Connected : ConsoleConnectionStatus.Disconnected,
            Trust = evaluation.Trust,
            Message = "Trust state refreshed."
        };
    }

    private async Task<(ConsoleTrustEvaluation Evaluation, bool Unreachable)> EvaluateAsync(
        ConsoleEnvironmentProfile profile,
        ConsoleEnvironmentState state,
        X509Certificate2? certificate,
        bool acknowledgeServerCertificate,
        bool revalidateClientCertificateChange,
        CancellationToken cancellationToken)
    {
        // The certificate is resolved once by the caller and reused here for the probe and the
        // server validation so the connection later attaches exactly the validated certificate.
        var clientThumbprint = certificate is null ? null : NativeServerTrust.ComputeSha256Thumbprint(certificate);
        var clientNotAfter = certificate is null ? (DateTimeOffset?)null : certificate.NotAfter.ToUniversalTime();

        var observedFingerprint = await _serverProbe
            .ObserveServerFingerprintAsync(profile, certificate, cancellationToken)
            .ConfigureAwait(false);

        ConsoleClientCertificateValidationResult? validation = null;
        var unreachable = string.Equals(
                profile.ServerBaseUri.Scheme,
                Uri.UriSchemeHttps,
                StringComparison.OrdinalIgnoreCase)
            && string.IsNullOrEmpty(observedFingerprint);
        var pinnedServer = string.IsNullOrEmpty(state.PinnedServerFingerprint)
            ? null
            : state.PinnedServerFingerprint;
        var serverChanged = !string.IsNullOrEmpty(observedFingerprint)
            && !string.IsNullOrEmpty(pinnedServer)
            && !string.Equals(observedFingerprint, pinnedServer, StringComparison.OrdinalIgnoreCase);

        if (profile.ClientCertificate.Enabled
            && certificate is not null
            && !unreachable
            && (!serverChanged || acknowledgeServerCertificate))
        {
            var trustedFingerprint = acknowledgeServerCertificate && !string.IsNullOrEmpty(observedFingerprint)
                ? observedFingerprint
                : pinnedServer ?? observedFingerprint;
            try
            {
                validation = await _validationClient
                    .ValidateAsync(profile, certificate, trustedFingerprint, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception) when (!cancellationToken.IsCancellationRequested)
            {
                unreachable = true;
            }
        }

        var evaluation = _evaluator.Evaluate(new ConsoleTrustEvaluationContext
        {
            Profile = profile,
            State = state,
            ObservedServerFingerprint = observedFingerprint,
            ClientCertificateThumbprint = clientThumbprint,
            ClientCertificateIssuer = SummarizeIssuer(certificate),
            ClientCertificateNotAfter = clientNotAfter,
            Validation = validation,
            Acknowledge = acknowledgeServerCertificate,
            RevalidateClientCertificateChange = revalidateClientCertificateChange,
            Now = _timeProvider.GetUtcNow()
        });

        return (evaluation, unreachable);
    }

    private async Task PersistAsync(
        ConsoleEnvironmentState state,
        ConsoleTrustEvaluation evaluation,
        bool markConnected,
        CancellationToken cancellationToken)
    {
        var persisted = state with
        {
            Trust = evaluation.Trust,
            TrustBlocked = evaluation.IsBlocked,
            PinnedServerFingerprint = evaluation.ServerFingerprintToPin,
            PinnedClientCertificateThumbprint = evaluation.ClientThumbprintToPin
        };

        if (markConnected)
        {
            persisted = persisted with { LastConnectedAt = _timeProvider.GetUtcNow() };
        }

        await _profiles.SaveStateAsync(persisted, cancellationToken).ConfigureAwait(false);
    }

    private async Task<ConsoleEnvironmentState> GetOrCreateStateAsync(string profileId, CancellationToken cancellationToken)
    {
        var state = await _profiles.GetStateAsync(profileId, cancellationToken).ConfigureAwait(false);
        return state ?? new ConsoleEnvironmentState { ProfileId = profileId, LastRoute = "/environments" };
    }

    private void ReplaceConnection(string profileId, NativeHonuaConnection connection)
    {
        if (_connections.TryRemove(profileId, out var existing))
        {
            existing.Dispose();
        }

        _connections[profileId] = connection;
    }

    private static string? SummarizeIssuer(X509Certificate2? certificate) =>
        certificate is null || string.IsNullOrWhiteSpace(certificate.Issuer) ? null : certificate.Issuer;

    public async ValueTask DisposeAsync()
    {
        foreach (var key in _connections.Keys.ToArray())
        {
            if (_connections.TryRemove(key, out var connection))
            {
                await connection.DisposeAsync().ConfigureAwait(false);
            }
        }
    }
}
