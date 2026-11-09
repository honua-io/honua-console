using System.Text.Json;

namespace Honua.Console.Contracts;

public sealed partial class HonuaAdminOperateHttpClient
{
    // SHIM(honua-sdk-dotnet#166): The published manifest projection omits the installed data-store inventory.
    public Task<HonuaAdminEndpointResult<JsonElement>> GetConnectionProviderManifestAsync(
        CancellationToken cancellationToken = default) =>
        _transport.SendAsync(new AdminEndpointCall<JsonElement>(
            () => Request(HttpMethod.Get, "/api/v1/capabilities/manifest"),
            "GET /api/v1/capabilities/manifest",
            new(AdminFailureMappings.StatusDetail,
                AdminResponseBodies.Required<JsonElement>(
                    BareBody(AdminBodyRead.Streamed, "capability manifest"), MissingData))), cancellationToken);
}
