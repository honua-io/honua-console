using Honua.Console.Shell.Models;
using Honua.Console.Shell.Services;
using Microsoft.AspNetCore.Components;

namespace Honua.Console.Shell.Layout;

// v4 navigation: the whole catalog is loaded so every Folder shows its count, and a search also reads layers.
public partial class ConsolePublicationNavigation
{
    private async Task CompleteV4CatalogAsync(string? nameQuery, int filterRevision)
    {
        if (!V4Enabled) return;
        if (nameQuery is null) await LoadRemainingServicesAsync();
        else await SearchLayersAsync(nameQuery, filterRevision);
    }

    private async Task LoadRemainingServicesAsync()
    {
        for (var page = 1; page < EagerServicePages && _tree.HasUnloadedServices; page++)
        {
            if (!string.IsNullOrWhiteSpace(_tree.Filter) || !await NextServicePageReadyAsync()) return;
            await InvokeAsync(StateHasChanged);
        }
        if (_disposed || !string.IsNullOrWhiteSpace(_tree.Filter)) return;
        _tree.RevealPath(CurrentPath);
        await InvokeAsync(StateHasChanged);
    }

    private void RevealCurrentLocationIfMatching() => _tree.RevealMatchingPath(CurrentPath);

    private void CollapseAll()
    {
        foreach (var node in ConsolePublicationTree.Descendants(_tree.Root)) node.Expanded = false;
        _tree.Root.Expanded = true;
    }

    private Task<OperateSectionResult<IReadOnlyList<string>>> ConnectionsAsync(CancellationToken token) =>
        _connectionsRead ??= ConsoleReadBudget.RunAsync(Source.ReadConnectionsAsync, ConsoleReadBudget.Default,
            "Connections could not be read.", token);

    private async Task SearchLayersAsync(string nameQuery, int filterRevision)
    {
        var revision = _authorityRevision;
        var token = _authorityLifetime.Token;
        bool Stale() => _disposed || revision != _authorityRevision || filterRevision != _filterRevision;
        var connections = await ConnectionsAsync(token);
        if (Stale()) return;
        if (!connections.IsAllowed) _connectionsRead = null;
        var search = connections.IsAllowed
            ? await ConsoleReadBudget.RunAsync(readToken => Source.SearchLayersAsync(nameQuery, connections.Value ?? [], readToken),
                ConsoleReadBudget.Default, "Layers could not be searched.", token)
            : OperateSectionResult<IReadOnlyList<ConsolePublicationLayerMatch>>.Denied(connections.Status, connections.Message);
        if (Stale()) return;
        _layerSearch = search;
        await InvokeAsync(StateHasChanged);
    }

    private async Task OpenSearchResultAsync(ConsoleSearchTarget target)
    {
        var cleared = FilterChanged(new ChangeEventArgs { Value = "" });
        var filterRevision = _filterRevision;
        await cleared;
        if (_disposed || filterRevision != _filterRevision) return;
        var service = target.ServiceName is null ? null : ConsolePublicationTree.Descendants(_tree.Root).FirstOrDefault(node =>
            node.Kind == ConsolePublicationNodeKind.Service && string.Equals(node.ServiceName, target.ServiceName, StringComparison.OrdinalIgnoreCase));
        if (service is not null) await ExpandAsync(service);
        if (_disposed || filterRevision != _filterRevision) return;
        var revealed = _tree.PathTo(target.Href).Count > 0 ? target.Href : service?.Href ?? target.Href;
        _tree.RevealPath(revealed);
        await InvokeAsync(StateHasChanged);
    }
}
