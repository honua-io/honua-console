using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Honua.Console.Contracts;

// SHIM(honua-sdk-dotnet#166): like OperateAdminShims, there is no consumable stable Honua.Sdk.Admin package
// that projects the GeoServices VersionManagementServer surface (branch-versioning lifecycle + reconcile/
// inspectConflicts/resolveConflicts/post, honua-server #371 / PR #1551). Keep these wire records and the thin
// HTTP client in the Console contracts boundary until honua-sdk-dotnet publishes a consumable stable Admin
// package and honua-console#7 wires the feed; do not add a sibling-repo ProjectReference. See SDK_SHIM_POLICY
// "Active Shims".
//
// Unlike the /api/v1/admin surface, the VersionManagementServer is a GeoServices REST surface: it reads its
// inputs from form/query values (NOT a JSON body) and returns BARE Esri-shaped JSON DTOs (NOT the admin
// {success,data} envelope). This client therefore posts application/x-www-form-urlencoded and deserializes
// the raw DTOs directly, mirroring the server models in
// src/Honua.Protocols.GeoServices/VersionManagementServer/Models/VersionManagementModels.cs.

/// <summary>Connection options for the VersionManagementServer client (base URL + optional admin API key).</summary>
public sealed record HonuaVersionManagementClientOptions(Uri BaseUri, string? ApiKey = null);

/// <summary>Auto-resolution policy applied by a reconcile (mirrors the server's named policies).</summary>
public enum HonuaVersionReconcilePolicy
{
    /// <summary>No auto-resolution; every conflict is left for manual review.</summary>
    None,

    /// <summary>Resolve each conflict in favor of the most recently edited image.</summary>
    LastWriteWins,

    /// <summary>Resolve each conflict in favor of the branch (version) image.</summary>
    VersionWins,

    /// <summary>Resolve each conflict in favor of the DEFAULT (target) image.</summary>
    DefaultWins
}

/// <summary>Per-feature manual resolution choice (mirrors the server's take-version/default/base).</summary>
public enum HonuaVersionConflictChoice
{
    /// <summary>Keep the branch (version) edit.</summary>
    TakeVersion,

    /// <summary>Keep the DEFAULT (target) edit.</summary>
    TakeDefault,

    /// <summary>Revert to the common-ancestor (base) image.</summary>
    TakeBase
}

/// <summary>Access level for a branch version (mirrors the server's private/protected/public).</summary>
public enum HonuaVersionAccess
{
    /// <summary>Only the owner can read/edit.</summary>
    Private,

    /// <summary>Anyone can read; only the owner can edit.</summary>
    Protected,

    /// <summary>Anyone can read/edit.</summary>
    Public
}

/// <summary>Esri-shaped branch-version identity (mirrors <c>VersionInfo</c>).</summary>
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

/// <summary>One field-level three-way diff for a conflicting feature (mirrors <c>VersionConflictFieldDiffInfo</c>).</summary>
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

/// <summary>A single conflicting feature's three-way images and per-field diffs (mirrors <c>VersionConflictInfo</c>).</summary>
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

/// <summary>Result of a reconcile (mirrors <c>ReconcileResponse</c>).</summary>
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

/// <summary>Result of inspectConflicts (mirrors <c>InspectConflictsResponse</c>).</summary>
public sealed record HonuaInspectConflictsResult
{
    [JsonPropertyName("success")]
    public bool Success { get; init; }

    [JsonPropertyName("hasConflicts")]
    public bool HasConflicts { get; init; }

    [JsonPropertyName("conflicts")]
    public IReadOnlyList<HonuaVersionConflict> Conflicts { get; init; } = [];
}

/// <summary>Result of resolveConflicts (mirrors <c>ResolveConflictsResponse</c>).</summary>
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

/// <summary>Result of post (mirrors <c>PostResponse</c>).</summary>
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

/// <summary>A single manual resolution choice submitted to resolveConflicts.</summary>
public sealed record HonuaVersionConflictResolution(int LayerId, long ObjectId, HonuaVersionConflictChoice Choice);

/// <summary>
/// Thin HTTP client for honua-server's GeoServices VersionManagementServer (branch-versioning lifecycle +
/// conflict resolution, #371 / PR #1551). Mirrors the route shapes and form/query parameter names of
/// <c>VersionManagementServerEndpoints</c> and returns the bare Esri-shaped DTOs. Network/transport/contract
/// failures degrade into a <see cref="HonuaAdminEndpointIssue"/> via the shared result type rather than
/// throwing, so the console can render a neutral missing/unavailable state.
/// </summary>
public interface IHonuaVersionManagementClient
{
    /// <summary>Lists the branch versions for a service (<c>GET .../VersionManagementServer/versions</c>).</summary>
    Task<HonuaAdminEndpointResult<IReadOnlyList<HonuaVersionInfo>>> ListVersionsAsync(
        string serviceId,
        CancellationToken cancellationToken = default);

