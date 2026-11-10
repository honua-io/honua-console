using Honua.Console.Shell.Models;
using Honua.Console.Shell.Services;
using Microsoft.AspNetCore.Components;

namespace Honua.Console.Shell.Pages;

public partial class OperateServiceDetailPage
{
    private bool _interactiveReady;
    private bool _loading = true;
    private ConsoleAgentUnsavedSummary _folderUnsaved = new();
    private ConsoleAgentUnsavedSummary _metadataUnsaved = new();

    private ConsoleAgentEntity? SelectedService => _service is null ? null : new()
    {
        Kind = ConsoleAgentEntityKind.Service,
        Id = _service.Name,
        ServiceName = _service.Name
    };

    private ConsoleAgentPageContext AgentContext => new()
    {
        Route = $"/operate/services/{Uri.EscapeDataString(ServiceName)}/settings",
        Entities = SelectedService is { } entity ? [entity] : [],
        Selection = SelectedService is { } selected ? [selected] : [],
        Filters = new Dictionary<string, string> { ["tab"] = _selectedTab },
        Unsaved = new()
        {
            ChangedFieldCount = ChangedSettingsCount + _folderUnsaved.ChangedFieldCount + _metadataUnsaved.ChangedFieldCount,
            PendingOperationCount = (_loading ? 1 : 0) + (_savingProtocols ? 1 : 0) + (_savingAccess ? 1 : 0)
                + _folderUnsaved.PendingOperationCount + _metadataUnsaved.PendingOperationCount
        }
    };

    private int ChangedSettingsCount => !_settings.Bound ? 0 :
        (!_selectedProtocols.SetEquals(_settings.EnabledProtocols) ? 1 : 0)
        + (_allowAnonymous != _settings.AllowAnonymous ? 1 : 0)
        + (_allowAnonymousWrite != _settings.AllowAnonymousWrite ? 1 : 0)
        + (!RolesMatch(_allowedRoles, _settings.AllowedRoles) ? 1 : 0)
        + (!RolesMatch(_allowedWriteRoles, _settings.AllowedWriteRoles) ? 1 : 0);

    private static bool RolesMatch(string current, IReadOnlyList<string> saved) =>
        new HashSet<string>(ParseRoles(current), StringComparer.OrdinalIgnoreCase).SetEquals(saved);


    protected override void OnAfterRender(bool firstRender)
    {
        if (firstRender)
        {
            _interactiveReady = true;
            StateHasChanged();
        }
    }

    private OperateServiceDetail? _service;
    private IReadOnlyList<OperateCapabilityState> _states = Array.Empty<OperateCapabilityState>();

    // Curated fallback protocol set used when the server doesn't enumerate AvailableProtocols. Mirrors
    // honua-server's ServiceProtocols constants (the operator-facing serving protocols).
    private static readonly string[] FallbackProtocols =
    {
        "FeatureServer", "MapServer", "OgcFeatures", "Wms", "Wfs20", "Wmts",
        "OData", "OGC-API-Tiles", "Stac"
    };

    private ServiceSettingsView _settings = ServiceSettingsView.Unbound(string.Empty, "Loading…");
    private IReadOnlyList<string> _availableProtocols = Array.Empty<string>();
    private readonly HashSet<string> _selectedProtocols = new(StringComparer.OrdinalIgnoreCase);
    private bool _allowAnonymous;
    private bool _allowAnonymousWrite;
    private string _allowedRoles = string.Empty;
    private string _allowedWriteRoles = string.Empty;
    private bool _savingProtocols;
    private bool _savingAccess;
    private ServiceConfigurationResult? _configResult;

    protected override async Task OnParametersSetAsync()
    {
        _loading = true;
        var view = await OperateData.GetServicesViewAsync();
        _service = view.Services.FirstOrDefault(
            service => string.Equals(service.Name, ServiceName, StringComparison.OrdinalIgnoreCase));
        _states = OperateCapabilityStateFilters.ForSurface(view.CapabilityStates, "Services", "Layers");

        await LoadSettingsAsync();
        _loading = false;
    }

    private async Task LoadSettingsAsync()
    {
        _settings = await ServiceConfig.GetSettingsAsync(ServiceName);
        _availableProtocols = _settings.AvailableProtocols.Count > 0
            ? _settings.AvailableProtocols
            : FallbackProtocols;
        _selectedProtocols.Clear();
        foreach (var protocol in _settings.EnabledProtocols)
        {
            _selectedProtocols.Add(protocol);
        }

        _allowAnonymous = _settings.AllowAnonymous;
        _allowAnonymousWrite = _settings.AllowAnonymousWrite;
        _allowedRoles = string.Join(", ", _settings.AllowedRoles);
        _allowedWriteRoles = string.Join(", ", _settings.AllowedWriteRoles);
    }

    private static string RuntimeSettingScope(OperateRuntimeSetting setting) =>
        setting.Name.StartsWith("mapServer.", StringComparison.OrdinalIgnoreCase) ? "Compatibility default" : setting.ApplyScope;

    private static string[] ParseRoles(string? raw) =>
        (raw ?? string.Empty)
            .Split(new[] { ',', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private void ToggleProtocol(string protocol, bool enabled)
    {
        if (enabled)
        {
            _selectedProtocols.Add(protocol);
        }
        else
        {
            _selectedProtocols.Remove(protocol);
        }

        _configResult = null;
    }

    private Task SaveProtocolsAsync()
    {
        _configResult = null;
        return Notifications.RunGuardedAsync(
            async () =>
            {
                _configResult = await ServiceConfig.UpdateProtocolsAsync(new ServiceProtocolsCommand
                {
                    ServiceName = ServiceName,
                    EnabledProtocols = _selectedProtocols.ToArray()
                });
                if (_configResult.Succeeded)
                {
                    await LoadSettingsAsync();
                }
            },
            setBusy: busy => { _savingProtocols = busy; StateHasChanged(); },
            failureMessage: "Couldn't save protocols");
    }

    private Task SaveAccessAsync()
    {
        _configResult = null;
        return Notifications.RunGuardedAsync(
            async () =>
            {
                _configResult = await ServiceConfig.UpdateAccessPolicyAsync(new ServiceAccessPolicyCommand
                {
                    ServiceName = ServiceName,
                    AllowAnonymous = _allowAnonymous,
                    AllowAnonymousWrite = _allowAnonymousWrite,
                    AllowedRoles = ParseRoles(_allowedRoles),
                    AllowedWriteRoles = ParseRoles(_allowedWriteRoles)
                });
                if (_configResult.Succeeded)
                {
                    await LoadSettingsAsync();
                }
            },
            setBusy: busy => { _savingAccess = busy; StateHasChanged(); },
            failureMessage: "Couldn't save access policy");
    }

}
