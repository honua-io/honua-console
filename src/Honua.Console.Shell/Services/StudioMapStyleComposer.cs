using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Honua.Console.Contracts;
using Honua.Console.Shell.Models;

namespace Honua.Console.Shell.Services;

/// <summary>Combines genuine server styles for the resolved layers supported by the Console editor.</summary>
public static class StudioMapStyleComposer
{
    public static string Signature(StudioMapEditorState state) => JsonSerializer.Serialize(new
    {
        state.PackageId,
        state.PackageCreatedAt,
        state.Title,
        state.Description,
        state.ShareTier,
        state.EmbedAllowed,
        state.ShowLegend,
        state.PopupsEnabled,
        state.InteractionsEnabled,
        state.Basemap,
        state.InitialExtent,
        layers = state.Layers.Select(layer => new
        {
            layer.SourceRef,
            layer.BoundServiceId,
            layer.BoundLayerId,
            layer.SourceBinding,
            layer.Title,
            layer.Visible,
            layer.Style,
            layer.Filter,
            layer.PopupFields
        }).ToArray()
    });

    public static bool MatchesSavedStyle(StudioMapEditorState state) =>
        state.CanonicalMapStyle is { ValueKind: JsonValueKind.Object } style
        && style.TryGetProperty("version", out var version) && version.ValueKind == JsonValueKind.Number && version.TryGetInt32(out var number) && number == 8
        && style.TryGetProperty("sources", out var sources) && sources.ValueKind == JsonValueKind.Object
        && style.TryGetProperty("layers", out var layers) && layers.ValueKind == JsonValueKind.Array
        && state.SavedStyleSignature == Signature(state);

