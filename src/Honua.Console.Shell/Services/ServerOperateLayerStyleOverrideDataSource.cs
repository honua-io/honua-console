using System.Text.Json;
using Honua.Console.Contracts;
using Honua.Console.Shell.Models;

namespace Honua.Console.Shell.Services;

/// <summary>
/// Live, server-bound implementation of <see cref="IOperateLayerStyleOverrideDataSource"/> backed by the
/// shipped honua-server per-layer authoring endpoints (<c>GET/PUT /api/v1/admin/metadata/layers/{id}/popup-info</c>
/// and <c>.../drawing-info</c>, AdminLayerAuthoringEndpoints.cs:38-41). The Operate resource-presentation
/// editor route keys on a canonical resource id; this source resolves it to the layer's (global) integer id
/// via the live layers projection (<see cref="IOperateTransitionDataSource.GetLayersViewAsync"/>) and then
/// reads/writes the two GeoServices presentation documents.
///
/// popupInfo and drawingInfo are stored on the layer's canonical resource (not per service exposure), so the
/// layer is presented as a single slot: <see cref="OperateLayerSlotStyleOverride.PopupInfoJson"/> carries the
/// stored popupInfo template and <see cref="OperateLayerSlotStyleOverride.DrawingInfoJson"/> the stored
/// drawingInfo renderer, both as the raw server JSON pretty-printed for editing. Nothing is fabricated: an
/// unresolved layer or a server rejection surfaces an explicit binding state (Console Patterns Charter §11).
/// </summary>
public sealed class ServerOperateLayerStyleOverrideDataSource : IOperateLayerStyleOverrideDataSource
{
    internal const string Surface = "Resource presentation overrides";
    internal const string PopupContract = "GET/PUT /api/v1/admin/metadata/layers/{id}/popup-info";
    internal const string DrawingContract = "GET/PUT /api/v1/admin/metadata/layers/{id}/drawing-info";

