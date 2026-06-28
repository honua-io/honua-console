using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Honua.Console.Shell.Models;

namespace Honua.Console.Shell.Services;

/// <summary>
/// Binds the SensorThings (STA v1.1) browse surface to a real honua-server
/// through the conformance-shaped <c>/sta/v1.1</c> read API. Base address and
/// bearer token come from the active <see cref="ConsoleEnvironmentProfile"/> and
/// its account session, reusing the connection pattern from the Operate
/// observability client (BaseAddress = profile.ServerBaseUri, Authorization =
/// Bearer &lt;token&gt;, X-API-Key fallback). A single shared
/// <see cref="HttpClient"/> is reused; each request builds an absolute URI and
/// attaches its own auth header. Deserialization is source-generated for
/// trim/AOT safety.
/// </summary>
public sealed class HttpConsoleSensorThingsClient : IConsoleSensorThingsClient
{
    private const string BasePath = "sta/v1.1";

    private readonly HttpClient _http;
    private readonly IConsoleEnvironmentProfileStore _profileStore;
    private readonly IConsoleAccountSessionStore _sessionStore;
    private readonly string? _adminApiKey;

    public HttpConsoleSensorThingsClient(
        HttpClient http,
        IConsoleEnvironmentProfileStore profileStore,
        IConsoleAccountSessionStore sessionStore,
        string? adminApiKey = null)
    {
        _http = http ?? throw new ArgumentNullException(nameof(http));
        _profileStore = profileStore ?? throw new ArgumentNullException(nameof(profileStore));
        _sessionStore = sessionStore ?? throw new ArgumentNullException(nameof(sessionStore));
        _adminApiKey = adminApiKey;
    }

    public Task<SensorThingsReadResult<StaEntitySet<StaThing>>> ListThingsAsync(
        int top = 50,
        int skip = 0,
        CancellationToken cancellationToken = default) =>
        FetchAsync(
            $"{BasePath}/Things?$top={Clamp(top)}&$skip={Math.Max(0, skip)}&$count=true",
            SensorThingsJsonContext.Default.StaEntitySetStaThing,
            cancellationToken);

    public Task<SensorThingsReadResult<StaEntitySet<StaDatastream>>> ListDatastreamsAsync(
        int top = 50,
        int skip = 0,
        CancellationToken cancellationToken = default) =>
        FetchAsync(
            $"{BasePath}/Datastreams?$top={Clamp(top)}&$skip={Math.Max(0, skip)}&$count=true",
            SensorThingsJsonContext.Default.StaEntitySetStaDatastream,
            cancellationToken);

    public Task<SensorThingsReadResult<StaDatastream>> GetDatastreamAsync(
        long datastreamId,
        bool expandObservations = false,
        int observationTop = 200,
        CancellationToken cancellationToken = default)
    {
        var path = $"{BasePath}/Datastreams({datastreamId.ToString(CultureInfo.InvariantCulture)})";
        if (expandObservations)
        {
            path += $"?$expand=Observations($orderby=phenomenonTime asc;$top={Clamp(observationTop)})";
        }

        return FetchAsync(path, SensorThingsJsonContext.Default.StaDatastream, cancellationToken);
    }

    public Task<SensorThingsReadResult<StaEntitySet<StaObservation>>> ListDatastreamObservationsAsync(
        long datastreamId,
        int top = 200,
        int skip = 0,
        CancellationToken cancellationToken = default) =>
        FetchAsync(
            $"{BasePath}/Datastreams({datastreamId.ToString(CultureInfo.InvariantCulture)})/Observations"
            + $"?$orderby=phenomenonTime asc&$top={Clamp(top)}&$skip={Math.Max(0, skip)}&$count=true",
            SensorThingsJsonContext.Default.StaEntitySetStaObservation,
            cancellationToken);

