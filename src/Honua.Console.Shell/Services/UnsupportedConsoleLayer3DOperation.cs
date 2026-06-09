using Honua.Console.Shell.Models;

namespace Honua.Console.Shell.Services;

/// <summary>
/// Missing-binding implementation of the layer 3D-extrusion / 3D-symbology + lifecycle-status authoring
/// operation. Used when no honua-server base URL is configured: it performs no network call and returns
/// explicit missing-binding results.
/// </summary>
public sealed class UnsupportedConsoleLayer3DOperation : IConsoleLayer3DOperation
{
    private const string BindingDetail =
        "Configure Honua:Server:BaseUrl or HONUA_SERVER_BASE_URL so the console can read and author a layer's 3D extrusion / symbology and lifecycle status on honua-server.";

    public Task<ConsoleLayer3D> GetExtrusionAsync(int layerId, CancellationToken cancellationToken = default) =>
        Task.FromResult(ConsoleLayer3D.Unbound(BindingDetail));

    public Task<ConsoleSetLayerMetadataResult> SetExtrusionAsync(
        int layerId,
        ConsoleLayerExtrusionSettings? extrusion,
        bool clearExtrusion,
        ConsoleSymbology3D? symbology3D,
        bool clearSymbology3D,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(ConsoleSetLayerMetadataResult.MissingBinding(BindingDetail));

    public Task<ConsoleLayerStatus> GetStatusAsync(int layerId, CancellationToken cancellationToken = default) =>
        Task.FromResult(ConsoleLayerStatus.Unbound(BindingDetail));

    public Task<ConsoleSetLayerMetadataResult> SetStatusAsync(
        int layerId,
        string? lifecycle,
        string? state,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(ConsoleSetLayerMetadataResult.MissingBinding(BindingDetail));
}
