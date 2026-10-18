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
    private readonly IConsoleAuthorityBindingValidator? _authority;

    public HonuaServerStudioMapStyleCatalogDataSource(IHonuaOgcStylesClient client, IConsoleAuthorityBindingValidator? authority = null)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _authority = authority;
    }

    public async Task<StudioMapStyleCatalog> GetStyleCatalogAsync(CancellationToken cancellationToken = default)
    {
        var result = await _client.ListStylesAsync(cancellationToken).ConfigureAwait(false);
        if (result.Issue is { } issue)
        {
            return new StudioMapStyleCatalog([], null, ToCapabilityState(issue));
        }

        var list = result.Data!;
        var options = (list.Styles ?? [])
            .Select(style => new StudioMapStyleOption(style.Id, style.Title))
            .ToArray();

        return new StudioMapStyleCatalog(options, list.Default, null);
    }

    public Task<HonuaAdminEndpointResult<HonuaOgcStylesheet>> GetLayerStylesheetAsync(
        int layerId, CancellationToken cancellationToken = default) =>
        _client.GetLayerStylesheetAsync(layerId, cancellationToken);

    public Task<HonuaAdminEndpointResult<HonuaOgcStylesheet>> GetStylesheetAsync(
        string styleId,
        HonuaOgcStyleEncoding encoding,
        CancellationToken cancellationToken = default) =>
        _client.GetStylesheetAsync(styleId, encoding, cancellationToken);

    public Task<string?> CaptureAuthorityAsync(CancellationToken cancellationToken = default) =>
        _authority?.CaptureAsync(cancellationToken) ?? Task.FromResult<string?>(null);

    public Task<HonuaAdminEndpointResult<HonuaOgcStylesheet>> GetStylesheetBoundAsync(string styleId, HonuaOgcStyleEncoding encoding, string authorityBinding, CancellationToken cancellationToken = default) =>
        _client.GetStylesheetBoundAsync(styleId, encoding, authorityBinding, cancellationToken);

    public Task<HonuaOgcStyleSaveResult> SaveStylesheetAsync(
        string styleId,
        HonuaOgcStyleEncoding encoding,
        string content,
        bool strict,
        CancellationToken cancellationToken = default) =>
        _client.UpdateStylesheetAsync(styleId, encoding, content, strict, cancellationToken);

    private static StudioMapCapabilityState ToCapabilityState(HonuaAdminEndpointIssue issue) =>
        new(
            Surface,
            issue.State,
            issue.Contract,
            issue.StatusCode is null
                ? issue.Detail
                : $"{issue.Detail} HTTP {issue.StatusCode.Value.ToString(CultureInfo.InvariantCulture)}.");
    public Task<HonuaOgcStyleSaveResult> SaveStylesheetConditionallyAsync(
        string styleId, HonuaOgcStyleEncoding encoding, string content, bool strict, string expectedEntityTag, string expectedImpactHash,
        CancellationToken cancellationToken = default) =>
        _client.UpdateStylesheetConditionallyAsync(styleId, encoding, content, strict, expectedEntityTag, expectedImpactHash, cancellationToken);

    public Task<HonuaAdminEndpointResult<HonuaSharedStyleImpact>> GetSharedStyleImpactAsync(string styleId,
        CancellationToken cancellationToken = default) => _client.GetSharedStyleImpactAsync(styleId, cancellationToken);

    public Task<HonuaAdminEndpointResult<HonuaSharedStyleImpact>> GetSharedStyleImpactBoundAsync(string styleId, string authorityBinding, CancellationToken cancellationToken = default) =>
        _client.GetSharedStyleImpactBoundAsync(styleId, authorityBinding, cancellationToken);
    public Task<HonuaOgcStyleSaveResult> SaveStylesheetBoundAsync(string styleId, HonuaOgcStyleEncoding encoding, string content, bool strict, string expectedEntityTag, string expectedImpactHash, string authorityBinding, CancellationToken cancellationToken = default) =>
        _client.UpdateStylesheetBoundAsync(styleId, encoding, content, strict, expectedEntityTag, expectedImpactHash, authorityBinding, cancellationToken);
    public Task<bool> IsAuthorityCurrentAsync(string binding, CancellationToken cancellationToken = default) => _authority?.IsCurrentAsync(binding, cancellationToken) ?? Task.FromResult(false);
}
