using Honua.Console.Shell.Models;

namespace Honua.Console.Shell.Services;

/// <summary>
/// Authors a layer's editing policy on honua-server
/// (<c>PUT /api/v1/admin/services/{serviceName}/layers/{layerId}/metadata</c>).
/// </summary>
public interface IConsoleLayerMetadataOperation
{
    Task<ConsoleSetLayerMetadataResult> SetEditingAsync(
        ConsoleLayerEditingChange change,
        CancellationToken cancellationToken = default);
}
