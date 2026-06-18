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

    /// <summary>
    /// Enables the chosen <c>ServiceProtocols</c> on a service slot through honua-server's
    /// <c>PUT /api/v1/admin/services/{serviceName}/protocols</c> endpoint, returning the canonical set of
    /// protocols the service actually exposes after the change. The publish flow calls this once after the
    /// layer publish so a multi-protocol selection (FeatureServer + MapServer + STAC) is genuinely exposed on
    /// each protocol's preview route — rather than re-posting the same layer publish per protocol and
    /// over-reporting which publications are live (issue: resource-first publish flow review §1).
    /// </summary>
    Task<ServiceProtocolEnableResult> EnableProtocolsAsync(
        string serviceName,
        IReadOnlyList<string> protocols,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists the publishable (PostGIS spatial) tables on a connection so the publish-layer form can offer a
    /// real table picker. Returns an empty list when no server is configured or the connection exposes none.
    /// </summary>
    Task<IReadOnlyList<ServiceLayerPublishTable>> ListTablesAsync(
        string connectionId,
        CancellationToken cancellationToken = default);
}
