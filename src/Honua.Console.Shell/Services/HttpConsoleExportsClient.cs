using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Honua.Console.Contracts;

namespace Honua.Console.Shell.Services;

// A 404 or 501 on a collection read means the server has no Exports yet; writes never fall back to the admin key.
public sealed class HttpConsoleExportsClient : IConsoleExportsClient
{
    private const long MaxErrorBodyBytes = 64 * 1024;
    private const string NoProfileMessage =
        "No active environment profile is selected. Connect an environment to work with Exports.";
    private const string UnsupportedMessage =
        "The connected server does not offer Exports or object-storage Connections yet.";

    private readonly HttpClient _http;
    private readonly ConsoleServerAccess _access;

    public HttpConsoleExportsClient(HttpClient http, ConsoleServerAccess access)
    {
        _http = http ?? throw new ArgumentNullException(nameof(http));
        _access = access ?? throw new ArgumentNullException(nameof(access));
    }

    public Task<OperateSectionResult<ExportListResponse>> ListAsync(CancellationToken cancellationToken = default) =>
        ReadAsync(
            new ServerCall<ExportListResponse>(ExportAdminRoutes.Exports, ExportAdminJsonContext.Default.ExportListEnvelope)
            {
                NotFound = OperateSectionStatus.Unsupported
            },
            cancellationToken);

    public Task<OperateSectionResult<ExportResponse>> GetAsync(string exportId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(exportId);
        return ReadAsync(
            new ServerCall<ExportResponse>(ExportAdminRoutes.Export(exportId), ExportAdminJsonContext.Default.ExportEnvelope),
            cancellationToken);
    }

    public Task<OperateSectionResult<ExportResponse>> CreateAsync(ExportCreateRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return WriteAsync(
            new ServerCall<ExportResponse>(ExportAdminRoutes.Exports, ExportAdminJsonContext.Default.ExportEnvelope)
            {
                Method = HttpMethod.Post,
                Body = JsonSerializer.Serialize(request, ExportAdminJsonContext.Default.ExportCreateRequest)
            },
            cancellationToken);
    }

    public Task<OperateSectionResult<ExportResponse>> UnpublishAsync(string exportId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(exportId);
        return WriteAsync(
            new ServerCall<ExportResponse>(ExportAdminRoutes.Export(exportId), ExportAdminJsonContext.Default.ExportEnvelope)
            {
                Method = HttpMethod.Delete,
                NoContent = () => new ExportResponse { Id = exportId, Status = "unpublished" }
            },
            cancellationToken);
    }

    public Task<OperateSectionResult<ObjectStorageSetupResponse>> GetObjectStorageSetupAsync(
        string bucket, string prefix, CancellationToken cancellationToken = default) =>
        WriteAsync(
            new ServerCall<ObjectStorageSetupResponse>(
                ExportAdminRoutes.ObjectStorageSetupFor(bucket ?? string.Empty, prefix ?? string.Empty),
                ExportAdminJsonContext.Default.ObjectStorageSetupEnvelope)
            {
                Method = HttpMethod.Post,
                NotFound = OperateSectionStatus.Unsupported
            },
            cancellationToken);

    public Task<OperateSectionResult<ObjectStorageTestResponse>> TestObjectStorageAsync(
        ObjectStorageConnectionRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return WriteAsync(
            new ServerCall<ObjectStorageTestResponse>(ExportAdminRoutes.ConnectionDraftTest, ExportAdminJsonContext.Default.ObjectStorageTestEnvelope)
            {
                Method = HttpMethod.Post,
                Body = JsonSerializer.Serialize(request, ExportAdminJsonContext.Default.ObjectStorageConnectionRequest)
            },
            cancellationToken);
    }

    public Task<OperateSectionResult<ObjectStorageConnectionResponse>> CreateObjectStorageAsync(
        ObjectStorageConnectionRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return WriteAsync(
            new ServerCall<ObjectStorageConnectionResponse>(ExportAdminRoutes.Connections, ExportAdminJsonContext.Default.ObjectStorageConnectionEnvelope)
            {
                Method = HttpMethod.Post,
                Body = JsonSerializer.Serialize(request, ExportAdminJsonContext.Default.ObjectStorageConnectionRequest)
            },
            cancellationToken);
    }

    public Task<OperateSectionResult<ObjectStorageBrowseResponse>> BrowseAsync(
        Guid connectionId, string prefix, CancellationToken cancellationToken = default) =>
        ReadAsync(
            new ServerCall<ObjectStorageBrowseResponse>(
                ExportAdminRoutes.ConnectionObjects(connectionId, prefix ?? string.Empty),
                ExportAdminJsonContext.Default.ObjectStorageBrowseEnvelope),
            cancellationToken);

    private async Task<OperateSectionResult<T>> ReadAsync<T>(ServerCall<T> call, CancellationToken cancellationToken)
        where T : class
    {
        var profile = await _access.Profiles.GetActiveProfileAsync(cancellationToken).ConfigureAwait(false);
        if (profile is null)
        {
            return OperateSectionResult<T>.Denied(OperateSectionStatus.Unavailable, NoProfileMessage);
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, ConsoleServerHttp.BuildUri(profile.ServerBaseUri, call.Route));
        await ConsoleServerHttp.AttachAuthenticationAsync(request, _access.Sessions, profile, _access.AdminApiKey, cancellationToken)
            .ConfigureAwait(false);
        return await SendAsync(request, call, cancellationToken).ConfigureAwait(false);
    }

