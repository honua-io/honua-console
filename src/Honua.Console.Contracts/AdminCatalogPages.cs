using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Honua.Console.Contracts;

public readonly record struct HonuaAdminCatalogQuery(int Page, int PageSize, string? NameQuery)
{
    public const int MaxPageSize = 100;

    public static HonuaAdminCatalogQuery First { get; } = new(1, MaxPageSize, null);

    public HonuaAdminCatalogQuery Next() => this with { Page = Page + 1 };

    public string AppendTo(string path, string? serviceName = null)
    {
        var parts = new List<string>
        {
            "page=" + Page.ToString(CultureInfo.InvariantCulture),
            "pageSize=" + PageSize.ToString(CultureInfo.InvariantCulture),
        };
        if (!string.IsNullOrWhiteSpace(serviceName))
        {
            parts.Add("serviceName=" + Uri.EscapeDataString(serviceName));
        }

        if (!string.IsNullOrWhiteSpace(NameQuery))
        {
            parts.Add("q=" + Uri.EscapeDataString(NameQuery.Trim()));
        }

        return path + "?" + string.Join('&', parts);
    }
}

public readonly record struct HonuaAdminLayerListRequest(string? ServiceName, HonuaAdminCatalogQuery Page);

[JsonConverter(typeof(HonuaAdminCatalogPageConverter))]
public sealed record HonuaAdminServicePage
{
    public HonuaAdminServiceSummary[] Items { get; init; } = [];

    public int Page { get; init; }

    public int PageSize { get; init; }

    public int Total { get; init; }

    public int LayerTotal { get; init; }
}

[JsonConverter(typeof(HonuaAdminCatalogPageConverter))]
public sealed record HonuaAdminLayerPage
{
    public HonuaAdminPublishedLayerSummary[] Items { get; init; } = [];

    public int Page { get; init; }

    public int PageSize { get; init; }

    public int Total { get; init; }
}

public static class HonuaAdminCatalogReader
{
    public static async Task<HonuaAdminEndpointResult<IReadOnlyList<TItem>>> ReadAllAsync<TPage, TItem>(
        Func<HonuaAdminCatalogQuery, CancellationToken, Task<HonuaAdminEndpointResult<TPage>>> read,
        Func<TPage, IReadOnlyList<TItem>?> items,
        Func<TPage, int> total,
        CancellationToken cancellationToken)
    {
        var collected = new List<TItem>();
        var query = HonuaAdminCatalogQuery.First;
        while (true)
        {
            var result = await read(query, cancellationToken).ConfigureAwait(false);
            if (result.Issue is { } issue)
            {
                return HonuaAdminEndpointResult<IReadOnlyList<TItem>>.FromIssue(issue);
            }

            if (result.Data is null)
            {
                return HonuaAdminEndpointResult<IReadOnlyList<TItem>>.FromIssue(new HonuaAdminEndpointIssue(
                    "Unavailable",
                    "catalog-page",
                    "The server did not return a catalog page."));
            }

            var pageItems = items(result.Data) ?? [];
            collected.AddRange(pageItems);
            var matchTotal = total(result.Data);
            var pageCount = query.PageSize <= 0 ? 1 : Math.Max(1, (matchTotal + query.PageSize - 1) / query.PageSize);
            if (pageItems.Count == 0 || collected.Count >= matchTotal || query.Page >= pageCount)
            {
                return HonuaAdminEndpointResult<IReadOnlyList<TItem>>.FromData(collected);
            }

            query = query.Next();
        }
    }

    internal static HonuaAdminServicePage ReadServicePage(JsonElement body, JsonSerializerOptions options) =>
        Read<HonuaAdminServicePage, HonuaAdminServiceSummary>(
            body,
            CompleteServices,
            (items, element) => ServicePage(items, element, options));

    internal static HonuaAdminLayerPage ReadLayerPage(JsonElement body, JsonSerializerOptions options) =>
        Read<HonuaAdminLayerPage, HonuaAdminPublishedLayerSummary>(
            body,
            CompleteLayers,
            (items, element) => LayerPage(items, element, options));

    internal static void WriteServicePage(Utf8JsonWriter writer, HonuaAdminServicePage page, JsonSerializerOptions options) =>
        new CatalogWriter(writer, options).Write(page.Items, new PageNumbers(page.Page, page.PageSize, page.Total, page.LayerTotal));

    internal static void WriteLayerPage(Utf8JsonWriter writer, HonuaAdminLayerPage page, JsonSerializerOptions options) =>
        new CatalogWriter(writer, options).Write(page.Items, new PageNumbers(page.Page, page.PageSize, page.Total, null));

    private static TPage Read<TPage, TItem>(
        JsonElement body,
        Func<TItem[], TPage> completeList,
        Func<TItem[], JsonElement, TPage> page)
        where TItem : class
    {
        if (body.ValueKind == JsonValueKind.Array)
        {
            return completeList(Items<TItem>(body));
        }

        if (body.ValueKind != JsonValueKind.Object)
        {
            throw new JsonException("An admin catalog page is an object or a list.");
        }

        var listed = Property(body, "items");
        var items = listed is null ? [] : Items<TItem>(listed.Value);
        return page(items, body);
    }

