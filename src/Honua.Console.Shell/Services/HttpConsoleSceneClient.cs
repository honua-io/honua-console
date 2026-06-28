using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Honua.Console.Shell.Models;

namespace Honua.Console.Shell.Services;

/// <summary>
/// Binds the console's 3D scene surface to a real honua-server: scene discovery
/// (<c>GET /api/scenes</c>), LAS point-cloud ingest
/// (<c>POST /api/v1/admin/scenes/ingest/pointcloud</c>), and tileset URL
/// resolution (<c>/scenes/{id}/tileset.json</c>). Base address and bearer token
/// come from the active <see cref="ConsoleEnvironmentProfile"/> and its account
/// session, reusing the Operate observability client's connection pattern (Bearer
/// token, X-API-Key fallback for admin endpoints). Discovery deserialization is
/// source-generated for trim/AOT safety.
/// </summary>
public sealed class HttpConsoleSceneClient : IConsoleSceneClient
{
    private const string DiscoveryPath = "api/scenes";
    private const string IngestPath = "api/v1/admin/scenes/ingest/pointcloud";

    private readonly HttpClient _http;
    private readonly IConsoleEnvironmentProfileStore _profileStore;
    private readonly IConsoleAccountSessionStore _sessionStore;
    private readonly string? _adminApiKey;

    public HttpConsoleSceneClient(
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

    public async Task<SceneReadResult<SceneListResponse>> ListScenesAsync(
        CancellationToken cancellationToken = default)
    {
        var profile = await _profileStore.GetActiveProfileAsync(cancellationToken).ConfigureAwait(false);
        if (profile is null)
        {
            return SceneReadResult<SceneListResponse>.Denied(
                SceneReadStatus.Unavailable, ScenePresentation.MissingBindingMessage);
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, BuildUri(profile.ServerBaseUri, DiscoveryPath));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        await ApplyAuthAsync(request, profile, cancellationToken).ConfigureAwait(false);

        try
        {
            using var response = await _http
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                return SceneReadResult<SceneListResponse>.Denied(
                    MapStatus(response.StatusCode),
                    $"The honua-server scene API returned {(int)response.StatusCode} {response.ReasonPhrase}.");
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            var value = await JsonSerializer
                .DeserializeAsync(stream, SceneJsonContext.Default.SceneListResponse, cancellationToken)
                .ConfigureAwait(false);
            return value is null
                ? SceneReadResult<SceneListResponse>.Denied(
                    SceneReadStatus.Unavailable, "The honua-server scene API returned an empty response.")
                : SceneReadResult<SceneListResponse>.Ok(value);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or JsonException)
        {
            return SceneReadResult<SceneListResponse>.Denied(
                SceneReadStatus.Unavailable,
                "The honua-server scene API is unreachable or returned an unreadable response.");
        }
    }

    public async Task<SceneReadResult<PointCloudIngestResult>> IngestPointCloudAsync(
        PointCloudIngestRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var profile = await _profileStore.GetActiveProfileAsync(cancellationToken).ConfigureAwait(false);
        if (profile is null)
        {
            return SceneReadResult<PointCloudIngestResult>.Denied(
                SceneReadStatus.Unavailable, ScenePresentation.MissingBindingMessage);
        }

        if (request.Content.Length == 0)
        {
            return SceneReadResult<PointCloudIngestResult>.Denied(
                SceneReadStatus.InvalidInput, "Select a non-empty LAS point cloud to ingest.");
        }

        using var content = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(request.Content);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        content.Add(fileContent, "file", request.FileName);
        if (!string.IsNullOrWhiteSpace(request.DisplayName))
        {
            content.Add(new StringContent(request.DisplayName), "displayName");
        }
        if (!string.IsNullOrWhiteSpace(request.Description))
        {
            content.Add(new StringContent(request.Description), "description");
        }
        if (request.SourceCrs is { } crs)
        {
            content.Add(new StringContent(crs.ToString(CultureInfo.InvariantCulture)), "sourceCrs");
        }

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, BuildUri(profile.ServerBaseUri, IngestPath))
        {
            Content = content
        };
        httpRequest.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        await ApplyAuthAsync(httpRequest, profile, cancellationToken).ConfigureAwait(false);

        try
        {
            using var response = await _http
                .SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                return SceneReadResult<PointCloudIngestResult>.Denied(
                    MapStatus(response.StatusCode),
                    $"The point-cloud ingest endpoint returned {(int)response.StatusCode} {response.ReasonPhrase}.");
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            var value = await JsonSerializer
                .DeserializeAsync(stream, SceneJsonContext.Default.PointCloudIngestResult, cancellationToken)
                .ConfigureAwait(false);
            return value is null
                ? SceneReadResult<PointCloudIngestResult>.Denied(
                    SceneReadStatus.Unavailable, "The point-cloud ingest endpoint returned an empty response.")
                : SceneReadResult<PointCloudIngestResult>.Ok(value);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or JsonException)
        {
            return SceneReadResult<PointCloudIngestResult>.Denied(
                SceneReadStatus.Unavailable,
                "The point-cloud ingest endpoint is unreachable or returned an unreadable response.");
        }
    }

    public async Task<string?> ResolveTilesetUrlAsync(
        string sceneId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sceneId))
        {
            return null;
        }

        var profile = await _profileStore.GetActiveProfileAsync(cancellationToken).ConfigureAwait(false);
        if (profile is null)
        {
            return null;
        }

        return BuildUri(profile.ServerBaseUri, $"scenes/{Uri.EscapeDataString(sceneId)}/tileset.json").AbsoluteUri;
    }

    private async Task ApplyAuthAsync(
        HttpRequestMessage request,
        ConsoleEnvironmentProfile profile,
        CancellationToken cancellationToken)
    {
        var token = await ResolveTokenAsync(profile, cancellationToken).ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(token))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }
        else if (!string.IsNullOrWhiteSpace(_adminApiKey))
        {
            request.Headers.TryAddWithoutValidation("X-API-Key", _adminApiKey);
        }
    }

    private Task<string?> ResolveTokenAsync(ConsoleEnvironmentProfile profile, CancellationToken cancellationToken) =>
        ConsoleServerHttp.ResolveForwardableBearerAsync(_sessionStore, profile, cancellationToken);

    private static Uri BuildUri(Uri baseUri, string relativePath) =>
        ConsoleServerHttp.BuildUri(baseUri, relativePath);

    private static SceneReadStatus MapStatus(HttpStatusCode code) => code switch
    {
        HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => SceneReadStatus.Forbidden,
        HttpStatusCode.PaymentRequired => SceneReadStatus.PaymentRequired,
        HttpStatusCode.Conflict => SceneReadStatus.Conflict,
        HttpStatusCode.BadRequest => SceneReadStatus.InvalidInput,
        HttpStatusCode.NotFound => SceneReadStatus.Missing,
        HttpStatusCode.NotImplemented => SceneReadStatus.Unsupported,
        _ => SceneReadStatus.Unavailable
    };
}
