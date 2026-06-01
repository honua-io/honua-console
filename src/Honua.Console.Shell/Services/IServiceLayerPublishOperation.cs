using Honua.Console.Shell.Models;

namespace Honua.Console.Shell.Services;

/// <summary>
/// The console's service-layer-publish OPERATION (issue #144). Performs a REAL publish of a PostGIS
/// table as a queryable service layer against honua-server's admin layer-publishing endpoint
/// (<c>POST /api/v1/admin/connections/{id}/layers</c>) and returns the resulting server state, or an
/// explicit failure carrying the rejection reason and any field-addressable validation errors.
///
/// Unlike the publishing-wizard scaffolding (which only captured local intent), this abstraction lands
/// the layer on the server. The live implementation is DI-gated on a configured server base URL; when no
/// server is configured the surface binds to <see cref="UnsupportedServiceLayerPublishOperation"/>, which
/// returns a missing-binding result and performs no network call (Console Patterns Charter section 11 —
/// never fabricate a publish).
/// </summary>
public interface IServiceLayerPublishOperation
{
    Task<ServiceLayerPublishResult> PublishAsync(
        ServiceLayerPublishCommand command,
        CancellationToken cancellationToken = default);
}
