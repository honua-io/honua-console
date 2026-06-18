using Honua.Console.Shell.Models;

namespace Honua.Console.Shell.Services;

/// <summary>
/// Active when no live honua-server is configured for this Console build. Per the
/// Console missing-binding convention, the read renders an explicit unsupported state
/// rather than fabricating a server version. The server-upgrade flow then gates on the
/// unknown current version instead of offering an upgrade against invented data.
/// </summary>
public sealed class UnsupportedConsoleServerVersionClient : IConsoleServerVersionClient
{
    private const string Message =
        "Server-version detection is not configured for this Console build. Connect an environment to detect its "
        + "running Honua version before planning an upgrade.";

    public Task<OperateSectionResult<ServerVersionInfo>> GetServerVersionAsync(
        CancellationToken cancellationToken = default) =>
        Task.FromResult(OperateSectionResult<ServerVersionInfo>.Denied(OperateSectionStatus.Unsupported, Message));
}
