using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Honua.Console.Contracts;

// Workspace roles inherit downward; a narrower scope overrides them.
public static class HonuaConsoleAccessScopeLevels
{
    public const string Workspace = "workspace";
    public const string Environment = "environment";
    public const string Content = "content";
    public const string Publication = "publication";
    public const string ResourceField = "resource-field";
}

public static class HonuaConsolePermissionGrants
{
    public const string Granted = "granted";
    public const string EnvScoped = "env-scoped";
    public const string Scoped = "scoped";
    public const string Custom = "custom";
    public const string NotGranted = "not-granted";
}

public sealed record HonuaConsoleRbacClientOptions(Uri BaseUri, string? ApiKey = null);

public interface IHonuaConsoleRbacClient
{
    Uri BaseUri { get; }

    Task<HonuaAdminEndpointResult<HonuaConsoleRbacOverview>> GetOverviewAsync(
        string workspaceId,
        CancellationToken cancellationToken = default);

    Task<HonuaAdminEndpointResult<HonuaConsoleTeamMembership>> GetMembershipAsync(
        string workspaceId,
        CancellationToken cancellationToken = default);

    Task<HonuaAdminEndpointResult<HonuaConsoleRbacRole>> CreateRoleAsync(
        string workspaceId,
        HonuaConsoleRoleWriteRequest request,
        CancellationToken cancellationToken = default);

    Task<HonuaAdminEndpointResult<HonuaConsoleRbacRole>> UpdateRoleAsync(
        string workspaceId,
        string roleId,
        HonuaConsoleRoleWriteRequest request,
        CancellationToken cancellationToken = default);

    Task<HonuaAdminEndpointResult<bool>> DeleteRoleAsync(
        string workspaceId,
        string roleId,
        CancellationToken cancellationToken = default);

    Task<HonuaAdminEndpointResult<HonuaConsoleRoleAuditPage>> GetRoleAuditAsync(
        string workspaceId,
        int pageSize = 50,
        CancellationToken cancellationToken = default);
}

public sealed class HonuaConsoleRbacHttpClient : IHonuaConsoleRbacClient, IDisposable
{
    private const string Base = "/api/v1/console/access";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private static readonly AdminBodyFormat EnvelopeFormat = new(
        JsonOptions,
        AdminBodyRead.Streamed,
        "The Honua server Console Access response did not match the expected contract");

    private const string MissingData = "The Honua server Console Access response did not include data.";

    private readonly HttpClient _httpClient;
    private readonly AdminEndpointTransport _transport;

    public HonuaConsoleRbacHttpClient(HttpClient httpClient, HonuaConsoleRbacClientOptions options)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(options);

