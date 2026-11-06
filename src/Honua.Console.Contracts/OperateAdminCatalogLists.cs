namespace Honua.Console.Contracts;

public sealed partial class HonuaAdminOperateHttpClient
{
    public Task<HonuaAdminEndpointResult<HonuaAdminLayerPage>> ListConnectionLayersAsync(
        string connectionId,
        HonuaAdminLayerListRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionId);

        return GetEnvelopeAsync<HonuaAdminLayerPage>(
            request.Page.AppendTo(
                $"/api/v1/admin/connections/{Uri.EscapeDataString(connectionId)}/layers/",
                request.ServiceName),
            "GET /api/v1/admin/connections/{id}/layers",
            cancellationToken);
    }

    public Task<HonuaAdminEndpointResult<HonuaAdminServicePage>> ListServicesAsync(
        HonuaAdminCatalogQuery query,
        CancellationToken cancellationToken = default) =>
        GetEnvelopeAsync<HonuaAdminServicePage>(
            query.AppendTo("/api/v1/admin/services/"),
            "GET /api/v1/admin/services",
            cancellationToken);
}
