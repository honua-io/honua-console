using Honua.Console.Shell.Models;

namespace Honua.Console.Shell.Services;

/// <summary>
/// The console's service-configuration OPERATIONS (Wave 5, plan §3 Family A + §6 Wave 5). Performs REAL
/// mutations against honua-server's admin surfaces:
/// <list type="bullet">
/// <item>enable/disable a published layer (<c>PUT /api/v1/admin/connections/{id}/layers/{layerId}/enabled</c>),</item>
/// <item>change a service's enabled protocols (<c>PUT /api/v1/admin/services/{serviceName}/protocols</c>),</item>
/// <item>change a service's access policy (<c>PUT /api/v1/admin/services/{serviceName}/access-policy</c>).</item>
/// </list>
/// Each returns the post-change server state (read back from the server's own re-read), or an explicit
/// failure. The live implementation is DI-gated on a configured server base URL; when no server is
/// configured the surface binds to <see cref="UnsupportedServiceConfigurationOperation"/>, which returns a
/// missing-binding result and performs no network call (Console Patterns Charter section 11 — never
/// fabricate a configuration change).
/// </summary>
public interface IServiceConfigurationOperation
{
    /// <summary>Reads a service's current enabled/available protocols and access policy from the server.</summary>
    Task<ServiceSettingsView> GetSettingsAsync(
        string serviceName,
        CancellationToken cancellationToken = default);

    Task<ServiceConfigurationResult> SetLayerEnabledAsync(
        ServiceLayerEnableCommand command,
        CancellationToken cancellationToken = default);

    Task<ServiceConfigurationResult> UpdateProtocolsAsync(
        ServiceProtocolsCommand command,
        CancellationToken cancellationToken = default);

    Task<ServiceConfigurationResult> UpdateAccessPolicyAsync(
        ServiceAccessPolicyCommand command,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates a service's MapServer render settings (<c>PUT /api/v1/admin/services/{serviceName}/mapserver</c>).
    /// The server PUT may answer 501 Not Implemented on a build that has not landed the write path (a known V2
    /// gap); the result then carries the "Unsupported" state and an honest detail rather than a fabricated
    /// success (Console Patterns Charter section 11).
    /// </summary>
    Task<ServiceConfigurationResult> UpdateMapServerSettingsAsync(
        ServiceMapServerSettingsCommand command,
        CancellationToken cancellationToken = default);
}