        BaseUri = options.BaseUri;
        _httpClient = httpClient;
        _httpClient.BaseAddress ??= BaseUri;
        _transport = new AdminEndpointTransport(
            httpClient,
            options.ApiKey,
            AdminTransportFailure.Unreachable("The Honua server Console Access endpoint"));
    }

    public Uri BaseUri { get; }

    public Task<HonuaAdminEndpointResult<HonuaConsoleRbacOverview>> GetOverviewAsync(
        string workspaceId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceId);

        return SendAsync<HonuaConsoleRbacOverview>(
            $"{Base}/{Uri.EscapeDataString(workspaceId)}/roles",
            "GET /api/v1/console/access/{workspaceId}/roles",
            cancellationToken);
    }

    public Task<HonuaAdminEndpointResult<HonuaConsoleTeamMembership>> GetMembershipAsync(
        string workspaceId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceId);

        return SendAsync<HonuaConsoleTeamMembership>(
            $"{Base}/{Uri.EscapeDataString(workspaceId)}/members",
            "GET /api/v1/console/access/{workspaceId}/members",
            cancellationToken);
    }

    public Task<HonuaAdminEndpointResult<HonuaConsoleRbacRole>> CreateRoleAsync(
        string workspaceId,
        HonuaConsoleRoleWriteRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceId);
        ArgumentNullException.ThrowIfNull(request);

        return SendWithBodyAsync<HonuaConsoleRbacRole>(
            () => JsonRequest(HttpMethod.Post, $"{Base}/{Uri.EscapeDataString(workspaceId)}/roles", request),
            "POST /api/v1/console/access/{workspaceId}/roles",
            cancellationToken);
    }

    public Task<HonuaAdminEndpointResult<HonuaConsoleRbacRole>> UpdateRoleAsync(
        string workspaceId,
        string roleId,
        HonuaConsoleRoleWriteRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceId);
        ArgumentException.ThrowIfNullOrWhiteSpace(roleId);
        ArgumentNullException.ThrowIfNull(request);

        return SendWithBodyAsync<HonuaConsoleRbacRole>(
            () => JsonRequest(HttpMethod.Put, $"{Base}/{Uri.EscapeDataString(workspaceId)}/roles/{Uri.EscapeDataString(roleId)}", request),
            "PUT /api/v1/console/access/{workspaceId}/roles/{roleId}",
            cancellationToken);
    }

    public async Task<HonuaAdminEndpointResult<bool>> DeleteRoleAsync(
        string workspaceId,
        string roleId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceId);
        ArgumentException.ThrowIfNullOrWhiteSpace(roleId);

        var path = $"{Base}/{Uri.EscapeDataString(workspaceId)}/roles/{Uri.EscapeDataString(roleId)}";
        var call = new AdminEndpointCall<bool>(
            () => new HttpRequestMessage(HttpMethod.Delete, path),
            "DELETE /api/v1/console/access/{workspaceId}/roles/{roleId}",
            new(
                AdminFailureMappings.FromStatus(CreateWriteIssue),
                static (_, _, _) => Task.FromResult(HonuaAdminEndpointResult<bool>.FromData(true))));
        return await _transport.SendAsync(call, cancellationToken).ConfigureAwait(false);
    }

    public Task<HonuaAdminEndpointResult<HonuaConsoleRoleAuditPage>> GetRoleAuditAsync(
        string workspaceId,
        int pageSize = 50,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceId);

        return SendAsync<HonuaConsoleRoleAuditPage>(
            $"{Base}/{Uri.EscapeDataString(workspaceId)}/roles/audit?pageSize={pageSize}",
            "GET /api/v1/console/access/{workspaceId}/roles/audit",
            cancellationToken);
    }

    public void Dispose() => _httpClient.Dispose();

    private static HttpRequestMessage JsonRequest(HttpMethod method, string path, object body) =>
        new(method, path) { Content = JsonContent.Create(body, body.GetType(), options: JsonOptions) };

    private Task<HonuaAdminEndpointResult<T>> SendWithBodyAsync<T>(
        Func<HttpRequestMessage> createRequest,
        string contract,
        CancellationToken cancellationToken) =>
        _transport.SendAsync(
            new AdminEndpointCall<T>(
                createRequest,
                contract,
                new(
                    AdminFailureMappings.FromStatus(CreateWriteIssue),
                    AdminResponseBodies.Envelope<T>(EnvelopeFormat, MissingData))),
            cancellationToken);

    private Task<HonuaAdminEndpointResult<T>> SendAsync<T>(
        string path,
        string contract,
        CancellationToken cancellationToken) =>
        _transport.SendAsync(
            new AdminEndpointCall<T>(
                () => new HttpRequestMessage(HttpMethod.Get, path),
                contract,
                new(
                    AdminFailureMappings.FromStatus(CreateReadIssue),
                    AdminResponseBodies.Envelope<T>(EnvelopeFormat, MissingData)))
            {
                Completion = HttpCompletionOption.ResponseHeadersRead
            },
            cancellationToken);

    private static string ReadState(HttpStatusCode status) => status switch
    {
        HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => "Missing permission",
        HttpStatusCode.NotFound or HttpStatusCode.MethodNotAllowed or HttpStatusCode.NotImplemented => "Unsupported",
        _ => "Unavailable"
    };

    private static HonuaAdminEndpointIssue CreateReadIssue(string contract, HttpStatusCode status)
    {
        var detail = status switch
        {
            HttpStatusCode.Unauthorized => "The Honua server rejected the request because admin authentication is missing.",
            HttpStatusCode.Forbidden => "The current principal lacks permission to read workspace access (manage-roles).",
            HttpStatusCode.NotFound => "The Honua server does not expose the Console Access (RBAC) contract for this workspace.",
            _ => $"The Honua server returned HTTP {(int)status} ({status})."
        };
        return new HonuaAdminEndpointIssue(ReadState(status), contract, detail, (int)status);
    }

    private static HonuaAdminEndpointIssue CreateWriteIssue(string contract, HttpStatusCode status)
    {
        var state = status == HttpStatusCode.BadRequest ? "Rejected" : ReadState(status);
        var detail = status switch
        {
            HttpStatusCode.Unauthorized => "The Honua server rejected the request because admin authentication is missing.",
            HttpStatusCode.Forbidden => "The current principal lacks permission to manage workspace roles.",
            HttpStatusCode.BadRequest => "The Honua server rejected the role change (e.g. blank name, or a built-in role cannot be edited or deleted).",
            HttpStatusCode.NotFound => "The role was not found, or the Console Access contract is not exposed for this workspace.",
            _ => $"The Honua server returned HTTP {(int)status} ({status})."
        };
        return new HonuaAdminEndpointIssue(state, contract, detail, (int)status);
    }
}

