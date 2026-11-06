using Honua.Console.Contracts;
using Honua.Console.Shell.Services;

namespace Honua.Console.Shell.Models;

public enum ConsolePublicationNodeKind { Environment, Folder, Service, Layer }

public sealed class ConsolePublicationNode
{
    public required string Key { get; init; }
    public required string Label { get; init; }
    public required ConsolePublicationNodeKind Kind { get; init; }
    public string? Href { get; init; }
    public string? ServiceName { get; init; }
    public string? GeometryType { get; init; }
    public int ReportedCount { get; init; }
    public List<ConsolePublicationNode> Children { get; set; } = [];
    public bool Expanded { get; set; }
    public bool ChildrenLoaded { get; set; }
    public bool Loading { get; set; }
    public string? ReadMessage { get; set; }
    public OperateSectionStatus? ReadStatus { get; set; }
    public int VisibleOffset { get; set; }
    public int VisibleLimit { get; set; } = ConsolePublicationTree.PageSize;
    public int? ChildCount => Kind switch
    {
        ConsolePublicationNodeKind.Folder => Children.Count,
        ConsolePublicationNodeKind.Service => ReportedCount,
        _ => null
    };
    public bool IsLoadedEmptyService => Kind == ConsolePublicationNodeKind.Service && ChildrenLoaded && Children.Count == 0 && ReadStatus is null;
    public bool HasReadStatus => Loading || ReadStatus is not null || ReadMessage is not null || IsLoadedEmptyService;
    public bool HasChildren => Children.Count > 0 || (Kind == ConsolePublicationNodeKind.Service && ReportedCount > 0);
}

public sealed class ConsolePublicationTree
{
    public const int PageSize = 25;
    private Dictionary<string, bool>? _unfilteredExpansion;
    public ConsolePublicationNode Root { get; private set; } = new()
    {
        Key = "environment",
        Label = "Services",
        Kind = ConsolePublicationNodeKind.Environment,
        Href = "/",
        Expanded = true
    };
    public string Filter { get; set; } = "";
    public int ServiceCount { get; private set; }
    public int LayerTotal { get; private set; }
    public int LoadedLayerCount => Descendants(Root).Count(node => node.Kind == ConsolePublicationNodeKind.Layer);

