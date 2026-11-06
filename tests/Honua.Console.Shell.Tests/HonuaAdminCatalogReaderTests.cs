using System.Net;
using System.Text;
using System.Text.Json;
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

    private static async Task<HonuaAdminEndpointResult<IReadOnlyList<HonuaAdminServiceSummary>>> ReadAllServices(
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
