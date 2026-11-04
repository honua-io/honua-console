using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Honua.Console.Contracts;

public sealed record HonuaVersionManagementClientOptions(Uri BaseUri, string? ApiKey = null);

public enum HonuaVersionReconcilePolicy
{
    // Leaves every conflict for manual review.
    None,

    LastWriteWins,

    VersionWins,

    DefaultWins
}

public enum HonuaVersionConflictChoice
{
    TakeVersion,

    TakeDefault,

    // Reverts the feature to the common-ancestor image.
    TakeBase
}

public enum HonuaVersionAccess
{
    Private,

    // Anyone can read; only the owner can edit.
    Protected,

    Public
}

public sealed record HonuaVersionInfo
{
    [JsonPropertyName("versionGuid")]
    public string VersionGuid { get; init; } = string.Empty;

    [JsonPropertyName("versionName")]
    public string VersionName { get; init; } = string.Empty;

    [JsonPropertyName("owner")]
    public string Owner { get; init; } = string.Empty;

    [JsonPropertyName("access")]
    public string Access { get; init; } = "private";

    [JsonPropertyName("status")]
    public string Status { get; init; } = "active";

    [JsonPropertyName("description")]
    public string? Description { get; init; }

    [JsonPropertyName("parentVersionGuid")]
    public string? ParentVersionGuid { get; init; }

    [JsonPropertyName("creationMoment")]
    public long CreationMoment { get; init; }

    [JsonPropertyName("modifiedMoment")]
    public long ModifiedMoment { get; init; }
}

public sealed record HonuaVersionConflictFieldDiff
{
    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    [JsonPropertyName("base")]
    public string? Base { get; init; }

    [JsonPropertyName("default")]
    public string? Default { get; init; }

    [JsonPropertyName("version")]
    public string? Version { get; init; }
}

public sealed record HonuaVersionConflict
{
    [JsonPropertyName("layerId")]
    public int LayerId { get; init; }

    [JsonPropertyName("objectId")]
    public long ObjectId { get; init; }

    [JsonPropertyName("conflictType")]
    public string ConflictType { get; init; } = string.Empty;

    [JsonPropertyName("baseAttributes")]
    public string? BaseAttributes { get; init; }

    [JsonPropertyName("defaultAttributes")]
    public string? DefaultAttributes { get; init; }

    [JsonPropertyName("versionAttributes")]
    public string? VersionAttributes { get; init; }

    [JsonPropertyName("baseGeometry")]
    public string? BaseGeometry { get; init; }

    [JsonPropertyName("defaultGeometry")]
    public string? DefaultGeometry { get; init; }

    [JsonPropertyName("versionGeometry")]
    public string? VersionGeometry { get; init; }

    [JsonPropertyName("fieldDiffs")]
    public IReadOnlyList<HonuaVersionConflictFieldDiff> FieldDiffs { get; init; } = [];
}

public sealed record HonuaReconcileResult
{
    [JsonPropertyName("success")]
    public bool Success { get; init; }

    [JsonPropertyName("hasConflicts")]
    public bool HasConflicts { get; init; }

    [JsonPropertyName("canPost")]
    public bool CanPost { get; init; }

    [JsonPropertyName("autoResolvedCount")]
    public int AutoResolvedCount { get; init; }

    [JsonPropertyName("conflicts")]
    public IReadOnlyList<HonuaVersionConflict> Conflicts { get; init; } = [];
}

public sealed record HonuaInspectConflictsResult
{
    [JsonPropertyName("success")]
    public bool Success { get; init; }

    [JsonPropertyName("hasConflicts")]
    public bool HasConflicts { get; init; }

    [JsonPropertyName("conflicts")]
    public IReadOnlyList<HonuaVersionConflict> Conflicts { get; init; } = [];
}

public sealed record HonuaResolveConflictsResult
{
    [JsonPropertyName("success")]
    public bool Success { get; init; }

    [JsonPropertyName("resolved")]
    public int Resolved { get; init; }

    [JsonPropertyName("remaining")]
    public int Remaining { get; init; }

    [JsonPropertyName("canPost")]
    public bool CanPost { get; init; }
}

public sealed record HonuaPostResult
{
    [JsonPropertyName("success")]
    public bool Success { get; init; }

    [JsonPropertyName("appliedChanges")]
    public int AppliedChanges { get; init; }

    [JsonPropertyName("serverGeneration")]
    public long ServerGeneration { get; init; }

    [JsonPropertyName("blockedByConflicts")]
    public bool BlockedByConflicts { get; init; }
}

public sealed record HonuaVersionConflictResolution(int LayerId, long ObjectId, HonuaVersionConflictChoice Choice);

public interface IHonuaVersionManagementClient
{
    Task<HonuaAdminEndpointResult<IReadOnlyList<HonuaVersionInfo>>> ListVersionsAsync(
        string serviceId,
        CancellationToken cancellationToken = default);

