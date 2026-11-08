using Honua.Console.Contracts;
using Honua.Console.Shell.Models;

namespace Honua.Console.Shell.Services;

public sealed class ConsolePublicationPathReader(IConsolePublicationNavigationSource source)
{
    public async Task<OperateSectionResult<IReadOnlyList<ConsolePublicationNode>>> ReadAsync(
        ConsolePublicationLayerSelection selection, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(selection.ServiceName)) return Empty();
        var services = await ReadServicesAsync(selection.ServiceName, cancellationToken);
        if (!services.IsAllowed || services.Value is null) return Denied(services.Status, services.Message);
        if (services.Value.Count != 1) return Empty();

        var tree = new ConsolePublicationTree();
        tree.Load(new HonuaAdminServicePage { Items = services.Value.ToArray(), Total = 1 }, "Services");
        var service = ConsolePublicationTree.Descendants(tree.Root).Single(node => node.Kind == ConsolePublicationNodeKind.Service);
        service.Children = ConsolePublicationTree.LayerNodes(service,
            [new(selection.ResourceId, selection.LayerName, "Unknown")]);
        return OperateSectionResult<IReadOnlyList<ConsolePublicationNode>>.Allowed(tree.PathTo($"/operate/layers/{Uri.EscapeDataString(selection.ResourceId)}"));
    }

    private static OperateSectionResult<IReadOnlyList<ConsolePublicationNode>> Empty() =>
        OperateSectionResult<IReadOnlyList<ConsolePublicationNode>>.Allowed([]);

    private async Task<OperateSectionResult<IReadOnlyList<HonuaAdminServiceSummary>>> ReadServicesAsync(string serviceName, CancellationToken token)
    {
        var services = new List<HonuaAdminServiceSummary>();
        var query = HonuaAdminCatalogQuery.First with { NameQuery = serviceName };
        while (true)
        {
            token.ThrowIfCancellationRequested();
            var read = await source.ReadServicesAsync(query, token);
            if (!read.IsAllowed || read.PartialResult || read.Value is null)
                return OperateSectionResult<IReadOnlyList<HonuaAdminServiceSummary>>.Denied(read.Status, read.Message);
            var page = read.Value;
            services.AddRange(page.Items);
            if (services.Count >= page.Total)
                return OperateSectionResult<IReadOnlyList<HonuaAdminServiceSummary>>.Allowed(services
                    .Where(service => string.Equals(service.ServiceName, serviceName, StringComparison.OrdinalIgnoreCase)).ToArray());
            if (page.Items.Length == 0 || page.Page != query.Page)
                return OperateSectionResult<IReadOnlyList<HonuaAdminServiceSummary>>.Denied(
                    OperateSectionStatus.Unavailable, "The service catalog did not return a complete page.");
            query = query.Next();
        }
    }

    private static OperateSectionResult<IReadOnlyList<ConsolePublicationNode>> Denied(OperateSectionStatus status, string message) =>
        OperateSectionResult<IReadOnlyList<ConsolePublicationNode>>.Denied(status, message);
}
