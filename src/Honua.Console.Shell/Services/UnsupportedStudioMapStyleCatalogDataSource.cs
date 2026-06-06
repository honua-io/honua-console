using Honua.Console.Contracts;
using Honua.Console.Shell.Models;

namespace Honua.Console.Shell.Services;

/// <summary>
/// Style-picker data source used when no honua-server base address is configured. It returns a catalog with
/// an explicit missing-binding state instead of fabricating styleIds, keeping the merged runtime free of a
/// standing in-memory style list (Console Patterns Charter section 11). The map builder degrades to the
/// legacy free-form style string when this state is present; the picker stays bound to the real
/// honua-server OGC API - Styles list (ADR-0048) once a server is configured.
/// </summary>
public sealed class UnsupportedStudioMapStyleCatalogDataSource : IStudioMapStyleCatalogDataSource
{
    private const string Surface = "Map builder (style catalog)";

    private static readonly StudioMapCapabilityState MissingBinding = new(
        Surface,
        "Missing binding",
        "Honua:Server:BaseUrl",
        "Configure Honua:Server:BaseUrl or HONUA_SERVER_BASE_URL so the map builder can list the server's "
        + "styles (GET /ogc/styles, ADR-0048). Until then, type the layer's style reference directly.");

    public Task<StudioMapStyleCatalog> GetStyleCatalogAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(new StudioMapStyleCatalog([], null, MissingBinding));

    private const string BindingDetail =
        "Configure Honua:Server:BaseUrl or HONUA_SERVER_BASE_URL so the style editor can read and write styles "
        + "on honua-server (/ogc/styles, ADR-0048).";

    public Task<HonuaAdminEndpointResult<HonuaOgcStylesheet>> GetStylesheetAsync(
        string styleId,
        HonuaOgcStyleEncoding encoding,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(HonuaAdminEndpointResult<HonuaOgcStylesheet>.FromIssue(
            new HonuaAdminEndpointIssue("Missing binding", "GET /ogc/styles/{styleId}", BindingDetail)));

    public Task<HonuaOgcStyleSaveResult> SaveStylesheetAsync(
        string styleId,
        HonuaOgcStyleEncoding encoding,
        string content,
        bool strict,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(HonuaOgcStyleSaveResult.Fail("Missing binding", BindingDetail));
}