    /// <summary>Creates a branch version (<c>POST .../create</c>).</summary>
    Task<HonuaAdminEndpointResult<HonuaVersionInfo>> CreateVersionAsync(
        string serviceId,
        string versionName,
        string? owner,
        HonuaVersionAccess access,
        string? description,
        CancellationToken cancellationToken = default);

    /// <summary>Alters a branch version's name/access/description (<c>POST .../versions/{guid}/alter</c>).</summary>
    Task<HonuaAdminEndpointResult<HonuaVersionMomentResult>> AlterVersionAsync(
        string serviceId,
        string versionGuid,
        string? versionName,
        HonuaVersionAccess? access,
        string? description,
        CancellationToken cancellationToken = default);

    /// <summary>Deletes a branch version (<c>POST .../versions/{guid}/delete</c>).</summary>
    Task<HonuaAdminEndpointResult<HonuaVersionMomentResult>> DeleteVersionAsync(
        string serviceId,
        string versionGuid,
        CancellationToken cancellationToken = default);

    /// <summary>Reconciles a branch version against DEFAULT with an auto-resolution policy (<c>POST .../reconcile</c>).</summary>
    Task<HonuaAdminEndpointResult<HonuaReconcileResult>> ReconcileAsync(
        string serviceId,
        string versionGuid,
        HonuaVersionReconcilePolicy policy,
        bool abortIfConflicts,
        CancellationToken cancellationToken = default);

    /// <summary>Reads the pending conflict set for a branch version (<c>GET .../inspectConflicts</c>).</summary>
    Task<HonuaAdminEndpointResult<HonuaInspectConflictsResult>> InspectConflictsAsync(
        string serviceId,
        string versionGuid,
        CancellationToken cancellationToken = default);

    /// <summary>Submits manual per-feature resolutions (<c>POST .../resolveConflicts</c>).</summary>
    Task<HonuaAdminEndpointResult<HonuaResolveConflictsResult>> ResolveConflictsAsync(
        string serviceId,
        string versionGuid,
        IReadOnlyList<HonuaVersionConflictResolution> resolutions,
        CancellationToken cancellationToken = default);

    /// <summary>Publishes a reconciled branch version onto DEFAULT (<c>POST .../post</c>).</summary>
    Task<HonuaAdminEndpointResult<HonuaPostResult>> PostAsync(
        string serviceId,
        string versionGuid,
        CancellationToken cancellationToken = default);
}

/// <summary>Generic success/moment result for delete/alter (mirrors <c>VersionMomentResponse</c>).</summary>
public sealed record HonuaVersionMomentResult
{
    [JsonPropertyName("success")]
    public bool Success { get; init; }

    [JsonPropertyName("moment")]
    public long Moment { get; init; }
}

