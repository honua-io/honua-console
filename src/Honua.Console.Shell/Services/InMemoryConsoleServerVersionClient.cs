using Honua.Console.Shell.Models;

namespace Honua.Console.Shell.Services;

public sealed class InMemoryConsoleServerVersionClient : IConsoleServerVersionClient
{
    private readonly OperateSectionResult<ServerVersionInfo> _result;

    public InMemoryConsoleServerVersionClient(ServerVersionInfo version)
        => _result = OperateSectionResult<ServerVersionInfo>.Allowed(version);

    public InMemoryConsoleServerVersionClient(OperateSectionStatus status, string message)
        => _result = OperateSectionResult<ServerVersionInfo>.Denied(status, message);

    public Task<OperateSectionResult<ServerVersionInfo>> GetServerVersionAsync(
        CancellationToken cancellationToken = default) =>
        Task.FromResult(_result);
}