    Task<HonuaAdminEndpointResult<HonuaVersionInfo>> CreateVersionAsync(
        string serviceId,
        string versionName,
        string? owner,
        HonuaVersionAccess access,
        string? description,
        CancellationToken cancellationToken = default);

    Task<HonuaAdminEndpointResult<HonuaVersionMomentResult>> AlterVersionAsync(
        string serviceId,
        string versionGuid,
        string? versionName,
        HonuaVersionAccess? access,
        string? description,
        CancellationToken cancellationToken = default);

    Task<HonuaAdminEndpointResult<HonuaVersionMomentResult>> DeleteVersionAsync(
        string serviceId,
        string versionGuid,
        CancellationToken cancellationToken = default);

    Task<HonuaAdminEndpointResult<HonuaReconcileResult>> ReconcileAsync(
        string serviceId,
        string versionGuid,
        HonuaVersionReconcilePolicy policy,
        bool abortIfConflicts,
        CancellationToken cancellationToken = default);

    Task<HonuaAdminEndpointResult<HonuaInspectConflictsResult>> InspectConflictsAsync(
        string serviceId,
        string versionGuid,
        CancellationToken cancellationToken = default);

    Task<HonuaAdminEndpointResult<HonuaResolveConflictsResult>> ResolveConflictsAsync(
        string serviceId,
        string versionGuid,
        IReadOnlyList<HonuaVersionConflictResolution> resolutions,
        CancellationToken cancellationToken = default);

    Task<HonuaAdminEndpointResult<HonuaPostResult>> PostAsync(
        string serviceId,
        string versionGuid,
        CancellationToken cancellationToken = default);
}

public sealed record HonuaVersionMomentResult
{
    [JsonPropertyName("success")]
    public bool Success { get; init; }

    [JsonPropertyName("moment")]
    public long Moment { get; init; }
}

