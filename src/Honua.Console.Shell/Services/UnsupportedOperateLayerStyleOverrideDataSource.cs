using Honua.Console.Shell.Models;

namespace Honua.Console.Shell.Services;

/// <summary>
/// Missing-binding implementation of <see cref="IOperateLayerStyleOverrideDataSource"/>. The per-slot
/// style/popup override contract is not yet exposed by honua-server, so the merged build registers this
/// source: the override read and write return an explicit missing-binding state and never fabricate
/// per-slot overrides (Console Patterns Charter section 11). The style editor page still reads the REAL
/// /ogc/styles list through <see cref="IStudioMapStyleCatalogDataSource"/>; only the override portion is
/// unbound. Mirrors <see cref="UnsupportedOperateAlertRulesDataSource"/>.
/// </summary>
public sealed class UnsupportedOperateLayerStyleOverrideDataSource : IOperateLayerStyleOverrideDataSource
{
    internal const string Surface = "Resource presentation overrides";

    // Slot-presentation overrides ride the resource presentation tab / service publication slot contract.
    internal const string Contract = "honua-server resource-presentation slot overrides (pending)";

    internal const string Detail =
        "Per-publication-slot style and popup overrides bind to the server-owned resource-presentation slot "
        + "contract, which honua-server does not yet expose. Configure Honua:Server:BaseUrl (or "
        + "HONUA_SERVER_BASE_URL) and wait for that contract to land. The available base styles below come "
        + "from the live /ogc/styles list; Console will not fabricate per-slot overrides.";

    private static readonly OperateLayerStyleBindingState MissingBinding =
        new(Surface, OperateLayerStyleBindingState.MissingBinding, Contract, Detail);

    public Task<OperateLayerStyleOverrideView> GetOverridesAsync(
        string resourceId,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(new OperateLayerStyleOverrideView(resourceId, [], MissingBinding));

    public Task<OperateLayerStyleOverrideSaveResult> SaveOverrideAsync(
        OperateLayerSlotStyleOverrideEdit edit,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(edit);
        return Task.FromResult(OperateLayerStyleOverrideSaveResult.Blocked(MissingBinding));
    }
}
