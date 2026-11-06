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

    internal static HonuaAdminServicePage ReadServicePage(JsonElement body) =>
        Read<HonuaAdminServicePage, HonuaAdminServiceSummary>(body, CompleteServices, ServicePage);

    internal static HonuaAdminLayerPage ReadLayerPage(JsonElement body) =>
        Read<HonuaAdminLayerPage, HonuaAdminPublishedLayerSummary>(body, CompleteLayers, LayerPage);

    internal static void WriteServicePage(Utf8JsonWriter writer, HonuaAdminServicePage page, JsonSerializerOptions options) =>
        WritePage(writer, page.Items, new PageNumbers(page.Page, page.PageSize, page.Total, page.LayerTotal), options);

    internal static void WriteLayerPage(Utf8JsonWriter writer, HonuaAdminLayerPage page, JsonSerializerOptions options) =>
        WritePage(writer, page.Items, new PageNumbers(page.Page, page.PageSize, page.Total, null), options);

    private static TPage Read<TPage, TItem>(
        JsonElement body,
        Func<TItem[], TPage> completeList,
        Func<TItem[], JsonElement, TPage> page)
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

    private static HonuaAdminServicePage ServicePage(HonuaAdminServiceSummary[] items, JsonElement body) => new()
    {
        Items = items,
        Page = Number(body, "page"),
        PageSize = Number(body, "pageSize"),
        Total = Number(body, "total"),
        LayerTotal = Number(body, "layerTotal")
    };

    private static HonuaAdminLayerPage CompleteLayers(HonuaAdminPublishedLayerSummary[] items) => new()
    {
        Items = items,
        Page = 1,
        PageSize = items.Length,
        Total = items.Length
    };

    private static HonuaAdminLayerPage LayerPage(HonuaAdminPublishedLayerSummary[] items, JsonElement body) => new()
    {
        Items = items,
        Page = Number(body, "page"),
        PageSize = Number(body, "pageSize"),
        Total = Number(body, "total")
    };

    private readonly record struct PageNumbers(int Page, int PageSize, int Total, int? LayerTotal);

    private static void WritePage<TItem>(Utf8JsonWriter writer, TItem[] items, PageNumbers numbers, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WritePropertyName(Name(options, "Items"));
        JsonSerializer.Serialize(writer, items, ItemJson);
        WriteNumber(writer, options, "Page", numbers.Page);
        WriteNumber(writer, options, "PageSize", numbers.PageSize);
        WriteNumber(writer, options, "Total", numbers.Total);
        if (numbers.LayerTotal is { } layerTotal)
        {
            WriteNumber(writer, options, "LayerTotal", layerTotal);
        }

        writer.WriteEndObject();
    }

    private static void WriteNumber(Utf8JsonWriter writer, JsonSerializerOptions options, string propertyName, int value) =>
        writer.WriteNumber(Name(options, propertyName), value);

    private static string Name(JsonSerializerOptions options, string propertyName) =>
        options.PropertyNamingPolicy?.ConvertName(propertyName) ?? propertyName;

    private static TItem[] Items<TItem>(JsonElement body)
    {
        if (body.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return [];
        }

        if (body.ValueKind != JsonValueKind.Array)
        {
            throw new JsonException("An admin catalog list is an array.");
        }

        return body.Deserialize<TItem[]>(ItemJson) ?? [];
    }

    private static int Number(JsonElement body, string name)
    {
        if (Property(body, name) is not { } value || value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out var number))
        {
            return 0;
        }

        return number;
    }

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
            ReadDocument(ref reader, HonuaAdminCatalogReader.ReadServicePage);

        public override void Write(Utf8JsonWriter writer, HonuaAdminServicePage value, JsonSerializerOptions options) =>
            HonuaAdminCatalogReader.WriteServicePage(writer, value, options);
    }

    private sealed class LayerConverter : JsonConverter<HonuaAdminLayerPage>
    {
        public override HonuaAdminLayerPage Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            ReadDocument(ref reader, HonuaAdminCatalogReader.ReadLayerPage);

        public override void Write(Utf8JsonWriter writer, HonuaAdminLayerPage value, JsonSerializerOptions options) =>
            HonuaAdminCatalogReader.WriteLayerPage(writer, value, options);
    }
}
