using System.Net;
using System.Text;
using System.Text.Json;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Services;

namespace Honua.Console.Native.Core.Tests;

public sealed class ConsoleSensorThingsClientTests
{
    [Fact]
    public async Task ListDatastreams_ReadsStaCollectionEndpoint()
    {
        var handler = new RecordingHandler(request => request.RequestUri!.AbsolutePath switch
        {
            "/sta/v1.1/Datastreams" => Json(
                """
                {
                  "@iot.count": 1,
                  "value": [
                    {
                      "@iot.id": 1,
                      "@iot.selfLink": "https://server.example/sta/v1.1/Datastreams(1)",
                      "name": "Air temperature",
                      "description": "Hourly air temperature.",
                      "observationType": "OM_Measurement",
                      "unitOfMeasurement": { "name": "degree Celsius", "symbol": "°C", "definition": "ucum" },
                      "Observations@iot.navigationLink": "https://server.example/sta/v1.1/Datastreams(1)/Observations"
                    }
                  ]
                }
                """),
            _ => new HttpResponseMessage(HttpStatusCode.NotFound)
        });
        var client = CreateClient(handler);

        var result = await client.ListDatastreamsAsync();

        Assert.True(result.IsOk);
        var datastream = Assert.Single(result.Value!.Value);
        Assert.Equal(1, datastream.IotId);
        Assert.Equal("Air temperature", datastream.Name);
        Assert.Equal("°C", datastream.UnitOfMeasurement!.Symbol);
        // $top/$skip/$count query options are sent to the STA collection.
        var query = handler.Requests[0].RequestUri!.Query;
        Assert.Contains("$top=", query, StringComparison.Ordinal);
        Assert.Contains("$count=true", query, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetDatastreamTimeSeries_OrdersObservationsForChart()
    {
        // Synthetic seeded datastream id=1 from honua-server #1842.
        var handler = new RecordingHandler(request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path == "/sta/v1.1/Datastreams(1)")
            {
                return Json(
                    """
                    {
                      "@iot.id": 1,
                      "@iot.selfLink": "https://server.example/sta/v1.1/Datastreams(1)",
                      "name": "Air temperature",
                      "description": "Hourly air temperature.",
                      "observationType": "OM_Measurement",
                      "unitOfMeasurement": { "name": "degree Celsius", "symbol": "°C", "definition": "ucum" }
                    }
                    """);
            }

            if (path == "/sta/v1.1/Datastreams(1)/Observations")
            {
                return Json(
                    """
                    {
                      "@iot.count": 3,
                      "value": [
                        { "@iot.id": 2, "@iot.selfLink": "s", "phenomenonTime": "2026-06-18T01:00:00Z", "result": 16.5 },
                        { "@iot.id": 1, "@iot.selfLink": "s", "phenomenonTime": "2026-06-18T00:00:00Z", "result": 15.0 },
                        { "@iot.id": 3, "@iot.selfLink": "s", "phenomenonTime": "2026-06-18T02:00:00Z", "result": 17.25 }
                      ]
                    }
                    """);
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });
        var client = CreateClient(handler);

        var result = await client.GetDatastreamTimeSeriesAsync(1);

        Assert.True(result.IsOk);
        var series = result.Value!;
        Assert.Equal(1, series.DatastreamId);
        Assert.Equal("Air temperature", series.DatastreamName);
        Assert.Equal("°C", series.UnitSymbol);
        Assert.Equal(3, series.Points.Count);
        // Points are sorted ascending by phenomenon time regardless of server order.
        Assert.Equal(15.0, series.Points[0].Result);
        Assert.Equal(16.5, series.Points[1].Result);
        Assert.Equal(17.25, series.Points[2].Result);
        Assert.Equal(15.0, series.Minimum);
        Assert.Equal(17.25, series.Maximum);
    }

    [Fact]
    public async Task GetDatastreamTimeSeries_ToleratesNonNumericAndNullResults()
    {
        // OGC STA v1.1 result is an open type. A category/string result, a null result, and a
        // boolean must not throw JsonException (which previously degraded the WHOLE collection to
        // Unavailable); numeric (including numeric-string) results still chart.
        var handler = new RecordingHandler(request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path == "/sta/v1.1/Datastreams(1)")
            {
                return Json("""{ "@iot.id": 1, "@iot.selfLink": "s", "name": "Mixed", "description": "d", "observationType": "t" }""");
            }

            if (path == "/sta/v1.1/Datastreams(1)/Observations")
            {
                return Json(
                    """
                    {
                      "@iot.count": 5,
                      "value": [
                        { "@iot.id": 1, "@iot.selfLink": "s", "phenomenonTime": "2026-06-18T00:00:00Z", "result": 15.0 },
                        { "@iot.id": 2, "@iot.selfLink": "s", "phenomenonTime": "2026-06-18T01:00:00Z", "result": "category-a" },
                        { "@iot.id": 3, "@iot.selfLink": "s", "phenomenonTime": "2026-06-18T02:00:00Z", "result": null },
                        { "@iot.id": 4, "@iot.selfLink": "s", "phenomenonTime": "2026-06-18T03:00:00Z", "result": true },
                        { "@iot.id": 5, "@iot.selfLink": "s", "phenomenonTime": "2026-06-18T04:00:00Z", "result": "17.5" }
                      ]
                    }
                    """);
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });
        var client = CreateClient(handler);

        var result = await client.GetDatastreamTimeSeriesAsync(1);

        Assert.True(result.IsOk);
        var series = result.Value!;
        // Only the two numeric results (a JSON number and a numeric string) are charted; the
        // string-category, null, and boolean results are skipped without failing the read.
        Assert.Equal(2, series.Points.Count);
        Assert.Equal(15.0, series.Points[0].Result);
        Assert.Equal(17.5, series.Points[1].Result);
    }