    public void Load(HonuaAdminServicePage page, string environmentLabel)
    {
        var services = page.Items ?? [];
        Filter = string.Empty;
        _unfilteredExpansion = null;
        ServiceCount = page.Total;
        LayerTotal = page.LayerTotal;
        Root = new()
        {
            Key = "environment",
            Label = environmentLabel,
            Kind = ConsolePublicationNodeKind.Environment,
            Href = "/",
            Expanded = true,
            ChildrenLoaded = true,
            ReportedCount = page.Total
        };
        var folders = new Dictionary<string, ConsolePublicationNode>(StringComparer.Ordinal);
        foreach (var service in services.OrderBy(DisplayName, StringComparer.OrdinalIgnoreCase))
        {
            var parent = GetOrAddServiceParent(service, folders);
            parent.Children.Add(ServiceNode(service));
        }
        Root.Children = Root.Children.OrderBy(node => node.Kind).ThenBy(node => node.Label, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private ConsolePublicationNode GetOrAddServiceParent(HonuaAdminServiceSummary service, Dictionary<string, ConsolePublicationNode> folders)
    {
        if (string.IsNullOrWhiteSpace(service.FolderId) || string.IsNullOrWhiteSpace(service.FolderName)) return Root;
        if (folders.TryGetValue(service.FolderId, out var parent)) return parent;
        parent = new()
        {
            Key = "folder:" + Uri.EscapeDataString(service.FolderId),
            Label = service.FolderName,
            Kind = ConsolePublicationNodeKind.Folder,
            ChildrenLoaded = true
        };
        folders.Add(service.FolderId, parent);
        Root.Children.Add(parent);
        return parent;
    }

    public IReadOnlyList<ConsolePublicationNode> MatchingChildren(ConsolePublicationNode parent) =>
        parent.Children.Where(Matches).ToArray();

    public IReadOnlyList<ConsolePublicationNode> VisibleChildren(ConsolePublicationNode parent) =>
        MatchingChildren(parent).Skip(parent.VisibleOffset).Take(parent.VisibleLimit).ToArray();

    public void ShowMore(ConsolePublicationNode parent) => parent.VisibleLimit += PageSize;

    public void SetFilter(string filter)
    {
        if (string.IsNullOrWhiteSpace(Filter) && !string.IsNullOrWhiteSpace(filter))
            _unfilteredExpansion = Descendants(Root).Prepend(Root).ToDictionary(node => node.Key, node => node.Expanded);
        Filter = filter;
        foreach (var node in Descendants(Root).Prepend(Root)) ShowFirstPage(node);
        if (string.IsNullOrWhiteSpace(filter))
        {
            RestoreUnfilteredExpansion();
            return;
        }
        Root.Expanded = true;
        foreach (var node in Descendants(Root).Where(node => MatchingChildren(node).Count > 0)) node.Expanded = true;
    }

    private void RestoreUnfilteredExpansion()
    {
        if (_unfilteredExpansion is null) return;
        foreach (var node in Descendants(Root).Prepend(Root))
            if (_unfilteredExpansion.TryGetValue(node.Key, out var expanded)) node.Expanded = expanded;
        _unfilteredExpansion = null;
    }

    public void ShowFirstPage(ConsolePublicationNode parent)
    {
        parent.VisibleOffset = 0;
        parent.VisibleLimit = PageSize;
    }

    public IEnumerable<ConsolePublicationNode> VisibleNodes() => VisibleNodes(Root);

    private IEnumerable<ConsolePublicationNode> VisibleNodes(ConsolePublicationNode parent)
    {
        yield return parent;
        if (!parent.Expanded) yield break;
        foreach (var child in VisibleChildren(parent))
            foreach (var descendant in VisibleNodes(child)) yield return descendant;
    }

    public void RevealPath(string path)
    {
        var target = Descendants(Root).FirstOrDefault(node => node.Href == path);
        if (target is not null && !Matches(target)) SetFilter("");
        RevealPath(Root, path);
    }

    private bool RevealPath(ConsolePublicationNode parent, string path)
    {
        if (parent.Href == path) return true;
        var index = MatchingChildren(parent).ToList().FindIndex(child => RevealPath(child, path));
        if (index < 0) return false;
        parent.Expanded = true;
        parent.VisibleOffset = index / PageSize * PageSize;
        parent.VisibleLimit = PageSize;
        return true;
    }

    private bool Matches(ConsolePublicationNode node) => string.IsNullOrWhiteSpace(Filter)
        || node.Label.Contains(Filter, StringComparison.OrdinalIgnoreCase)
        || node.ServiceName?.Contains(Filter, StringComparison.OrdinalIgnoreCase) == true
        || node.Children.Any(Matches);

    public static IEnumerable<ConsolePublicationNode> Descendants(ConsolePublicationNode parent) =>
        parent.Children.SelectMany(child => Descendants(child).Prepend(child));

    private static string DisplayName(HonuaAdminServiceSummary service) =>
        new[] { service.Title, service.PublicName, service.ServiceName }.First(value => !string.IsNullOrWhiteSpace(value))!;

    private static ConsolePublicationNode ServiceNode(HonuaAdminServiceSummary service) => new()
    {
        Key = "service:" + Uri.EscapeDataString(service.ServiceName!),
        Label = DisplayName(service),
        Kind = ConsolePublicationNodeKind.Service,
        ServiceName = service.ServiceName,
        ReportedCount = service.LayerCount,
        Href = $"/operate/services/{Uri.EscapeDataString(service.ServiceName!)}/settings"
    };
}
