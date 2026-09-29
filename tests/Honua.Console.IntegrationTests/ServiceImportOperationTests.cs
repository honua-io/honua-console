using System.Net;
using System.Net.Http.Json;
using Honua.Console.Contracts;
using Honua.Console.Shell.Services;

namespace Honua.Console.IntegrationTests;

/// <summary>
/// Unit coverage for <see cref="HonuaServerConsoleServiceImportOperation"/>'s job-status mapping. A failed
/// reading is not the same as a failed import: transport blips and transient 5xx must keep the job active so
/// the poller keeps polling, while definitive client outcomes (auth/permission, unsupported) end polling.
/// Backed by the real HTTP admin client over a stubbed transport; contacts no server.
/// </summary>
public sealed class ServiceImportOperationTests
{
    private static readonly Uri BaseAddress = new("https://server.example/");

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task StartLayerImport_MapsOnlyTheCurrentTargetsOverwriteAuthorization(bool overwriteExisting)
    {
        string? requestJson = null;
        var operation = CreateOperation(request =>
        {
            requestJson = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new { jobId = "job-1", status = 1 }),
            };
        });

        await operation.StartLayerImportAsync(new()
        {
            ServiceUrl = "https://source.example/FeatureServer",
            LayerId = 7,
            TableName = "roads_7",
            OverwriteExisting = overwriteExisting,
        });

        Assert.NotNull(requestJson);
        using var document = System.Text.Json.JsonDocument.Parse(requestJson);
        Assert.Equal(overwriteExisting, document.RootElement.GetProperty("overwriteExisting").GetBoolean());
    }

    [Fact]
    public async Task GetImportJob_TransientServerError_IsNotTerminal_AndFlagsTransient()
    {
        var operation = CreateOperation(_ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));

        var job = await operation.GetImportJobAsync("job-1");

        Assert.False(job.Terminal);
        Assert.True(job.TransientFailure);
        Assert.False(job.Succeeded);
    }

    [Fact]
    public async Task GetImportJob_TransportFailure_IsNotTerminal_AndFlagsTransient()
    {
        var operation = CreateOperation(_ => throw new HttpRequestException("connection refused"));

        var job = await operation.GetImportJobAsync("job-1");

        Assert.False(job.Terminal);
        Assert.True(job.TransientFailure);
    }

    [Fact]
    public async Task GetImportJob_Forbidden_IsTerminal_NotTransient()
    {
        var operation = CreateOperation(_ => new HttpResponseMessage(HttpStatusCode.Forbidden));

        var job = await operation.GetImportJobAsync("job-1");

        Assert.True(job.Terminal);
        Assert.False(job.TransientFailure);
        Assert.False(job.Succeeded);
    }

    [Fact]
    public async Task GetImportJob_ServerDeclaredFailure_IsTerminal_NotTransient()
    {
        var operation = CreateOperation(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            // status 7 = Failed: a server-declared terminal outcome carried in the success body.
            Content = JsonContent.Create(new { jobId = "job-1", status = 7, errorMessage = "import failed" }),
        });

        var job = await operation.GetImportJobAsync("job-1");

        Assert.True(job.Terminal);
        Assert.False(job.TransientFailure);
        Assert.False(job.Succeeded);
    }

    private static HonuaServerConsoleServiceImportOperation CreateOperation(
        Func<HttpRequestMessage, HttpResponseMessage> responder)
    {
        var httpClient = new HttpClient(new StubHandler(responder)) { BaseAddress = BaseAddress };
        var client = new HonuaAdminOperateHttpClient(
            httpClient,
            new HonuaAdminOperateClientOptions(BaseAddress, "test-key"));
        return new HonuaServerConsoleServiceImportOperation(client);
    }

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(responder(request));
    }
}