    [Fact]
    public async Task GetDatastream_WithExpand_RequestsExpandObservations()
    {
        var handler = new RecordingHandler(_ => Json(
            """
            { "@iot.id": 1, "@iot.selfLink": "s", "name": "Air temperature", "description": "d", "observationType": "t" }
            """));
        var client = CreateClient(handler);

        var result = await client.GetDatastreamAsync(1, expandObservations: true);

        Assert.True(result.IsOk);
        var query = Uri.UnescapeDataString(handler.Requests[0].RequestUri!.Query);
        Assert.Contains("$expand=Observations", query, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Reads_MapNotImplementedToUnsupported()
    {
        var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.NotImplemented));
        var client = CreateClient(handler);

        var result = await client.ListThingsAsync();

        Assert.False(result.IsOk);
        Assert.Equal(SensorThingsReadStatus.Unsupported, result.Status);
    }

    [Fact]
    public async Task Reads_WhenNoProfile_ReturnMissingBinding()
    {
        var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var profiles = new InMemoryConsoleEnvironmentProfileStore([], activeProfileId: null);
        var client = new HttpConsoleSensorThingsClient(
            new HttpClient(handler), profiles, new InMemoryConsoleAccountSessionStore());

        var result = await client.ListDatastreamsAsync();

        Assert.False(result.IsOk);
        Assert.Equal(SensorThingsReadStatus.Unavailable, result.Status);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task Unsupported_AlwaysRendersMissingBinding()
    {
        var client = new UnsupportedConsoleSensorThingsClient();

        var things = await client.ListThingsAsync();
        var series = await client.GetDatastreamTimeSeriesAsync(1);

        Assert.Equal(SensorThingsReadStatus.Unavailable, things.Status);
        Assert.Equal(SensorThingsReadStatus.Unavailable, series.Status);
    }

    [Fact]
    public async Task InMemory_SeedsSyntheticDatastreamOne()
    {
        var client = new InMemoryConsoleSensorThingsClient();

        var datastreams = await client.ListDatastreamsAsync();
        var series = await client.GetDatastreamTimeSeriesAsync(1);

        Assert.True(datastreams.IsOk);
        Assert.Contains(datastreams.Value!.Value, d => d.IotId == 1);
        Assert.True(series.IsOk);
        Assert.Equal(48, series.Value!.Points.Count);
        Assert.Equal("°C", series.Value.UnitSymbol);
    }

    private static HttpConsoleSensorThingsClient CreateClient(HttpMessageHandler handler, string? adminApiKey = null)
    {
        var profile = new ConsoleEnvironmentProfile
        {
            Id = "live",
            DisplayName = "Live Server",
            ServerBaseUri = new Uri("https://server.example"),
            UpdatedAt = DateTimeOffset.Parse("2026-06-18T19:00:00Z"),
            Account = new ConsoleAccountBinding
            {
                AuthMode = ConsoleAccountAuthMode.AccountRbac,
                AccountId = "operator.live"
            }
        };
        var profiles = new InMemoryConsoleEnvironmentProfileStore([profile], activeProfileId: profile.Id);
        return new HttpConsoleSensorThingsClient(
            new HttpClient(handler), profiles, new InMemoryConsoleAccountSessionStore(), adminApiKey);
    }

    private static HttpResponseMessage Json(string body) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };

    private sealed class RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(CloneRequest(request));
            return Task.FromResult(responder(request));
        }

        private static HttpRequestMessage CloneRequest(HttpRequestMessage request)
        {
            var clone = new HttpRequestMessage(request.Method, request.RequestUri);
            foreach (var header in request.Headers)
            {
                clone.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }

            return clone;
        }
    }
}
