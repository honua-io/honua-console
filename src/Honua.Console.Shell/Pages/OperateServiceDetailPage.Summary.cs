using System.Globalization;
using Honua.Console.Shell.Layout;
using Honua.Console.Shell.Models;
using Microsoft.AspNetCore.Components;

namespace Honua.Console.Shell.Pages;

// v4 service header and Administration values (mockup 03), derived from the reads the page already makes.
public partial class OperateServiceDetailPage
{
    private const string SettingsNotRead = "Not available: service settings were not read";
    private ElementReference _protocolsPanel;
    private ElementReference _accessPanel;
    private ElementReference _limitsPanel;
    private ElementReference _folderEditor;
    private ElementReference _metadataEditor;
    private ElementReference? _focusPanel;

    // The served path the endpoint URLs use; an unresolved Folder is marked rather than shown as the root.
    private string ServicePath => _service?.DisplayPath ?? ServiceName;

    // Endpoints are built from the enabled protocols only; ServiceType carries a placeholder when there are none.
    private int ProtocolCount => _settings.Bound ? _settings.EnabledProtocols.Count : _service?.Endpoints.Count ?? 0;

    // Without a settings read, an empty endpoint list may mean the protocols were not reported, not that there are none.
    private bool ProtocolsReported => _settings.Bound || ProtocolCount > 0;

    private string ProtocolSummary => ProtocolsReported
        ? ConsolePublicationTreeContext.FormatCount(ProtocolCount, "protocol") : "protocols not reported";

    // DisplayName falls back to the description when a service has no title; never show it twice.
    private string? ServiceDescription => _service?.Description is { } description
        && !string.Equals(description, _service.DisplayName, StringComparison.Ordinal) ? description : null;

    private IReadOnlyList<ServiceConnection> Connections => _service is null ? [] : _service.Layers
        .Where(layer => !string.IsNullOrWhiteSpace(layer.ConnectionId) && !string.IsNullOrWhiteSpace(layer.ConnectionName))
        .Select(layer => new ServiceConnection(layer.ConnectionId!, layer.ConnectionName!))
        .Distinct()
        .ToArray();

    private string ReadAccess => _settings.AllowAnonymous ? "anonymous read"
        : _settings.AllowedRoles.Count > 0 ? "read limited to roles" : "signed-in read";

    private string? AccessSummary => !_settings.Bound ? null
        : _settings.AllowAnonymousWrite ? $"{ReadAccess} · anonymous write" : ReadAccess;

    private string AccessDetail
    {
        get
        {
            if (!_settings.Bound) return SettingsNotRead;
            var parts = new List<string> { char.ToUpperInvariant(ReadAccess[0]) + ReadAccess[1..] };
            if (_settings.AllowedRoles.Count > 0) parts.Add("read: " + string.Join(", ", _settings.AllowedRoles));
            if (_settings.AllowAnonymousWrite) parts.Add("anonymous write");
            if (_settings.AllowedWriteRoles.Count > 0) parts.Add("write: " + string.Join(", ", _settings.AllowedWriteRoles));
            return string.Join(" · ", parts);
        }
    }

    private string RenderLimits
    {
        get
        {
            if (!_settings.Bound) return SettingsNotRead;
            if (_settings.MapServer is not { } mapServer) return "Not reported";
            var parts = new List<string>();
            if (mapServer.MaxImageWidth is { } width && mapServer.MaxImageHeight is { } height)
                parts.Add(string.Create(CultureInfo.InvariantCulture, $"{width} × {height} px"));
            if (mapServer.MaxFeaturesPerLayer is { } features)
                parts.Add(string.Create(CultureInfo.InvariantCulture, $"{features:N0} features per layer"));
            return parts.Count == 0 ? "Not reported" : string.Join(" · ", parts);
        }
    }

    // Switching tabs from below the tab strip moves focus to the opened panel, so keyboard users keep their place.
    private void OpenTab(string tabId)
    {
        _selectedTab = tabId;
        _focusPanel = tabId switch
        {
            "service-protocols-tab" => _protocolsPanel,
            "service-access-tab" => _accessPanel,
            "service-limits-tab" => _limitsPanel,
            _ => null
        };
    }

    // Same-page fragment links scroll but do not move focus in Blazor; move it to the target as well.
    private void FocusSection(ElementReference target) => _focusPanel = target;

    // In-page anchors must carry the current path: a bare "#id" resolves against <base href> and leaves the page.
    private string SectionHref(string id)
    {
        var uri = Navigation.Uri;
        var hash = uri.IndexOf('#', StringComparison.Ordinal);
        return (hash < 0 ? uri : uri[..hash]) + "#" + id;
    }

    private sealed record ServiceConnection(string Id, string Name);
}
