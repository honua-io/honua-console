using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Honua.Console.Contracts;

namespace Honua.Console.Shell.Tests;

public sealed class HonuaAdminCatalogReaderTests
{
    [Fact]
    public async Task PagedServiceListKeepsTheServerPage()
    {
        using var client = Client(_ => """
            {"success":true,"data":{"items":[{"serviceName":"roads","layerCount":2}],"page":1,"pageSize":100,"total":2,"layerTotal":5}}
            """);
        var page = await client.ListServicesAsync(HonuaAdminCatalogQuery.First);
        Assert.True(page.Data is not null, page.Issue?.Detail);
        Assert.Equal("roads", Assert.Single(page.Data!.Items).ServiceName);
        Assert.Equal(1, page.Data.Page);
        Assert.Equal(100, page.Data.PageSize);
        Assert.Equal(2, page.Data.Total);
        Assert.Equal(5, page.Data.LayerTotal);
    }

    [Fact]
    public async Task PlainServiceListIsOneCompletePageWithNoNextPage()
    {
        const string Body = """
            {"success":true,"data":[{"serviceName":"roads","layerCount":2},{"serviceName":"parcels","layerCount":3}]}
            """;
        using var client = Client(request =>
        {
            Assert.Equal(1, Page(request));
            return Body;
        });
        var page = await client.ListServicesAsync(HonuaAdminCatalogQuery.First);
        Assert.True(page.Data is not null, page.Issue?.Detail);
        Assert.Equal(["roads", "parcels"], page.Data!.Items.Select(service => service.ServiceName));
        Assert.Equal(1, page.Data.Page);
        Assert.Equal(2, page.Data.PageSize);
        Assert.Equal(2, page.Data.Total);
        Assert.Equal(5, page.Data.LayerTotal);

        var all = await ReadAllServices(client);
        Assert.True(all.Data is not null, all.Issue?.Detail);
        Assert.Equal(2, all.Data!.Count);
    }

    [Fact]
    public void OperateObservabilityReadsAPlainServiceListAsOneCompletePage()
    {
        var envelope = JsonSerializer.Deserialize(
            """
            {"success":true,"data":[{"serviceName":"roads","layerCount":2},{"serviceName":"parcels","layerCount":3}]}
            """,
            OperateObservabilityJsonContext.Default.AdminServiceListEnvelope);
        Assert.NotNull(envelope?.Data);
        Assert.Equal(["roads", "parcels"], envelope!.Data!.Items.Select(service => service.ServiceName));
        Assert.Equal(1, envelope.Data.Page);
        Assert.Equal(2, envelope.Data.PageSize);
        Assert.Equal(2, envelope.Data.Total);
        Assert.Equal(5, envelope.Data.LayerTotal);
    }

    [Fact]
    public async Task QuotedServiceTotalWalksTheNextPage()
    {
        var calls = 0;
        using var client = Client(_ =>
        {
            calls++;
            var items = calls == 1 ? HundredServices() : """[{"serviceName":"last"}]""";
            return PageEnvelope(items, calls, "\"101\"");
        });
        var all = await ReadAllServices(client);
        Assert.True(all.Data is not null, all.Issue?.Detail);
        Assert.Equal(101, all.Data!.Count);
        Assert.Equal("last", all.Data[^1].ServiceName);
        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task MalformedServiceTotalIsRejected()
    {
        using var client = Client(_ => """
            {"success":true,"data":{"items":[{"serviceName":"roads"}],"page":1,"pageSize":100,"total":true}}
            """);
        var page = await client.ListServicesAsync(HonuaAdminCatalogQuery.First);
        Assert.Null(page.Data);
        Assert.NotNull(page.Issue);
    }

    [Fact]
    public async Task NullServiceEntryIsRejected()
    {
        using var client = Client(_ => """{"success":true,"data":[null]}""");
        var page = await client.ListServicesAsync(HonuaAdminCatalogQuery.First);
        Assert.Null(page.Data);
        Assert.NotNull(page.Issue);
    }

    [Fact]
    public void OperateObservabilityRejectsANullServiceEntry() =>
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize(
            """{"success":true,"data":[null]}""",
            OperateObservabilityJsonContext.Default.AdminServiceListEnvelope));

