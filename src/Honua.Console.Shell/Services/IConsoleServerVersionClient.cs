using Honua.Console.Shell.Models;

namespace Honua.Console.Shell.Services;

/// <summary>
/// Reads the running Honua version of the active environment's server from the
/// capability manifest (<c>GET /api/v1/capabilities/manifest</c>, the <c>server</c>
/// block). The server-upgrade flow uses this to detect a target environment's current
/// version — including the case where the target is on an OLDER Honua version — so the
/// upgrade path can be shown and the upgrade gated against a no-op or downgrade.
///
/// Each read returns an <see cref="OperateSectionResult{T}"/> carrying a status that
/// drives the shared missing / forbidden / unsupported / unavailable surfaces. Per the
/// Console Patterns Charter section 11, this binds to a live server and never to a
/// standing in-memory/mock source; when no environment is bound the read returns a
/// missing-binding result rather than seeded data.
/// </summary>
public interface IConsoleServerVersionClient
{
    /// <summary>
    /// Reads the active environment server's version block from the capability manifest.
    /// </summary>
    Task<OperateSectionResult<ServerVersionInfo>> GetServerVersionAsync(
        CancellationToken cancellationToken = default);
}
