using Honua.Console.Contracts;
using Honua.Console.Shell.Models;

namespace Honua.Console.Shell.Services;

public sealed class ConsolePublicationPathReader(IConsolePublicationNavigationSource source)
{
    public async Task<OperateSectionResult<IReadOnlyList<ConsolePublicationNode>>> ReadAsync(
        string path, CancellationToken cancellationToken)
    {
        var services = await ReadServicesAsync(cancellationToken);
        if (!services.IsAllowed || services.Value is null) return Denied(services.Status, services.Message);
        var connections = await source.ReadConnectionsAsync(cancellationToken);
        if (!connections.IsAllowed || connections.Value is null) return Denied(connections.Status, connections.Message);
        var resourceId = Uri.UnescapeDataString(path["/operate/layers/".Length..]);
        var owners = connections.Value.Where(connection => resourceId.StartsWith(
            OperateResourceIdentity.NormalizeToken(connection) + "-layer-", StringComparison.Ordinal)).ToArray();
        if (owners.Length != 1) return OperateSectionResult<IReadOnlyList<ConsolePublicationNode>>.Allowed([]);

        var tree = new ConsolePublicationTree();
        tree.Load(new HonuaAdminServicePage { Items = services.Value.ToArray(), Total = services.Value.Count }, "Services");
        var nodes = ConsolePublicationTree.Descendants(tree.Root)
            .Where(node => node.Kind == ConsolePublicationNodeKind.Service && node.ReportedCount > 0).ToArray();
        var reads = await BoundedFanOut.RunAsync(nodes,
            (node, token) => source.ReadLayersAsync(node.ServiceName!, owners, token),
            maxConcurrency: 4, cancellationToken: cancellationToken);
        for (var index = 0; index < nodes.Length; index++)
        {
            var read = reads[index];
            if (!read.IsAllowed || read.PartialResult || read.Value is null) return Denied(read.Status, read.Message);
            nodes[index].Children = ConsolePublicationTree.LayerNodes(nodes[index], read.Value);
        }
        return OperateSectionResult<IReadOnlyList<ConsolePublicationNode>>.Allowed(tree.PathTo(path));
    }

    private async Task<OperateSectionResult<IReadOnlyList<HonuaAdminServiceSummary>>> ReadServicesAsync(CancellationToken token)
    {
        var services = new List<HonuaAdminServiceSummary>();
        var query = HonuaAdminCatalogQuery.First;
        while (true)
        {
            token.ThrowIfCancellationRequested();
            var read = await source.ReadServicesAsync(query, token);
            if (!read.IsAllowed || read.PartialResult || read.Value is null)
                return OperateSectionResult<IReadOnlyList<HonuaAdminServiceSummary>>.Denied(read.Status, read.Message);
            var page = read.Value;
            services.AddRange(page.Items);
            if (services.Count >= page.Total)
                return OperateSectionResult<IReadOnlyList<HonuaAdminServiceSummary>>.Allowed(services);
            if (page.Items.Length == 0 || page.Page != query.Page)
                return OperateSectionResult<IReadOnlyList<HonuaAdminServiceSummary>>.Denied(
                    OperateSectionStatus.Unavailable, "The service catalog did not return a complete page.");
            query = query.Next();
        }
    }

    private static OperateSectionResult<IReadOnlyList<ConsolePublicationNode>> Denied(OperateSectionStatus status, string message) =>
        OperateSectionResult<IReadOnlyList<ConsolePublicationNode>>.Denied(status, message);
}
