using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Honua.Console.Contracts;

// SHIM(honua-server#1162): honua-server owns the Console metadata/RBAC contract — the hierarchical
// scope model (workspace > environment > content > publication > resource-field), the role definitions
// and their per-permission grants, the workspace membership roster, and pending invitations. These are
// projected through the shared Console metadata/RBAC API in Honua.Server.Features.Console. honua-sdk-dotnet
// does not yet project these as a consumable stable package, and honua-console wires no SDK NuGet feed
// (see SDK_SHIM_POLICY.md and the share/operate/content-publication shims). Per the Console Patterns
// Charter section 11 and SDK_SHIM_POLICY, the Console Access (RBAC) surface binds through a thin HttpClient
// behind this single Honua.Console.Contracts boundary: the wire records below mirror the server projection
// and the client speaks the real Console RBAC read surface. Do not add a sibling-repo ProjectReference.
// Swap these for SDK types when honua-sdk-dotnet ships a consumable Console RBAC projection.
//
// The Console Access reads are admin reads wrapped in the shared {success,data,message,timestamp} envelope
// (Honua.Infrastructure.Models.ApiResponse<T>), so this client reuses the shared HonuaAdminApiResponse<T>
// envelope and the HonuaAdminEndpointResult<T>/HonuaAdminEndpointIssue result records (defined in
// OperateAdminShims.cs). When no server base URL is configured, the Console Access surface renders an
// explicit missing-binding state instead of fabricating role/member data.

/// <summary>
/// The five scope levels of the Console RBAC hierarchy, ordered from broadest to narrowest. Workspace roles
/// inherit down; overrides at any narrower level take precedence (mirrors honua-server ConsoleAccessScope).
/// </summary>
public static class HonuaConsoleAccessScopeLevels
{
    public const string Workspace = "workspace";
    public const string Environment = "environment";
    public const string Content = "content";
    public const string Publication = "publication";
    public const string ResourceField = "resource-field";
}

/// <summary>
/// How a role grants a permission: a full grant, an environment-scoped grant, a per-content scope-only
/// grant, a conditional/custom grant, or no grant (mirrors honua-server ConsolePermissionGrant).
/// </summary>
public static class HonuaConsolePermissionGrants
{
    public const string Granted = "granted";
    public const string EnvScoped = "env-scoped";
    public const string Scoped = "scoped";
    public const string Custom = "custom";
    public const string NotGranted = "not-granted";
}

public sealed record HonuaConsoleRbacClientOptions(Uri BaseUri, string? ApiKey = null);

/// <summary>
/// Typed client for the honua-server Console Access (RBAC) surface (honua-server#1162). All reads target
/// the workspace-scoped admin routes under <c>/api/v1/console/access</c>.
/// </summary>
public interface IHonuaConsoleRbacClient
{
    Uri BaseUri { get; }

    /// <summary>Reads the server-owned role × permission overview (scope hierarchy, roles, matrix legend).</summary>
    Task<HonuaAdminEndpointResult<HonuaConsoleRbacOverview>> GetOverviewAsync(
        string workspaceId,
        CancellationToken cancellationToken = default);

