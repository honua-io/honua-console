using Honua.Console.Shell.Models;

namespace Honua.Console.Shell.Services;

/// <summary>
/// Test/demo-only in-memory server-version client. It is NEVER the DI default (Console
/// Patterns Charter section 11: server-owned data binds to a live server, not a standing
/// mock); it exists so bUnit/component tests and explicit demo shells can drive the
/// server-upgrade flow's version-mismatch detection without a backend.
/// </summary>
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
