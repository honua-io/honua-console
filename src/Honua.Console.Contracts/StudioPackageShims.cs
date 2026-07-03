using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Honua.Sdk.Studio.Exceptions;
using Honua.Sdk.Studio.Packages;

namespace Honua.Console.Contracts;

// D1 (#265): the honua-server Studio package lifecycle + validation/preview API is now projected by the
// server-owned Honua.Sdk.Studio package. The duplicated wire DTOs that used to live here (the "5th
// capability fork") are gone — the enums, domain DTOs, request bodies, and the ApiResponse<T> envelope
// are consumed from Honua.Sdk.Studio.Packages via the global using aliases in StudioSdkAliases.cs, so the
// existing DataSource/authoring-shell code keeps referencing the same simple names.
//
// What remains here is Console-only and has NO SDK equivalent:
//   * the secret-safe draft LIST projection (StudioPackageDraftSummary / StudioPackageDraftListResponse),
//     served by GET /api/v1/studio/package-drafts, which the SDK client does not expose;
//   * the non-throwing StudioEndpointResult<T> / StudioEndpointIssue result envelope the DataSources depend
//     on (missing-binding is a first-class state, so transport/HTTP failures must surface as a neutral issue
//     rather than an exception); and
//   * HttpStudioPackageLifecycleClient, now a THIN ADAPTER over the SDK's IHonuaStudioPackageClient: it
//     delegates every lifecycle call to Honua.Sdk.Studio.Packages.HonuaStudioPackageClient and translates the
//     SDK's throwing contract (HonuaStudioApiException / HonuaStudioContractException / transport faults) back
//     into the console's result-envelope contract, preserving the Contract strings, State vocabulary,
//     StatusCode, 409-conflict detection, and StudioValidationSummary diagnostics extraction unchanged.

#region Console-only draft list projection

/// <summary>
/// Secret-safe summary of a Studio package draft returned by the list endpoint
/// (<c>GET /api/v1/studio/package-drafts</c>). Carries identity, family, package key, validation status,
/// generation, and timestamps, but never the full package graph (no envelope body/bindings), so
/// enumerating existing packages never leaks credentialed binding details. Console-only: the SDK's
/// <see cref="IHonuaStudioPackageClient"/> does not project a draft list.
/// </summary>
public sealed record StudioPackageDraftSummary
{
    [JsonPropertyName("draftId")] public Guid DraftId { get; init; }
    [JsonPropertyName("itemId")] public Guid ItemId { get; init; }
    [JsonPropertyName("packageKey")] public string PackageKey { get; init; } = string.Empty;
    [JsonPropertyName("workspaceId")] public string? WorkspaceId { get; init; }
    [JsonPropertyName("ownerId")] public string? OwnerId { get; init; }
    [JsonPropertyName("family")] public StudioPackageFamily Family { get; init; }
    [JsonPropertyName("validationStatus")] public StudioPackageValidationStatus ValidationStatus { get; init; }
    [JsonPropertyName("baseVersionId")] public Guid? BaseVersionId { get; init; }
    [JsonPropertyName("generation")] public long Generation { get; init; }
    [JsonPropertyName("createdBy")] public string? CreatedBy { get; init; }
    [JsonPropertyName("updatedBy")] public string? UpdatedBy { get; init; }
    [JsonPropertyName("createdAt")] public DateTimeOffset CreatedAt { get; init; }
    [JsonPropertyName("updatedAt")] public DateTimeOffset UpdatedAt { get; init; }
}

public sealed record StudioPackageDraftListResponse
{
    [JsonPropertyName("drafts")] public IReadOnlyList<StudioPackageDraftSummary> Drafts { get; init; } = [];
}

#endregion

#region Result envelope

public sealed record StudioEndpointResult<T>(T? Data, StudioEndpointIssue? Issue)
{
    public bool IsSuccess => Issue is null;

