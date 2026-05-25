using Honua.Console.Contracts;
using Honua.Console.Shell.Models;

namespace Honua.Console.Native.Core.Security;

/// <summary>
/// Pure, host-agnostic trust evaluation: given a profile's pinned trust state, a freshly observed
/// server fingerprint, the bound client certificate identity, and the server's validation result,
/// computes the resulting <see cref="HonuaEnvironmentTrustState"/> and whether the native connection
/// must be blocked until the operator acknowledges or revalidates (AC#2). No I/O; fully unit-testable.
/// </summary>
public sealed class ConsoleTrustEvaluator
{
    public ConsoleTrustEvaluation Evaluate(ConsoleTrustEvaluationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(context.Profile);

        var requiresCert = context.Profile.ClientCertificate.Enabled;
        var pinnedServer = context.State?.PinnedServerFingerprint ?? string.Empty;
        var pinnedClient = context.State?.PinnedClientCertificateThumbprint ?? string.Empty;
        var observedServer = context.ObservedServerFingerprint ?? string.Empty;
        var clientThumb = context.ClientCertificateThumbprint ?? string.Empty;

        var blocked = false;
        string? reason = null;
        string? message = null;
        var status = HonuaCertificateValidationStatus.NotConfigured;

        // ----- Server identity pinning (trust-on-first-use, change blocks until acknowledged) -----
        var serverFingerprintToPin = pinnedServer;
        var displayServerFingerprint = !string.IsNullOrEmpty(observedServer) ? observedServer : pinnedServer;

        var serverChanged = !string.IsNullOrEmpty(observedServer)
            && !string.IsNullOrEmpty(pinnedServer)
            && !string.Equals(observedServer, pinnedServer, StringComparison.OrdinalIgnoreCase);

        if (context.Acknowledge && !string.IsNullOrEmpty(observedServer))
        {
            // Operator explicitly re-pins the newly observed server identity.
            serverChanged = false;
            serverFingerprintToPin = observedServer;
        }
        else if (!string.IsNullOrEmpty(observedServer) && string.IsNullOrEmpty(pinnedServer))
        {
            // Trust on first use: pin what we observed.
            serverFingerprintToPin = observedServer;
        }

        if (serverChanged)
        {
            blocked = true;
            status = HonuaCertificateValidationStatus.Untrusted;
            reason = ConsoleTrustReasonCodes.ServerCertificateChanged;
            message = "The server TLS certificate fingerprint changed since it was last trusted. "
                + "Acknowledge the new certificate or revalidate before connecting.";
        }

        // ----- Server-validated certificate status -----
        HonuaCertificateValidationStatus? serverValidationStatus = null;
        if (requiresCert && context.Validation is { } validation)
        {
            serverValidationStatus = ConsoleCertificateValidationCodes.ToStatus(
                validation.Code,
                validation.Valid,
                validation.DaysUntilExpiry,
                context.ExpiringSoonThresholdDays);

            if (!blocked)
            {
                status = serverValidationStatus.Value;
                if (ConsoleCertificateValidationCodes.IsBlocking(serverValidationStatus.Value))
                {
                    blocked = true;
                    reason = string.IsNullOrWhiteSpace(validation.Code) ? reason : validation.Code;
                    message = string.IsNullOrWhiteSpace(validation.Detail) ? message : validation.Detail;
                }
            }
        }
        else if (requiresCert && string.IsNullOrEmpty(clientThumb) && !blocked)
        {
            status = HonuaCertificateValidationStatus.Missing;
            blocked = true;
            reason = ConsoleCertificateValidationCodes.Missing;
            message = "The profile requires a client certificate, but the configured certificate reference could not be resolved.";
        }
        else if (requiresCert && context.Validation is null && !blocked)
        {
            // Certificate bound but not validated this round (e.g. revalidation deferred); keep last status.
            status = context.State?.Trust?.Status ?? HonuaCertificateValidationStatus.NotConfigured;
        }

        // ----- Client certificate identity pinning -----
        var clientChanged = requiresCert
            && !string.IsNullOrEmpty(clientThumb)
            && !string.IsNullOrEmpty(pinnedClient)
            && !string.Equals(clientThumb, pinnedClient, StringComparison.OrdinalIgnoreCase);

        var clientChangeValidated = clientChanged
            && context.RevalidateClientCertificateChange
            && serverValidationStatus is { } validationStatus
            && !ConsoleCertificateValidationCodes.IsBlocking(validationStatus);

        if (clientChanged && !clientChangeValidated && !blocked)
        {
            blocked = true;
            status = HonuaCertificateValidationStatus.Untrusted;
            reason = ConsoleTrustReasonCodes.ClientCertificateChanged;
            message = "The bound client certificate changed since it was last trusted. "
                + "Revalidate the certificate before connecting.";
        }

        var clientThumbprintToPin = pinnedClient;
        if (requiresCert && !string.IsNullOrEmpty(clientThumb) && (!clientChanged || clientChangeValidated))
        {
            clientThumbprintToPin = clientThumb;
        }

        var expiresAt = context.ClientCertificateNotAfter;
        if (expiresAt is null && requiresCert && context.Validation is { Valid: true, DaysUntilExpiry: { } days })
        {
            expiresAt = context.Now.AddDays(days);
        }

        var trust = new HonuaEnvironmentTrustState
        {
            Status = status,
            CheckedAt = context.Now,
            ExpiresAt = expiresAt,
            ServerFingerprint = string.IsNullOrEmpty(displayServerFingerprint) ? null : displayServerFingerprint,
            IssuerSummary = context.ClientCertificateIssuer,
            PolicyId = context.Validation?.TrustProfileId,
            ReasonCode = reason,
            SanitizedMessage = message
        };

        // While blocked, hold the existing pins. On acknowledge, force the newly observed server pin.
        var resolvedServerPin = blocked ? pinnedServer : serverFingerprintToPin;
        if (context.Acknowledge && !string.IsNullOrEmpty(observedServer))
        {
            resolvedServerPin = observedServer;
        }

        return new ConsoleTrustEvaluation
        {
            Trust = trust,
            IsBlocked = blocked,
            ServerFingerprintToPin = resolvedServerPin,
            ClientThumbprintToPin = blocked ? pinnedClient : clientThumbprintToPin
        };
    }
}