    public async Task<SensorThingsReadResult<SensorThingsTimeSeries>> GetDatastreamTimeSeriesAsync(
        long datastreamId,
        int top = 500,
        CancellationToken cancellationToken = default)
    {
        var datastreamFetch = await GetDatastreamAsync(datastreamId, expandObservations: false, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        var observationsFetch = await ListDatastreamObservationsAsync(datastreamId, top, 0, cancellationToken)
            .ConfigureAwait(false);

        if (!observationsFetch.IsOk || observationsFetch.Value is null)
        {
            return SensorThingsReadResult<SensorThingsTimeSeries>.Denied(
                observationsFetch.Status, observationsFetch.Message);
        }

        var points = new List<SensorThingsTimeSeriesPoint>(observationsFetch.Value.Value.Count);
        foreach (var observation in observationsFetch.Value.Value)
        {
            // Skip observations whose result is non-numeric or null (an open-typed STA result, e.g.
            // a category/truth reading or a gap) — they have no numeric value to chart but must not
            // break the series, which now tolerates them instead of failing the whole collection.
            if (observation.NumericResult is { } numericResult
                && DateTimeOffset.TryParse(
                    observation.PhenomenonTime,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                    out var time))
            {
                points.Add(new SensorThingsTimeSeriesPoint(time, numericResult));
            }
        }

        points.Sort((left, right) => left.PhenomenonTime.CompareTo(right.PhenomenonTime));

        var datastream = datastreamFetch.IsOk ? datastreamFetch.Value : null;
        var series = new SensorThingsTimeSeries(
            DatastreamId: datastreamId,
            DatastreamName: datastream?.Name
                ?? $"Datastream {datastreamId.ToString(CultureInfo.InvariantCulture)}",
            UnitSymbol: datastream?.UnitOfMeasurement?.Symbol ?? string.Empty,
            Points: points);

        return SensorThingsReadResult<SensorThingsTimeSeries>.Ok(series);
    }

    private static int Clamp(int top) => Math.Clamp(top, 1, 1000);

    private async Task<SensorThingsReadResult<T>> FetchAsync<T>(
        string relativePath,
        JsonTypeInfo<T> typeInfo,
        CancellationToken cancellationToken)
        where T : class
    {
        var profile = await _profileStore.GetActiveProfileAsync(cancellationToken).ConfigureAwait(false);
        if (profile is null)
        {
            return SensorThingsReadResult<T>.Denied(
                SensorThingsReadStatus.Unavailable,
                SensorThingsPresentation.MissingBindingMessage);
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, BuildUri(profile.ServerBaseUri, relativePath));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        var token = await ResolveTokenAsync(profile, cancellationToken).ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(token))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }
        else if (!string.IsNullOrWhiteSpace(_adminApiKey))
        {
            request.Headers.TryAddWithoutValidation("X-API-Key", _adminApiKey);
        }

        try
        {
            using var response = await _http
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                return SensorThingsReadResult<T>.Denied(
                    MapStatus(response.StatusCode),
                    $"The honua-server SensorThings API returned {(int)response.StatusCode} {response.ReasonPhrase}.");
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            var value = await JsonSerializer.DeserializeAsync(stream, typeInfo, cancellationToken).ConfigureAwait(false);
            return value is null
                ? SensorThingsReadResult<T>.Denied(
                    SensorThingsReadStatus.Unavailable,
                    "The honua-server SensorThings API returned an empty response.")
                : SensorThingsReadResult<T>.Ok(value);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or JsonException)
        {
            return SensorThingsReadResult<T>.Denied(
                SensorThingsReadStatus.Unavailable,
                "The honua-server SensorThings API is unreachable or returned an unreadable response.");
        }
    }

    private Task<string?> ResolveTokenAsync(ConsoleEnvironmentProfile profile, CancellationToken cancellationToken) =>
        ConsoleServerHttp.ResolveForwardableBearerAsync(_sessionStore, profile, cancellationToken);

    private static Uri BuildUri(Uri baseUri, string relativePath) =>
        ConsoleServerHttp.BuildUri(baseUri, relativePath);

    private static SensorThingsReadStatus MapStatus(HttpStatusCode code) => code switch
    {
        HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => SensorThingsReadStatus.Forbidden,
        HttpStatusCode.NotFound => SensorThingsReadStatus.Missing,
        HttpStatusCode.NotImplemented => SensorThingsReadStatus.Unsupported,
        _ => SensorThingsReadStatus.Unavailable
    };
}
