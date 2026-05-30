using System.Globalization;
using Honua.Console.Contracts;
using Honua.Console.Shell.Models;

namespace Honua.Console.Shell.Services;

/// <summary>
/// Studio report-builder data source bound to the real honua-server content publication registry
/// (honua-server#1183) through the <see cref="IHonuaContentPublicationClient"/> shim. The read path
/// speaks the live console lifecycle (<c>/api/v1/console/publications/{publicationId}</c>); there is
/// no in-memory publication data in the merged result (Console Patterns Charter section 11). Endpoint
/// issues (missing permission, not found, unsupported verb, transport) surface as explicit capability
/// states instead of throwing or fabricating data. A publication that is not a report kind is rejected
/// as unsupported so the report builder never renders a map/dashboard/app artifact.
/// </summary>
public sealed class HonuaServerStudioReportPublicationDataSource : IStudioReportPublicationDataSource
{
    private const string Surface = "Report builder";
    private const string GetContract = "GET /api/v1/console/publications/{publicationId}";

    private readonly IHonuaContentPublicationClient _client;

    public HonuaServerStudioReportPublicationDataSource(IHonuaContentPublicationClient client)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
    }

    public async Task<StudioReportPublicationLoad> LoadAsync(
        string publicationId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(publicationId);

        var result = await _client.GetAsync(publicationId, cancellationToken).ConfigureAwait(false);
        if (result.Issue is { } issue)
        {
            return new StudioReportPublicationLoad(null, [ToCapabilityState(GetContract, issue)]);
        }

        var detail = result.Data!;
        if (!string.Equals(detail.Route.Kind, HonuaContentPublicationKinds.Report, StringComparison.Ordinal))
        {
            return new StudioReportPublicationLoad(
                null,
                [
                    new StudioReportCapabilityState(
                        Surface,
                        "Unsupported",
                        GetContract,
                        $"Publication '{publicationId}' is a '{detail.Route.Kind}' artifact, not a report. Open it in the matching Studio editor.")
                ]);
        }

        return new StudioReportPublicationLoad(StudioReportPublicationMapper.ToView(detail), []);
    }

    private static StudioReportCapabilityState ToCapabilityState(string contract, HonuaAdminEndpointIssue issue) =>
        new(
            Surface,
            issue.State,
            issue.Contract ?? contract,
            issue.StatusCode is null
                ? issue.Detail
                : $"{issue.Detail} HTTP {issue.StatusCode.Value.ToString(CultureInfo.InvariantCulture)}.");
}
