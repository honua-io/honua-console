using System.Net;
using Honua.Console.Contracts;

namespace Honua.Console.Native.Core.Tests;

public sealed class HonuaAdminOperateHttpClientPipelineTests
{
    private const string Unreachable = "The Honua server endpoint could not be reached: ";
    private const string EnvelopeMismatch = "The Honua server response did not match the expected admin API shape: ";
    private const string MissingData = "The Honua server response did not include data.";

    private const string GetEnvelope = "GET envelope";
    private const string PostEnvelope = "POST envelope";
    private const string PostEnvelopeWithoutBody = "POST envelope without body";
    private const string PutEnvelope = "PUT envelope";
    private const string PutAuthoringDocument = "PUT authoring document";
    private const string GetTableDiscovery = "GET table discovery";
    private const string GetImportFormats = "GET import formats";
    private const string PostImportUpload = "POST import upload";
    private const string PostExternalDiscovery = "POST external discovery";
    private const string PostGeoservicesImport = "POST geoservices import";
    private const string GetGeoservicesImportJob = "GET geoservices import job";

    private static readonly Dictionary<string, Func<HonuaAdminOperateHttpClient, CancellationToken, Task<Outcome>>> Calls = new()
    {
        [GetEnvelope] = (client, token) => Run(client.ListConnectionsAsync(token)),
        [PostEnvelope] = (client, token) => Run(client.CreateConnectionAsync(new HonuaAdminCreateConnectionRequest { Name = "roads" }, token)),
        [PostEnvelopeWithoutBody] = (client, token) => Run(client.TestConnectionAsync("conn-1", token)),
        [PutEnvelope] = (client, token) => Run(client.UpdateLayerFilterAsync(7, new HonuaAdminLayerFilterUpdate(), token)),
        [PutAuthoringDocument] = (client, token) => Run(client.UpdateLayerPopupInfoAsync(7, null, token)),
        [GetTableDiscovery] = (client, token) => Run(client.ListConnectionTablesAsync("conn-1", token)),
        [GetImportFormats] = (client, token) => Run(client.GetImportFormatsAsync(token)),
        [PostImportUpload] = (client, token) => Run(client.ImportFileAsync([1, 2, 3], "roads.csv", "roads", null, token)),
        [PostExternalDiscovery] = (client, token) => Run(client.DiscoverExternalServiceAsync("https://external.example/arcgis/rest/services", null, token)),
        [PostGeoservicesImport] = (client, token) => Run(client.StartGeoservicesImportAsync(
            new HonuaAdminGeoservicesImportRequest { ServiceUrl = "https://external.example/FeatureServer", TableName = "roads" }, token)),
        [GetGeoservicesImportJob] = (client, token) => Run(client.GetGeoservicesImportJobAsync("job-1", token)),
    };

    public static TheoryData<string> AllCalls => new(Calls.Keys);

    public static TheoryData<string> EnvelopeCalls => new(GetEnvelope, PostEnvelope, PostEnvelopeWithoutBody, PutEnvelope, PutAuthoringDocument);

    public static TheoryData<string> EnvelopeWrites => new(PostEnvelope, PostEnvelopeWithoutBody, PutEnvelope, PutAuthoringDocument);

    public static TheoryData<string> StatusDetailFailures => new(GetEnvelope, GetTableDiscovery, GetImportFormats);

    public static TheoryData<string> ServerMessageFailures => new(
        PostEnvelope, PostEnvelopeWithoutBody, PutEnvelope, PutAuthoringDocument,
        PostImportUpload, PostExternalDiscovery, PostGeoservicesImport, GetGeoservicesImportJob);

    public static TheoryData<string> BareBodyServerMessageFailures => new(
        PostImportUpload, PostExternalDiscovery, PostGeoservicesImport, GetGeoservicesImportJob);

    [Theory]
    [MemberData(nameof(AllCalls))]
    public async Task TransportFailure_IsUnavailableWithoutStatus(string call)
    {
        var outcome = await SendAsync(call, ScriptedHttpHandler.Throwing(new HttpRequestException("connection refused")));

        AssertIssue(outcome, "Unavailable", Unreachable + "connection refused", statusCode: null);
    }

    [Theory]
    [MemberData(nameof(AllCalls))]
    public async Task Timeout_IsUnavailableWithoutStatus(string call)
    {
        var outcome = await SendAsync(call, ScriptedHttpHandler.Throwing(new TaskCanceledException("timed out")));

        AssertIssue(outcome, "Unavailable", Unreachable + "timed out", statusCode: null);
    }

