using Honua.Console.Contracts;
using Honua.Console.Shell.Models;

namespace Honua.Console.Shell.Services;

public sealed partial class HttpConsoleOperateObservabilityClient
{
    public async Task<OperateSectionResult<ConsoleActivityFeedPage>> ReadAsync(
        ConsoleActivityFeedQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        var fetch = await FetchAsync(
            OperateAdminRoutes.Audit + ConsoleActivityPresentation.QueryString(query),
            OperateObservabilityJsonContext.Default.ObservabilityAuditPageResponse,
            cancellationToken).ConfigureAwait(false);
        return fetch.Ok
            ? OperateSectionResult<ConsoleActivityFeedPage>.Allowed(new(fetch.Value!.Items, fetch.Value.NextCursor))
            : OperateSectionResult<ConsoleActivityFeedPage>.Denied(fetch.Status, fetch.Message, fetch.Detail);
    }
}
