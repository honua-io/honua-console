using Honua.Console.Shell.Models;

namespace Honua.Console.Shell.Services;

/// <summary>
/// Studio report-builder data source. The first server-bound slice reads a report's server-owned
/// publication state (route + immutable versions) from the honua-server content publication registry
/// (honua-server#1183) through the Honua.Console.Contracts shim; there is no standing in-memory
/// publication client in the merged result (Console Patterns Charter section 11). When no server
/// binding is configured, the unsupported implementation surfaces an explicit missing-binding state
/// rather than fabricating report data. Authoring (data bindings, Vega-Lite charts, layout, narrative)
/// and publish/share/embed mutation are built on top of this read binding in follow-up slices.
/// </summary>
public interface IStudioReportPublicationDataSource
{
    /// <summary>
    /// Loads a report publication's current route state and version history, or returns capability
    /// states (missing binding/permission, not found, unsupported) when it cannot be read.
    /// </summary>
    Task<StudioReportPublicationLoad> LoadAsync(
        string publicationId,
        CancellationToken cancellationToken = default);
}
