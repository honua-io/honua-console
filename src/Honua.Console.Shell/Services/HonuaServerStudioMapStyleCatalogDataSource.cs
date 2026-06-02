using System.Globalization;
using Honua.Console.Contracts;
using Honua.Console.Shell.Models;

namespace Honua.Console.Shell.Services;

/// <summary>
/// Studio map-builder style-picker data source bound to the honua-server OGC API - Styles list
/// (<c>GET /ogc/styles</c>, ADR-0048) through the <see cref="IHonuaOgcStylesClient"/> shim. The read speaks
/// the live, public OGC metadata surface; there is no in-memory style list in the merged result (Console
/// Patterns Charter section 11). An endpoint issue (missing permission, not found / unsupported, transport)
/// surfaces as an explicit capability state on the returned catalog instead of throwing or fabricating
/// styleIds, so the builder can fall back to the legacy free-form style string.
/// </summary>
public sealed class HonuaServerStudioMapStyleCatalogDataSource : IStudioMapStyleCatalogDataSource
{
    private const string Surface = "Map builder (style catalog)";

    private readonly IHonuaOgcStylesClient _client;

    public HonuaServerStudioMapStyleCatalogDataSource(IHonuaOgcStylesClient client)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
    }

    public async Task<StudioMapStyleCatalog> GetStyleCatalogAsync(CancellationToken cancellationToken = default)
    {
        var result = await _client.ListStylesAsync(cancellationToken).ConfigureAwait(false);
        if (result.Issue is { } issue)
        {
            return new StudioMapStyleCatalog([], null, ToCapabilityState(issue));
        }

        var list = result.Data!;
        var options = list.Styles
            .Select(style => new StudioMapStyleOption(style.Id, style.Title))
            .ToArray();

        return new StudioMapStyleCatalog(options, list.Default, null);
    }

    private static StudioMapCapabilityState ToCapabilityState(HonuaAdminEndpointIssue issue) =>
        new(
            Surface,
            issue.State,
            issue.Contract,
            issue.StatusCode is null
                ? issue.Detail
                : $"{issue.Detail} HTTP {issue.StatusCode.Value.ToString(CultureInfo.InvariantCulture)}.");
}
