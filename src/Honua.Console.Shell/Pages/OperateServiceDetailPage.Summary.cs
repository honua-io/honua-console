using System.Globalization;
using Honua.Console.Shell.Models;

namespace Honua.Console.Shell.Pages;

// v4 service header and Administration values (mockup 03), derived from the reads the page already makes.
public partial class OperateServiceDetailPage
{
    private string ServicePath => _service is null ? ServiceName
        : string.IsNullOrWhiteSpace(_service.FolderName) ? _service.Name : $"{_service.FolderName}/{_service.Name}";

    private int ProtocolCount => _settings.Bound ? _settings.EnabledProtocols.Count : _service?.Protocols.Count ?? 0;

    // DisplayName falls back to the description when a service has no title; never show it twice.
    private string? ServiceDescription => _service?.Description is { } description
        && !string.Equals(description, _service.DisplayName, StringComparison.Ordinal) ? description : null;

    private IReadOnlyList<ServiceConnection> Connections => _service is null ? [] : _service.Layers
        .Where(layer => !string.IsNullOrWhiteSpace(layer.ConnectionId) && !string.IsNullOrWhiteSpace(layer.ConnectionName))
        .Select(layer => new ServiceConnection(layer.ConnectionId!, layer.ConnectionName!))
        .Distinct()
        .ToArray();

    private string? AccessSummary => !_settings.Bound ? null
        : _settings.AllowAnonymousWrite ? "anonymous read and write"
        : _settings.AllowAnonymous ? "anonymous read" : "signed-in read";

    private string? AccessDetail
    {
        get
        {
            if (AccessSummary is not { } summary) return null;
            var parts = new List<string> { char.ToUpperInvariant(summary[0]) + summary[1..] };
            if (_settings.AllowedRoles.Count > 0) parts.Add("read: " + string.Join(", ", _settings.AllowedRoles));
            if (_settings.AllowedWriteRoles.Count > 0) parts.Add("write: " + string.Join(", ", _settings.AllowedWriteRoles));
            return string.Join(" · ", parts);
        }
    }

    private string RenderLimits
    {
        get
        {
            if (_settings.MapServer is not { } mapServer) return "Compatibility defaults";
            var parts = new List<string>();
            if (mapServer.MaxImageWidth is { } width && mapServer.MaxImageHeight is { } height)
                parts.Add(string.Create(CultureInfo.InvariantCulture, $"{width} × {height} px"));
            if (mapServer.MaxFeaturesPerLayer is { } features)
                parts.Add(string.Create(CultureInfo.InvariantCulture, $"{features:N0} features per layer"));
            return parts.Count == 0 ? "Compatibility defaults" : string.Join(" · ", parts);
        }
    }

    // In-page anchors must carry the current path: a bare "#id" resolves against <base href> and leaves the page.
    private string SectionHref(string id)
    {
        var uri = Navigation.Uri;
        var hash = uri.IndexOf('#', StringComparison.Ordinal);
        return (hash < 0 ? uri : uri[..hash]) + "#" + id;
    }

    private sealed record ServiceConnection(string Id, string Name);
}