public sealed record HonuaConsoleRoleWriteRequest
{
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    [JsonPropertyName("description")]
    public string? Description { get; init; }

    [JsonPropertyName("grants")]
    public IReadOnlyList<HonuaConsoleRbacGrant> Grants { get; init; } = Array.Empty<HonuaConsoleRbacGrant>();
}

public sealed record HonuaConsoleRoleAuditEntry
{
    [JsonPropertyName("id")]
    public long Id { get; init; }

    [JsonPropertyName("timestamp")]
    public string? Timestamp { get; init; }

    [JsonPropertyName("actor")]
    public string? Actor { get; init; }

    [JsonPropertyName("action")]
    public string? Action { get; init; }

    [JsonPropertyName("roleId")]
    public string? RoleId { get; init; }

    [JsonPropertyName("outcome")]
    public string? Outcome { get; init; }

    [JsonPropertyName("details")]
    public string? Details { get; init; }
}

public sealed record HonuaConsoleRoleAuditPage
{
    [JsonPropertyName("entries")]
    public IReadOnlyList<HonuaConsoleRoleAuditEntry> Entries { get; init; } = Array.Empty<HonuaConsoleRoleAuditEntry>();

    [JsonPropertyName("nextCursor")]
    public string? NextCursor { get; init; }
}

public sealed record HonuaConsoleAccessScope
{
    [JsonPropertyName("level")]
    public required string Level { get; init; }

    [JsonPropertyName("label")]
    public required string Label { get; init; }

    [JsonPropertyName("description")]
    public string? Description { get; init; }
}

public sealed record HonuaConsoleRbacPermission
{
    [JsonPropertyName("key")]
    public required string Key { get; init; }

    [JsonPropertyName("label")]
    public required string Label { get; init; }
}

public sealed record HonuaConsoleRbacGrant
{
    [JsonPropertyName("permission")]
    public required string Permission { get; init; }

    [JsonPropertyName("grant")]
    public required string Grant { get; init; }

    [JsonPropertyName("qualifier")]
    public string? Qualifier { get; init; }
}

public sealed record HonuaConsoleRbacRole
{
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    [JsonPropertyName("name")]
    public required string Name { get; init; }