    public static async Task<(JsonElement? Style, string? Issue)> ComposeAsync(
        StudioMapEditorState state, IStudioMapStyleCatalogDataSource styles, CancellationToken cancellationToken)
    {
        if (state.Basemap != "server-default")
        {
            return (null, "Select Layer styles only (server-default) to use the real server stylesheet. Symbolic external basemaps cannot be composed by this editor.");
        }
        if (state.Layers.Any(layer => !string.IsNullOrWhiteSpace(layer.Filter)))
        {
            return (null, "Save the filter in the server layer stylesheet before publishing; this editor cannot safely translate SQL filter text.");
        }

        JsonObject? result = state.CanonicalMapStyle is { ValueKind: JsonValueKind.Object } canonical
            && canonical.TryGetProperty("version", out var canonicalVersion) && canonicalVersion.ValueKind == JsonValueKind.Number
            && canonicalVersion.TryGetInt32(out var styleVersion) && styleVersion == 8
            ? JsonNode.Parse(canonical.GetRawText()) as JsonObject : null;
        var combinedSources = new JsonObject();
        var combinedLayers = new JsonArray();
        var originalOperationalSources = new HashSet<string>(state.Layers.Select(layer => layer.SourceRef), StringComparer.Ordinal);
        if (state.CanonicalPackage is { ValueKind: JsonValueKind.Object } package
            && package.TryGetProperty("sourceBindings", out var originalBindings) && originalBindings.ValueKind == JsonValueKind.Array)
        {
            foreach (var binding in originalBindings.EnumerateArray())
            {
                if (binding.ValueKind == JsonValueKind.Object && binding.TryGetProperty("sourceId", out var sourceId)
                    && sourceId.ValueKind == JsonValueKind.String)
                {
                    originalOperationalSources.Add(sourceId.GetString()!);
                }
            }
        }
        if (result is not null)
        {
            if (result["metadata"] is JsonObject oldMetadata && oldMetadata["honua:console:bindings"] is JsonObject oldBindings)
            {
                foreach (var binding in oldBindings)
                {
                    if (binding.Value is JsonObject oldBinding && oldBinding["sources"] is JsonArray oldSources)
                    {
                        foreach (var oldSource in oldSources.OfType<JsonValue>())
                        {
                            if (oldSource.TryGetValue<string>(out var name)) originalOperationalSources.Add(name);
                        }
                    }
                }
            }
            if (result.ContainsKey("terrain") || result.ContainsKey("imports") || result["layers"] is not JsonArray existingLayers
                || result["sources"] is not JsonObject existingSources
                || existingLayers.Any(value => value is not JsonObject || ((JsonObject)value).ContainsKey("ref")))
            {
                return (null, "The existing map style has unsupported terrain, imports, or layer dependencies. Preserve it in the server style editor.");
            }
            foreach (var source in existingSources)
            {
                if (!originalOperationalSources.Contains(source.Key))
                {
                    combinedSources[source.Key] = source.Value?.DeepClone();
                }
            }
            foreach (var value in existingLayers)
            {
                var existingLayer = (JsonObject)value!;
                if (existingLayer["source"] is null
                    || (existingLayer["source"] is JsonValue sourceValue && sourceValue.TryGetValue<string>(out var sourceName)
                        && combinedSources.ContainsKey(sourceName)))
                {
                    combinedLayers.Add(existingLayer.DeepClone());
                }
            }
        }
        var composedBindings = new JsonObject();
        var selectedStyleIds = state.Layers.Where(layer => !string.IsNullOrWhiteSpace(layer.Style)).Select(layer => layer.Style).ToHashSet(StringComparer.Ordinal);
        if (selectedStyleIds.Count > 0)
        {
            var catalog = await styles.GetStyleCatalogAsync(cancellationToken).ConfigureAwait(false);
            if (catalog.Issue is not null || selectedStyleIds.Any(id => !catalog.IsKnown(id)))
            {
                return (null, "Select a style advertised by the server; symbolic or unavailable style references cannot be published.");
            }
        }
        var index = 0;
        foreach (var layer in state.Layers.AsEnumerable().Reverse())
        {
            if (!int.TryParse(layer.BoundLayerId, NumberStyles.None, CultureInfo.InvariantCulture, out var layerId)
                || layerId < 0 || string.IsNullOrWhiteSpace(layer.BoundServiceId))
            {
                return (null, "Resolve each map layer to a published Honua service and numeric layer before saving a publishable style.");
            }
            var style = ExtractExistingStyle(state, layer);
            if (style is null)
            {
                var fetched = await styles.GetLayerStylesheetAsync(layerId, cancellationToken).ConfigureAwait(false);
                if (fetched.Issue is { } issue)
                {
                    return (null, $"The layer stylesheet could not be read: {issue.Detail}");
                }
                style = ParseStyle(fetched.Data!.Content);
                if (style is null)
                {
                    return (null, "The server returned a malformed MapLibre version 8 stylesheet.");
                }
                if (!string.IsNullOrWhiteSpace(layer.Style))
                {
                    var selected = await styles.GetStylesheetAsync(layer.Style, HonuaOgcStyleEncoding.MapLibre, cancellationToken).ConfigureAwait(false);
                    if (selected.Issue is { } selectedIssue)
                    {
                        return (null, $"The selected stylesheet could not be read: {selectedIssue.Detail}");
                    }
                    var selectedStyle = ParseStyle(selected.Data!.Content);
                    if (selectedStyle is null || ((JsonObject)selectedStyle["sources"]!).Count != 1
                        || ((JsonObject)style["sources"]!).Count != 1)
                    {
                        return (null, "Only a single-source server style can be applied to one published layer.");
                    }
                    var selectedSource = ((JsonObject)selectedStyle["sources"]!).First().Key;
                    selectedStyle["sources"] = new JsonObject
                    {
                        [selectedSource] = ((JsonObject)style["sources"]!).First().Value?.DeepClone()
                    };
                    var targetSourceLayers = ((JsonArray)style["layers"]!).OfType<JsonObject>()
                        .Select(item => item["source-layer"]?.ToJsonString()).Where(value => value is not null).Distinct().ToArray();
                    if (targetSourceLayers.Length > 1)
                    {
                        return (null, "The target style contains multiple vector source layers; use its server stylesheet directly.");
                    }
                    foreach (var selectedLayer in ((JsonArray)selectedStyle["layers"]!).OfType<JsonObject>())
                    {
                        if (selectedLayer["source"] is not null)
                        {
                            if (targetSourceLayers.Length == 1) selectedLayer["source-layer"] = JsonNode.Parse(targetSourceLayers[0]!);
                            else selectedLayer.Remove("source-layer");
                        }
                    }
                    style = selectedStyle;
                }
            }
            if (style.ContainsKey("terrain") || style.ContainsKey("imports")
                || ((JsonArray)style["layers"]!).Any(value => value is not JsonObject || ((JsonObject)value).ContainsKey("ref")))
            {
                return (null, "This stylesheet uses terrain, imports, or layer references that this editor cannot safely compose.");
            }
            if (result is null)
            {
                result = (JsonObject)style.DeepClone();
            }
            else if ((result["sprite"] is not null && style["sprite"] is not null && !JsonNode.DeepEquals(result["sprite"], style["sprite"]))
                || (result["glyphs"] is not null && style["glyphs"] is not null && !JsonNode.DeepEquals(result["glyphs"], style["glyphs"])))
            {
                return (null, "The layer styles use incompatible sprite or glyph resources.");
            }
            foreach (var resource in new[] { "sprite", "glyphs" })
            {
                if (result[resource] is null && style[resource] is { } resourceValue)
                {
                    result[resource] = resourceValue.DeepClone();
                }
            }
            var prefix = $"console-{index++}-";
            var sourceNames = new JsonArray();
            var layerNames = new JsonArray();
            foreach (var source in (JsonObject)style["sources"]!)
            {
                if (combinedSources.ContainsKey(prefix + source.Key)) return (null, "The existing map contains a conflicting source identifier.");
                sourceNames.Add(prefix + source.Key);
                combinedSources[prefix + source.Key] = source.Value?.DeepClone();
            }
            foreach (var value in (JsonArray)style["layers"]!)
            {
                var styledLayer = (JsonObject)value!.DeepClone();
                if (styledLayer["id"] is not JsonValue id || !id.TryGetValue<string>(out var name))
                {
                    return (null, "The server stylesheet contains a layer without an identifier.");
                }
                styledLayer["id"] = prefix + name;
                if (styledLayer["source"] is JsonValue sourceValue)
                {
                    if (!sourceValue.TryGetValue<string>(out var sourceName)
                        || !((JsonObject)style["sources"]!).ContainsKey(sourceName))
                    {
                        return (null, "The server stylesheet references a missing source.");
                    }
                    styledLayer["source"] = prefix + sourceName;
                    var layout = styledLayer["layout"]?.DeepClone() as JsonObject ?? new JsonObject();
                    layout["visibility"] = layer.Visible ? "visible" : "none";
                    styledLayer["layout"] = layout;
                }
                else if (combinedLayers.Any(value => value is JsonObject background && background["source"] is null))
                {
                    // Keep the genuine first stylesheet's background only once.
                    continue;
                }
                layerNames.Add(prefix + name);
                combinedLayers.Add(styledLayer);
            }
            composedBindings[layer.SourceRef] = new JsonObject
            {
                ["serviceId"] = layer.BoundServiceId,
                ["layerId"] = layer.BoundLayerId,
                ["styleId"] = layer.Style,
                ["sources"] = sourceNames,
                ["layers"] = layerNames
            };
        }
        if (result is null || combinedSources.Count == 0 || combinedLayers.Count == 0)
        {
            return (null, "The resolved styles contain no sources or render layers.");
        }
        var metadata = result["metadata"]?.DeepClone() as JsonObject ?? new JsonObject();
        metadata["honua:console:bindings"] = composedBindings;
        result["metadata"] = metadata;
        result["sources"] = combinedSources;
        result["layers"] = combinedLayers;
        return (JsonSerializer.SerializeToElement(result), null);
    }
    private static JsonObject? ParseStyle(string content)
    {
        try
        {
            var style = JsonNode.Parse(content) as JsonObject;
            return style?["version"]?.GetValue<int>() == 8 && style["sources"] is JsonObject && style["layers"] is JsonArray ? style : null;
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException)
        {
            return null;
        }
    }

