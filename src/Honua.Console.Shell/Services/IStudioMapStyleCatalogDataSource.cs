using Honua.Console.Shell.Models;

namespace Honua.Console.Shell.Services;

/// <summary>
/// Supplies the Studio map builder's per-layer style picker with the styleIds the server advertises on the
/// OGC API - Styles list (<c>GET /ogc/styles</c>, ADR-0048, umbrella honua-server#1387) through the
/// Honua.Console.Contracts shim. The live binding activates only when a server base URL is configured; with
/// no server configured the unsupported implementation returns a missing-binding capability state on the
/// catalog so the builder degrades to the legacy free-form style string rather than fabricating a list.
/// </summary>
public interface IStudioMapStyleCatalogDataSource
{
    /// <summary>Loads the server-advertised style catalog, or a catalog carrying a capability issue.</summary>
    Task<StudioMapStyleCatalog> GetStyleCatalogAsync(CancellationToken cancellationToken = default);
}
