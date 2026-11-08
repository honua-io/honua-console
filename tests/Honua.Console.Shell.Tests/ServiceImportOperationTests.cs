using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Honua.Console.Contracts;
using Honua.Console.Shell.Services;
using Honua.Sdk.Admin.Models;

namespace Honua.Console.Shell.Tests;

public sealed class ServiceImportOperationTests
{
    private static readonly Uri BaseAddress = new("https://server.example/");

    private static readonly ImportStatusCase[] StatusContract = JsonSerializer.Deserialize<ImportStatusCase[]>(
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "geoservices-import-status.json")),
        JsonSerializerOptions.Web)!;

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
        using var document = JsonDocument.Parse(requestJson);
        Assert.Equal(overwriteExisting, document.RootElement.GetProperty("overwriteExisting").GetBoolean());
    }

    [Fact]
    public void ImportStatusContract_MatchesEveryPublishedSdkWireValue()
    {
        Assert.Equal(StatusContract.Select(status => (status.Code, status.Name)),
            Enum.GetValues<GeoservicesImportStatus>().Select(status => ((int)status, status.ToString())));
    }

    [Theory]
    [MemberData(nameof(ServerStatuses))]
    public async Task GetImportJob_ServerStatus_MapsLifecycle(string statusJson, string label, string outcome)
    {
        var operation = CreateOperation(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new { jobId = "job-1", status = JsonSerializer.Deserialize<JsonElement>(statusJson) }),
        });

        var job = await operation.GetImportJobAsync("job-1");

        Assert.Equal(label, job.Status);
        Assert.Equal(outcome != "running", job.Terminal);
        Assert.Equal(outcome == "succeeded", job.Succeeded);
        Assert.False(job.TransientFailure);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(-1)]
    [InlineData(99)]
    public async Task GetImportJob_UnrecognizedStatus_KeepsPolling(int? status)
    {
        var operation = CreateOperation(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new { jobId = "job-1", status }),
        });

        var job = await operation.GetImportJobAsync("job-1");

        Assert.Equal("Working", job.Status);
        Assert.False(job.Terminal);
        Assert.False(job.Succeeded);
    }

    [Fact]
    public async Task GetImportJob_UnrecognizedNamedStatus_IsUnsupportedAndTerminal()
    {
        var operation = CreateOperation(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new { jobId = "job-1", status = "Paused" }),
        });

        var job = await operation.GetImportJobAsync("job-1");

        Assert.Equal("Unsupported", job.Status);
        Assert.True(job.Terminal);
        Assert.False(job.Succeeded);
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
            Content = JsonContent.Create(new { jobId = "job-1", status = (int)GeoservicesImportStatus.Failed, errorMessage = "import failed" }),
        });

        var job = await operation.GetImportJobAsync("job-1");

        Assert.True(job.Terminal);
        Assert.False(job.TransientFailure);
        Assert.False(job.Succeeded);
    }

    public static IEnumerable<object[]> ServerStatuses()
    {
        foreach (var status in StatusContract)
        {
            yield return [JsonSerializer.Serialize(status.Code), status.Label, status.Outcome];
            yield return [JsonSerializer.Serialize(status.Name), status.Label, status.Outcome];
        }
    }

    private sealed record ImportStatusCase(int Code, string Name, string Label, string Outcome);

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
