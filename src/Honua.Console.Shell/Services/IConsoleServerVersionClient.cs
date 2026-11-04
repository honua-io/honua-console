using Honua.Console.Shell.Models;

namespace Honua.Console.Shell.Services;

public interface IConsoleServerVersionClient
{
    Task<OperateSectionResult<ServerVersionInfo>> GetServerVersionAsync(
        CancellationToken cancellationToken = default);
}