    private async Task<OperateSectionResult<T>> WriteAsync<T>(ServerCall<T> call, CancellationToken cancellationToken)
        where T : class
    {
        var profile = await _access.Profiles.GetActiveProfileAsync(cancellationToken).ConfigureAwait(false);
        if (profile is null)
        {
            return OperateSectionResult<T>.Denied(OperateSectionStatus.Unavailable, NoProfileMessage);
        }

        using var request = new HttpRequestMessage(call.Method, ConsoleServerHttp.BuildUri(profile.ServerBaseUri, call.Route));
        if (call.Body is not null)
        {
            request.Content = new StringContent(call.Body, Encoding.UTF8, "application/json");
        }

        var authentication = await ConsoleServerHttp.AttachMutationAuthenticationAsync(
                request, _access.OperatorBearer, profile, _access.AdminApiKey, _access.CredentialMode, cancellationToken)
            .ConfigureAwait(false);
        if (!authentication.IsAuthenticated)
        {
            return OperateSectionResult<T>.Denied(OperateSectionStatus.Forbidden, authentication.Message);
        }

        return await SendAsync(request, call, cancellationToken).ConfigureAwait(false);
    }

    private async Task<OperateSectionResult<T>> SendAsync<T>(
        HttpRequestMessage request,
        ServerCall<T> call,
        CancellationToken cancellationToken)
        where T : class
    {
        try
        {
            using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return await DeniedAsync<T>(response, call.NotFound, cancellationToken).ConfigureAwait(false);
            }

            return response.StatusCode == HttpStatusCode.NoContent && call.NoContent is { } noContent
                ? OperateSectionResult<T>.Allowed(noContent(), message: string.Empty)
                : await ReadEnvelopeAsync(response, call, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or JsonException)
        {
            return OperateSectionResult<T>.Denied(
                OperateSectionStatus.Unavailable,
                "The honua-server admin API is unreachable or returned an unreadable response.");
        }
    }

    private static async Task<OperateSectionResult<T>> DeniedAsync<T>(
        HttpResponseMessage response,
        OperateSectionStatus notFound,
        CancellationToken cancellationToken)
    {
        var status = MapStatus(response.StatusCode, notFound);
        if (status == OperateSectionStatus.Unsupported)
        {
            return OperateSectionResult<T>.Denied(status, UnsupportedMessage);
        }

        // Buffer once: the admin envelope carries the reason in "message", problem details in "detail".
        await response.Content.LoadIntoBufferAsync(MaxErrorBodyBytes, cancellationToken).ConfigureAwait(false);
        var envelopeMessage = await ReadEnvelopeMessageAsync(response, cancellationToken).ConfigureAwait(false);
        var problem = await ConsoleServerHttp.ReadProblemAsync(
                response, "The connected server refused the request.", cancellationToken)
            .ConfigureAwait(false);
        return OperateSectionResult<T>.Denied(status, envelopeMessage ?? problem.Message, problem.Detail);
    }

    private static async Task<string?> ReadEnvelopeMessageAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            using var document = JsonDocument.Parse(body);
            return document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty("message", out var message)
                && message.ValueKind == JsonValueKind.String
                && !string.IsNullOrWhiteSpace(message.GetString())
                    ? message.GetString()!.ReplaceLineEndings(" ")
                    : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static OperateSectionStatus MapStatus(HttpStatusCode code, OperateSectionStatus notFound) => code switch
    {
        HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => OperateSectionStatus.Forbidden,
        HttpStatusCode.NotFound => notFound,
        HttpStatusCode.NotImplemented or HttpStatusCode.MethodNotAllowed => OperateSectionStatus.Unsupported,
        HttpStatusCode.BadRequest or HttpStatusCode.UnprocessableEntity => OperateSectionStatus.Rejected,
        HttpStatusCode.Conflict or HttpStatusCode.PreconditionFailed => OperateSectionStatus.Conflict,
        _ => OperateSectionStatus.Unavailable
    };

    private static async Task<OperateSectionResult<T>> ReadEnvelopeAsync<T>(
        HttpResponseMessage response,
        ServerCall<T> call,
        CancellationToken cancellationToken)
        where T : class
    {
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        var envelope = await JsonSerializer.DeserializeAsync(stream, call.Response, cancellationToken).ConfigureAwait(false);
        return envelope is { Success: true, Data: { } data }
            ? OperateSectionResult<T>.Allowed(data, message: envelope.Message ?? string.Empty)
            : OperateSectionResult<T>.Denied(
                OperateSectionStatus.Unavailable,
                string.IsNullOrWhiteSpace(envelope?.Message)
                    ? "The honua-server admin API reported the request as unsuccessful."
                    : envelope!.Message!);
    }

    // One admin call: a 404 means Missing unless the route is the probe for the whole surface.
    private sealed record ServerCall<T>(string Route, JsonTypeInfo<ConsoleApiEnvelope<T>> Response)
    {
        public HttpMethod Method { get; init; } = HttpMethod.Get;

        public string? Body { get; init; }

        public OperateSectionStatus NotFound { get; init; } = OperateSectionStatus.Missing;

        // The result of a success with no body (204), for calls whose server reply may be empty.
        public Func<T>? NoContent { get; init; }
    }
}
