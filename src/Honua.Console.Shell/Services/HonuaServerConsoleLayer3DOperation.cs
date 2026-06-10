using System.Text.Json;
using Honua.Console.Contracts;
using Honua.Console.Shell.Models;

namespace Honua.Console.Shell.Services;

/// <summary>
/// Live implementation of the layer 3D-extrusion / 3D-symbology + lifecycle-status authoring operation. Reads
/// and writes a layer's 3D extrusion + 3D symbology and its lifecycle status on honua-server through
/// <see cref="IHonuaAdminOperateClient"/> and maps the result (or rejection). It never fabricates success —
/// every result reflects what the server read back.
/// </summary>
public sealed class HonuaServerConsoleLayer3DOperation : IConsoleLayer3DOperation
{
    private readonly IHonuaAdminOperateClient _client;

    public HonuaServerConsoleLayer3DOperation(IHonuaAdminOperateClient client)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
    }

    public async Task<ConsoleLayer3D> GetExtrusionAsync(int layerId, CancellationToken cancellationToken = default)
    {
        var result = await _client.GetLayerExtrusionAsync(layerId, cancellationToken).ConfigureAwait(false);
        if (result.Data is { } data)
        {
            return new ConsoleLayer3D
            {
                Bound = true,
                LayerId = data.LayerId,
                Extrusion = FromAdmin(data.Extrusion),
                Symbology3D = FromAdmin(data.Symbology3D),
            };
        }

        return ConsoleLayer3D.Unbound(
            result.Issue?.Detail ?? "The Honua server did not return 3D extrusion metadata for this layer.");
    }

    public async Task<ConsoleSetLayerMetadataResult> SetExtrusionAsync(
        int layerId,
        ConsoleLayerExtrusionSettings? extrusion,
        bool clearExtrusion,
        ConsoleSymbology3D? symbology3D,
        bool clearSymbology3D,
        CancellationToken cancellationToken = default)
    {
        var request = new HonuaAdminLayerExtrusionUpdate
        {
            Extrusion = clearExtrusion ? null : ToAdmin(extrusion),
            ClearExtrusion = clearExtrusion,
            Symbology3D = clearSymbology3D ? null : ToAdmin(symbology3D),
            ClearSymbology3D = clearSymbology3D,
        };

        var result = await _client.UpdateLayerExtrusionAsync(layerId, request, cancellationToken).ConfigureAwait(false);
        return MapResult(result.Data is not null, result.Issue, "Saved the layer's 3D extrusion / symbology on honua-server.");
    }

    public async Task<ConsoleLayerStatus> GetStatusAsync(int layerId, CancellationToken cancellationToken = default)
    {
        var result = await _client.GetLayerStatusAsync(layerId, cancellationToken).ConfigureAwait(false);
        if (result.Data is { } data)
        {
            return new ConsoleLayerStatus
            {
                Bound = true,
                LayerId = data.LayerId,
                Lifecycle = data.Lifecycle,
                State = data.State,
            };
        }

        return ConsoleLayerStatus.Unbound(
            result.Issue?.Detail ?? "The Honua server did not return lifecycle status for this layer.");
    }

    public async Task<ConsoleSetLayerMetadataResult> SetStatusAsync(
        int layerId,
        string? lifecycle,
        string? state,
        CancellationToken cancellationToken = default)
    {
        var request = new HonuaAdminLayerStatusUpdate
        {
            Lifecycle = string.IsNullOrWhiteSpace(lifecycle) ? null : lifecycle,
            State = string.IsNullOrWhiteSpace(state) ? null : state,
        };

        var result = await _client.UpdateLayerStatusAsync(layerId, request, cancellationToken).ConfigureAwait(false);
        return MapResult(result.Data is not null, result.Issue, "Saved the layer's lifecycle status on honua-server.");
    }

    private static ConsoleLayerExtrusionSettings? FromAdmin(HonuaAdminLayerExtrusionSettings? value) =>
        value is null
            ? null
            : new ConsoleLayerExtrusionSettings
            {
                HeightField = value.HeightField,
                BaseHeightField = value.BaseHeightField,
                Unit = value.Unit,
                DefaultHeight = value.DefaultHeight,
                MaterialHint = value.MaterialHint,
            };

    private static HonuaAdminLayerExtrusionSettings? ToAdmin(ConsoleLayerExtrusionSettings? value) =>
        value is null
            ? null
            : new HonuaAdminLayerExtrusionSettings
            {
                HeightField = value.HeightField,
                BaseHeightField = value.BaseHeightField,
                Unit = value.Unit,
                DefaultHeight = value.DefaultHeight,
                MaterialHint = value.MaterialHint,
            };

    private static ConsoleSymbology3D? FromAdmin(HonuaAdminSymbology3D? value) =>
        value is null
            ? null
            : new ConsoleSymbology3D
            {
                DefaultColor = FromAdmin(value.DefaultColor),
                DefaultOpacity = value.DefaultOpacity,
                Rules = value.Rules.Select(FromAdmin).ToArray(),
            };

    private static HonuaAdminSymbology3D? ToAdmin(ConsoleSymbology3D? value) =>
        value is null
            ? null
            : new HonuaAdminSymbology3D
            {
                DefaultColor = ToAdmin(value.DefaultColor),
                DefaultOpacity = value.DefaultOpacity,
                Rules = value.Rules.Select(ToAdmin).ToArray(),
            };

    private static ConsoleSymbology3DRule FromAdmin(HonuaAdminSymbology3DRule rule) =>
        new()
        {
            Attribute = rule.Attribute,
            Comparison = rule.Comparison,
            Value = rule.Value is { } v && v.ValueKind != JsonValueKind.Null ? ValueToText(v) : null,
            Color = FromAdmin(rule.Color),
            Opacity = rule.Opacity,
            Visible = rule.Visible,
        };

    private static HonuaAdminSymbology3DRule ToAdmin(ConsoleSymbology3DRule rule) =>
        new()
        {
            Attribute = rule.Attribute,
            Comparison = rule.Comparison,
            Value = TextToValue(rule.Value),
            Color = ToAdmin(rule.Color),
            Opacity = rule.Opacity,
            Visible = rule.Visible,
        };

    private static ConsoleRgbColor? FromAdmin(HonuaAdminRgbColor? color) =>
        color is null ? null : new ConsoleRgbColor { Red = color.Red, Green = color.Green, Blue = color.Blue };

    private static HonuaAdminRgbColor? ToAdmin(ConsoleRgbColor? color) =>
        color is null ? null : new HonuaAdminRgbColor { Red = color.Red, Green = color.Green, Blue = color.Blue };

    // Render a persisted scalar value as the text the editor shows: a JSON string surfaces unquoted, any other
    // scalar surfaces via its raw JSON text (numbers/booleans verbatim).
    private static string ValueToText(JsonElement value) =>
        value.ValueKind == JsonValueKind.String ? value.GetString() ?? string.Empty : value.GetRawText();

    // Parse the editor's text back into a JSON scalar: a number stays a number, true/false stay booleans,
    // everything else (including blank) is sent as a JSON string so the server gets a well-typed scalar.
    private static JsonElement? TextToValue(string? text)
    {
        if (text is null)
        {
            return null;
        }

        var trimmed = text.Trim();
        if (double.TryParse(trimmed, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var number))
        {
            return JsonSerializer.SerializeToElement(number);
        }

        if (bool.TryParse(trimmed, out var boolean))
        {
            return JsonSerializer.SerializeToElement(boolean);
        }

        return JsonSerializer.SerializeToElement(text);
    }

    private static ConsoleSetLayerMetadataResult MapResult(bool succeeded, HonuaAdminEndpointIssue? issue, string successDetail)
    {
        if (succeeded)
        {
            return new ConsoleSetLayerMetadataResult
            {
                Succeeded = true,
                State = "Updated",
                Detail = successDetail,
            };
        }

        return new ConsoleSetLayerMetadataResult
        {
            Succeeded = false,
            State = issue?.State ?? "Unavailable",
            Detail = issue?.Detail ?? "The Honua server did not accept the metadata update.",
        };
    }
}