    public static StudioEndpointResult<T> FromData(T data) => new(data, null);

    public static StudioEndpointResult<T> FromIssue(StudioEndpointIssue issue) => new(default, issue);
}

public sealed record StudioEndpointIssue(
    string State,
    string Contract,
    string Detail,
    int? StatusCode = null)
{
    /// <summary>True when the server reported an optimistic-concurrency conflict and the caller must reload.</summary>
    public bool IsConflict => StatusCode == (int)HttpStatusCode.Conflict;

    /// <summary>
    /// Structured Studio validation diagnostics parsed from a non-2xx validation body
    /// (<see cref="StudioValidationSummary"/> diagnostics <c>{code,severity,path,message}</c>), when the
    /// server returned one. Empty when the failure carried no validation summary (e.g. a transport error,
    /// auth failure, or a plain ProblemDetails). The console binds these onto editor fields via the
    /// Wave-0 <c>ServerFieldErrorMapper</c> + a per-editor JSON-Pointer resolver so a server finding
    /// surfaces inline next to the offending input instead of being discarded.
    /// </summary>
    public IReadOnlyList<StudioValidationDiagnostic> Diagnostics { get; init; } = [];
}

#endregion

#region Typed client

public sealed record StudioPackageLifecycleClientOptions(Uri BaseUri, string? ApiKey = null);

public interface IStudioPackageLifecycleClient
{
    Uri BaseUri { get; }

    Task<StudioEndpointResult<StudioPackageFamilyCapabilities>> ListPackageFamiliesAsync(
        CancellationToken cancellationToken = default);

    Task<StudioEndpointResult<StudioPackageDraft>> CreatePackageDraftAsync(
        CreateStudioPackageDraftRequest request,
        CancellationToken cancellationToken = default);