/// <summary>Inputs for a single trust evaluation.</summary>
public sealed record ConsoleTrustEvaluationContext
{
    public required ConsoleEnvironmentProfile Profile { get; init; }

    /// <summary>Persisted profile state carrying the pinned fingerprints and last trust state.</summary>
    public ConsoleEnvironmentState? State { get; init; }

    /// <summary>SHA-256 fingerprint observed during the current TLS handshake, when reachable.</summary>
    public string? ObservedServerFingerprint { get; init; }

    /// <summary>SHA-256 thumbprint of the resolved bound client certificate, when present.</summary>
    public string? ClientCertificateThumbprint { get; init; }

    /// <summary>Sanitized issuer summary of the bound client certificate, when present.</summary>
    public string? ClientCertificateIssuer { get; init; }

    /// <summary>Bound client certificate expiry, when known.</summary>
    public DateTimeOffset? ClientCertificateNotAfter { get; init; }

    /// <summary>Server validation result for the bound client certificate, when validated this round.</summary>
    public ConsoleClientCertificateValidationResult? Validation { get; init; }

    /// <summary>Whether the operator is acknowledging (re-pinning) the observed server identity.</summary>
    public bool Acknowledge { get; init; }

    /// <summary>
    /// Whether a changed client certificate may replace the pinned thumbprint when the server
    /// validates the new certificate. Connect attempts keep this false; explicit revalidation sets
    /// it true so a changed certificate blocks until it is revalidated.
    /// </summary>
    public bool RevalidateClientCertificateChange { get; init; }

    public int ExpiringSoonThresholdDays { get; init; } = ConsoleCertificateValidationCodes.DefaultExpiringSoonThresholdDays;

    public DateTimeOffset Now { get; init; } = DateTimeOffset.UtcNow;
}

/// <summary>Output of a trust evaluation.</summary>
public sealed record ConsoleTrustEvaluation
{
    public required HonuaEnvironmentTrustState Trust { get; init; }

    /// <summary>True when the native connection must be refused until acknowledged or revalidated.</summary>
    public bool IsBlocked { get; init; }

    /// <summary>Server fingerprint the caller should persist as the pinned value.</summary>
    public string ServerFingerprintToPin { get; init; } = string.Empty;

    /// <summary>Client certificate thumbprint the caller should persist as the pinned value.</summary>
    public string ClientThumbprintToPin { get; init; } = string.Empty;
}
