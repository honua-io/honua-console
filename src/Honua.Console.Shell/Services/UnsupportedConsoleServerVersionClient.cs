using Honua.Console.Shell.Models;

namespace Honua.Console.Shell.Services;

public sealed class UnsupportedConsoleServerVersionClient : IConsoleServerVersionClient
{
    private const string Message =
        "Server-version detection is not configured for this Console build. Connect an environment to detect its "
        + "running Honua version before planning an upgrade.";

    public Task<OperateSectionResult<ServerVersionInfo>> GetServerVersionAsync(
        CancellationToken cancellationToken = default) =>
        Task.FromResult(OperateSectionResult<ServerVersionInfo>.Denied(OperateSectionStatus.Unsupported, Message));
}
