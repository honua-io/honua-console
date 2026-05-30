using Honua.Console.Shell.Models;

namespace Honua.Console.Shell.Services;

/// <summary>
/// Missing-binding implementation of <see cref="ITemporalCapabilityClient"/>. This is the only
/// temporal client registered in the merged build until the honua-server#1166 (temporal capability)
/// and honua-server#1167 (disconnected sync conflict review) contracts land. It returns no sources and
/// a single neutral capability state so the viewer renders an explicit explanation rather than an empty
/// surface (issue AC #1), and it never fabricates temporal history from a standing mock.
/// </summary>
public sealed class UnsupportedTemporalCapabilityClient : ITemporalCapabilityClient
{
    private static readonly TemporalViewerWorkspace Workspace = new(
        Sources: [],
        CapabilityStates:
        [
            new TemporalCapabilityState(
                "Temporal viewer",
                "Missing binding",
                "honua-server#1166 / honua-server#1167",
                "Temporal history and disconnected sync conflict review bind to the server-owned temporal "
                    + "capability manifest (honua-server#1166) and the sync conflict review contract "
                    + "(honua-server#1167). Configure Honua:Server:BaseUrl (or HONUA_SERVER_BASE_URL) and wait "
                    + "for those contracts to land; Console will not fabricate temporal data from a mock."),
        ]);

    public Task<TemporalViewerWorkspace> GetWorkspaceAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(Workspace);
}
