using System.Net;
using System.Text;
using Honua.Console.Contracts;
using Honua.Console.Shell.Services;

namespace Honua.Console.Shell.Tests;

public sealed class ServiceLayerTableReadTests
{
    [Fact]
    public async Task ServerErrorIsReportedNotReturnedAsNoTables()
    {
        using var handler = new Handler(_ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
        {
            Content = new StringContent("{\"title\":\"Database unavailable\",\"status\":503}", Encoding.UTF8, "application/problem+json")
        });
        var operation = NewOperation(handler, out var http, out var client);
        using (http)
        using (client)
        {
            var ex = await Assert.ThrowsAsync<ServiceLayerTableReadException>(() => operation.ListTablesAsync("conn"));
            Assert.Equal("Connections", ex.State.Surface);
            Assert.Equal(503, ex.State.StatusCode);
            Assert.Contains("HTTP 503", ex.State.Detail, StringComparison.Ordinal);
            Assert.Equal("/api/v1/admin/connections/conn/tables", handler.Path);
        }
    }

    [Fact]
    public async Task TransportFailureIsReportedNotReturnedAsNoTables()
    {
        using var handler = new Handler(_ => throw new HttpRequestException("Connection refused"));
        var operation = NewOperation(handler, out var http, out var client);
        using (http)
        using (client)
        {
            var ex = await Assert.ThrowsAsync<ServiceLayerTableReadException>(() => operation.ListTablesAsync("conn"));
            Assert.Equal("Connections", ex.State.Surface);
            Assert.Equal("Unavailable", ex.State.State);
            Assert.Null(ex.State.StatusCode);
            Assert.False(string.IsNullOrWhiteSpace(ex.State.Detail));
        }
    }

    [Fact]
    public async Task SuccessfulEmptyReadStaysEmpty()
    {
        using var handler = new Handler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{\"tables\":[]}", Encoding.UTF8, "application/json")
        });
        var operation = NewOperation(handler, out var http, out var client);
        using (http)
        using (client)
        {
            Assert.Empty(await operation.ListTablesAsync("conn"));
        }
    }

    private static HonuaServerServiceLayerPublishOperation NewOperation(Handler handler, out HttpClient http, out HonuaAdminOperateHttpClient client)
    {
        http = new HttpClient(handler);
        client = new HonuaAdminOperateHttpClient(http, new(new Uri("https://fixture.invalid/")));
        return new HonuaServerServiceLayerPublishOperation(client);
    }

    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public string? Path { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Path = request.RequestUri!.AbsolutePath;
            return Task.FromResult(respond(request));
        }
    }
}