    private static JsonObject? ExtractExistingStyle(StudioMapEditorState state, StudioMapLayerEditor layer)
    {
        if (state.CanonicalMapStyle is not { ValueKind: JsonValueKind.Object } canonical) return null;
        var style = ParseStyle(canonical.GetRawText());
        if (style is null) return null;
        var sources = (JsonObject)style["sources"]!;
        var layers = (JsonArray)style["layers"]!;
        var sourceNames = new HashSet<string>(StringComparer.Ordinal);
        var layerNames = new HashSet<string>(StringComparer.Ordinal);
        var trackedComposition = false;
        if (style["metadata"] is JsonObject metadata && metadata["honua:console:bindings"] is JsonObject bindings
            && bindings[layer.SourceRef] is JsonObject binding
            && StringValue(binding["serviceId"]) == layer.BoundServiceId
            && StringValue(binding["layerId"]) == layer.BoundLayerId
            && StringValue(binding["styleId"]) == layer.Style
            && binding["sources"] is JsonArray boundSources && binding["layers"] is JsonArray boundLayers)
        {
            trackedComposition = true;
            foreach (var value in boundSources.OfType<JsonValue>()) if (value.TryGetValue<string>(out var name)) sourceNames.Add(name);
            foreach (var value in boundLayers.OfType<JsonValue>()) if (value.TryGetValue<string>(out var name)) layerNames.Add(name);
        }
        else if (layer.HasCanonicalSource && string.IsNullOrWhiteSpace(layer.Style) && sources.ContainsKey(layer.SourceRef))
        {
            sourceNames.Add(layer.SourceRef);
            foreach (var value in layers.OfType<JsonObject>())
            {
                if (StringValue(value["source"]) == layer.SourceRef && value["id"] is JsonValue id
                    && id.TryGetValue<string>(out var name)) layerNames.Add(name);
            }
        }
        if (sourceNames.Count == 0 || layerNames.Count == 0 || sourceNames.Any(name => !sources.ContainsKey(name))) return null;
        var renamedSources = sourceNames.ToDictionary(name => name,
            name => trackedComposition ? RemoveOwnPrefix(name) : name, StringComparer.Ordinal);
        var selectedSources = new JsonObject();
        foreach (var name in sourceNames) selectedSources[renamedSources[name]] = sources[name]?.DeepClone();
        var selectedLayers = new JsonArray();
        foreach (var value in layers.OfType<JsonObject>())
        {
            if (value["id"] is JsonValue id && id.TryGetValue<string>(out var name) && layerNames.Contains(name))
            {
                var selectedLayer = (JsonObject)value.DeepClone();
                selectedLayer["id"] = trackedComposition ? RemoveOwnPrefix(name) : name;
                if (StringValue(selectedLayer["source"]) is { } sourceName && renamedSources.TryGetValue(sourceName, out var renamed))
                {
                    selectedLayer["source"] = renamed;
                }
                selectedLayers.Add(selectedLayer);
            }
        }
        style["sources"] = selectedSources;
        style["layers"] = selectedLayers;
        return style;
    }

    private static string? StringValue(JsonNode? node) => node is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;

    // Called only for names explicitly recorded in our previous composition metadata.
    private static string RemoveOwnPrefix(string name)
    {
        if (!name.StartsWith("console-", StringComparison.Ordinal)) return name;
        var separator = name.IndexOf('-', "console-".Length);
        return separator > 0 && int.TryParse(name.AsSpan("console-".Length, separator - "console-".Length), out _)
            ? name[(separator + 1)..] : name;
    }

}
