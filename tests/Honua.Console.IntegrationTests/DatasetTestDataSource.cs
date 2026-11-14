using Honua.Console.Shell.Models;
using Honua.Console.Shell.Services;

namespace Honua.Console.IntegrationTests;

internal sealed class DatasetDataSource(int extraDrafts = 0) : IOperateTransitionDataSource
{
    private static OperateResourceEditPreview Resource(string id) => new(id, id, "file", "private_change", "Review required",
        new OperateBlastRadius([], ["Public parcels"], [], [], [], []),
        [new("Warning", "owner", "Check field values", "Review source")], ["Fields", "Preview"]);
    private readonly OperateTransitionWorkspace _workspace = new([],
        [Resource("parcels"), Resource("draft"), .. Enumerable.Range(1, extraDrafts).Select(index => Resource($"draft-{index:00}"))],
        [new("Public parcels", "Public parcels", "FeatureServer", "Running", "managed", [new(1, "Parcels", "Polygon", "parcels", "parcels")], [], [])], [], []);
    public Task<OperateTransitionWorkspace> GetWorkspaceAsync(CancellationToken cancellationToken = default) => Task.FromResult(_workspace);
    public Task<OperateConnectionSummary?> FindConnectionAsync(string connectionId, CancellationToken cancellationToken = default) => Task.FromResult<OperateConnectionSummary?>(null);
    public Task<OperateResourceEditPreview?> FindResourceEditAsync(string resourceId, CancellationToken cancellationToken = default) => Task.FromResult(_workspace.ResourceEdits.FirstOrDefault(x => x.ResourceId == resourceId));
    public Task<OperateServiceDetail?> FindServiceAsync(string serviceName, CancellationToken cancellationToken = default) => Task.FromResult(_workspace.Services.FirstOrDefault(x => x.Name == serviceName));
}