    [Theory]
    [MemberData(nameof(AllCalls))]
    public async Task CallerCancellation_IsReportedAsUnavailable(string call)
    {
        using var client = CreateClient(ScriptedHttpHandler.WaitingForCancellation());
        using var cancellation = new CancellationTokenSource();

        var pending = Calls[call](client, cancellation.Token);
        await cancellation.CancelAsync();
        var outcome = await pending;

        Assert.Equal("Unavailable", outcome.Issue?.State);
        Assert.StartsWith(Unreachable, outcome.Issue?.Detail);
    }

    [Theory]
    [MemberData(nameof(StatusDetailFailures))]
    public async Task ReadFailure_UsesStatusDetailAndIgnoresServerMessage(string call)
    {
        var outcome = await SendAsync(call, ScriptedHttpHandler.Returning(HttpStatusCode.BadRequest, """{"message":"server reason"}"""));

        AssertIssue(outcome, "Rejected", "The Honua server rejected the admin request; resolve the reported issues before retrying.", 400);
    }

    [Theory]
    [MemberData(nameof(StatusDetailFailures))]
    public async Task ReadFailure_404_IsUnsupportedContract(string call)
    {
        var outcome = await SendAsync(call, ScriptedHttpHandler.Returning(HttpStatusCode.NotFound));

        AssertIssue(outcome, "Unsupported", "The Honua server does not expose this admin API contract.", 404);
    }

    [Theory]
    [MemberData(nameof(ServerMessageFailures))]
    public async Task WriteFailure_SurfacesServerMessage(string call)
    {
        var outcome = await SendAsync(call, ScriptedHttpHandler.Returning(HttpStatusCode.Conflict, """{"message":"Layer name already used"}"""));

        AssertIssue(outcome, "Conflict", "Layer name already used", 409);
    }

    [Theory]
    [MemberData(nameof(ServerMessageFailures))]
    public async Task WriteFailure_FallsBackToProblemDetailsTitle(string call)
    {
        var outcome = await SendAsync(call, ScriptedHttpHandler.Returning(HttpStatusCode.UnprocessableEntity, """{"title":"Invalid layer"}"""));

        AssertIssue(outcome, "Rejected", "Invalid layer", 422);
    }

    [Theory]
    [MemberData(nameof(ServerMessageFailures))]
    public async Task WriteFailure_WithoutServerMessage_UsesStatusDetail(string call)
    {
        var outcome = await SendAsync(call, ScriptedHttpHandler.Returning(HttpStatusCode.Conflict));

        AssertIssue(outcome, "Conflict", "The Honua server reported a conflict for the admin request; reload before retrying.", 409);
    }

    [Theory]
    [MemberData(nameof(EnvelopeWrites))]
    public async Task EnvelopeWriteFailure_KeepsOnlyFieldErrorsWithMessages(string call)
    {
        var outcome = await SendAsync(call, ScriptedHttpHandler.Returning(HttpStatusCode.BadRequest, FieldErrorBody));

        var field = Assert.Single(outcome.Issue!.FieldErrors);
        Assert.Equal("name", field.FieldId);
        Assert.Equal("Already used", field.Message);
    }

    [Theory]
    [MemberData(nameof(BareBodyServerMessageFailures))]
    public async Task BareBodyWriteFailure_KeepsReceiptFieldErrors(string call)
    {
        var outcome = await SendAsync(call, ScriptedHttpHandler.Returning(HttpStatusCode.BadRequest, FieldErrorBody));

        Assert.Equal(["name", "slug"], outcome.Issue!.FieldErrors.Select(error => error.FieldId));
    }

    [Theory]
    [MemberData(nameof(EnvelopeCalls))]
    public async Task EnvelopeSuccessFalse_IsUnavailableWithServerMessage(string call)
    {
        var outcome = await SendAsync(call, ScriptedHttpHandler.Returning(HttpStatusCode.OK, """{"success":false,"message":"Licence expired"}"""));

        AssertIssue(outcome, "Unavailable", "Licence expired", 200);
    }

    [Theory]
    [MemberData(nameof(EnvelopeCalls))]
    public async Task EnvelopeWithoutData_IsUnavailable(string call)
    {
        var outcome = await SendAsync(call, ScriptedHttpHandler.Returning(HttpStatusCode.OK, """{"success":true,"data":null}"""));

        AssertIssue(outcome, "Unavailable", MissingData, 200);
    }

