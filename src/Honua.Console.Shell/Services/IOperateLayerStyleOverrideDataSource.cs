using Honua.Console.Shell.Models;

namespace Honua.Console.Shell.Services;

public interface IOperateLayerStyleOverrideDataSource
{
    Task<OperateLayerStyleOverrideView> GetOverridesAsync(
        string resourceId,
        CancellationToken cancellationToken = default);

    Task<OperateLayerStyleOverrideSaveResult> SaveOverrideAsync(
        OperateLayerSlotStyleOverrideEdit edit,
        CancellationToken cancellationToken = default);
}