    Task<StudioEndpointResult<StudioPackageDraft>> GetPackageDraftAsync(
        Guid draftId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists secret-safe package-draft summaries (server route <c>GET /api/v1/studio/package-drafts</c>),
    /// optionally filtered by package <paramref name="family"/> and validation <paramref name="status"/>, so
    /// a Studio surface can enumerate existing packages instead of reporting the list as unsupported.
    /// </summary>
    Task<StudioEndpointResult<StudioPackageDraftListResponse>> ListPackageDraftsAsync(
        StudioPackageFamily? family = null,
        StudioPackageValidationStatus? status = null,
        CancellationToken cancellationToken = default);

    Task<StudioEndpointResult<StudioPackageDraft>> UpdatePackageDraftAsync(
        Guid draftId,
        UpdateStudioPackageDraftRequest request,
        CancellationToken cancellationToken = default);

    Task<StudioEndpointResult<StudioValidationSummary>> ValidatePackageDraftAsync(
        Guid draftId,
        CancellationToken cancellationToken = default);

    Task<StudioEndpointResult<StudioPreviewPlan>> CreatePreviewPlanAsync(
        Guid draftId,
        CancellationToken cancellationToken = default);

    Task<StudioEndpointResult<StudioContentVersion>> SaveContentVersionAsync(
        Guid draftId,
        SaveStudioContentVersionRequest request,
        CancellationToken cancellationToken = default);

    Task<StudioEndpointResult<StudioContentVersionListResponse>> ListContentVersionsAsync(
        Guid itemId,
        CancellationToken cancellationToken = default);

    Task<StudioEndpointResult<StudioContentVersion>> GetContentVersionAsync(
        Guid itemId,
        Guid versionId,
        CancellationToken cancellationToken = default);

    Task<StudioEndpointResult<StudioPublicationRequest>> CreatePublishRequestAsync(
        Guid itemId,
        Guid versionId,
        CreateStudioPublicationRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Reopens an immutable content version as a fresh editable draft (server route
    /// <c>POST /api/v1/studio/content-items/{itemId}/versions/{versionId}/reopen</c>). The server clones the
    /// version body into a new draft generation, so the published version is never mutated in place;
    /// subsequent saves create new content versions instead.
    /// </summary>
    Task<StudioEndpointResult<StudioPackageDraft>> ReopenContentVersionAsync(
        Guid itemId,
        Guid versionId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Reopens an immutable content version as a fresh editable draft (server route
    /// <c>POST /api/v1/studio/content-items/{itemId}/versions/{versionId}/reopen</c>). Alias retained for
    /// the dashboard/app builder lifecycle bindings.
    /// </summary>
    Task<StudioEndpointResult<StudioPackageDraft>> ReopenVersionAsync(
        Guid itemId,
        Guid versionId,
        CancellationToken cancellationToken = default);

    /// <summary>Requests a rollback to a prior published version, repointing the content item's
    /// current/published pointer to an earlier immutable version (server route
    /// <c>POST /api/v1/studio/content-items/{itemId}/rollback-requests</c>).</summary>
    Task<StudioEndpointResult<StudioRollbackRequest>> RollbackAsync(
        Guid itemId,
        CreateStudioRollbackRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>Alias for <see cref="RollbackAsync"/> retained for the app builder lifecycle binding.</summary>
    Task<StudioEndpointResult<StudioRollbackRequest>> CreateRollbackRequestAsync(
        Guid itemId,
        CreateStudioRollbackRequest request,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Thin adapter over the SDK's <see cref="IHonuaStudioPackageClient"/>. Every lifecycle call is delegated to
/// <see cref="HonuaStudioPackageClient"/> (which throws on non-2xx / contract drift) and the throwing result
/// is translated back into the console's non-throwing <see cref="StudioEndpointResult{T}"/> envelope so the
/// Studio DataSources can render a neutral issue state instead of surfacing an exception. The console
/// <see cref="HttpClient"/> from <c>HonuaServerClientFactory</c> already carries the base address and the
/// profile/session binding handler, so the admin <c>X-API-Key</c> is attached here as a default header (the
/// binding handler still swaps it for an operator bearer when one resolves) instead of via the SDK's own
/// auth handler.
/// </summary>
public sealed class HttpStudioPackageLifecycleClient : IStudioPackageLifecycleClient, IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly HttpClient _httpClient;
    private readonly string? _apiKey;
    private readonly IHonuaStudioPackageClient _sdk;

    public HttpStudioPackageLifecycleClient(HttpClient httpClient, StudioPackageLifecycleClientOptions options)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(options);

        BaseUri = options.BaseUri;
        _apiKey = options.ApiKey;
        _httpClient = httpClient;
        _httpClient.BaseAddress ??= BaseUri;

        // The admin key is the request-time fallback the binding handler keeps in place when no operator
        // bearer resolves (see HonuaServerBindingHandler). The prior direct-HTTP client attached it per
        // request; attaching it as a default header keeps every SDK-delegated request authenticated
        // identically without reaching for the SDK's auth handler.
        if (!string.IsNullOrWhiteSpace(_apiKey) && !_httpClient.DefaultRequestHeaders.Contains("X-API-Key"))
        {
            _httpClient.DefaultRequestHeaders.TryAddWithoutValidation("X-API-Key", _apiKey);
        }

        _sdk = new HonuaStudioPackageClient(_httpClient);
    }

    public Uri BaseUri { get; }

    public Task<StudioEndpointResult<StudioPackageFamilyCapabilities>> ListPackageFamiliesAsync(
        CancellationToken cancellationToken = default) =>
        ExecuteAsync(
            ct => _sdk.GetPackageFamiliesAsync(ct),
            "GET /api/v1/studio/package-families",
            cancellationToken);

    public Task<StudioEndpointResult<StudioPackageDraft>> CreatePackageDraftAsync(
        CreateStudioPackageDraftRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return ExecuteAsync(
            ct => _sdk.CreateDraftAsync(request, ct),
            "POST /api/v1/studio/package-drafts",
            cancellationToken);
    }

    public Task<StudioEndpointResult<StudioPackageDraft>> GetPackageDraftAsync(
        Guid draftId,
        CancellationToken cancellationToken = default) =>
        ExecuteAsync(
            ct => _sdk.GetDraftAsync(draftId, ct),
            "GET /api/v1/studio/package-drafts/{draftId}",
            cancellationToken);

    public Task<StudioEndpointResult<StudioPackageDraftListResponse>> ListPackageDraftsAsync(
        StudioPackageFamily? family = null,
        StudioPackageValidationStatus? status = null,
        CancellationToken cancellationToken = default)
    {
        // The SDK's IHonuaStudioPackageClient does not project the secret-safe draft list, so this one call
        // stays a direct GET against the console-only StudioPackageDraftListResponse projection.
        var queryParts = new List<string>();
        if (family is { } f)
        {
            queryParts.Add($"family={Uri.EscapeDataString(FamilyToWire(f))}");
        }

        if (status is { } s)
        {
            queryParts.Add($"status={Uri.EscapeDataString(StatusToWire(s))}");
        }

        var query = queryParts.Count == 0 ? string.Empty : "?" + string.Join("&", queryParts);
        return ListPackageDraftsCoreAsync(
            $"/api/v1/studio/package-drafts{query}",
            "GET /api/v1/studio/package-drafts (list)",
            cancellationToken);
    }

    public Task<StudioEndpointResult<StudioPackageDraft>> UpdatePackageDraftAsync(
        Guid draftId,
        UpdateStudioPackageDraftRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return ExecuteAsync(
            ct => _sdk.UpdateDraftAsync(draftId, request, ct),
            "PUT /api/v1/studio/package-drafts/{draftId}",
            cancellationToken);
    }

    public Task<StudioEndpointResult<StudioValidationSummary>> ValidatePackageDraftAsync(
        Guid draftId,
        CancellationToken cancellationToken = default) =>
        ExecuteAsync(
            ct => _sdk.ValidateDraftAsync(draftId, ct),
            "POST /api/v1/studio/package-drafts/{draftId}/validate",
            cancellationToken);

    public Task<StudioEndpointResult<StudioPreviewPlan>> CreatePreviewPlanAsync(
        Guid draftId,
        CancellationToken cancellationToken = default) =>
        ExecuteAsync(
            ct => _sdk.PreviewPlanAsync(draftId, ct),
            "POST /api/v1/studio/package-drafts/{draftId}/preview-plan",
            cancellationToken);

    public Task<StudioEndpointResult<StudioContentVersion>> SaveContentVersionAsync(
        Guid draftId,
        SaveStudioContentVersionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return ExecuteAsync(
            ct => _sdk.CreateContentVersionAsync(draftId, request, ct),
            "POST /api/v1/studio/package-drafts/{draftId}/content-versions",
            cancellationToken);
    }

    public Task<StudioEndpointResult<StudioContentVersionListResponse>> ListContentVersionsAsync(
        Guid itemId,
        CancellationToken cancellationToken = default) =>
        ExecuteAsync(
            ct => _sdk.ListVersionsAsync(itemId, ct),
            "GET /api/v1/studio/content-items/{itemId}/versions",
            cancellationToken);

    public Task<StudioEndpointResult<StudioContentVersion>> GetContentVersionAsync(
        Guid itemId,
        Guid versionId,
        CancellationToken cancellationToken = default) =>
        ExecuteAsync(
            ct => _sdk.GetVersionAsync(itemId, versionId, ct),
            "GET /api/v1/studio/content-items/{itemId}/versions/{versionId}",
            cancellationToken);

    public Task<StudioEndpointResult<StudioPublicationRequest>> CreatePublishRequestAsync(
        Guid itemId,
        Guid versionId,
        CreateStudioPublicationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return ExecuteAsync(
            ct => _sdk.CreatePublishRequestAsync(itemId, versionId, request, ct),
            "POST /api/v1/studio/content-items/{itemId}/versions/{versionId}/publish-requests",
            cancellationToken);
    }

    public Task<StudioEndpointResult<StudioPackageDraft>> ReopenContentVersionAsync(
        Guid itemId,
        Guid versionId,
        CancellationToken cancellationToken = default) =>
        ExecuteAsync(
            ct => _sdk.ReopenVersionAsync(itemId, versionId, ct),
            "POST /api/v1/studio/content-items/{itemId}/versions/{versionId}/reopen",
            cancellationToken);

    public Task<StudioEndpointResult<StudioPackageDraft>> ReopenVersionAsync(
        Guid itemId,
        Guid versionId,
        CancellationToken cancellationToken = default) =>
        ReopenContentVersionAsync(itemId, versionId, cancellationToken);

    public Task<StudioEndpointResult<StudioRollbackRequest>> RollbackAsync(
        Guid itemId,
        CreateStudioRollbackRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return ExecuteAsync(
            ct => _sdk.RollbackAsync(itemId, request, ct),
            "POST /api/v1/studio/content-items/{itemId}/rollback-requests",
            cancellationToken);
    }

    public Task<StudioEndpointResult<StudioRollbackRequest>> CreateRollbackRequestAsync(
        Guid itemId,
        CreateStudioRollbackRequest request,
        CancellationToken cancellationToken = default) =>
        RollbackAsync(itemId, request, cancellationToken);

    public void Dispose() => _httpClient.Dispose();

    private static string FamilyToWire(StudioPackageFamily family) => family switch
    {
        StudioPackageFamily.Query => "query",
        StudioPackageFamily.Analysis => "analysis",
        StudioPackageFamily.Map => "map",
        StudioPackageFamily.Dashboard => "dashboard",
        StudioPackageFamily.Report => "report",
        StudioPackageFamily.Form => "form",
        StudioPackageFamily.App => "app",
        StudioPackageFamily.Workflow => "workflow",
        StudioPackageFamily.Geoprocessing => "gp",
        StudioPackageFamily.Etl => "etl",
        _ => family.ToString().ToLowerInvariant(),
    };

    private static string StatusToWire(StudioPackageValidationStatus status) => status switch
    {
        StudioPackageValidationStatus.NotValidated => "not-validated",
        StudioPackageValidationStatus.Valid => "valid",
        StudioPackageValidationStatus.Warning => "warning",
        StudioPackageValidationStatus.Invalid => "invalid",
        _ => status.ToString().ToLowerInvariant(),
    };

    /// <summary>
    /// Delegates one SDK lifecycle call and translates its throwing contract into the console's non-throwing
    /// result envelope: an <see cref="HonuaStudioApiException"/> (non-2xx) becomes the same
    /// state/detail/StatusCode issue the direct-HTTP client produced (with validation diagnostics parsed out
    /// of the response body); an <see cref="HonuaStudioContractException"/> (successful status, drifted/empty
    /// body) becomes an "Unsupported" shape-mismatch issue; and a transport fault becomes an "Unavailable"
    /// issue. A genuine cancellation is left to propagate.
    /// </summary>
    private async Task<StudioEndpointResult<TResponse>> ExecuteAsync<TResponse>(
        Func<CancellationToken, Task<TResponse>> operation,
        string contract,
        CancellationToken cancellationToken)
        where TResponse : class
    {
        try
        {
            var data = await operation(cancellationToken).ConfigureAwait(false);
            return StudioEndpointResult<TResponse>.FromData(data);
        }
        catch (HonuaStudioApiException ex)
        {
            // The Studio validate/create/update endpoints return a structured validation body on a rejection
            // (the StudioValidationSummary diagnostics {code,severity,path,message}). Parse and carry it on the
            // issue instead of discarding it, so the console can bind each diagnostic onto the offending editor
            // field (map layer / query predicate) via the Wave-0 ServerFieldErrorMapper.
            var diagnostics = ParseDiagnostics(ex.ResponseBody);
            return StudioEndpointResult<TResponse>.FromIssue(
                CreateIssue(contract, ex.StatusCode) with { Diagnostics = diagnostics });
        }
        catch (HonuaStudioContractException ex)
        {
            return StudioEndpointResult<TResponse>.FromIssue(new StudioEndpointIssue(
                "Unsupported",
                contract,
                $"The Honua server Studio response did not match the expected API shape: {ex.Message}"));
        }
        catch (HttpRequestException ex)
        {
            return StudioEndpointResult<TResponse>.FromIssue(new StudioEndpointIssue(
                "Unavailable",
                contract,
                $"The Honua server Studio endpoint could not be reached: {ex.Message}"));
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            return StudioEndpointResult<TResponse>.FromIssue(new StudioEndpointIssue(
                "Unavailable",
                contract,
                $"The Honua server Studio endpoint could not be reached: {ex.Message}"));
        }
    }

    private async Task<StudioEndpointResult<StudioPackageDraftListResponse>> ListPackageDraftsCoreAsync(
        string path,
        string contract,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);

        HttpResponseMessage response;
        try
        {
            response = await _httpClient.SendAsync(
                    request,
                    HttpCompletionOption.ResponseHeadersRead,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            return StudioEndpointResult<StudioPackageDraftListResponse>.FromIssue(new StudioEndpointIssue(
                "Unavailable",
                contract,
                $"The Honua server Studio endpoint could not be reached: {ex.Message}"));
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            return StudioEndpointResult<StudioPackageDraftListResponse>.FromIssue(new StudioEndpointIssue(
                "Unavailable",
                contract,
                $"The Honua server Studio endpoint could not be reached: {ex.Message}"));
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                var diagnostics = await ReadDiagnosticsAsync(response, cancellationToken).ConfigureAwait(false);
                return StudioEndpointResult<StudioPackageDraftListResponse>.FromIssue(
                    CreateIssue(contract, response.StatusCode) with { Diagnostics = diagnostics });
            }

            StudioApiResponse<StudioPackageDraftListResponse>? envelope;
            try
            {
                envelope = await response.Content
                    .ReadFromJsonAsync<StudioApiResponse<StudioPackageDraftListResponse>>(JsonOptions, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (JsonException ex)
            {
                return StudioEndpointResult<StudioPackageDraftListResponse>.FromIssue(new StudioEndpointIssue(
                    "Unsupported",
                    contract,
                    $"The Honua server Studio response did not match the expected API shape: {ex.Message}",
                    (int)response.StatusCode));
            }

            if (envelope?.Success == true && envelope.Data is not null)
            {
                return StudioEndpointResult<StudioPackageDraftListResponse>.FromData(envelope.Data);
            }

            return StudioEndpointResult<StudioPackageDraftListResponse>.FromIssue(new StudioEndpointIssue(
                "Unavailable",
                contract,
                envelope?.Message ?? "The Honua server Studio response did not include data.",
                (int)response.StatusCode));
        }
    }

    private static StudioEndpointIssue CreateIssue(string contract, HttpStatusCode statusCode)
    {
        var state = statusCode switch
        {
            HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => "Missing permission",
            HttpStatusCode.NotFound or HttpStatusCode.MethodNotAllowed or HttpStatusCode.NotImplemented => "Unsupported",
            HttpStatusCode.Conflict => "Conflict",
            _ => "Unavailable"
        };

        var detail = statusCode switch
        {
            HttpStatusCode.Unauthorized => "The Honua server rejected the Studio request because admin authentication is missing.",
            HttpStatusCode.Forbidden => "The Honua server rejected the Studio request because the current principal lacks admin permission.",
            HttpStatusCode.NotFound => "The Honua server does not expose this Studio package contract, or the draft/version was not found.",
            HttpStatusCode.MethodNotAllowed => "The Honua server exposes the route but not the required Studio API verb.",
            HttpStatusCode.NotImplemented => "The Honua server reports this Studio capability is not implemented.",
            HttpStatusCode.Conflict => "The Studio draft changed on the server (optimistic concurrency); reload before retrying.",
            _ => string.Format(
                CultureInfo.InvariantCulture,
                "The Honua server returned HTTP {0} ({1}) for the Studio request.",
                (int)statusCode,
                statusCode)
        };

        return new StudioEndpointIssue(state, contract, detail, (int)statusCode);
    }

    /// <summary>
    /// Best-effort parse of the Studio validation diagnostics carried on a non-2xx response body captured by
    /// the SDK's <see cref="HonuaStudioApiException.ResponseBody"/>. Never throws: a null/empty/unparseable
    /// body yields an empty list so the caller still surfaces the HTTP-level issue.
    /// </summary>
    private static IReadOnlyList<StudioValidationDiagnostic> ParseDiagnostics(string? body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return [];
        }

        try
        {
            using var document = JsonDocument.Parse(body);
            return ExtractDiagnostics(document.RootElement);
        }
        catch (JsonException)
        {
            return [];
        }
    }

    /// <summary>
    /// Best-effort parse of the Studio validation diagnostics carried on a non-2xx response for the
    /// direct-HTTP draft-list path. Probes the same common locations as <see cref="ExtractDiagnostics"/> and
    /// never throws.
    /// </summary>
    private static async Task<IReadOnlyList<StudioValidationDiagnostic>> ReadDiagnosticsAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        string body;
        try
        {
            body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException or IOException)
        {
            return [];
        }

        return ParseDiagnostics(body);
    }

    private static IReadOnlyList<StudioValidationDiagnostic> ExtractDiagnostics(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            return [];
        }

        // Top-level diagnostics (bare StudioValidationSummary or a ProblemDetails diagnostics extension).
        if (TryReadDiagnostics(root, out var direct))
        {
            return direct;
        }

        // ApiResponse envelope: diagnostics live under data (a summary) or data.validation (a draft/version/plan).
        if (root.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Object)
        {
            if (TryReadDiagnostics(data, out var fromData))
            {
                return fromData;
            }

            if (data.TryGetProperty("validation", out var validation)
                && TryReadDiagnostics(validation, out var fromValidation))
            {
                return fromValidation;
            }
        }

        // Some flat throwers nest the summary under an "errors" extension.
        if (root.TryGetProperty("errors", out var errors) && TryReadDiagnostics(errors, out var fromErrors))
        {
            return fromErrors;
        }

        return [];
    }

    private static bool TryReadDiagnostics(JsonElement element, out IReadOnlyList<StudioValidationDiagnostic> diagnostics)
    {
        diagnostics = [];

        JsonElement array;
        if (element.ValueKind == JsonValueKind.Array)
        {
            array = element;
        }
        else if (element.ValueKind == JsonValueKind.Object
            && element.TryGetProperty("diagnostics", out var nested)
            && nested.ValueKind == JsonValueKind.Array)
        {
            array = nested;
        }
        else
        {
            return false;
        }

        var parsed = new List<StudioValidationDiagnostic>();
        foreach (var item in array.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            StudioValidationDiagnostic? diagnostic;
            try
            {
                diagnostic = item.Deserialize<StudioValidationDiagnostic>(JsonOptions);
            }
            catch (JsonException)
            {
                // The SDK diagnostic record has required members; a body item missing one is skipped rather
                // than aborting the whole parse, preserving the never-throws contract.
                continue;
            }

            if (diagnostic is not null && !string.IsNullOrEmpty(diagnostic.Code))
            {
                parsed.Add(diagnostic);
            }
        }

        diagnostics = parsed;
        return parsed.Count > 0;
    }
}

#endregion
