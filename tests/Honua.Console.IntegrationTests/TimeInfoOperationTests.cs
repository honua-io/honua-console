using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Honua.Console.Contracts;
using Honua.Console.Shell.Services;

namespace Honua.Console.IntegrationTests;

/// <summary>
/// Docker-free coverage for the service time-info OPERATION (gap report Bucket 3-A #4): the
/// <see cref="HonuaServerConsoleTimeInfoOperation"/> over a stubbed admin client and the missing-binding
/// <see cref="UnsupportedConsoleTimeInfoOperation"/>. Asserts the time-info read comes off the service
/// settings, the save issues the real timeinfo PUT (PUT /api/v1/admin/services/{svc}/timeinfo) carrying the
/// three fields, and that the unconfigured surface never performs a network call. No fabricated time fields.
/// </summary>
public sealed class TimeInfoOperationTests
{
    private static readonly Uri BaseAddress = new("https://honua.test");

    [Fact]
    public async Task GetTimeInfo_ReadsFromServiceSettings()
    {
        string? path = null;
        var settings = new HonuaAdminServiceSettingsResponse
        {
            ServiceName = "svc",
            TimeInfo = new HonuaAdminTimeInfoResponse
            {
                StartTimeField = "observed_at",
                EndTimeField = "observed_until",
                TrackIdField = "vehicle_id",
            },
        };
        var operation = new HonuaServerConsoleTimeInfoOperation(CreateClient(request =>
        {
            path = request.RequestUri?.AbsolutePath;
            return Ok(settings);
        }));

        var result = await operation.GetTimeInfoAsync("svc");

        Assert.True(result.Bound);
        Assert.Equal("/api/v1/admin/services/svc/settings", path);
        Assert.Equal("observed_at", result.StartTimeField);
        Assert.Equal("observed_until", result.EndTimeField);
        Assert.Equal("vehicle_id", result.TrackIdField);
    }

    [Fact]
    public async Task SetTimeInfo_IssuesPutToTimeInfoRoute_WithTheThreeFields()
    {
        string? path = null;
        HttpMethod? method = null;
        string? body = null;
        var settings = new HonuaAdminServiceSettingsResponse
        {
            ServiceName = "svc",
            TimeInfo = new HonuaAdminTimeInfoResponse
            {
                StartTimeField = "observed_at",
                EndTimeField = "observed_until",
                TrackIdField = "vehicle_id",
            },
        };
        var operation = new HonuaServerConsoleTimeInfoOperation(CreateClient(request =>
        {
            path = request.RequestUri?.AbsolutePath;
            method = request.Method;
            body = request.Content?.ReadAsStringAsync().GetAwaiter().GetResult();
            return Ok(settings);
        }));

        var result = await operation.SetTimeInfoAsync("svc", "observed_at", "observed_until", "vehicle_id");

        Assert.True(result.Succeeded);
        Assert.Equal("Updated", result.State);
        Assert.Equal(HttpMethod.Put, method);
        Assert.Equal("/api/v1/admin/services/svc/timeinfo", path);

        using var doc = JsonDocument.Parse(body!);
        Assert.Equal("observed_at", doc.RootElement.GetProperty("startTimeField").GetString());
        Assert.Equal("observed_until", doc.RootElement.GetProperty("endTimeField").GetString());
        Assert.Equal("vehicle_id", doc.RootElement.GetProperty("trackIdField").GetString());

        // The result reflects the server's re-read of the post-change time-info.
        Assert.Equal("observed_at", result.StartTimeField);
        Assert.Equal("vehicle_id", result.TrackIdField);
    }

    [Fact]
    public async Task SetTimeInfo_BlankFields_AreSentAsNull_ToClearThemServerSide()
    {
        string? body = null;
        var operation = new HonuaServerConsoleTimeInfoOperation(CreateClient(request =>
        {
            body = request.Content?.ReadAsStringAsync().GetAwaiter().GetResult();
            return Ok(new HonuaAdminServiceSettingsResponse { ServiceName = "svc" });
        }));

        var result = await operation.SetTimeInfoAsync("svc", "observed_at", "   ", null);

        Assert.True(result.Succeeded);
        using var doc = JsonDocument.Parse(body!);
        Assert.Equal("observed_at", doc.RootElement.GetProperty("startTimeField").GetString());
        Assert.Equal(JsonValueKind.Null, doc.RootElement.GetProperty("endTimeField").ValueKind);
        Assert.Equal(JsonValueKind.Null, doc.RootElement.GetProperty("trackIdField").ValueKind);
    }

    [Fact]
    public async Task SetTimeInfo_WhenServerRejects_MapsFailureWithDetail()
    {
        var operation = new HonuaServerConsoleTimeInfoOperation(CreateClient(_ =>
            new HttpResponseMessage(HttpStatusCode.BadRequest)
            {
                Content = JsonContent.Create(new { success = false, message = "startTimeField 'nope' is not a field." })
            }));

        var result = await operation.SetTimeInfoAsync("svc", "nope", null, null);

        Assert.False(result.Succeeded);
        Assert.Contains("is not a field", result.Detail!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Unsupported_NeverCallsNetwork_AndReturnsMissingBinding()
    {
        var operation = new UnsupportedConsoleTimeInfoOperation();

        var read = await operation.GetTimeInfoAsync("svc");
        var write = await operation.SetTimeInfoAsync("svc", "observed_at", null, null);

        Assert.False(read.Bound);
        Assert.Contains("HONUA_SERVER_BASE_URL", read.Detail!, StringComparison.Ordinal);
        Assert.False(write.Succeeded);
        Assert.Equal("Missing binding", write.State);
        Assert.Contains("HONUA_SERVER_BASE_URL", write.Detail!, StringComparison.Ordinal);
    }

    private static HttpResponseMessage Ok<T>(T data) =>
        new(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new { success = true, data, timestamp = DateTimeOffset.UtcNow })
        };

    private static IHonuaAdminOperateClient CreateClient(Func<HttpRequestMessage, HttpResponseMessage> responder)
    {
        var httpClient = new HttpClient(new StubHandler(responder)) { BaseAddress = BaseAddress };
        return new HonuaAdminOperateHttpClient(httpClient, new HonuaAdminOperateClientOptions(BaseAddress, "test-key"));
    }

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            _ = request.Content?.ReadAsStringAsync(cancellationToken).GetAwaiter().GetResult();
            return Task.FromResult(responder(request));
        }
    }
}