/// <inheritdoc cref="IHonuaVersionManagementClient" />
public sealed class HonuaVersionManagementHttpClient : IHonuaVersionManagementClient, IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _httpClient;
    private readonly string? _apiKey;

    /// <summary>Creates the client over a configured <see cref="HttpClient"/> and connection options.</summary>
    public HonuaVersionManagementHttpClient(HttpClient httpClient, HonuaVersionManagementClientOptions options)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(options);

        BaseUri = options.BaseUri;
        _apiKey = options.ApiKey;
        _httpClient = httpClient;
        _httpClient.BaseAddress ??= BaseUri;
    }

    /// <summary>The configured server base URI.</summary>
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

        return SendAsync<VersionListBody, IReadOnlyList<HonuaVersionInfo>>(
            () => new HttpRequestMessage(HttpMethod.Get, path),
            contract,
            body => body.Versions ?? [],
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
        return SendAsync<CreateVersionBody, HonuaVersionInfo>(
            () => Form($"{Base(serviceId)}/create", form),
            contract,
            body => body.VersionInfo ?? new HonuaVersionInfo(),
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
        return SendAsync<HonuaVersionMomentResult, HonuaVersionMomentResult>(
            () => Form($"{Base(serviceId)}/versions/{Uri.EscapeDataString(versionGuid)}/alter", form),
            contract,
            body => body,
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
        return SendAsync<HonuaVersionMomentResult, HonuaVersionMomentResult>(
            () => Form($"{Base(serviceId)}/versions/{Uri.EscapeDataString(versionGuid)}/delete", form),
            contract,
            body => body,
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
        return SendAsync<HonuaReconcileResult, HonuaReconcileResult>(
            () => Form($"{Base(serviceId)}/versions/{Uri.EscapeDataString(versionGuid)}/reconcile", form),
            contract,
            body => body,
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
        return SendAsync<HonuaInspectConflictsResult, HonuaInspectConflictsResult>(
            () => new HttpRequestMessage(HttpMethod.Get, path),
            contract,
            body => body,
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
        return SendAsync<HonuaResolveConflictsResult, HonuaResolveConflictsResult>(
            () => Form($"{Base(serviceId)}/versions/{Uri.EscapeDataString(versionGuid)}/resolveConflicts", form),
            contract,
            body => body,
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
        return SendAsync<HonuaPostResult, HonuaPostResult>(
            () => Form($"{Base(serviceId)}/versions/{Uri.EscapeDataString(versionGuid)}/post", form),
            contract,
            body => body,
            cancellationToken);
    }

    private static HttpRequestMessage Form(string path, IReadOnlyDictionary<string, string> form) =>
        new(HttpMethod.Post, path) { Content = new FormUrlEncodedContent(form) };

    private async Task<HonuaAdminEndpointResult<TResult>> SendAsync<TBody, TResult>(
        Func<HttpRequestMessage> requestFactory,
        string contract,
        Func<TBody, TResult> project,
        CancellationToken cancellationToken)
    {
        using var request = requestFactory();
        request.Headers.TryAddWithoutValidation("Accept", "application/json");
        if (!string.IsNullOrWhiteSpace(_apiKey))
        {
            request.Headers.TryAddWithoutValidation("X-API-Key", _apiKey);
        }

        HttpResponseMessage response;
        try
        {
            response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            return Unavailable<TResult>(contract, ex.Message);
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            return Unavailable<TResult>(contract, ex.Message);
        }

        using (response)
        {
            var payload = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                return HonuaAdminEndpointResult<TResult>.FromIssue(CreateIssue(contract, response.StatusCode, payload));
            }

            TBody? body;
            try
            {
                body = string.IsNullOrWhiteSpace(payload)
                    ? default
                    : JsonSerializer.Deserialize<TBody>(payload, JsonOptions);
            }
            catch (JsonException ex)
            {
                return HonuaAdminEndpointResult<TResult>.FromIssue(new HonuaAdminEndpointIssue(
                    "Unsupported",
                    contract,
                    $"The Honua server VersionManagementServer response did not match the expected contract: {ex.Message}",
                    (int)response.StatusCode));
            }

            if (body is null)
            {
                return HonuaAdminEndpointResult<TResult>.FromIssue(new HonuaAdminEndpointIssue(
                    "Unavailable",
                    contract,
                    "The Honua server VersionManagementServer response did not include a body.",
                    (int)response.StatusCode));
            }

            return HonuaAdminEndpointResult<TResult>.FromData(project(body));
        }
    }

    private static HonuaAdminEndpointResult<T> Unavailable<T>(string contract, string detail) =>
        HonuaAdminEndpointResult<T>.FromIssue(new HonuaAdminEndpointIssue(
            "Unavailable", contract, $"The Honua server endpoint could not be reached: {detail}"));

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

    // Extracts the actionable rejection text from a GeoServices error body ({ error: { message, details[] } })
    // or an RFC-7807 ProblemDetails body, so a validation/conflict rejection reaches the operator verbatim.
    private static string? ParseFailureMessage(string? payload)
    {
        if (string.IsNullOrWhiteSpace(payload))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(payload);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            if (document.RootElement.TryGetProperty("error", out var error)
                && error.ValueKind == JsonValueKind.Object
                && error.TryGetProperty("message", out var errorMessage)
                && errorMessage.ValueKind == JsonValueKind.String
                && errorMessage.GetString() is { Length: > 0 } esriMessage)
            {
                return esriMessage;
            }

            foreach (var property in new[] { "message", "detail", "title" })
            {
                if (document.RootElement.TryGetProperty(property, out var value)
                    && value.ValueKind == JsonValueKind.String
                    && value.GetString() is { Length: > 0 } message)
                {
                    return message;
                }
            }

            return null;
        }
        catch (JsonException)
        {
            return null;
        }
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

    /// <summary>Disposes the underlying <see cref="HttpClient"/>.</summary>
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
