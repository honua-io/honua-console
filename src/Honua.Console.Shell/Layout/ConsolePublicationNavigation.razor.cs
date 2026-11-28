using Honua.Console.Contracts;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Services;
using Microsoft.AspNetCore.Components;

namespace Honua.Console.Shell.Layout;

// v4 navigation: the whole catalog is loaded so every Folder shows its count, and a search also reads layers.
public partial class ConsolePublicationNavigation
{
    private async Task CompleteV4CatalogAsync(string? nameQuery, int searchRevision)
    {
        if (!V4Enabled) return;
        if (nameQuery is null) await LoadRemainingServicesAsync();
        else await SearchLayersAsync(nameQuery, searchRevision);
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

    private async Task SearchLayersAsync(string nameQuery, int searchRevision)
    {
        var revision = _authorityRevision;
        var token = _searchLifetime.Token;
        bool Stale() => _disposed || revision != _authorityRevision || searchRevision != _searchRevision;
        var connections = await ConnectionsAsync(_authorityLifetime.Token);
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
        var clearing = FilterChanged(new ChangeEventArgs { Value = "" });
        var searchRevision = _searchRevision;
        bool Stale() => _disposed || searchRevision != _searchRevision;
        await UnfilteredTargetReadyAsync(target, clearing, Stale);
        if (Stale()) return;
        var service = FindService(target.ServiceName);
        if (service is not null) await ExpandAsync(service);
        if (Stale()) return;
        var revealed = _tree.PathTo(target.Href).Count > 0 ? target.Href : service?.Href ?? target.Href;
        _tree.RevealPath(revealed);
        await InvokeAsync(StateHasChanged);
    }

    // Waits until the result exists in the unfiltered catalog, so a large catalog does not delay the reveal.
    private async Task UnfilteredTargetReadyAsync(ConsoleSearchTarget target, Task clearing, Func<bool> stale)
    {
        while (!stale() && !clearing.IsCompleted)
        {
            var applied = _catalogApplied.Task;
            if (HoldsUnfilteredCatalog && (FindService(target.ServiceName) is not null || _tree.PathTo(target.Href).Count > 0)) return;
            await Task.WhenAny(clearing, applied);
        }
    }

    private ConsolePublicationNode? FindService(string? serviceName) => serviceName is null ? null
        : ConsolePublicationTree.Descendants(_tree.Root).FirstOrDefault(node =>
            node.Kind == ConsolePublicationNodeKind.Service && string.Equals(node.ServiceName, serviceName, StringComparison.OrdinalIgnoreCase));

    private bool HoldsUnfilteredCatalog => _settled is { Query.NameQuery: null } && _inflight is not { Apply: CatalogApply.Replace }
        && string.IsNullOrWhiteSpace(_tree.Filter);

    private TaskCompletionSource _catalogApplied = new(TaskCreationOptions.RunContinuationsAsynchronously);

    private void SignalCatalogApplied()
    {
        var applied = _catalogApplied;
        _catalogApplied = new(TaskCreationOptions.RunContinuationsAsynchronously);
        applied.TrySetResult();
    }

    private int _searchRevision;
    private CancellationTokenSource _searchLifetime = new();

    private int RestartSearch()
    {
        _searchLifetime.Cancel();
        _searchLifetime.Dispose();
        _searchLifetime = CancellationTokenSource.CreateLinkedTokenSource(_authorityLifetime.Token);
        _layerSearch = null;
        return ++_searchRevision;
    }

    private CancellationToken CatalogToken(HonuaAdminCatalogQuery query) =>
        query.NameQuery is null ? _authorityLifetime.Token : _searchLifetime.Token;

    // A complete unfiltered catalog is searched in place: no catalog read, and clearing restores the tree as it was.
    private bool SearchesLoadedCatalog => V4Enabled && !_loading && _inflight is null && !_tree.HasUnloadedServices
        && _servicesRead is { IsAllowed: true } && _settled is { Query.NameQuery: null } settled
        && settled.AuthorityRevision == _authorityRevision;

    private async Task SearchLoadedCatalogAsync(string filter, int searchRevision)
    {
        _tree.SetFilter(filter);
        if (string.IsNullOrWhiteSpace(filter)) RevealCurrentLocationIfMatching();
        await InvokeAsync(StateHasChanged);
        if (!string.IsNullOrWhiteSpace(filter) && !FilterTooLong) await SearchLayersAsync(filter.Trim(), searchRevision);
    }

    private OperateSectionResult<HonuaAdminServicePage>? _pageFailure;

    private string? PageFailure => _pageFailure is null ? null : _pageFailure.Message ?? "Some services could not be read.";

    // Under v4 a later service page that fails leaves the services already shown in place; the footer offers the retry.
    private bool KeepsTreeOnFailure(CatalogAttempt attempt, OperateSectionResult<HonuaAdminServicePage> result)
    {
        if (!V4Enabled || attempt.Apply != CatalogApply.Append || result is { IsAllowed: true, Value: not null }) return false;
        _failed = attempt;
        _pageFailure = result;
        return true;
    }

    private ConsolePublicationNode? _menuNode;
    private bool _newFolderOpen;

    private void OpenMenu(ConsolePublicationMenuRequest request)
    {
        _newFolderOpen = false;
        _menuNode = string.Equals(_menuNode?.Key, request.Node.Key, StringComparison.Ordinal) ? null : request.Node;
    }

    private void CloseMenu() => _menuNode = null;

    private void OpenNewFolder()
    {
        _menuNode = null;
        _newFolderOpen = true;
    }

    private void CloseNewFolder() => _newFolderOpen = false;

    private Task HandleFolderCreatedAsync()
    {
        _newFolderOpen = false;
        _menuNode = null;
        return _folderChanges is not null ? Task.CompletedTask : ReloadAuthorityAsync();
    }
}