    [Theory]
    [MemberData(nameof(EnvelopeCalls))]
    public async Task MalformedEnvelope_IsUnsupported(string call)
    {
        var outcome = await SendAsync(call, ScriptedHttpHandler.Returning(HttpStatusCode.OK, "{not json"));

        Assert.Equal("Unsupported", outcome.Issue?.State);
        Assert.StartsWith(EnvelopeMismatch, outcome.Issue?.Detail);
        Assert.Equal(200, outcome.Issue?.StatusCode);
    }

    [Fact]
    public async Task EmptyReadEnvelope_IsUnsupported()
    {
        var outcome = await SendAsync(GetEnvelope, ScriptedHttpHandler.Returning(HttpStatusCode.OK));

        Assert.Equal("Unsupported", outcome.Issue?.State);
        Assert.StartsWith(EnvelopeMismatch, outcome.Issue?.Detail);
    }

    [Theory]
    [MemberData(nameof(EnvelopeWrites))]
    public async Task EmptyWriteEnvelope_IsUnavailableWithoutData(string call)
    {
        var outcome = await SendAsync(call, ScriptedHttpHandler.Returning(HttpStatusCode.OK));

        AssertIssue(outcome, "Unavailable", MissingData, 200);
    }

    [Theory]
    [InlineData(GetEnvelope, "[]")]
    [InlineData(PostEnvelope, "{}")]
    [InlineData(PostEnvelopeWithoutBody, "{}")]
    [InlineData(PutEnvelope, "{}")]
    [InlineData(PutAuthoringDocument, "{}")]
    public async Task EnvelopeData_IsReturned(string call, string data)
    {
        var outcome = await SendAsync(call, ScriptedHttpHandler.Returning(HttpStatusCode.OK, $$"""{"success":true,"data":{{data}}}"""));

        Assert.Null(outcome.Issue);
        Assert.NotNull(outcome.Data);
    }

    [Theory]
    [InlineData(GetTableDiscovery, "table-discovery")]
    [InlineData(GetImportFormats, "formats")]
    [InlineData(PostImportUpload, "import-result")]
    [InlineData(PostExternalDiscovery, "discovery")]
    [InlineData(PostGeoservicesImport, "import-job")]
    [InlineData(GetGeoservicesImportJob, "import-progress")]
    public async Task MalformedBareBody_NamesTheExpectedShape(string call, string shape)
    {
        var outcome = await SendAsync(call, ScriptedHttpHandler.Returning(HttpStatusCode.OK, "{not json"));

        Assert.Equal("Unsupported", outcome.Issue?.State);
        Assert.StartsWith($"The Honua server response did not match the expected {shape} shape: ", outcome.Issue?.Detail);
        Assert.Equal(200, outcome.Issue?.StatusCode);
    }

    [Theory]
    [InlineData(GetTableDiscovery)]
    [InlineData(GetImportFormats)]
    public async Task EmptyStreamedBareBody_IsUnsupported(string call)
    {
        var outcome = await SendAsync(call, ScriptedHttpHandler.Returning(HttpStatusCode.OK));

        Assert.Equal("Unsupported", outcome.Issue?.State);
    }

    [Fact]
    public async Task NullTableDiscovery_IsAnEmptyTableList()
    {
        var outcome = await SendAsync(GetTableDiscovery, ScriptedHttpHandler.Returning(HttpStatusCode.OK, "null"));

        Assert.Null(outcome.Issue);
        Assert.Empty(Assert.IsType<HonuaAdminTableInfo[]>(outcome.Data));
    }

    [Theory]
    [InlineData(GetImportFormats, typeof(HonuaAdminImportFormats))]
    [InlineData(PostImportUpload, typeof(HonuaAdminImportResult))]
    [InlineData(PostExternalDiscovery, typeof(HonuaAdminExternalServiceDiscovery))]
    [InlineData(GetGeoservicesImportJob, typeof(HonuaAdminGeoservicesImportProgress))]
    public async Task NullBareBody_IsAnEmptyResult(string call, Type resultType)
    {
        var outcome = await SendAsync(call, ScriptedHttpHandler.Returning(HttpStatusCode.OK, "null"));

        Assert.Null(outcome.Issue);
        Assert.IsType(resultType, outcome.Data);
    }

