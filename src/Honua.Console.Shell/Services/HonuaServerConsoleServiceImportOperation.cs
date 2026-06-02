using Honua.Console.Contracts;
using Honua.Console.Shell.Models;

namespace Honua.Console.Shell.Services;

/// <summary>
/// Live implementation of the service-import operation. Calls honua-server's external-service discovery
/// through <see cref="IHonuaAdminOperateClient"/> and maps the result (or rejection) into a
/// <see cref="ConsoleServiceImportResult"/>, including the per-service catalog hierarchy. It never fabricates
/// discovery results.
/// </summary>
public sealed class HonuaServerConsoleServiceImportOperation : IConsoleServiceImportOperation
{
    private readonly IHonuaAdminOperateClient _client;

    public HonuaServerConsoleServiceImportOperation(IHonuaAdminOperateClient client)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
    }

    public async Task<ConsoleServiceImportResult> DiscoverAsync(
        string serviceUrl,
        ConsoleServiceImportAuth? auth = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serviceUrl);

        var credentials = MapCredentials(auth);
        var result = await _client.DiscoverExternalServiceAsync(serviceUrl, credentials, cancellationToken)
            .ConfigureAwait(false);

        if (result.Data is { } discovery && !string.IsNullOrWhiteSpace(discovery.ServiceType))
        {
            var services = MapServices(discovery);
            var layers = services.SelectMany(service => service.Layers).ToArray();
            var detail = discovery.IsCatalog
                ? $"Discovered a catalog with {services.Count} importable service(s) and {layers.Length} layer(s)."
                : $"Discovered a {discovery.ServiceType} ('{discovery.ServiceName}') with {layers.Length} importable layer(s).";

            return new ConsoleServiceImportResult
            {
                Succeeded = true,
                State = "Discovered",
                Detail = detail,
                ServiceType = discovery.ServiceType,
                ServiceName = discovery.ServiceName,
                Srid = discovery.Srid,
                IsCatalog = discovery.IsCatalog,
                Services = services,
                Layers = layers,
            };
        }

        var issue = result.Issue;
        return new ConsoleServiceImportResult
        {
            Succeeded = false,
            State = issue?.State ?? "Unavailable",
            Detail = issue?.Detail ?? "The Honua server could not discover the service.",
        };
    }

    private static HonuaAdminExternalServiceCredentials? MapCredentials(ConsoleServiceImportAuth? auth)
    {
        if (auth is null || !auth.RequiresCredentials)
        {
            return null;
        }

        return new HonuaAdminExternalServiceCredentials
        {
            Mode = auth.Mode,
            Username = auth.Username,
            Password = auth.Password,
            Token = auth.Token,
            TokenUrl = auth.TokenUrl,
            ClientId = auth.ClientId,
            ClientSecret = auth.ClientSecret,
            Referer = auth.Referer,
        };
    }

    private static IReadOnlyList<ConsoleServiceImportService> MapServices(HonuaAdminExternalServiceDiscovery discovery)
    {
        // Prefer the per-service hierarchy; fall back to a single synthetic service from the flat candidate list
        // for older servers that don't return the Services array.
        if (discovery.Services.Count > 0)
        {
            return discovery.Services
                .Select(service => new ConsoleServiceImportService
                {
                    ServiceName = string.IsNullOrWhiteSpace(service.ServiceName)
                        ? (service.ServiceUrl ?? "service")
                        : service.ServiceName!,
                    ServiceType = service.ServiceType,
                    FolderPath = service.FolderPath,
                    ServiceUrl = service.ServiceUrl,
                    Srid = service.Srid,
                    Layers = MapLayers(service.Candidates),
                })
                .ToArray();
        }

        return
        [
            new ConsoleServiceImportService
            {
                ServiceName = discovery.ServiceName ?? "service",
                ServiceType = discovery.ServiceType,
                ServiceUrl = discovery.NormalizedUrl,
                Srid = discovery.Srid,
                Layers = MapLayers(discovery.Candidates),
            }
        ];
    }

    private static IReadOnlyList<ConsoleServiceImportLayer> MapLayers(
        IReadOnlyList<HonuaAdminExternalLayerCandidate> candidates) =>
        candidates
            .Where(candidate => !string.IsNullOrWhiteSpace(candidate.Name))
            .Select(candidate => new ConsoleServiceImportLayer
            {
                LayerId = candidate.LayerId,
                Name = candidate.Name!,
                GeometryType = candidate.GeometryType,
                FeatureCount = candidate.FeatureCount,
                ServiceUrl = candidate.ServiceUrl,
            })
            .ToArray();
}