    private static readonly JsonSerializerOptions WriteOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };

    private readonly IOperateTransitionDataSource _operate;
    private readonly IHonuaAdminOperateClient _admin;

    public ServerOperateLayerStyleOverrideDataSource(
        IOperateTransitionDataSource operate,
        IHonuaAdminOperateClient admin)
    {
        _operate = operate ?? throw new ArgumentNullException(nameof(operate));
        _admin = admin ?? throw new ArgumentNullException(nameof(admin));
    }

    public async Task<OperateLayerStyleOverrideView> GetOverridesAsync(
        string resourceId,
        CancellationToken cancellationToken = default)
    {
        var (exposures, notFound) = await ResolveLayerAsync(resourceId, cancellationToken).ConfigureAwait(false);
        if (notFound is { } missing)
        {
            return new OperateLayerStyleOverrideView(resourceId, [], missing);
        }

        var first = exposures[0];
        var layerId = first.Layer.LayerId;

        var popup = await _admin.GetLayerPopupInfoAsync(layerId, cancellationToken).ConfigureAwait(false);
        if (popup.Issue is { } popupIssue)
        {
            return new OperateLayerStyleOverrideView(resourceId, [], ToBindingState(PopupContract, popupIssue));
        }

        var drawing = await _admin.GetLayerDrawingInfoAsync(layerId, cancellationToken).ConfigureAwait(false);
        if (drawing.Issue is { } drawingIssue)
        {
            return new OperateLayerStyleOverrideView(resourceId, [], ToBindingState(DrawingContract, drawingIssue));
        }

        // Every exposure shares the same canonical resource (and thus the same stored presentation), so a
        // single slot represents the layer; its service name is taken from the first exposure for display.
        var slot = new OperateLayerSlotStyleOverride(
            SlotId: layerId.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ServiceName: first.Service.Name,
            ServiceDisplayName: first.Layer.Name,
            PopupInfoJson: Pretty(popup.Data?.Document),
            DrawingInfoJson: Pretty(drawing.Data?.Document));

        return new OperateLayerStyleOverrideView(resourceId, [slot]);
    }

    public async Task<OperateLayerStyleOverrideSaveResult> SaveOverrideAsync(
        OperateLayerSlotStyleOverrideEdit edit,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(edit);

        var (_, notFound) = await ResolveLayerAsync(edit.ResourceId, cancellationToken).ConfigureAwait(false);
        if (notFound is { } missing)
        {
            return OperateLayerStyleOverrideSaveResult.Blocked(missing);
        }

        if (!int.TryParse(edit.SlotId, out var layerId))
        {
            return OperateLayerStyleOverrideSaveResult.Blocked(new OperateLayerStyleBindingState(
                Surface,
                OperateLayerStyleBindingState.Unsupported,
                PopupContract,
                $"The slot id '{edit.SlotId}' is not a layer id."));
        }

        // Parse the editor text into the raw documents up front so an invalid edit never reaches the server.
        if (!TryParseDocument(edit.PopupInfoJson, out var popupDoc, out var popupError))
        {
            return BlockedParse(PopupContract, "popup-info", popupError);
        }

        if (!TryParseDocument(edit.DrawingInfoJson, out var drawingDoc, out var drawingError))
        {
            return BlockedParse(DrawingContract, "drawing-info", drawingError);
        }

        var popupResult = await _admin.UpdateLayerPopupInfoAsync(layerId, popupDoc, cancellationToken).ConfigureAwait(false);
        if (popupResult.Issue is { } popupIssue)
        {
            return OperateLayerStyleOverrideSaveResult.Blocked(ToBindingState(PopupContract, popupIssue));
        }

        var drawingResult = await _admin.UpdateLayerDrawingInfoAsync(layerId, drawingDoc, cancellationToken).ConfigureAwait(false);
        if (drawingResult.Issue is { } drawingIssue)
        {
            return OperateLayerStyleOverrideSaveResult.Blocked(ToBindingState(DrawingContract, drawingIssue));
        }

        return new OperateLayerStyleOverrideSaveResult(Succeeded: true);
    }

    private async Task<(IReadOnlyList<(OperateServiceDetail Service, OperateServiceLayerProjection Layer)> Exposures,
        OperateLayerStyleBindingState? NotFound)> ResolveLayerAsync(
        string resourceId,
        CancellationToken cancellationToken)
    {
        var view = await _operate.GetLayersViewAsync(cancellationToken).ConfigureAwait(false);
        var exposures = view.Services
            .SelectMany(service => service.Layers.Select(layer => (Service: service, Layer: layer)))
            .Where(row => string.Equals(row.Layer.CanonicalResourceId, resourceId, StringComparison.OrdinalIgnoreCase))
            .OrderBy(row => row.Service.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (exposures.Length == 0)
        {
            return ([], new OperateLayerStyleBindingState(
                Surface,
                OperateLayerStyleBindingState.MissingBinding,
                "GET /api/v1/admin/services (layer projection)",
                $"No layer matching resource '{resourceId}' was found on the connected honua-server, so its "
                + "presentation cannot be authored."));
        }

        return (exposures, null);
    }

    private static OperateLayerStyleOverrideSaveResult BlockedParse(string contract, string label, string? detail) =>
        OperateLayerStyleOverrideSaveResult.Blocked(new OperateLayerStyleBindingState(
            Surface,
            OperateLayerStyleBindingState.Unsupported,
            contract,
            $"The {label} document is not valid JSON: {detail}"));

    // A blank editor value clears the stored document (null); otherwise it must parse to a JSON object.
    private static bool TryParseDocument(string? text, out JsonElement? document, out string? error)
    {
        document = null;
        error = null;
        if (string.IsNullOrWhiteSpace(text))
        {
            return true;
        }

        try
        {
            using var parsed = JsonDocument.Parse(text);
            if (parsed.RootElement.ValueKind != JsonValueKind.Object)
            {
                error = "the document must be a JSON object.";
                return false;
            }

            document = parsed.RootElement.Clone();
            return true;
        }
        catch (JsonException ex)
        {
            error = ex.Message;
            return false;
        }
    }

    private static string Pretty(JsonElement? document) =>
        document is { } element && element.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined)
            ? JsonSerializer.Serialize(element, WriteOptions)
            : string.Empty;

    private static OperateLayerStyleBindingState ToBindingState(string contract, HonuaAdminEndpointIssue issue)
    {
        var state = issue.State switch
        {
            "Missing permission" => OperateLayerStyleBindingState.Forbidden,
            "Unsupported" => OperateLayerStyleBindingState.Unsupported,
            _ => OperateLayerStyleBindingState.MissingBinding,
        };

        return new OperateLayerStyleBindingState(Surface, state, contract, issue.Detail);
    }
}
