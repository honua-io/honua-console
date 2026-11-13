namespace Honua.Console.Contracts;

// SHIM(honua-sdk-dotnet#166): SDK 2.2.0 has no API-key management projection.
// Owner: s-catalog-ui; replace through honua-console#7 when the SDK exposes these routes.
public sealed record HonuaAdminCreateApiKeyRequest(string Name, IReadOnlyList<string> Permissions)
{
    public DateTimeOffset? ExpiresAt { get; init; }
}

public sealed record HonuaAdminCreatedApiKey(HonuaAdminApiKeyResponse ApiKey, string Key);
public sealed record HonuaAdminApiKeyCommand<T>(string AuthorityBinding, T Value);

public partial interface IHonuaAdminOperateClient
{
    Task<HonuaAdminEndpointResult<HonuaAdminApiKeyResponse[]>> ListApiKeysBoundAsync(string binding,
        CancellationToken cancellationToken = default) => Task.FromResult(
            HonuaAdminEndpointResult<HonuaAdminApiKeyResponse[]>.FromIssue(new("Unsupported", "API keys", "Operator-bound API-key management is unavailable.")));

    Task<HonuaAdminEndpointResult<HonuaAdminCreatedApiKey>> CreateApiKeyAsync(HonuaAdminApiKeyCommand<HonuaAdminCreateApiKeyRequest> command,
        CancellationToken cancellationToken = default) => Task.FromResult(
            HonuaAdminEndpointResult<HonuaAdminCreatedApiKey>.FromIssue(new("Unsupported", "API keys", "API-key creation is unavailable.")));

    Task<HonuaAdminEndpointResult<HonuaAdminApiKeyResponse>> RevokeApiKeyAsync(HonuaAdminApiKeyCommand<Guid> command,
        CancellationToken cancellationToken = default) => Task.FromResult(
            HonuaAdminEndpointResult<HonuaAdminApiKeyResponse>.FromIssue(new("Unsupported", "API keys", "API-key revocation is unavailable.")));
}

public sealed partial class HonuaAdminOperateHttpClient
{
    private const string ApiKeysPath = "/api/v1/admin/api-keys/";

    public Task<HonuaAdminEndpointResult<HonuaAdminApiKeyResponse[]>> ListApiKeysBoundAsync(string binding,
        CancellationToken cancellationToken = default) => SendEnvelopeAsync<HonuaAdminApiKeyResponse[]>(
            () => BoundKeyRequest(Request(HttpMethod.Get, ApiKeysPath), binding), "GET /api/v1/admin/api-keys", cancellationToken);

    public Task<HonuaAdminEndpointResult<HonuaAdminCreatedApiKey>> CreateApiKeyAsync(HonuaAdminApiKeyCommand<HonuaAdminCreateApiKeyRequest> command,
        CancellationToken cancellationToken = default) => SendEnvelopeAsync<HonuaAdminCreatedApiKey>(
            () => BoundKeyRequest(JsonRequest(HttpMethod.Post, ApiKeysPath, command.Value), command.AuthorityBinding),
            "POST /api/v1/admin/api-keys", cancellationToken);

    public Task<HonuaAdminEndpointResult<HonuaAdminApiKeyResponse>> RevokeApiKeyAsync(HonuaAdminApiKeyCommand<Guid> command,
        CancellationToken cancellationToken = default) => SendEnvelopeAsync<HonuaAdminApiKeyResponse>(
            () => BoundKeyRequest(Request(HttpMethod.Post, $"{ApiKeysPath}{command.Value:D}/revoke"), command.AuthorityBinding),
            "POST /api/v1/admin/api-keys/{id}/revoke", cancellationToken);

    private static HttpRequestMessage BoundKeyRequest(HttpRequestMessage request, string binding)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(binding);
        request.Options.Set(ConsoleAuthorityRequestOptions.Expected, binding);
        return request;
    }
}