public sealed class HonuaVersionManagementHttpClient : IHonuaVersionManagementClient, IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    private static readonly AdminBodyFormat BodyFormat = new(
        JsonOptions,
        AdminBodyRead.Buffered,
        "The Honua server VersionManagementServer response did not match the expected contract");

    private readonly HttpClient _httpClient;
    private readonly AdminEndpointTransport _transport;

    public HonuaVersionManagementHttpClient(HttpClient httpClient, HonuaVersionManagementClientOptions options)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(options);

        BaseUri = options.BaseUri;
        _httpClient = httpClient;
        _httpClient.BaseAddress ??= BaseUri;
        _transport = new AdminEndpointTransport(
            httpClient,
            options.ApiKey,
            AdminTransportFailure.Unreachable("The Honua server endpoint"));
    }

    public Uri BaseUri { get; }

    private static string Base(string serviceId) =>
        $"/rest/services/{Uri.EscapeDataString(serviceId)}/VersionManagementServer";

    public Task<HonuaAdminEndpointResult<IReadOnlyList<HonuaVersionInfo>>> ListVersionsAsync(
        string serviceId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serviceId);
        var path = $"{Base(serviceId)}/versions?f=json";
        const string contract = "GET .../VersionManagementServer/versions";

        return _transport.SendAsync(
            Call(
                () => Get(path),
                contract,
                Body<VersionListBody, IReadOnlyList<HonuaVersionInfo>>(body => body.Versions ?? [])),
            cancellationToken);
    }

    public Task<HonuaAdminEndpointResult<HonuaVersionInfo>> CreateVersionAsync(
        string serviceId,
        string versionName,
        string? owner,
        HonuaVersionAccess access,
        string? description,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serviceId);
        ArgumentException.ThrowIfNullOrWhiteSpace(versionName);

        var form = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["f"] = "json",
            ["versionName"] = versionName,
            ["accessPermission"] = AccessToString(access)
        };
        if (!string.IsNullOrWhiteSpace(owner))
        {
            form["owner"] = owner;
        }

        if (!string.IsNullOrWhiteSpace(description))
        {
            form["description"] = description;
        }

        const string contract = "POST .../VersionManagementServer/create";
        return _transport.SendAsync(
            Call(
                () => Form($"{Base(serviceId)}/create", form),
                contract,
                Body<CreateVersionBody, HonuaVersionInfo>(body => body.VersionInfo ?? new HonuaVersionInfo())),
            cancellationToken);
    }

    public Task<HonuaAdminEndpointResult<HonuaVersionMomentResult>> AlterVersionAsync(
        string serviceId,
        string versionGuid,
        string? versionName,
        HonuaVersionAccess? access,
        string? description,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serviceId);
        ArgumentException.ThrowIfNullOrWhiteSpace(versionGuid);

        var form = new Dictionary<string, string>(StringComparer.Ordinal) { ["f"] = "json" };
        if (!string.IsNullOrWhiteSpace(versionName))
        {
            form["versionName"] = versionName;
        }

        if (access is { } resolvedAccess)
        {
            form["accessPermission"] = AccessToString(resolvedAccess);
        }

        if (description is not null)
        {
            form["description"] = description;
        }

        const string contract = "POST .../VersionManagementServer/versions/{guid}/alter";
        return _transport.SendAsync(
            Call(
                () => Form($"{Base(serviceId)}/versions/{Uri.EscapeDataString(versionGuid)}/alter", form),
                contract,
                Body<HonuaVersionMomentResult>()),
            cancellationToken);
    }

    public Task<HonuaAdminEndpointResult<HonuaVersionMomentResult>> DeleteVersionAsync(
        string serviceId,
        string versionGuid,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serviceId);
        ArgumentException.ThrowIfNullOrWhiteSpace(versionGuid);

        var form = new Dictionary<string, string>(StringComparer.Ordinal) { ["f"] = "json" };
        const string contract = "POST .../VersionManagementServer/versions/{guid}/delete";
        return _transport.SendAsync(
            Call(
                () => Form($"{Base(serviceId)}/versions/{Uri.EscapeDataString(versionGuid)}/delete", form),
                contract,
                Body<HonuaVersionMomentResult>()),
            cancellationToken);
    }

    public Task<HonuaAdminEndpointResult<HonuaReconcileResult>> ReconcileAsync(
        string serviceId,
        string versionGuid,
        HonuaVersionReconcilePolicy policy,
        bool abortIfConflicts,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serviceId);
        ArgumentException.ThrowIfNullOrWhiteSpace(versionGuid);

        var form = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["f"] = "json",
            ["conflictResolution"] = PolicyToString(policy)
        };
        if (abortIfConflicts)
        {
            form["abortIfConflicts"] = "true";
        }

        const string contract = "POST .../VersionManagementServer/versions/{guid}/reconcile";
        return _transport.SendAsync(
            Call(
                () => Form($"{Base(serviceId)}/versions/{Uri.EscapeDataString(versionGuid)}/reconcile", form),
                contract,
                Body<HonuaReconcileResult>()),
            cancellationToken);
    }

    public Task<HonuaAdminEndpointResult<HonuaInspectConflictsResult>> InspectConflictsAsync(
        string serviceId,
        string versionGuid,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serviceId);
        ArgumentException.ThrowIfNullOrWhiteSpace(versionGuid);

        var path = $"{Base(serviceId)}/versions/{Uri.EscapeDataString(versionGuid)}/inspectConflicts?f=json";
        const string contract = "GET .../VersionManagementServer/versions/{guid}/inspectConflicts";
        return _transport.SendAsync(
            Call(
                () => Get(path),
                contract,
                Body<HonuaInspectConflictsResult>()),
            cancellationToken);
    }

    public Task<HonuaAdminEndpointResult<HonuaResolveConflictsResult>> ResolveConflictsAsync(
        string serviceId,
        string versionGuid,
        IReadOnlyList<HonuaVersionConflictResolution> resolutions,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serviceId);
        ArgumentException.ThrowIfNullOrWhiteSpace(versionGuid);
        ArgumentNullException.ThrowIfNull(resolutions);

        // The server expects a `conflicts` JSON array of { layerId, objectId, choice } objects.
        var conflictsJson = JsonSerializer.Serialize(
            resolutions.Select(r => new ResolutionWire(r.LayerId, r.ObjectId, ChoiceToString(r.Choice))).ToArray(),
            JsonOptions);

        var form = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["f"] = "json",
            ["conflicts"] = conflictsJson
        };

        const string contract = "POST .../VersionManagementServer/versions/{guid}/resolveConflicts";
        return _transport.SendAsync(
            Call(
                () => Form($"{Base(serviceId)}/versions/{Uri.EscapeDataString(versionGuid)}/resolveConflicts", form),
                contract,
                Body<HonuaResolveConflictsResult>()),
            cancellationToken);
    }

    public Task<HonuaAdminEndpointResult<HonuaPostResult>> PostAsync(
        string serviceId,
        string versionGuid,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serviceId);
        ArgumentException.ThrowIfNullOrWhiteSpace(versionGuid);

        var form = new Dictionary<string, string>(StringComparer.Ordinal) { ["f"] = "json" };
        const string contract = "POST .../VersionManagementServer/versions/{guid}/post";
        return _transport.SendAsync(
            Call(
                () => Form($"{Base(serviceId)}/versions/{Uri.EscapeDataString(versionGuid)}/post", form),
                contract,
                Body<HonuaPostResult>()),
            cancellationToken);
    }

    private HttpRequestMessage Get(string path) => new(HttpMethod.Get, AdminEndpointPaths.Resolve(BaseUri, path));

    private HttpRequestMessage Form(string path, IReadOnlyDictionary<string, string> form) =>
        new(HttpMethod.Post, AdminEndpointPaths.Resolve(BaseUri, path)) { Content = new FormUrlEncodedContent(form) };

    private static AdminEndpointCall<T> Call<T>(
        Func<HttpRequestMessage> createRequest,
        string contract,
        AdminResponseReader<T> readBody) =>
        new(() => AcceptingJson(createRequest()), contract, new AdminResponseHandling<T>(MapFailureAsync, readBody));

    private static HttpRequestMessage AcceptingJson(HttpRequestMessage request)
    {
        request.Headers.TryAddWithoutValidation("Accept", "application/json");
        return request;
    }

    private static AdminResponseReader<T> Body<T>() => Body<T, T>(body => body);

    private static AdminResponseReader<TResult> Body<TBody, TResult>(Func<TBody, TResult> project) =>
        AdminResponseBodies.Required(BodyFormat, "The Honua server VersionManagementServer response did not include a body.", project);

    private static async Task<HonuaAdminEndpointIssue> MapFailureAsync(
        string contract,
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        var payload = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        return CreateIssue(contract, response.StatusCode, payload);
    }

    private static HonuaAdminEndpointIssue CreateIssue(string contract, HttpStatusCode statusCode, string? payload)
    {
        var state = statusCode switch
        {
            HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => "Missing permission",
            HttpStatusCode.NotFound or HttpStatusCode.MethodNotAllowed or HttpStatusCode.NotImplemented => "Unsupported",
            HttpStatusCode.Conflict => "Rejected",
            _ => "Unavailable"
        };

        var serverMessage = ParseFailureMessage(payload);
        var detail = !string.IsNullOrWhiteSpace(serverMessage)
            ? serverMessage
            : statusCode switch
            {
                HttpStatusCode.Unauthorized => "The Honua server rejected the request because admin authentication is missing.",
                HttpStatusCode.Forbidden => "The current principal lacks permission to manage branch versions.",
                HttpStatusCode.NotFound => "The Honua server does not expose the VersionManagementServer for this service.",
                HttpStatusCode.NotImplemented => "Branch versioning is not supported by the configured data provider.",
                _ => string.Format(
                    CultureInfo.InvariantCulture,
                    "The Honua server returned HTTP {0} ({1}).",
                    (int)statusCode,
                    statusCode)
            };

        return new HonuaAdminEndpointIssue(state, contract, detail, (int)statusCode);
    }

    // A GeoServices error body ({ error: { message } }) takes precedence over the ApiResponse/ProblemDetails text.
    private static string? ParseFailureMessage(string? payload) =>
        ParseGeoServicesErrorMessage(payload) ?? AdminFailureMappings.ParseServerMessage(payload);

    private static string? ParseGeoServicesErrorMessage(string? payload)
    {
        using var document = AdminFailureMappings.TryParseObject(payload);
        return document is not null
            && document.RootElement.TryGetProperty("error", out var error)
            && error.ValueKind == JsonValueKind.Object
            && error.TryGetProperty("message", out var message)
            && message.ValueKind == JsonValueKind.String
            && message.GetString() is { Length: > 0 } text
                ? text
                : null;
    }

    private static string PolicyToString(HonuaVersionReconcilePolicy policy) => policy switch
    {
        HonuaVersionReconcilePolicy.LastWriteWins => "lastWriteWins",
        HonuaVersionReconcilePolicy.VersionWins => "versionWins",
        HonuaVersionReconcilePolicy.DefaultWins => "defaultWins",
        _ => "none"
    };

    private static string ChoiceToString(HonuaVersionConflictChoice choice) => choice switch
    {
        HonuaVersionConflictChoice.TakeDefault => "default",
        HonuaVersionConflictChoice.TakeBase => "base",
        _ => "version"
    };

    private static string AccessToString(HonuaVersionAccess access) => access switch
    {
        HonuaVersionAccess.Public => "public",
        HonuaVersionAccess.Protected => "protected",
        _ => "private"
    };

    public void Dispose() => _httpClient.Dispose();

    private sealed record VersionListBody
    {
        [JsonPropertyName("versions")]
        public IReadOnlyList<HonuaVersionInfo>? Versions { get; init; }
    }

    private sealed record CreateVersionBody
    {
        [JsonPropertyName("success")]
        public bool Success { get; init; }

        [JsonPropertyName("versionInfo")]
        public HonuaVersionInfo? VersionInfo { get; init; }
    }

    private sealed record ResolutionWire(
        [property: JsonPropertyName("layerId")] int LayerId,
        [property: JsonPropertyName("objectId")] long ObjectId,
        [property: JsonPropertyName("choice")] string Choice);
}
