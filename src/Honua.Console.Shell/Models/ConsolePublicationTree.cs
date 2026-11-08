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
    public int LoadedServiceCount => Descendants(Root).Count(node => node.Kind == ConsolePublicationNodeKind.Service);
    public int LoadedLayerCount => Descendants(Root).Count(node => node.Kind == ConsolePublicationNodeKind.Layer);
    public bool HasUnloadedServices => LoadedServiceCount < ServiceCount;

    public bool HasHiddenLoadedChildren(ConsolePublicationNode parent) =>
        MatchingChildren(parent).Count > parent.VisibleOffset + parent.VisibleLimit;

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
        Root.Children = Ordered(Root.Children);
    }

    public void Append(HonuaAdminServicePage page)
    {
        ServiceCount = page.Total;
        LayerTotal = page.LayerTotal;
        var known = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var node in Descendants(Root))
        {
            if (node.Kind == ConsolePublicationNodeKind.Service && node.ServiceName is { } name)
                known.Add(name);
        }

        var folders = IndexFolders();
        foreach (var service in page.Items ?? [])
        {
            if (string.IsNullOrWhiteSpace(service.ServiceName) || !known.Add(service.ServiceName)) continue;
            InsertSorted(GetOrAddServiceParent(service, folders), ServiceNode(service));
        }

        Root.Children = Ordered(Root.Children);
    }

    private Dictionary<string, ConsolePublicationNode> IndexFolders()
    {
        var folders = new Dictionary<string, ConsolePublicationNode>(StringComparer.Ordinal);
        foreach (var node in Root.Children)
        {
            if (node.Kind != ConsolePublicationNodeKind.Folder || !node.Key.StartsWith("folder:", StringComparison.Ordinal))
                continue;
            folders[Uri.UnescapeDataString(node.Key["folder:".Length..])] = node;
        }

        return folders;
    }

    private static void InsertSorted(ConsolePublicationNode parent, ConsolePublicationNode service)
    {
        var index = parent.Children.FindIndex(child =>
            string.Compare(child.Label, service.Label, StringComparison.OrdinalIgnoreCase) > 0);
        if (index < 0) parent.Children.Add(service);
        else parent.Children.Insert(index, service);
    }

    private static List<ConsolePublicationNode> Ordered(List<ConsolePublicationNode> children) =>
        children.OrderBy(node => node.Kind).ThenBy(node => node.Label, StringComparer.OrdinalIgnoreCase).ToList();

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

    public IReadOnlyList<ConsolePublicationNode> PathTo(string path)
    {
        var matches = Descendants(Root).Prepend(Root).Where(node => node.Href == path).Take(2).ToArray();
        if (matches.Length != 1) return [];
        return FindPath(Root, matches[0])?.ToArray() ?? [];
    }

    public static List<ConsolePublicationNode> LayerNodes(ConsolePublicationNode service, IReadOnlyList<ConsolePublicationLayer> layers) =>
        layers.Select(layer => new ConsolePublicationNode
        {
            Key = service.Key + ":layer:" + layer.ResourceId, Label = layer.Name, Kind = ConsolePublicationNodeKind.Layer,
            GeometryType = layer.GeometryType, Href = $"/operate/layers/{Uri.EscapeDataString(layer.ResourceId)}"
        }).ToList();

    private static IEnumerable<ConsolePublicationNode>? FindPath(ConsolePublicationNode node, ConsolePublicationNode target)
    {
        if (node == target) return [node];
        foreach (var child in node.Children)
        {
            var path = FindPath(child, target);
            if (path is not null) return path.Prepend(node);
        }
        return null;
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