    [JsonPropertyName("description")]
    public string? Description { get; init; }

    [JsonPropertyName("isCustom")]
    public bool IsCustom { get; init; }

    [JsonPropertyName("grants")]
    public IReadOnlyList<HonuaConsoleRbacGrant> Grants { get; init; } = Array.Empty<HonuaConsoleRbacGrant>();
}

public sealed record HonuaConsoleRbacOverview
{
    [JsonPropertyName("workspaceId")]
    public required string WorkspaceId { get; init; }

    [JsonPropertyName("workspaceName")]
    public string? WorkspaceName { get; init; }

    [JsonPropertyName("scopes")]
    public IReadOnlyList<HonuaConsoleAccessScope> Scopes { get; init; } = Array.Empty<HonuaConsoleAccessScope>();

    [JsonPropertyName("permissions")]
    public IReadOnlyList<HonuaConsoleRbacPermission> Permissions { get; init; } = Array.Empty<HonuaConsoleRbacPermission>();

    [JsonPropertyName("roles")]
    public IReadOnlyList<HonuaConsoleRbacRole> Roles { get; init; } = Array.Empty<HonuaConsoleRbacRole>();

    [JsonPropertyName("builtInRoleCount")]
    public int BuiltInRoleCount { get; init; }

    [JsonPropertyName("customRoleCount")]
    public int CustomRoleCount { get; init; }

    [JsonPropertyName("membersAffected")]
    public int MembersAffected { get; init; }

    [JsonPropertyName("canManageRoles")]
    public bool CanManageRoles { get; init; }
}

public sealed record HonuaConsoleTeamMember
{
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    [JsonPropertyName("displayName")]
    public required string DisplayName { get; init; }

    [JsonPropertyName("identity")]
    public string? Identity { get; init; }

    [JsonPropertyName("roleId")]
    public string? RoleId { get; init; }

    [JsonPropertyName("roleName")]
    public required string RoleName { get; init; }

    [JsonPropertyName("isCustomRole")]
    public bool IsCustomRole { get; init; }

    [JsonPropertyName("scope")]
    public string? Scope { get; init; }

    [JsonPropertyName("lastActive")]
    public string? LastActive { get; init; }

    [JsonPropertyName("source")]
    public string? Source { get; init; }

    // One of "user", "api-key", or "group".
    [JsonPropertyName("principalKind")]
    public string? PrincipalKind { get; init; }
}

public sealed record HonuaConsoleTeamInvitation
{
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    [JsonPropertyName("identity")]
    public required string Identity { get; init; }

    [JsonPropertyName("roleName")]
    public required string RoleName { get; init; }

    [JsonPropertyName("isCustomRole")]
    public bool IsCustomRole { get; init; }

    [JsonPropertyName("scope")]
    public string? Scope { get; init; }

    [JsonPropertyName("status")]
    public string? Status { get; init; }
}

public sealed record HonuaConsoleTeamMembership
{
    [JsonPropertyName("workspaceId")]
    public required string WorkspaceId { get; init; }

    [JsonPropertyName("workspaceName")]
    public string? WorkspaceName { get; init; }

    [JsonPropertyName("members")]
    public IReadOnlyList<HonuaConsoleTeamMember> Members { get; init; } = Array.Empty<HonuaConsoleTeamMember>();

    [JsonPropertyName("invitations")]
    public IReadOnlyList<HonuaConsoleTeamInvitation> Invitations { get; init; } = Array.Empty<HonuaConsoleTeamInvitation>();

    [JsonPropertyName("activeCount")]
    public int ActiveCount { get; init; }

    [JsonPropertyName("pendingCount")]
    public int PendingCount { get; init; }

    [JsonPropertyName("deactivatedCount")]
    public int DeactivatedCount { get; init; }

    [JsonPropertyName("canInvite")]
    public bool CanInvite { get; init; }
}
