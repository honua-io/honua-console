using Honua.Console.Shell.Models;

namespace Honua.Console.Shell.Services;

/// <summary>
/// The console's service-import OPERATION: discovers the importable layers of a remote Esri/OGC service via
/// honua-server (<c>POST /api/v1/admin/external-services/discover</c>). The live implementation is DI-gated on
/// a configured server base URL; otherwise the surface binds to
/// <see cref="UnsupportedConsoleServiceImportOperation"/> (missing-binding, no network call).
/// </summary>
public interface IConsoleServiceImportOperation
{
    /// <summary>
    /// Discovers the service(s) and candidate layers at the given (HTTPS) URL. The URL may point to a single
    /// service (FeatureServer/MapServer/WFS/OGC) or an ArcGIS catalog root/folder, which is enumerated into
    /// multiple services. <paramref name="auth"/> carries optional credentials for protected sources.
    /// </summary>
    Task<ConsoleServiceImportResult> DiscoverAsync(
        string serviceUrl,
        ConsoleServiceImportAuth? auth = null,
        CancellationToken cancellationToken = default);
}
