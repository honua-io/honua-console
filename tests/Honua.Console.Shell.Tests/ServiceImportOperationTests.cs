using System.Net;
using System.Net.Http.Json;
using Honua.Console.Contracts;
using Honua.Console.Shell.Services;

namespace Honua.Console.Shell.Tests;

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

    [Theory]
    [InlineData(0, "Queued", false, false)]
    [InlineData(1, "Discovering", false, false)]
    [InlineData(2, "Retrieving features", false, false)]
    [InlineData(3, "Creating table", false, false)]
    [InlineData(4, "Inserting features", false, false)]
    [InlineData(5, "Publishing", false, false)]
    [InlineData(6, "Copying attachments", false, false)]
    [InlineData(7, "Validating", false, false)]
    [InlineData(8, "Completed", true, true)]
    [InlineData(9, "Needs review", true, false)]
    [InlineData(10, "Failed", true, false)]
    [InlineData(11, "Cancelled", true, false)]
    public async Task GetImportJob_ServerStatus_MapsLifecycle(
        int status, string label, bool terminal, bool succeeded)
    {
        var operation = CreateOperation(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new { jobId = "job-1", status }),
        });

        var job = await operation.GetImportJobAsync("job-1");

        Assert.Equal(label, job.Status);
        Assert.Equal(terminal, job.Terminal);
        Assert.Equal(succeeded, job.Succeeded);
        Assert.False(job.TransientFailure);
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
            Content = JsonContent.Create(new { jobId = "job-1", status = 10, errorMessage = "import failed" }),
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
