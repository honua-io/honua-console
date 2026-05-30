using Honua.Console.Shell.Models;

namespace Honua.Console.Shell.Services;

/// <summary>
/// Temporal data viewer + disconnected sync conflict review data source (honua-console#43).
/// The merged build binds the server-owned temporal capability manifest (honua-server#1166) and the
/// disconnected sync conflict review contract (honua-server#1167); there is no standing in-memory
/// temporal client in the shipped result (Console Patterns Charter section 11). When no server binding
/// is configured — or those contracts have not landed — the unsupported implementation surfaces an
/// explicit missing-binding state instead of fabricating temporal history or sync conflicts.
/// </summary>
public interface ITemporalCapabilityClient
{
    /// <summary>
    /// Lists the temporal-eligible sources the caller may inspect plus any binding/capability states.
    /// Unsupported or unbound sources are reported through <see cref="TemporalViewerWorkspace.CapabilityStates"/>
    /// so the viewer can render a capability explanation rather than an empty surface.
    /// </summary>
    Task<TemporalViewerWorkspace> GetWorkspaceAsync(CancellationToken cancellationToken = default);
}