    /// <summary>Reads the server-owned workspace membership roster plus pending invitations.</summary>
    Task<HonuaAdminEndpointResult<HonuaConsoleTeamMembership>> GetMembershipAsync(
        string workspaceId,
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

    private readonly HttpClient _httpClient;
    private readonly string? _apiKey;

    public HonuaConsoleRbacHttpClient(HttpClient httpClient, HonuaConsoleRbacClientOptions options)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(options);

        BaseUri = options.BaseUri;
        _apiKey = options.ApiKey;
        _httpClient = httpClient;
        _httpClient.BaseAddress ??= BaseUri;
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

    public void Dispose() => _httpClient.Dispose();

    private async Task<HonuaAdminEndpointResult<T>> SendAsync<T>(
        string path,
        string contract,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        if (!string.IsNullOrWhiteSpace(_apiKey))
        {
            request.Headers.TryAddWithoutValidation("X-API-Key", _apiKey);
        }

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
            return Unavailable<T>(contract, ex.Message);
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            return Unavailable<T>(contract, ex.Message);
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                var state = response.StatusCode switch
                {
                    System.Net.HttpStatusCode.Unauthorized or System.Net.HttpStatusCode.Forbidden => "Missing permission",
                    System.Net.HttpStatusCode.NotFound or System.Net.HttpStatusCode.MethodNotAllowed or System.Net.HttpStatusCode.NotImplemented => "Unsupported",
                    _ => "Unavailable"
                };
                var detail = response.StatusCode switch
                {
                    System.Net.HttpStatusCode.Unauthorized => "The Honua server rejected the request because admin authentication is missing.",
                    System.Net.HttpStatusCode.Forbidden => "The current principal lacks permission to read workspace access (manage-roles).",
                    System.Net.HttpStatusCode.NotFound => "The Honua server does not expose the Console Access (RBAC) contract for this workspace.",
                    _ => $"The Honua server returned HTTP {(int)response.StatusCode} ({response.StatusCode})."
                };
                return HonuaAdminEndpointResult<T>.FromIssue(
                    new HonuaAdminEndpointIssue(state, contract, detail, (int)response.StatusCode));
            }

            HonuaAdminApiResponse<T>? envelope;
            try
            {
                envelope = await response.Content
                    .ReadFromJsonAsync<HonuaAdminApiResponse<T>>(JsonOptions, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (JsonException ex)
            {
                return HonuaAdminEndpointResult<T>.FromIssue(new HonuaAdminEndpointIssue(
                    "Unsupported",
                    contract,
                    $"The Honua server Console Access response did not match the expected contract: {ex.Message}",
                    (int)response.StatusCode));
            }

            if (envelope?.Success == true && envelope.Data is not null)
            {
                return HonuaAdminEndpointResult<T>.FromData(envelope.Data);
            }

            return HonuaAdminEndpointResult<T>.FromIssue(new HonuaAdminEndpointIssue(
                "Unavailable",
                contract,
                envelope?.Message ?? "The Honua server Console Access response did not include data.",
                (int)response.StatusCode));
        }
    }

    private static HonuaAdminEndpointResult<T> Unavailable<T>(string contract, string detail) =>
        HonuaAdminEndpointResult<T>.FromIssue(new HonuaAdminEndpointIssue(
            "Unavailable",
            contract,
            $"The Honua server Console Access endpoint could not be reached: {detail}"));
}

// --- Wire records mirroring honua-server Honua.Core.Features.Console.Domain (#1162). ---

/// <summary>One level of the Console RBAC scope hierarchy as projected by the server.</summary>
public sealed record HonuaConsoleAccessScope
{
    [JsonPropertyName("level")]
    public required string Level { get; init; }

    [JsonPropertyName("label")]
    public required string Label { get; init; }

    [JsonPropertyName("description")]
    public string? Description { get; init; }
}

/// <summary>One permission column in the role × permission matrix.</summary>
public sealed record HonuaConsoleRbacPermission
{
    [JsonPropertyName("key")]
    public required string Key { get; init; }

    [JsonPropertyName("label")]
    public required string Label { get; init; }
}

/// <summary>How a role grants a single permission, with an optional qualifier label (e.g. "no PII").</summary>
public sealed record HonuaConsoleRbacGrant
{
    [JsonPropertyName("permission")]
    public required string Permission { get; init; }

    [JsonPropertyName("grant")]
    public required string Grant { get; init; }

    [JsonPropertyName("qualifier")]
    public string? Qualifier { get; init; }
}

/// <summary>A single role row in the matrix (built-in or custom) with its per-permission grants.</summary>
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

/// <summary>
/// The Console Access (RBAC) overview projection: the scope hierarchy strip, the permission columns, the
/// role rows with their grants, and the aggregate counts the toolbar surfaces.
/// </summary>
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

    /// <summary>Whether the caller may author/clone roles (manage-roles permission at workspace scope).</summary>
    [JsonPropertyName("canManageRoles")]
    public bool CanManageRoles { get; init; }
}

/// <summary>A workspace member row: identity, role, scope, last-active, and identity source.</summary>
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

    /// <summary>Principal kind: "user", "api-key", or "group".</summary>
    [JsonPropertyName("principalKind")]
    public string? PrincipalKind { get; init; }
}

/// <summary>A pending invitation row awaiting acceptance.</summary>
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

/// <summary>
/// The Console Access membership projection: the active/pending/deactivated counts, the member roster, and
/// the pending invitations, plus whether the caller may issue new scoped invites.
/// </summary>
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

    /// <summary>Whether the caller may issue invitations (edit-access permission at workspace scope).</summary>
    [JsonPropertyName("canInvite")]
    public bool CanInvite { get; init; }
}
