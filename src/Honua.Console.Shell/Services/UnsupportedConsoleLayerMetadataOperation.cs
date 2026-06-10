using Honua.Console.Shell.Models;

namespace Honua.Console.Shell.Services;

/// <summary>
/// Missing-binding implementation of the layer-metadata authoring operation. Used when no honua-server base
/// URL is configured: it performs no network call and returns explicit missing-binding results.
/// </summary>
public sealed class UnsupportedConsoleLayerMetadataOperation : IConsoleLayerMetadataOperation
{
    private const string BindingDetail =
        "Configure Honua:Server:BaseUrl or HONUA_SERVER_BASE_URL so the console can read and author layer display / editing / CRS metadata on honua-server.";

    public Task<ConsoleLayerDisplay> GetDisplayAsync(int layerId, CancellationToken cancellationToken = default) =>
        Task.FromResult(ConsoleLayerDisplay.Unbound(BindingDetail));

    public Task<ConsoleSetLayerMetadataResult> SetDisplayAsync(
        int layerId, ConsoleLayerDisplay display, CancellationToken cancellationToken = default) =>
        Task.FromResult(ConsoleSetLayerMetadataResult.MissingBinding(BindingDetail));

    public Task<ConsoleLayerEditing> GetEditingAsync(int layerId, CancellationToken cancellationToken = default) =>
        Task.FromResult(ConsoleLayerEditing.Unbound(BindingDetail));

    public Task<ConsoleSetLayerMetadataResult> SetEditingAsync(
        int layerId, ConsoleLayerEditing editing, CancellationToken cancellationToken = default) =>
        Task.FromResult(ConsoleSetLayerMetadataResult.MissingBinding(BindingDetail));

    public Task<ConsoleLayerSpatial> GetSpatialAsync(int layerId, CancellationToken cancellationToken = default) =>
        Task.FromResult(ConsoleLayerSpatial.Unbound(BindingDetail));

    public Task<ConsoleSetLayerMetadataResult> SetSpatialAsync(
        int layerId,
        IReadOnlyList<string>? supportedCrs,
        string? storageCrs,
        double? storageCrsCoordinateEpoch,
        bool clearStorageCrs,
        bool clearStorageCrsCoordinateEpoch,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(ConsoleSetLayerMetadataResult.MissingBinding(BindingDetail));
}