    private static HonuaAdminServicePage CompleteServices(HonuaAdminServiceSummary[] items) => new()
    {
        Items = items,
        Page = 1,
        PageSize = items.Length,
        Total = items.Length,
        LayerTotal = items.Sum(static service => service.LayerCount)
    };

    private static HonuaAdminServicePage ServicePage(
        HonuaAdminServiceSummary[] items,
        JsonElement body,
        JsonSerializerOptions options) => new()
        {
            Items = items,
            Page = Number(body, "page", options),
            PageSize = Number(body, "pageSize", options),
            Total = Number(body, "total", options),
            LayerTotal = Number(body, "layerTotal", options)
        };

    private static HonuaAdminLayerPage CompleteLayers(HonuaAdminPublishedLayerSummary[] items) => new()
    {
        Items = items,
        Page = 1,
        PageSize = items.Length,
        Total = items.Length
    };

    private static HonuaAdminLayerPage LayerPage(
        HonuaAdminPublishedLayerSummary[] items,
        JsonElement body,
        JsonSerializerOptions options) => new()
        {
            Items = items,
            Page = Number(body, "page", options),
            PageSize = Number(body, "pageSize", options),
            Total = Number(body, "total", options)
        };

    private readonly record struct PageNumbers(int Page, int PageSize, int Total, int? LayerTotal);

    private readonly record struct CatalogWriter(Utf8JsonWriter Json, JsonSerializerOptions Options)
    {
        public void Write<TItem>(TItem[] items, PageNumbers numbers)
        {
            Json.WriteStartObject();
            Json.WritePropertyName(Name(Options, "Items"));
            JsonSerializer.Serialize(Json, items, ItemJson);
            WriteNumber("Page", numbers.Page);
            WriteNumber("PageSize", numbers.PageSize);
            WriteNumber("Total", numbers.Total);
            if (numbers.LayerTotal is { } layerTotal)
            {
                WriteNumber("LayerTotal", layerTotal);
            }

            Json.WriteEndObject();
        }

        private void WriteNumber(string propertyName, int value) =>
            Json.WriteNumber(Name(Options, propertyName), value);
    }

    private static string Name(JsonSerializerOptions options, string propertyName) =>
        options.PropertyNamingPolicy?.ConvertName(propertyName) ?? propertyName;

    private static TItem[] Items<TItem>(JsonElement body)
        where TItem : class
    {
        if (body.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return [];
        }

        if (body.ValueKind != JsonValueKind.Array)
        {
            throw new JsonException("An admin catalog list is an array.");
        }

        var items = body.Deserialize<TItem[]>(ItemJson) ?? [];
        foreach (var item in items)
        {
            if (item is null)
            {
                throw new JsonException("An admin catalog list entry is missing.");
            }
        }

        return items;
    }

    private static int Number(JsonElement body, string name, JsonSerializerOptions options)
    {
        if (Property(body, name) is not { } value)
        {
            return 0;
        }

        return value.Deserialize<int>(NumberJson(options.NumberHandling));
    }

    private static JsonSerializerOptions NumberJson(JsonNumberHandling handling) =>
        handling.HasFlag(JsonNumberHandling.AllowReadingFromString) ? NumbersFromStrings : StrictNumbers;

    private static JsonElement? Property(JsonElement body, string name)
    {
        foreach (var property in body.EnumerateObject())
        {
            if (property.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                return property.Value;
            }
        }

        return null;
    }

    private static readonly JsonSerializerOptions StrictNumbers = new();

    private static readonly JsonSerializerOptions NumbersFromStrings = new()
    {
        NumberHandling = JsonNumberHandling.AllowReadingFromString
    };

    private static readonly JsonSerializerOptions ItemJson = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };
}

public sealed class HonuaAdminCatalogPageConverter : JsonConverterFactory
{
    public override bool CanConvert(Type typeToConvert) =>
        typeToConvert == typeof(HonuaAdminServicePage) || typeToConvert == typeof(HonuaAdminLayerPage);

    public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options)
    {
        if (typeToConvert == typeof(HonuaAdminServicePage))
        {
            return new ServiceConverter();
        }

        if (typeToConvert == typeof(HonuaAdminLayerPage))
        {
            return new LayerConverter();
        }

        throw new NotSupportedException(typeToConvert.FullName);
    }

    private static TPage ReadDocument<TPage>(ref Utf8JsonReader reader, Func<JsonElement, TPage> read)
    {
        using var document = JsonDocument.ParseValue(ref reader);
        return read(document.RootElement);
    }

    private sealed class ServiceConverter : JsonConverter<HonuaAdminServicePage>
    {
        public override HonuaAdminServicePage Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            ReadDocument(ref reader, body => HonuaAdminCatalogReader.ReadServicePage(body, options));

        public override void Write(Utf8JsonWriter writer, HonuaAdminServicePage value, JsonSerializerOptions options) =>
            HonuaAdminCatalogReader.WriteServicePage(writer, value, options);
    }

    private sealed class LayerConverter : JsonConverter<HonuaAdminLayerPage>
    {
        public override HonuaAdminLayerPage Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            ReadDocument(ref reader, body => HonuaAdminCatalogReader.ReadLayerPage(body, options));

        public override void Write(Utf8JsonWriter writer, HonuaAdminLayerPage value, JsonSerializerOptions options) =>
            HonuaAdminCatalogReader.WriteLayerPage(writer, value, options);
    }
}