    [Fact]
    public void OperateObservabilityRejectsAQuotedTotal() =>
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize(
            """{"success":true,"data":{"items":[],"page":1,"pageSize":100,"total":"101"}}""",
            OperateObservabilityJsonContext.Default.AdminServiceListEnvelope));

    [Fact]
    public async Task PagedLayerListKeepsTheServerPage()
    {
        using var client = Client(_ => """
            {"success":true,"data":{"items":[{"layerId":7,"layerName":"Parcels"}],"page":3,"pageSize":25,"total":40}}
            """);
        var page = await client.ListConnectionLayersAsync("conn", new HonuaAdminLayerListRequest(null, HonuaAdminCatalogQuery.First));
        Assert.True(page.Data is not null, page.Issue?.Detail);
        Assert.Equal("Parcels", Assert.Single(page.Data!.Items).LayerName);
        Assert.Equal(3, page.Data.Page);
        Assert.Equal(25, page.Data.PageSize);
        Assert.Equal(40, page.Data.Total);
    }

    [Fact]
    public async Task PlainLayerListIsOneCompletePageWithNoNextPage()
    {
        using var client = Client(request =>
        {
            Assert.Equal(1, Page(request));
            return """
                {"success":true,"data":[{"layerId":7,"layerName":"Parcels"},{"layerId":8,"layerName":"Roads"}]}
                """;
        });
        var page = await client.ListConnectionLayersAsync("conn", new HonuaAdminLayerListRequest("roads", HonuaAdminCatalogQuery.First));
        Assert.True(page.Data is not null, page.Issue?.Detail);
        Assert.Equal(["Parcels", "Roads"], page.Data!.Items.Select(layer => layer.LayerName));
        Assert.Equal(1, page.Data.Page);
        Assert.Equal(2, page.Data.PageSize);
        Assert.Equal(2, page.Data.Total);

        var all = await HonuaAdminCatalogReader.ReadAllAsync(
            (query, token) => client.ListConnectionLayersAsync("conn", new HonuaAdminLayerListRequest("roads", query), token),
            layerPage => layerPage.Items,
            layerPage => layerPage.Total,
            CancellationToken.None);
        Assert.True(all.Data is not null, all.Issue?.Detail);
        Assert.Equal(2, all.Data!.Count);
    }

    [Theory]
    [InlineData("\"x\"")]
    [InlineData("5")]
    [InlineData("{\"items\":{}}")]
    public async Task MalformedCatalogShapesAreRejected(string catalogJson)
    {
        using var client = Client(_ => "{\"success\":true,\"data\":" + catalogJson + "}");

        var services = await client.ListServicesAsync(HonuaAdminCatalogQuery.First);
        var layers = await client.ListConnectionLayersAsync("conn", new HonuaAdminLayerListRequest(null, HonuaAdminCatalogQuery.First));

        Assert.Null(services.Data);
        Assert.NotNull(services.Issue);
        Assert.Null(layers.Data);
        Assert.NotNull(layers.Issue);
    }

    [Theory]
    [InlineData(2147483647, 1)]
    [InlineData(-2147483648, -1)]
    public async Task OverflowingServiceLayerTotalIsRejected(int firstCount, int secondCount)
    {
        var services = new[]
        {
            new Honua.Sdk.Admin.Models.ServiceSummary { ServiceName = "roads", LayerCount = firstCount },
            new Honua.Sdk.Admin.Models.ServiceSummary { ServiceName = "parcels", LayerCount = secondCount }
        };
        var body = JsonSerializer.Serialize(new { success = true, data = services });
        using var client = Client(_ => body);

        var page = await client.ListServicesAsync(HonuaAdminCatalogQuery.First);

        Assert.Null(page.Data);
        Assert.NotNull(page.Issue);
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize(
            body, OperateObservabilityJsonContext.Default.AdminServiceListEnvelope));
    }

    [Theory]
    [InlineData("[{\"serviceName\":\"roads\",\"layerCount\":\"2\"}]")]
    [InlineData("{\"items\":[{\"serviceName\":\"roads\",\"layerCount\":\"2\"}],\"total\":1}")]
    public async Task EntryNumbersUseTheConsumersNumberHandling(string catalogJson)
    {
        var body = "{\"success\":true,\"data\":" + catalogJson + "}";
        using var client = Client(_ => body);

        var page = await client.ListServicesAsync(HonuaAdminCatalogQuery.First);

        Assert.True(page.Data is not null, page.Issue?.Detail);
        Assert.Equal(2, Assert.Single(page.Data!.Items).LayerCount);
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize(
            body, OperateObservabilityJsonContext.Default.AdminServiceListEnvelope));
    }

    [Theory]
    [InlineData(JsonSerializerDefaults.General)]
    [InlineData(JsonSerializerDefaults.Web)]
    public void PageSerializationUsesTheCallersNamingAndNumberPolicies(JsonSerializerDefaults defaults)
    {
        var options = new JsonSerializerOptions(defaults) { NumberHandling = JsonNumberHandling.WriteAsString | JsonNumberHandling.AllowReadingFromString };
        var page = new HonuaAdminServicePage
        {
            Items = [new Honua.Sdk.Admin.Models.ServiceSummary { ServiceName = "roads", LayerCount = 2 }],
            Page = 1,
            PageSize = 100,
            Total = 1,
            LayerTotal = 2
        };

        var json = JsonSerializer.Serialize(page, options);
        using var document = JsonDocument.Parse(json);
        var itemsName = defaults == JsonSerializerDefaults.Web ? "items" : "Items";
        var serviceName = defaults == JsonSerializerDefaults.Web ? "serviceName" : "ServiceName";
        var totalName = defaults == JsonSerializerDefaults.Web ? "total" : "Total";
        Assert.Equal("roads", document.RootElement.GetProperty(itemsName)[0].GetProperty(serviceName).GetString());
        Assert.Equal("1", document.RootElement.GetProperty(totalName).GetString());
        var roundTrip = JsonSerializer.Deserialize<HonuaAdminServicePage>(json, options);
        Assert.NotNull(roundTrip);
        Assert.Equal(2, Assert.Single(roundTrip.Items).LayerCount);
        Assert.Equal(1, roundTrip.Total);
    }

    private static string PageEnvelope(string items, int page, string totalJson) =>
        "{\"success\":true,\"data\":{\"items\":" + items
        + ",\"page\":" + page.ToString(System.Globalization.CultureInfo.InvariantCulture)
        + ",\"pageSize\":100,\"total\":" + totalJson + "}}";

    private static string HundredServices()
    {
        var names = new string[100];
        for (var index = 0; index < names.Length; index++)
        {
            names[index] = "{\"serviceName\":\"s" + index.ToString(System.Globalization.CultureInfo.InvariantCulture) + "\"}";
        }

        return "[" + string.Join(',', names) + "]";
    }

    private static async Task<HonuaAdminEndpointResult<IReadOnlyList<Honua.Sdk.Admin.Models.ServiceSummary>>> ReadAllServices(
        HonuaAdminOperateHttpClient client) =>
        await HonuaAdminCatalogReader.ReadAllAsync(
            client.ListServicesAsync,
            page => page.Items,
            page => page.Total,
            CancellationToken.None);

    private static int Page(HttpRequestMessage request)
    {
        var query = request.RequestUri!.Query;
        var marker = "page=";
        var start = query.IndexOf(marker, StringComparison.Ordinal);
        Assert.True(start >= 0, query);
        var end = query.IndexOf('&', start);
        var value = end < 0 ? query[(start + marker.Length)..] : query[(start + marker.Length)..end];
        return int.Parse(value, System.Globalization.CultureInfo.InvariantCulture);
    }

    private static HonuaAdminOperateHttpClient Client(Func<HttpRequestMessage, string> body)
    {
        var http = new HttpClient(new JsonHandler(body)) { BaseAddress = new Uri("https://catalog.example") };
        return new HonuaAdminOperateHttpClient(http, new HonuaAdminOperateClientOptions(http.BaseAddress));
    }

    private sealed class JsonHandler(Func<HttpRequestMessage, string> body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var json = body(request);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            });
        }
    }
}