    [Theory]
    [InlineData(PostImportUpload, typeof(HonuaAdminImportResult))]
    [InlineData(PostExternalDiscovery, typeof(HonuaAdminExternalServiceDiscovery))]
    [InlineData(GetGeoservicesImportJob, typeof(HonuaAdminGeoservicesImportProgress))]
    public async Task EmptyBufferedBareBody_IsAnEmptyResult(string call, Type resultType)
    {
        var outcome = await SendAsync(call, ScriptedHttpHandler.Returning(HttpStatusCode.OK));

        Assert.Null(outcome.Issue);
        Assert.IsType(resultType, outcome.Data);
    }

    [Theory]
    [InlineData("")]
    [InlineData("""{"jobId":" "}""")]
    public async Task GeoservicesImportWithoutJobId_IsUnsupported(string body)
    {
        var outcome = await SendAsync(PostGeoservicesImport, ScriptedHttpHandler.Returning(HttpStatusCode.Accepted, body));

        AssertIssue(outcome, "Unsupported", "The Honua server did not return an import job id.", 202);
    }

    [Fact]
    public async Task BareBodies_AreReturned()
    {
        var tables = await SendAsync(GetTableDiscovery, ScriptedHttpHandler.Returning(HttpStatusCode.OK, """{"tables":[{"schema":"public","table":"roads"}]}"""));
        var job = await SendAsync(PostGeoservicesImport, ScriptedHttpHandler.Returning(HttpStatusCode.Accepted, """{"jobId":"job-9"}"""));

        Assert.Equal("roads", Assert.Single(Assert.IsType<HonuaAdminTableInfo[]>(tables.Data)).Table);
        Assert.Equal("job-9", Assert.IsType<HonuaAdminGeoservicesImportJob>(job.Data).JobId);
    }

    [Theory]
    [MemberData(nameof(AllCalls))]
    public async Task ConfiguredApiKey_IsSent(string call)
    {
        var handler = ScriptedHttpHandler.Returning(HttpStatusCode.NotFound);

        await SendAsync(call, handler, apiKey: "test-key");

        Assert.Equal("test-key", Assert.Single(handler.Requests).ApiKey);
    }

    [Theory]
    [MemberData(nameof(AllCalls))]
    public async Task BlankApiKey_IsNotSent(string call)
    {
        var handler = ScriptedHttpHandler.Returning(HttpStatusCode.NotFound);

        await SendAsync(call, handler, apiKey: " ");

        Assert.Null(Assert.Single(handler.Requests).ApiKey);
    }

    [Theory]
    [InlineData(GetEnvelope)]
    [InlineData(PostEnvelope)]
    [InlineData(PutEnvelope)]
    [InlineData(PutAuthoringDocument)]
    [InlineData(GetTableDiscovery)]
    [InlineData(PostImportUpload)]
    [InlineData(GetGeoservicesImportJob)]
    public async Task BasePathPrefix_IsPreserved(string call)
    {
        var handler = ScriptedHttpHandler.Returning(HttpStatusCode.NotFound);

        await SendAsync(call, handler, baseUri: "https://server.example/honua");

        Assert.StartsWith("https://server.example/honua/api/v1/admin/", Assert.Single(handler.Requests).Uri?.AbsoluteUri);
    }

    private const string FieldErrorBody = """
        {"errors":[
          {"code":"duplicate","fieldId":"name","message":"Already used"},
          {"code":"blank","fieldId":"slug","message":""}
        ]}
        """;

    private static async Task<Outcome> SendAsync(
        string call,
        ScriptedHttpHandler handler,
        string? apiKey = "test-key",
        string baseUri = "https://server.example/")
    {
        using var client = CreateClient(handler, apiKey, baseUri);
        return await Calls[call](client, CancellationToken.None);
    }

    private static HonuaAdminOperateHttpClient CreateClient(
        ScriptedHttpHandler handler,
        string? apiKey = "test-key",
        string baseUri = "https://server.example/") =>
        new(new HttpClient(handler), new HonuaAdminOperateClientOptions(new Uri(baseUri), apiKey));

    private static void AssertIssue(Outcome outcome, string state, string detail, int? statusCode)
    {
        Assert.NotNull(outcome.Issue);
        Assert.Equal(state, outcome.Issue.State);
        Assert.Equal(detail, outcome.Issue.Detail);
        Assert.Equal(statusCode, outcome.Issue.StatusCode);
    }

    private static async Task<Outcome> Run<T>(Task<HonuaAdminEndpointResult<T>> call)
    {
        var result = await call;
        return new Outcome(result.Data, result.Issue);
    }

    private sealed record Outcome(object? Data, HonuaAdminEndpointIssue? Issue);
}
