using Honua.Console.Shell.Models;
using Microsoft.AspNetCore.Components;

namespace Honua.Console.Shell.Layout;

public sealed class ConsolePublicationTreeContext
{
    public required ConsolePublicationTree Tree { get; init; }
    public required string SelectedPath { get; init; }
    public bool FolderNavigationEnabled { get; init; }

    public string? Link(ConsolePublicationNode node) => node.Kind == ConsolePublicationNodeKind.Folder && !FolderNavigationEnabled ? null : node.Href;
    public EventCallback<ConsolePublicationNode> Expand { get; init; }
    public EventCallback<ConsolePublicationNode> Collapse { get; init; }
    public EventCallback<ConsolePublicationNode> Retry { get; init; }
    public EventCallback<ConsolePublicationNode> ShowFirstPage { get; init; }
    public EventCallback<ConsolePublicationNode> ShowMore { get; init; }

    public IEnumerable<ConsolePublicationNode> PagedParents => Tree.VisibleNodes()
        .Where(node => node.Expanded && (node.VisibleOffset > 0 || Tree.HasHiddenLoadedChildren(node) || OffersMoreServices(node)));

    public bool OffersMoreServices(ConsolePublicationNode node) =>
        node.Kind == ConsolePublicationNodeKind.Environment && Tree.HasUnloadedServices && !Tree.HasHiddenLoadedChildren(node);

    public ConsolePublicationTreePlacement ChildPlacement(ConsolePublicationNode parent, ConsolePublicationTreePlacement placement, int index) => new()
    {
        Level = placement.Level + 1,
        Position = parent.VisibleOffset + index + 1,
        SiblingCount = Tree.MatchingChildren(parent).Count,
        ParentKey = parent.Key
    };

    public string? OwnedContent(ConsolePublicationNode node)
    {
        if (!node.Expanded) return null;
        var identifiers = new List<string>();
        if (node.HasReadStatus) identifiers.Add(StatusId(node));
        if (Tree.VisibleChildren(node).Count > 0) identifiers.Add(GroupId(node));
        return identifiers.Count == 0 ? null : string.Join(' ', identifiers);
    }

    public static string FormatCount(int count, string unit) => $"{count} {unit}{(count == 1 ? "" : "s")}";

    public static string GroupId(ConsolePublicationNode node) => "console-tree-group-" + node.Key;
    public static string StatusId(ConsolePublicationNode node) => "console-tree-status-" + node.Key;
}

public sealed class ConsolePublicationTreePlacement
{
    public int Level { get; init; } = 1;
    public int Position { get; init; } = 1;
    public int SiblingCount { get; init; } = 1;
    public string ParentKey { get; init; } = "";
}
