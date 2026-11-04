using Honua.Console.Shell.Models;

namespace Honua.Console.Shell.Services;

// The upgrade flow reads this version, including from an older server, so it can show the path and refuse a no-op or a downgrade.
public interface IConsoleServerVersionClient
{
    Task<OperateSectionResult<ServerVersionInfo>> GetServerVersionAsync(
        CancellationToken cancellationToken = default);
}
