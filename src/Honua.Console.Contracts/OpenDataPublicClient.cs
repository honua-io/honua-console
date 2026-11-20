using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Honua.Console.Contracts;

// Reads the server's anonymous open-data routes. These answer for anyone, and the server decides
// eligibility for every caller alike, so this client sends no API key: what Console shows on a
// public page is exactly what an anonymous reader is allowed to open.
public interface IHonuaOpenDataClient
{
    Task<HonuaAdminEndpointResult<HonuaOpenDataDatasetList>> ListDatasetsAsync(
        string? cursor = null,
        int limit = 50,
        CancellationToken cancellationToken = default);

    Task<HonuaAdminEndpointResult<HonuaOpenDataPage>> GetDatasetAsync(
        string id,
        CancellationToken cancellationToken = default);
}

public sealed class HonuaOpenDataHttpClient : IHonuaOpenDataClient
{
    private const string DatasetsBase = "/api/v1/open-data/datasets";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private static readonly AdminBodyFormat EnvelopeFormat = new(
        JsonOptions,
        AdminBodyRead.Streamed,
        "The Honua server open-data response did not match the expected contract");

    private readonly AdminEndpointTransport _transport;

    public HonuaOpenDataHttpClient(HttpClient httpClient, Uri baseUri)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(baseUri);

        httpClient.BaseAddress ??= baseUri;
        _transport = new AdminEndpointTransport(
            httpClient,
            apiKey: null,
            AdminTransportFailure.Unreachable("The Honua server open-data endpoint"));
    }

    public Task<HonuaAdminEndpointResult<HonuaOpenDataDatasetList>> ListDatasetsAsync(
        string? cursor = null,
        int limit = 50,
        CancellationToken cancellationToken = default)
    {
        var query = $"?limit={limit.ToString(CultureInfo.InvariantCulture)}";
        if (!string.IsNullOrWhiteSpace(cursor))
        {
            query += $"&cursor={Uri.EscapeDataString(cursor)}";
        }

        return GetAsync<HonuaOpenDataDatasetList>(DatasetsBase + query, "GET /api/v1/open-data/datasets", cancellationToken);
    }

    public Task<HonuaAdminEndpointResult<HonuaOpenDataPage>> GetDatasetAsync(
        string id,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);

        return GetAsync<HonuaOpenDataPage>(
            $"{DatasetsBase}/{Uri.EscapeDataString(id)}",
            "GET /api/v1/open-data/datasets/{id}",
            cancellationToken);
    }

    private Task<HonuaAdminEndpointResult<T>> GetAsync<T>(
        string path,
        string contract,
        CancellationToken cancellationToken) =>
        _transport.SendAsync(
            new AdminEndpointCall<T>(
                () => new HttpRequestMessage(HttpMethod.Get, path),
                contract,
                new(
                    AdminFailureMappings.StatusDetail,
                    AdminResponseBodies.Envelope<T>(EnvelopeFormat, "The Honua server open-data response did not include data.")))
            {
                Completion = HttpCompletionOption.ResponseHeadersRead
            },
            cancellationToken);
}

public sealed record HonuaOpenDataDatasetList
{
    public IReadOnlyList<HonuaOpenDataDatasetEntry> Items { get; init; } = [];

    public string? NextCursor { get; init; }
}

public sealed record HonuaOpenDataDatasetEntry
{
    public string ItemType { get; init; } = string.Empty;

    public HonuaOpenDataPage Page { get; init; } = new();
}

public sealed record HonuaOpenDataPage
{
    public string ItemId { get; init; } = string.Empty;

    public string? Title { get; init; }

    public string? Description { get; init; }

    public string? PublisherName { get; init; }

    public string? License { get; init; }

    public IReadOnlyList<string> Tags { get; init; } = [];

    public IReadOnlyList<HonuaOpenDataDistribution> Distributions { get; init; } = [];

    public DateTimeOffset? UpdatedAt { get; init; }
}

public sealed record HonuaOpenDataDistribution
{
    public string? Title { get; init; }

    public string AccessUrl { get; init; } = string.Empty;

    public string? MediaType { get; init; }

    public string? Format { get; init; }
}
