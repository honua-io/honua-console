using Honua.Console.Contracts;
using Honua.Console.Shell.Models;

namespace Honua.Console.Shell.Services;

public interface IStudioMapStyleCatalogDataSource
{
    Task<StudioMapStyleCatalog> GetStyleCatalogAsync(CancellationToken cancellationToken = default);

    Task<HonuaAdminEndpointResult<HonuaOgcStylesheet>> GetLayerStylesheetAsync(
        int layerId, CancellationToken cancellationToken = default) => Task.FromResult(
            HonuaAdminEndpointResult<HonuaOgcStylesheet>.FromIssue(new HonuaAdminEndpointIssue(
                "Unsupported", "GET /api/styles/{layerId}.json", "Layer styles are not bound.")));

    Task<HonuaAdminEndpointResult<HonuaOgcStylesheet>> GetStylesheetAsync(
        string styleId,
        HonuaOgcStyleEncoding encoding,
        CancellationToken cancellationToken = default);

    // The server stores MapLibre as the canonical stylesheet, whichever encoding was saved.
    Task<HonuaOgcStyleSaveResult> SaveStylesheetAsync(
        string styleId,
        HonuaOgcStyleEncoding encoding,
        string content,
        bool strict,
        CancellationToken cancellationToken = default);
    Task<HonuaOgcStyleSaveResult> SaveStylesheetConditionallyAsync(
        string styleId, HonuaOgcStyleEncoding encoding, string content, bool strict, string expectedEntityTag, string expectedImpactHash,
        CancellationToken cancellationToken = default) => Task.FromResult(
            HonuaOgcStyleSaveResult.Fail("Unsupported", "Versioned shared-style editing is not bound."));

    Task<HonuaAdminEndpointResult<HonuaSharedStyleImpact>> GetSharedStyleImpactAsync(string styleId,
        CancellationToken cancellationToken = default) => Task.FromResult(
            HonuaAdminEndpointResult<HonuaSharedStyleImpact>.FromIssue(new HonuaAdminEndpointIssue(
                "Unsupported", "GET /ogc/styles/{id}/impact", "Shared-style impact review is not bound.")));

    Task<HonuaAdminEndpointResult<HonuaSharedStyleImpact>> GetSharedStyleImpactBoundAsync(string styleId, string authorityBinding, CancellationToken cancellationToken = default) =>
        Task.FromResult(HonuaAdminEndpointResult<HonuaSharedStyleImpact>.FromIssue(new("Unsupported", "GET /ogc/styles/{id}/impact", "Operator-bound style editing is unavailable.")));
    Task<HonuaOgcStyleSaveResult> SaveStylesheetBoundAsync(string styleId, HonuaOgcStyleEncoding encoding, string content, bool strict, string expectedEntityTag, string expectedImpactHash, string authorityBinding, CancellationToken cancellationToken = default) =>
        Task.FromResult(HonuaOgcStyleSaveResult.Fail("Unsupported", "Operator-bound style editing is unavailable."));
    Task<string?> CaptureAuthorityAsync(CancellationToken cancellationToken = default) => Task.FromResult<string?>(null);
    Task<HonuaAdminEndpointResult<HonuaOgcStylesheet>> GetStylesheetBoundAsync(string styleId, HonuaOgcStyleEncoding encoding, string authorityBinding, CancellationToken cancellationToken = default) =>
        Task.FromResult(HonuaAdminEndpointResult<HonuaOgcStylesheet>.FromIssue(new("Unsupported", "GET /ogc/styles/{id}", "Operator-bound style reading is unavailable.")));
    Task<bool> IsAuthorityCurrentAsync(string binding, CancellationToken cancellationToken = default) => Task.FromResult(false);
}
