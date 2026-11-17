using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Honua.Console.Contracts;
using Honua.Console.Shell.Models;

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
    private readonly IConsoleEnvironmentProfileStore _profiles;
    private readonly IConsoleAccountSessionStore _sessions;
    private readonly string? _adminApiKey;
    private readonly IConsoleOperatorBearerProvider _operatorBearerProvider;
    private readonly ConsoleServerCredentialMode _credentialMode;

    public HttpConsoleExportsClient(
        HttpClient http,
        IConsoleEnvironmentProfileStore profiles,
        IConsoleAccountSessionStore sessions,
        IConsoleOperatorBearerProvider operatorBearerProvider,
        string? adminApiKey = null,
        ConsoleServerCredentialMode credentialMode = ConsoleServerCredentialMode.Interactive)
    {
        _http = http ?? throw new ArgumentNullException(nameof(http));
        _profiles = profiles ?? throw new ArgumentNullException(nameof(profiles));
        _sessions = sessions ?? throw new ArgumentNullException(nameof(sessions));
        _operatorBearerProvider = operatorBearerProvider ?? throw new ArgumentNullException(nameof(operatorBearerProvider));
        _adminApiKey = string.IsNullOrWhiteSpace(adminApiKey) ? null : adminApiKey;
        _credentialMode = credentialMode;
    }

    public Task<OperateSectionResult<ExportListResponse>> ListAsync(CancellationToken cancellationToken = default) =>
        ReadAsync(ExportAdminRoutes.Exports, ExportAdminJsonContext.Default.ExportListEnvelope, notFoundIsUnsupported: true, cancellationToken);

    public Task<OperateSectionResult<ExportResponse>> GetAsync(string exportId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(exportId);
        return ReadAsync(ExportAdminRoutes.Export(exportId), ExportAdminJsonContext.Default.ExportEnvelope, notFoundIsUnsupported: false, cancellationToken);
    }

    public Task<OperateSectionResult<ExportResponse>> CreateAsync(ExportCreateRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return WriteAsync(
            HttpMethod.Post,
            ExportAdminRoutes.Exports,
            JsonSerializer.Serialize(request, ExportAdminJsonContext.Default.ExportCreateRequest),
            ExportAdminJsonContext.Default.ExportEnvelope,
            cancellationToken);
    }

    public Task<OperateSectionResult<ExportResponse>> UnpublishAsync(string exportId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(exportId);
        return WriteAsync(HttpMethod.Delete, ExportAdminRoutes.Export(exportId), null, ExportAdminJsonContext.Default.ExportEnvelope, cancellationToken);
    }

    public Task<OperateSectionResult<ObjectStorageSetupResponse>> GetObjectStorageSetupAsync(
        string bucket, string prefix, CancellationToken cancellationToken = default) =>
        ReadAsync(
            ExportAdminRoutes.ObjectStorageSetupFor(bucket ?? string.Empty, prefix ?? string.Empty),
            ExportAdminJsonContext.Default.ObjectStorageSetupEnvelope,
            notFoundIsUnsupported: true,
            cancellationToken);

    public Task<OperateSectionResult<ObjectStorageTestResponse>> TestObjectStorageAsync(
        ObjectStorageConnectionRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return WriteAsync(
            HttpMethod.Post,
            ExportAdminRoutes.ConnectionDraftTest,
            JsonSerializer.Serialize(request, ExportAdminJsonContext.Default.ObjectStorageConnectionRequest),
            ExportAdminJsonContext.Default.ObjectStorageTestEnvelope,
            cancellationToken);
    }

    public Task<OperateSectionResult<ObjectStorageConnectionResponse>> CreateObjectStorageAsync(
        ObjectStorageConnectionRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return WriteAsync(
            HttpMethod.Post,
            ExportAdminRoutes.Connections,
            JsonSerializer.Serialize(request, ExportAdminJsonContext.Default.ObjectStorageConnectionRequest),
            ExportAdminJsonContext.Default.ObjectStorageConnectionEnvelope,
            cancellationToken);
    }

    public Task<OperateSectionResult<ObjectStorageBrowseResponse>> BrowseAsync(
        Guid connectionId, string prefix, CancellationToken cancellationToken = default) =>
        ReadAsync(
            ExportAdminRoutes.ConnectionObjects(connectionId, prefix ?? string.Empty),
            ExportAdminJsonContext.Default.ObjectStorageBrowseEnvelope,
            notFoundIsUnsupported: false,
            cancellationToken);

    private async Task<OperateSectionResult<T>> ReadAsync<T>(
        string route,
        JsonTypeInfo<ConsoleApiEnvelope<T>> typeInfo,
        bool notFoundIsUnsupported,
        CancellationToken cancellationToken)
        where T : class
    {
        var profile = await _profiles.GetActiveProfileAsync(cancellationToken).ConfigureAwait(false);
        if (profile is null)
        {
            return OperateSectionResult<T>.Denied(OperateSectionStatus.Unavailable, NoProfileMessage);
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, ConsoleServerHttp.BuildUri(profile.ServerBaseUri, route));
        await ConsoleServerHttp.AttachAuthenticationAsync(request, _sessions, profile, _adminApiKey, cancellationToken)
            .ConfigureAwait(false);
        return await SendAsync(request, typeInfo, notFoundIsUnsupported, cancellationToken).ConfigureAwait(false);
    }

    private async Task<OperateSectionResult<T>> WriteAsync<T>(
        HttpMethod method,
        string route,
        string? body,
        JsonTypeInfo<ConsoleApiEnvelope<T>> typeInfo,
        CancellationToken cancellationToken)
        where T : class
    {
        var profile = await _profiles.GetActiveProfileAsync(cancellationToken).ConfigureAwait(false);
        if (profile is null)
        {
            return OperateSectionResult<T>.Denied(OperateSectionStatus.Unavailable, NoProfileMessage);
        }

        using var request = new HttpRequestMessage(method, ConsoleServerHttp.BuildUri(profile.ServerBaseUri, route));
        if (body is not null)
        {
            request.Content = new StringContent(body, Encoding.UTF8, "application/json");
        }

        var authentication = await ConsoleServerHttp.AttachMutationAuthenticationAsync(
                request, _operatorBearerProvider, profile, _adminApiKey, _credentialMode, cancellationToken)
            .ConfigureAwait(false);
        if (!authentication.IsAuthenticated)
        {
            return OperateSectionResult<T>.Denied(OperateSectionStatus.Forbidden, authentication.Message);
        }

        return await SendAsync(request, typeInfo, notFoundIsUnsupported: false, cancellationToken).ConfigureAwait(false);
    }

    private async Task<OperateSectionResult<T>> SendAsync<T>(
        HttpRequestMessage request,
        JsonTypeInfo<ConsoleApiEnvelope<T>> typeInfo,
        bool notFoundIsUnsupported,
        CancellationToken cancellationToken)
        where T : class
    {
        try
        {
            using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return await DeniedAsync<T>(response, notFoundIsUnsupported, cancellationToken).ConfigureAwait(false);
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            var envelope = await JsonSerializer.DeserializeAsync(stream, typeInfo, cancellationToken).ConfigureAwait(false);
            return envelope is { Success: true, Data: { } data }
                ? OperateSectionResult<T>.Allowed(data, message: envelope.Message ?? string.Empty)
                : OperateSectionResult<T>.Denied(
                    OperateSectionStatus.Unavailable,
                    string.IsNullOrWhiteSpace(envelope?.Message)
                        ? "The honua-server admin API reported the request as unsuccessful."
                        : envelope!.Message!);
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
        bool notFoundIsUnsupported,
        CancellationToken cancellationToken)
    {
        var status = MapStatus(response.StatusCode, notFoundIsUnsupported);
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

    private static OperateSectionStatus MapStatus(HttpStatusCode code, bool notFoundIsUnsupported) => code switch
    {
        HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => OperateSectionStatus.Forbidden,
        HttpStatusCode.NotFound => notFoundIsUnsupported ? OperateSectionStatus.Unsupported : OperateSectionStatus.Missing,
        HttpStatusCode.NotImplemented or HttpStatusCode.MethodNotAllowed => OperateSectionStatus.Unsupported,
        HttpStatusCode.BadRequest or HttpStatusCode.UnprocessableEntity => OperateSectionStatus.Rejected,
        HttpStatusCode.Conflict or HttpStatusCode.PreconditionFailed => OperateSectionStatus.Conflict,
        _ => OperateSectionStatus.Unavailable
    };
}
