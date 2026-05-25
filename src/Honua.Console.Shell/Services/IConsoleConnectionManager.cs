using Honua.Console.Contracts;

namespace Honua.Console.Shell.Services;

/// <summary>
/// Native-host connection lifecycle and trust gate for an environment profile. Registered only by
/// the native host (<c>AddHonuaConsoleNativeCore()</c>); the browser host leaves this unresolved, so
/// shared routes resolve it as an optional service and render an unsupported state when absent.
/// </summary>
public interface IConsoleConnectionManager
{
    /// <summary>Whether a live native connection is currently held for the profile.</summary>
    bool IsConnected(string profileId);

    /// <summary>
    /// Opens a native connection through the trust gate. A changed server identity, changed or
    /// expired client certificate, or a blocking validation status returns a
    /// <see cref="ConsoleConnectionStatus.Blocked"/> outcome with no usable connection until the
    /// operator acknowledges or revalidates.
    /// </summary>
    Task<ConsoleConnectionOutcome> ConnectAsync(string profileId, CancellationToken cancellationToken = default);

    /// <summary>Disposes the live native connection for the profile, retaining the profile and its state.</summary>
    Task DisconnectAsync(string profileId, CancellationToken cancellationToken = default);

    /// <summary>Re-runs trust validation against the server without returning a connection.</summary>
    Task<ConsoleConnectionOutcome> RevalidateAsync(string profileId, CancellationToken cancellationToken = default);

    /// <summary>Re-pins the currently observed server certificate and clears a cert-changed block.</summary>
    Task<ConsoleConnectionOutcome> AcknowledgeServerCertificateAsync(string profileId, CancellationToken cancellationToken = default);
}

/// <summary>Outcome of a connect, revalidate, or acknowledge operation.</summary>
public sealed record ConsoleConnectionOutcome
{
    public required ConsoleConnectionStatus Status { get; init; }

    /// <summary>Server-validated trust state captured by the operation.</summary>
    public HonuaEnvironmentTrustState Trust { get; init; } = new();

    /// <summary>Sanitized operator-facing message.</summary>
    public string Message { get; init; } = string.Empty;

    /// <summary>True when the established native connection attached a client certificate.</summary>
    public bool UsesMutualTls { get; init; }

    public bool IsConnected => Status == ConsoleConnectionStatus.Connected;

    public bool IsBlocked => Status == ConsoleConnectionStatus.Blocked;
}

/// <summary>Connection lifecycle status.</summary>
public enum ConsoleConnectionStatus
{
    /// <summary>A live native connection is held.</summary>
    Connected,

    /// <summary>Trust is blocked; the connection is refused until acknowledged or revalidated.</summary>
    Blocked,

    /// <summary>The profile is not connected.</summary>
    Disconnected,

    /// <summary>The server could not be reached to establish or validate the connection.</summary>
    Unreachable
}
