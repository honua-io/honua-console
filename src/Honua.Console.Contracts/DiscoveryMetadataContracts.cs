using System.Globalization;

namespace Honua.Console.Contracts;

public enum DiscoveryMetadataTargetKind { Resource, Service }

public sealed record DiscoveryMetadataAddress
{
    private DiscoveryMetadataAddress(string path, DiscoveryMetadataTargetKind kind)
    {
        Path = path;
        Kind = kind;
    }

    internal string Path { get; }
    public DiscoveryMetadataTargetKind Kind { get; }

    public static DiscoveryMetadataAddress ForLayer(int layerId)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(layerId);
        return new($"/api/v1/admin/metadata/layers/{layerId.ToString(CultureInfo.InvariantCulture)}/discovery",
            DiscoveryMetadataTargetKind.Resource);
    }

    public static DiscoveryMetadataAddress ForService(string serviceName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serviceName);
        return new($"/api/v1/admin/services/{Uri.EscapeDataString(serviceName)}/discovery", DiscoveryMetadataTargetKind.Service);
    }
}

public sealed record DiscoveryMetadataUpdateRequest
{
    public required string Etag { get; init; }
    public string? Title { get; init; }
    public string? Description { get; init; }
    public string[] Keywords { get; init; } = [];
}

public sealed record DiscoveryMetadataResponse
{
    public required string Etag { get; init; }
    public required string EntityId { get; init; }
    public string? Title { get; init; }
    public string? Description { get; init; }
    public IReadOnlyList<string> Keywords { get; init; } = [];
    public IReadOnlyList<DiscoveryPublicationImpact> AffectedPublications { get; init; } = [];
}

public sealed record DiscoveryPublicationImpact(string PublicationId, string ServiceName, string Identifier);

public interface IHonuaDiscoveryMetadataClient
{
    Task<HonuaAdminEndpointResult<DiscoveryMetadataResponse>> ReadDiscoveryAsync(
        DiscoveryMetadataAddress address, CancellationToken cancellationToken = default);
    Task<HonuaAdminEndpointResult<DiscoveryMetadataResponse>> UpdateDiscoveryAsync(
        DiscoveryMetadataAddress address, DiscoveryMetadataUpdateRequest request, CancellationToken cancellationToken = default);
}

public sealed partial class HonuaAdminOperateHttpClient : IHonuaDiscoveryMetadataClient
{
    public Task<HonuaAdminEndpointResult<DiscoveryMetadataResponse>> ReadDiscoveryAsync(
        DiscoveryMetadataAddress address, CancellationToken cancellationToken = default) =>
        GetEnvelopeAsync<DiscoveryMetadataResponse>(address.Path, $"GET {address.Path}", cancellationToken);

    public Task<HonuaAdminEndpointResult<DiscoveryMetadataResponse>> UpdateDiscoveryAsync(
        DiscoveryMetadataAddress address, DiscoveryMetadataUpdateRequest request, CancellationToken cancellationToken = default) =>
        SendEnvelopeAsync<DiscoveryMetadataResponse>(() => JsonRequest(HttpMethod.Put, address.Path, request),
            $"PUT {address.Path}", cancellationToken);
}
