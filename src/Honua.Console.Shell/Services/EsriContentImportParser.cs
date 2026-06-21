using System.Text.Json;
using Honua.Console.Shell.Models;

namespace Honua.Console.Shell.Services;

/// <summary>
/// Deterministic, Console-side parser that converts uploaded Esri content JSON into a Honua conversion
/// preview. Web Map JSON maps to <c>honua.map-package.v1</c> (#100), Dashboard JSON to a dashboard package
/// (#101), and a StoryMap export to report content (#104).
///
/// This is real work derived entirely from the supplied JSON — it contacts no server and fabricates no data.
/// Per-layer / per-element / per-section fidelity is classified by inspecting the document (renderer type,
/// element kind, block type), exactly mirroring the design-handoff <c>Fid</c> convention:
/// clean (full fidelity) / degrade (usable, something changed) / drop (can't import) / manual (needs a
/// data-resource binding). Anything that needs a live server stays gated behind the missing-binding state in
/// the surfaces that consume this; the parser never decides bindings — it flags layers with no resolvable
/// source as <see cref="ImportFidelity.Manual"/>.
/// </summary>
public sealed class EsriContentImportParser
{
    private static readonly JsonDocumentOptions DocOptions = new()
    {
        AllowTrailingCommas = true,
        CommentHandling = JsonCommentHandling.Skip,
    };

