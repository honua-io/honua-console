using Honua.Sdk.Admin.Models;

namespace Honua.Console.Contracts;

public sealed record HonuaAdminApiKeyCommand<T>(string AuthorityBinding, T Value);

public partial interface IHonuaAdminOperateClient
{
    Task<HonuaAdminEndpointResult<AdminApiKeyResponse[]>> ListApiKeysBoundAsync(string binding,
        CancellationToken cancellationToken = default) => Task.FromResult(
            HonuaAdminEndpointResult<AdminApiKeyResponse[]>.FromIssue(new("Unsupported", "API keys", "Operator-bound API-key management is unavailable.")));

    Task<HonuaAdminEndpointResult<AdminApiKeySecretResponse>> CreateApiKeyAsync(HonuaAdminApiKeyCommand<CreateAdminApiKeyRequest> command,
        CancellationToken cancellationToken = default) => Task.FromResult(
            HonuaAdminEndpointResult<AdminApiKeySecretResponse>.FromIssue(new("Unsupported", "API keys", "API-key creation is unavailable.")));

    Task<HonuaAdminEndpointResult<AdminApiKeyResponse>> RevokeApiKeyAsync(HonuaAdminApiKeyCommand<Guid> command,
        CancellationToken cancellationToken = default) => Task.FromResult(
            HonuaAdminEndpointResult<AdminApiKeyResponse>.FromIssue(new("Unsupported", "API keys", "API-key revocation is unavailable.")));
}

public sealed partial class HonuaAdminOperateHttpClient
{
    private const string ApiKeysPath = "/api/v1/admin/api-keys/";

    public Task<HonuaAdminEndpointResult<AdminApiKeyResponse[]>> ListApiKeysBoundAsync(string binding,
        CancellationToken cancellationToken = default) => SendEnvelopeAsync<AdminApiKeyResponse[]>(
            () => BoundKeyRequest(Request(HttpMethod.Get, ApiKeysPath), binding), "GET /api/v1/admin/api-keys", cancellationToken);

    public Task<HonuaAdminEndpointResult<AdminApiKeySecretResponse>> CreateApiKeyAsync(HonuaAdminApiKeyCommand<CreateAdminApiKeyRequest> command,
        CancellationToken cancellationToken = default) => SendEnvelopeAsync<AdminApiKeySecretResponse>(
            () => BoundKeyRequest(JsonRequest(HttpMethod.Post, ApiKeysPath, command.Value), command.AuthorityBinding),
            "POST /api/v1/admin/api-keys", cancellationToken);

    public Task<HonuaAdminEndpointResult<AdminApiKeyResponse>> RevokeApiKeyAsync(HonuaAdminApiKeyCommand<Guid> command,
        CancellationToken cancellationToken = default) => SendEnvelopeAsync<AdminApiKeyResponse>(
            () => BoundKeyRequest(Request(HttpMethod.Post, $"{ApiKeysPath}{command.Value:D}/revoke"), command.AuthorityBinding),
            "POST /api/v1/admin/api-keys/{id}/revoke", cancellationToken);

    private static HttpRequestMessage BoundKeyRequest(HttpRequestMessage request, string binding)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(binding);
        request.Options.Set(ConsoleAuthorityRequestOptions.Expected, binding);
        return request;
    }
}
