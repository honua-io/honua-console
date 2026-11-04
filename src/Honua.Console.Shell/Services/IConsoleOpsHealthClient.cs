using Honua.Console.Contracts;

namespace Honua.Console.Shell.Services;

public interface IConsoleOpsHealthClient
{
    Task<OperateSectionResult<OpsHealthSnapshotResponse>> GetSnapshotAsync(
        CancellationToken cancellationToken = default);

    Task<OperateSectionResult<OpsHealthHistoryResponse>> GetHistoryAsync(
        ConsoleOpsHealthHistoryQuery query,
        CancellationToken cancellationToken = default);
}

public sealed record ConsoleOpsHealthHistoryQuery(string Window, string Resolution, bool PerReplica)
{
    public static ConsoleOpsHealthHistoryQuery Default { get; } = new("1h", "1m", false);
}