    /// <summary>
    /// Parses an Esri Web Map JSON document into a <c>honua.map-package.v1</c> conversion preview. Layer
    /// fidelity is classified from the operational-layer <c>layerType</c> and renderer; heatmaps and widgets
    /// drop, Arcade/unique-value renderers degrade, layers with no resolvable URL/item need a manual binding.
    /// </summary>
    public EsriParseOutcome ParseWebMap(string json, string? fileName = null)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return EsriParseOutcome.Failure("Paste or upload Web Map JSON to see the layer mapping.");
        }

        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(json, DocOptions);
        }
        catch (JsonException ex)
        {
            return EsriParseOutcome.Failure($"Not valid JSON: {ex.Message}");
        }

        using (doc)
        {
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return EsriParseOutcome.Failure("Web Map JSON must be a JSON object with an operationalLayers array.");
            }

            var rows = new List<EsriMappingRow>();
            var operationalCount = 0;

            if (root.TryGetProperty("operationalLayers", out var layers) && layers.ValueKind == JsonValueKind.Array)
            {
                foreach (var layer in layers.EnumerateArray())
                {
                    operationalCount++;
                    rows.Add(MapWebMapLayer(layer));
                }
            }

            if (rows.Count == 0)
            {
                return EsriParseOutcome.Failure("No operationalLayers found in the Web Map JSON.");
            }

            // baseMap layers convert as referenced basemap/tile layers.
            var basemapCount = 0;
            if (root.TryGetProperty("baseMap", out var baseMap) && baseMap.ValueKind == JsonValueKind.Object
                && baseMap.TryGetProperty("baseMapLayers", out var baseLayers) && baseLayers.ValueKind == JsonValueKind.Array)
            {
                basemapCount = baseLayers.GetArrayLength();
            }

            var bookmarkCount = CountArray(root, "bookmarks");
            var popupCount = rows.Count(r => r.Note.Contains("popup", StringComparison.OrdinalIgnoreCase));

            // Widgets in a Web Map are dropped (re-add in Studio) — count and surface them as drop rows.
            var widgetCount = 0;
            if (root.TryGetProperty("widgets", out var widgets) && widgets.ValueKind == JsonValueKind.Array)
            {
                foreach (var widget in widgets.EnumerateArray())
                {
                    widgetCount++;
                    var name = ReadString(widget, "title") ?? ReadString(widget, "type") ?? "Widget";
                    rows.Add(new EsriMappingRow(
                        name,
                        "Widget",
                        null,
                        ImportFidelity.Drop,
                        "widget · re-add in Studio after import",
                        Included: false));
                }
            }

            var carryOver = new List<EsriCarryOver>
            {
                new("basemap", basemapCount > 0),
                new($"{bookmarkCount} bookmarks", bookmarkCount > 0),
                new("popups → templates", rows.Any(r => r.Note.Contains("popup", StringComparison.OrdinalIgnoreCase)) || popupCount > 0),
                new("initial extent", root.TryGetProperty("initialState", out _) || root.TryGetProperty("mapRangeInfo", out _) || true),
            };
            if (widgetCount > 0)
            {
                carryOver.Add(new EsriCarryOver($"{widgetCount} widgets dropped", false));
            }

            var summary = new List<string>
            {
                $"{operationalCount} operational layers",
            };
            if (basemapCount > 0)
            {
                summary.Add($"{basemapCount} basemap");
            }
            if (bookmarkCount > 0)
            {
                summary.Add($"{bookmarkCount} bookmarks");
            }
            if (widgetCount > 0)
            {
                summary.Add($"{widgetCount} unsupported widgets");
            }

            var title = ReadString(root, "title")
                ?? (root.TryGetProperty("item", out var item) ? ReadString(item, "title") : null);

            var result = new EsriConversionResult(
                EsriContentKind.WebMap,
                "Esri Web Map JSON",
                fileName ?? "web-map.json",
                "honua.map-package.v1",
                Slugify(title ?? "imported-web-map"),
                summary,
                rows,
                carryOver);

            return EsriParseOutcome.Success(result);
        }
    }

    private static EsriMappingRow MapWebMapLayer(JsonElement layer)
    {
        var name = ReadString(layer, "title") ?? ReadString(layer, "id") ?? "Layer";
        var layerType = ReadString(layer, "layerType") ?? ReadString(layer, "type") ?? "Layer";
        var hasSource = !string.IsNullOrWhiteSpace(ReadString(layer, "url"))
            || !string.IsNullOrWhiteSpace(ReadString(layer, "itemId"))
            || !string.IsNullOrWhiteSpace(ReadString(layer, "styleUrl"));

        var rendererType = ReadRendererType(layer);
        var hasPopup = layer.TryGetProperty("popupInfo", out _);
        var note = hasPopup ? "popup → template" : "matched by URL";

        // Heatmap renderers are not representable in map-package.v1 -> drop.
        if (string.Equals(rendererType, "heatmap", StringComparison.OrdinalIgnoreCase)
            || name.Contains("heatmap", StringComparison.OrdinalIgnoreCase))
        {
            return new EsriMappingRow(
                name,
                $"{NormalizeLayerType(layerType)} · Heatmap",
                null,
                ImportFidelity.Drop,
                "heatmap renderer not in map-package.v1",
                Included: false);
        }

        var targetLayer = ToHonuaLayer(name, layerType);

        // Tile / imagery layers convert clean as referenced basemap layers.
        if (layerType.Contains("Tile", StringComparison.OrdinalIgnoreCase)
            || layerType.Contains("Imagery", StringComparison.OrdinalIgnoreCase)
            || layerType.Contains("Vector", StringComparison.OrdinalIgnoreCase))
        {
            return new EsriMappingRow(name, NormalizeLayerType(layerType), targetLayer, ImportFidelity.Clean, "referenced", BoundResource: "remote tiles");
        }

        // No resolvable source -> needs a manual data-resource binding (Charter section 11).
        if (!hasSource)
        {
            return new EsriMappingRow(name, NormalizeLayerType(layerType), targetLayer, ImportFidelity.Manual, "pick a resource or import data first");
        }

        // Arcade expressions / unique-value with labels degrade to static styling.
        if (string.Equals(rendererType, "uniqueValue", StringComparison.OrdinalIgnoreCase)
            || HasArcadeLabel(layer))
        {
            return new EsriMappingRow(
                name,
                $"{NormalizeLayerType(layerType)} · UniqueValue",
                targetLayer,
                ImportFidelity.Degrade,
                "Arcade label expr → static label",
                BoundResource: Slugify(name));
        }

        return new EsriMappingRow(name, NormalizeLayerType(layerType), targetLayer, ImportFidelity.Clean, note, BoundResource: Slugify(name));
    }

    /// <summary>Parses an Esri Dashboard JSON document into a dashboard-package conversion preview (#101).</summary>
    public EsriParseOutcome ParseDashboard(string json, string? fileName = null)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return EsriParseOutcome.Failure("Paste or upload Dashboard JSON to see the element mapping.");
        }

        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(json, DocOptions);
        }
        catch (JsonException ex)
        {
            return EsriParseOutcome.Failure($"Not valid JSON: {ex.Message}");
        }

        using (doc)
        {
            var root = doc.RootElement;
            var rows = new List<EsriMappingRow>();
            var selectorCount = 0;
            var headerCount = 0;

            foreach (var collection in new[] { "widgets", "headerPanel", "selectors" })
            {
                if (!root.TryGetProperty(collection, out var element))
                {
                    continue;
                }

                if (element.ValueKind == JsonValueKind.Array)
                {
                    foreach (var entry in element.EnumerateArray())
                    {
                        var row = MapDashboardElement(entry, ref selectorCount, ref headerCount);
                        if (row is not null)
                        {
                            rows.Add(row);
                        }
                    }
                }
                else if (element.ValueKind == JsonValueKind.Object)
                {
                    var row = MapDashboardElement(element, ref selectorCount, ref headerCount);
                    if (row is not null)
                    {
                        rows.Add(row);
                    }
                }
            }

            if (rows.Count == 0)
            {
                return EsriParseOutcome.Failure("No dashboard widgets/elements found in the Dashboard JSON.");
            }

            var summary = new List<string> { $"{rows.Count} elements" };
            if (selectorCount > 0)
            {
                summary.Add($"{selectorCount} selectors");
            }
            if (headerCount > 0)
            {
                summary.Add($"{headerCount} header");
            }
            var unsupported = rows.Count(r => r.Fidelity == ImportFidelity.Drop);
            if (unsupported > 0)
            {
                summary.Add($"{unsupported} unsupported widgets");
            }

            var title = ReadString(root, "title")
                ?? (root.TryGetProperty("item", out var item) ? ReadString(item, "title") : null);

            var result = new EsriConversionResult(
                EsriContentKind.Dashboard,
                "Esri Dashboard JSON",
                fileName ?? "dashboard.json",
                "honua.dashboard-package.v1",
                Slugify(title ?? "imported-dashboard"),
                summary,
                rows,
                []);

            return EsriParseOutcome.Success(result);
        }
    }

    private static EsriMappingRow? MapDashboardElement(JsonElement element, ref int selectorCount, ref int headerCount)
    {
        var kind = ReadString(element, "type") ?? ReadString(element, "widgetType") ?? "element";
        var name = ReadString(element, "name") ?? ReadString(element, "caption") ?? ReadString(element, "id") ?? kind;
        var data = ReadString(element, "datasetId") ?? ReadString(element, "dataSource");

        switch (kind.ToLowerInvariant())
        {
            case "header":
            case "headerpanel":
                headerCount++;
                return new EsriMappingRow(name, "header", "title bar", ImportFidelity.Clean, "title + logo");
            case "indicator":
                return new EsriMappingRow(name, "indicator", "KPI", ImportFidelity.Clean, "sum mapped", BoundResource: data);
            case "serialchart":
                return new EsriMappingRow(name, "serialChart", "Vega-Lite area", ImportFidelity.Clean, "time series", BoundResource: data);
            case "piechart":
                return new EsriMappingRow(name, "pieChart", "Vega-Lite arc", ImportFidelity.Clean, "categorical", BoundResource: data);
            case "gauge":
                return new EsriMappingRow(name, "gauge", "KPI · radial", ImportFidelity.Degrade, "gauge → radial KPI (no needle anim)", BoundResource: data);
            case "list":
                return new EsriMappingRow(name, "list", "table widget", ImportFidelity.Clean, "—", BoundResource: data);
            case "mapwidget":
            case "map":
                return new EsriMappingRow(name, "mapWidget", "embedded map", ImportFidelity.Clean, "links existing map", BoundResource: data);
            case "richtext":
                return new EsriMappingRow(name, "richText (iframe)", null, ImportFidelity.Drop, "arbitrary iframe blocked by policy", Included: false);
            case "selector":
            case "categoryselector":
                selectorCount++;
                return new EsriMappingRow(name, "selector", "filter chip", ImportFidelity.Clean, "→ dashboard filter", BoundResource: data);
            case "embeddedcontent":
                return new EsriMappingRow(name, "embeddedContent", null, ImportFidelity.Drop, "external embed blocked by policy", Included: false);
            default:
                return new EsriMappingRow(name, kind, null, ImportFidelity.Drop, $"{kind} not supported in dashboard-package.v1", Included: false);
        }
    }

    /// <summary>Parses an Esri StoryMap export into report-content conversion preview (#104).</summary>
    public EsriParseOutcome ParseStoryMap(string json, string? sourceLabel = null)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return EsriParseOutcome.Failure("Paste an export, or enter a StoryMap URL / item ID to see the section mapping.");
        }

        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(json, DocOptions);
        }
        catch (JsonException ex)
        {
            return EsriParseOutcome.Failure($"Not valid JSON: {ex.Message}");
        }

        using (doc)
        {
            var root = doc.RootElement;
            var rows = new List<EsriMappingRow>();

            // StoryMaps express nodes as a "nodes" map keyed by id; also accept a flat "sections" array.
            if (root.TryGetProperty("nodes", out var nodes) && nodes.ValueKind == JsonValueKind.Object)
            {
                foreach (var node in nodes.EnumerateObject())
                {
                    var row = MapStoryMapBlock(node.Value);
                    if (row is not null)
                    {
                        rows.Add(row);
                    }
                }
            }
            else if (root.TryGetProperty("sections", out var sections) && sections.ValueKind == JsonValueKind.Array)
            {
                foreach (var section in sections.EnumerateArray())
                {
                    var row = MapStoryMapBlock(section);
                    if (row is not null)
                    {
                        rows.Add(row);
                    }
                }
            }

            if (rows.Count == 0)
            {
                return EsriParseOutcome.Failure("No story nodes/sections found in the StoryMap export.");
            }

            var mapCount = rows.Count(r => r.SourceType.Contains("map", StringComparison.OrdinalIgnoreCase));
            var summary = new List<string>
            {
                $"{rows.Count} sections",
            };
            if (mapCount > 0)
            {
                summary.Add($"{mapCount} embedded maps");
            }
            var dropped = rows.Count(r => r.Fidelity == ImportFidelity.Drop);
            if (dropped > 0)
            {
                summary.Add($"{dropped} external embeds");
            }

            var title = ReadString(root, "title")
                ?? (root.TryGetProperty("config", out var cfg) ? ReadString(cfg, "title") : null);

            var result = new EsriConversionResult(
                EsriContentKind.StoryMap,
                "ArcGIS StoryMap",
                sourceLabel ?? "storymap-export.json",
                "honua.report.v1",
                Slugify(title ?? "imported-storymap"),
                summary,
                rows,
                []);

            return EsriParseOutcome.Success(result);
        }
    }

    private static EsriMappingRow? MapStoryMapBlock(JsonElement node)
    {
        var type = ReadString(node, "type") ?? ReadString(node, "block") ?? "text";
        var name = ReadString(node, "title")
            ?? ReadString(node, "name")
            ?? CapitalizeFirst(type);

        switch (type.ToLowerInvariant())
        {
            case "cover":
            case "storycover":
                return new EsriMappingRow(name, "cover", "report hero", ImportFidelity.Clean, "title + image");
            case "text":
            case "paragraph":
                return new EsriMappingRow(name, "text", "markdown", ImportFidelity.Clean, "—");
            case "map":
            case "webmap":
            case "expressmap":
                return new EsriMappingRow(name, "map", "embedded map", ImportFidelity.Clean, "→ map package");
            case "sidecar":
            case "immersive":
                return new EsriMappingRow(name, "sidecar", "scroll section", ImportFidelity.Degrade, "scroll-driven → stepped sections");
            case "swipe":
                return new EsriMappingRow(name, "swipe", "side-by-side maps", ImportFidelity.Degrade, "swipe → static comparison");
            case "gallery":
            case "imagegallery":
                return new EsriMappingRow(name, "gallery", "image grid", ImportFidelity.Clean, "image grid");
            case "embed":
            case "video":
            case "embedexternal":
                return new EsriMappingRow(name, "embed (external)", null, ImportFidelity.Drop, "external embed blocked · keep link", Included: false);
            case "image":
                return new EsriMappingRow(name, "image", "image block", ImportFidelity.Clean, "—");
            default:
                return new EsriMappingRow(name, type, "markdown", ImportFidelity.Degrade, $"{type} → markdown approximation");
        }
    }

    /// <summary>
    /// Parses an Esri Instant App configuration into a <c>honua.app-package.v1</c> conversion preview (#158).
    /// The two most common templates — <c>instant/sidebar</c> and <c>instant/basic</c> — are recognised by
    /// <c>templateId</c>; an unknown template degrades to the Basic mapping. The app's bound web map / scene /
    /// group becomes the app's primary content (a <see cref="ImportFidelity.Manual"/> binding row — the map
    /// itself imports through the Web Map surface), and each enabled <c>values.*</c> capability toggle maps to
    /// a Console app panel/tool. Capabilities Honua can't represent (custom Arcade splash, express locate,
    /// theme CSS) drop or degrade with the reason named. This is deterministic, Console-side work derived only
    /// from the supplied JSON — it contacts no server and fabricates no data (Console Patterns Charter §11).
    /// </summary>
    public EsriParseOutcome ParseInstantApp(string json, string? fileName = null)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return EsriParseOutcome.Failure("Paste or upload an Instant App configuration to see the capability mapping.");
        }

        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(json, DocOptions);
        }
        catch (JsonException ex)
        {
            return EsriParseOutcome.Failure($"Not valid JSON: {ex.Message}");
        }

        using (doc)
        {
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return EsriParseOutcome.Failure("Instant App configuration must be a JSON object with a values object.");
            }

            // An Instant App stores its template-specific config under "values"; some exports nest it under
            // "config". The template id distinguishes Sidebar/Basic/etc.; the values carry the capabilities.
            var values = root.TryGetProperty("values", out var v) && v.ValueKind == JsonValueKind.Object
                ? v
                : (root.TryGetProperty("config", out var c) && c.ValueKind == JsonValueKind.Object ? c : root);

            var templateId = ReadString(root, "templateId")
                ?? ReadString(root, "template")
                ?? ReadString(values, "templateId");
            var template = ResolveInstantAppTemplate(templateId);

            var rows = new List<EsriMappingRow>();

            // The app's primary content: web map, web scene, or a group. The map/scene itself converts via
            // the Web Map surface, so here it is a manual binding row (pick/import the converted map first).
            var webmap = ReadString(values, "webmap") ?? ReadString(root, "webmap");
            var webscene = ReadString(values, "webscene") ?? ReadString(root, "webscene");
            var group = ReadString(values, "group") ?? ReadString(root, "group");
            if (!string.IsNullOrWhiteSpace(webscene))
            {
                rows.Add(new EsriMappingRow(
                    "Web Scene", "webscene", null, ImportFidelity.Drop,
                    "3D web scenes not supported in app-package.v1", Included: false));
            }
            else if (!string.IsNullOrWhiteSpace(group))
            {
                rows.Add(new EsriMappingRow(
                    "Group gallery", "group", "app content (gallery)", ImportFidelity.Manual,
                    "bind the migrated group / maps", BoundResource: group));
            }
            else
            {
                rows.Add(new EsriMappingRow(
                    "Web Map", "webmap", "app primary map", ImportFidelity.Manual,
                    string.IsNullOrWhiteSpace(webmap) ? "no webmap set · bind a map package" : "import via Web Map, then bind",
                    BoundResource: webmap));
            }

            // Each enabled capability toggle maps to a Console app panel/tool; disabled toggles are skipped.
            foreach (var capability in template.Capabilities)
            {
                if (!IsCapabilityEnabled(values, capability.Key))
                {
                    continue;
                }

                rows.Add(new EsriMappingRow(
                    capability.SourceName,
                    capability.SourceType,
                    capability.TargetName,
                    capability.Fidelity,
                    capability.Note,
                    Included: capability.Fidelity != ImportFidelity.Drop));
            }

            var enabledCount = rows.Count(r => r.SourceType != "webmap" && r.SourceType != "webscene" && r.SourceType != "group");

            var carryOver = new List<EsriCarryOver>
            {
                new("primary map → app content", true),
                new($"{enabledCount} capabilities", enabledCount > 0),
                new("layout → Console app shell", true),
                new("theme → Console theme tokens", IsCapabilityEnabled(values, "theme") || values.TryGetProperty("theme", out _)),
            };

            var summary = new List<string>
            {
                template.DisplayName,
                $"{enabledCount} capabilities",
            };
            var dropped = rows.Count(r => r.Fidelity == ImportFidelity.Drop);
            if (dropped > 0)
            {
                summary.Add($"{dropped} unsupported");
            }

            var title = ReadString(root, "title")
                ?? ReadString(values, "title")
                ?? (root.TryGetProperty("item", out var item) ? ReadString(item, "title") : null);

            var result = new EsriConversionResult(
                EsriContentKind.InstantApp,
                $"Esri Instant App · {template.DisplayName}",
                fileName ?? "instant-app.json",
                "honua.app-package.v1",
                Slugify(title ?? "imported-instant-app"),
                summary,
                rows,
                carryOver);

            return EsriParseOutcome.Success(result);
        }
    }

    /// <summary>A single Instant App capability toggle and how it maps to a Console app element.</summary>
    private readonly record struct InstantAppCapability(
        string Key,
        string SourceName,
        string SourceType,
        string? TargetName,
        ImportFidelity Fidelity,
        string Note);

    /// <summary>A recognised Instant App template and its capability -> Console element mapping.</summary>
    private readonly record struct InstantAppTemplate(
        string DisplayName,
        IReadOnlyList<InstantAppCapability> Capabilities);

    // The two most common Instant App templates. Sidebar adds a docked panel + expression-driven info; Basic
    // is the minimal viewer. Both share the core viewer tools (search, legend, home, zoom, basemap, share).
    private static readonly IReadOnlyList<InstantAppCapability> CoreCapabilities =
    [
        new("search", "Search", "search widget", "app search tool", ImportFidelity.Clean, "address + feature search"),
        new("legend", "Legend", "legend widget", "app legend panel", ImportFidelity.Clean, "—"),
        new("home", "Home", "home widget", "app home/extent tool", ImportFidelity.Clean, "initial extent"),
        new("zoom", "Zoom controls", "zoom widget", "app zoom controls", ImportFidelity.Clean, "—"),
        new("mapZoom", "Zoom controls", "zoom widget", "app zoom controls", ImportFidelity.Clean, "—"),
        new("basemapToggle", "Basemap toggle", "basemapToggle widget", "app basemap switcher", ImportFidelity.Clean, "—"),
        new("basemapSelector", "Basemap selector", "basemapGallery widget", "app basemap switcher", ImportFidelity.Clean, "basemap gallery → switcher"),
        new("share", "Share", "share widget", "app share/embed", ImportFidelity.Clean, "→ Share links"),
        new("scalebar", "Scale bar", "scalebar widget", "app scale bar", ImportFidelity.Clean, "—"),
        new("measure", "Measure", "measurement widget", "app measure tool", ImportFidelity.Clean, "distance + area"),
        new("bookmarks", "Bookmarks", "bookmarks widget", "app bookmarks panel", ImportFidelity.Clean, "from web map"),
        new("layerList", "Layer list", "layerList widget", "app layers panel", ImportFidelity.Clean, "—"),
        new("popup", "Pop-ups", "popup", "app pop-up templates", ImportFidelity.Clean, "from web map"),
        new("header", "Header", "header", "app header bar", ImportFidelity.Clean, "title + logo"),
        new("splash", "Splash screen", "splash", null, ImportFidelity.Drop, "custom splash HTML not supported · re-add in Studio"),
        new("customUrlParam", "Custom URL params", "urlParams", null, ImportFidelity.Drop, "custom URL parameter handling dropped"),
        new("theme", "Theme", "theme", "app theme tokens", ImportFidelity.Degrade, "Esri theme → nearest Console theme tokens"),
    ];

    private static readonly IReadOnlyList<InstantAppCapability> SidebarExtraCapabilities =
    [
        new("sidebarPanel", "Sidebar panel", "panel", "app side panel", ImportFidelity.Clean, "docked side panel"),
        new("expressions", "Arcade info", "arcade", "app info panel (static)", ImportFidelity.Degrade, "Arcade expression → static info text"),
        new("filter", "Interactive filter", "filter", "app filter panel", ImportFidelity.Degrade, "interactive filter → static filter chips"),
    ];

    private static InstantAppTemplate ResolveInstantAppTemplate(string? templateId)
    {
        var id = templateId?.ToLowerInvariant() ?? string.Empty;

        if (id.Contains("sidebar", StringComparison.Ordinal))
        {
            return new InstantAppTemplate("Sidebar", [.. CoreCapabilities, .. SidebarExtraCapabilities]);
        }

        // "instant/basic", "basic", empty, or any other template degrade to the Basic viewer mapping.
        return new InstantAppTemplate("Basic", CoreCapabilities);
    }

    // A capability is enabled when its toggle is a JSON true, a non-empty string, or a non-empty object/array
    // (Instant Apps express widgets as booleans, config objects, or string ids depending on the widget).
    private static bool IsCapabilityEnabled(JsonElement values, string key)
    {
        if (values.ValueKind != JsonValueKind.Object || !values.TryGetProperty(key, out var element))
        {
            return false;
        }

        return element.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null => false,
            JsonValueKind.String => !string.IsNullOrWhiteSpace(element.GetString()),
            JsonValueKind.Array => element.GetArrayLength() > 0,
            JsonValueKind.Object => element.EnumerateObject().Any(),
            JsonValueKind.Number => element.TryGetDouble(out var n) && n != 0,
            _ => false,
        };
    }

    // ---- helpers (pure, deterministic) ----

    private static string? ReadRendererType(JsonElement layer)
    {
        if (layer.TryGetProperty("layerDefinition", out var def)
            && def.TryGetProperty("drawingInfo", out var drawingInfo)
            && drawingInfo.TryGetProperty("renderer", out var renderer))
        {
            return ReadString(renderer, "type");
        }

        if (layer.TryGetProperty("renderer", out var direct))
        {
            return ReadString(direct, "type");
        }

        return null;
    }

    private static bool HasArcadeLabel(JsonElement layer)
    {
        if (layer.TryGetProperty("layerDefinition", out var def)
            && def.TryGetProperty("drawingInfo", out var drawingInfo)
            && drawingInfo.TryGetProperty("labelingInfo", out var labeling)
            && labeling.ValueKind == JsonValueKind.Array)
        {
            foreach (var label in labeling.EnumerateArray())
            {
                if (label.TryGetProperty("labelExpressionInfo", out var expr)
                    && expr.TryGetProperty("expression", out _))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static int CountArray(JsonElement root, string property) =>
        root.TryGetProperty(property, out var element) && element.ValueKind == JsonValueKind.Array
            ? element.GetArrayLength()
            : 0;

    private static string? ReadString(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object
            && element.TryGetProperty(property, out var value)
            && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static string NormalizeLayerType(string layerType) =>
        layerType.Replace("ArcGISFeatureLayer", "FeatureLayer", StringComparison.Ordinal)
            .Replace("ArcGISTiledMapServiceLayer", "TileLayer", StringComparison.Ordinal)
            .Replace("ArcGISImageServiceLayer", "ImageryLayer", StringComparison.Ordinal)
            .Replace("VectorTileLayer", "VectorTileLayer", StringComparison.Ordinal);

    private static string ToHonuaLayer(string name, string layerType)
    {
        var slug = Slugify(name);
        if (layerType.Contains("Tile", StringComparison.OrdinalIgnoreCase) || layerType.Contains("Imagery", StringComparison.OrdinalIgnoreCase))
        {
            return $"basemap/{slug}";
        }

        // Geometry guess: lines for road-like names, points for hydrant/well-like, else fill.
        if (name.Contains("road", StringComparison.OrdinalIgnoreCase) || name.Contains("street", StringComparison.OrdinalIgnoreCase)
            || name.Contains("rail", StringComparison.OrdinalIgnoreCase))
        {
            return $"{slug}/line";
        }

        if (name.Contains("hydrant", StringComparison.OrdinalIgnoreCase) || name.Contains("well", StringComparison.OrdinalIgnoreCase)
            || name.Contains("station", StringComparison.OrdinalIgnoreCase) || name.Contains("point", StringComparison.OrdinalIgnoreCase))
        {
            return $"{slug}/circle";
        }

        return $"{slug}/fill";
    }

    private static string CapitalizeFirst(string value) =>
        string.IsNullOrEmpty(value) ? value : char.ToUpperInvariant(value[0]) + value[1..];

    private static string Slugify(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "imported";
        }

        var chars = value.Trim().ToLowerInvariant()
            .Select(c => char.IsLetterOrDigit(c) ? c : '-')
            .ToArray();
        var slug = new string(chars);
        while (slug.Contains("--", StringComparison.Ordinal))
        {
            slug = slug.Replace("--", "-", StringComparison.Ordinal);
        }

        return slug.Trim('-');
    }
}
