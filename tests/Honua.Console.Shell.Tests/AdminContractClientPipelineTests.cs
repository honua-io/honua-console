using System.Net;
using Honua.Console.Contracts;

namespace Honua.Console.Shell.Tests;

public sealed class AdminContractClientPipelineTests
{
    private static readonly Uri BaseUri = new("https://server.example/");

    private static readonly ClientCall[] ClientCalls =
    [
        new(
            "content GET",
            (handler, token) => Run(new HonuaConsoleContentHttpClient(Http(handler), new HonuaConsoleContentClientOptions(BaseUri, "key")).GetAsync("item-1", token)),
            Endpoint: "The Honua server Console content endpoint",
            Mismatch: "The Honua server Console content response did not match the expected contract: ",
            MissingBody: EnvelopeWithoutData,
            Missing: "The Honua server Console content response did not include data.",
            NotFound: "The Honua server does not expose this admin API contract."),
        new(
            "content POST",
            (handler, token) => Run(new HonuaConsoleContentHttpClient(Http(handler), new HonuaConsoleContentClientOptions(BaseUri, "key"))
                .CreateAsync(new HonuaCreateConsoleContentItemRequest { Name = "roads" }, token)),
            Endpoint: "The Honua server Console content endpoint",
            Mismatch: "The Honua server Console content response did not match the expected contract: ",
            MissingBody: EnvelopeWithoutData,
            Missing: "The Honua server Console content response did not include data.",
            NotFound: "The Honua server does not expose this admin API contract."),
        new(
            "catalog discovery GET",
            (handler, token) => Run(new HonuaCatalogDiscoveryHttpClient(Http(handler), new HonuaCatalogDiscoveryClientOptions(BaseUri, "key")).GetRegistryAsync("ws-1", token)),
            Endpoint: "The Honua server catalog discovery endpoint",
            Mismatch: "The Honua server catalog discovery response did not match the expected contract: ",
            MissingBody: EnvelopeWithoutData,
            Missing: "The Honua server catalog discovery response did not include data.",
            NotFound: "The Honua server does not expose the catalog discovery-endpoints registry (honua-server#1279)."),
        new(
            "rbac GET",
            (handler, token) => Run(new HonuaConsoleRbacHttpClient(Http(handler), new HonuaConsoleRbacClientOptions(BaseUri, "key")).GetOverviewAsync("ws-1", token)),
            Endpoint: "The Honua server Console Access endpoint",
            Mismatch: "The Honua server Console Access response did not match the expected contract: ",
            MissingBody: EnvelopeWithoutData,
            Missing: "The Honua server Console Access response did not include data.",
            NotFound: "The Honua server does not expose the Console Access (RBAC) contract."),
        new(
            "rbac POST",
            (handler, token) => Run(new HonuaConsoleRbacHttpClient(Http(handler), new HonuaConsoleRbacClientOptions(BaseUri, "key"))
                .CreateRoleAsync("ws-1", new HonuaConsoleRoleWriteRequest { Name = "Editors" }, token)),
            Endpoint: "The Honua server Console Access endpoint",
            Mismatch: "The Honua server Console Access response did not match the expected contract: ",
            MissingBody: EnvelopeWithoutData,
            Missing: "The Honua server Console Access response did not include data.",
            NotFound: "The role was not found, or the Console Access contract is not exposed."),
        new(
            "share GET",
            (handler, token) => Run(new HonuaConsoleShareHttpClient(Http(handler), new HonuaConsoleShareClientOptions(BaseUri, "key")).GetShareAsync("item-1", token)),
            Endpoint: "The Honua server Console share endpoint",
            Mismatch: "The Honua server Console share response did not match the expected contract: ",
            MissingBody: EnvelopeWithoutData,
            Missing: "The Honua server Console share response did not include data.",
            NotFound: "The Honua server has no Console item or share token with that ID."),
        new(
            "form package GET",
            (handler, token) => Run(new HonuaFormPackageHttpClient(Http(handler), new HonuaFormPackageClientOptions(BaseUri, "key")).ListPackagesAsync(token)),
            Endpoint: "The Honua server form endpoint",
            Mismatch: "The Honua server form response did not match the expected contract: ",
            MissingBody: "null",
            Missing: "The Honua server form response body was empty.",
            NotFound: "The Honua server does not expose this admin API contract."),
        new(
            "content publication GET",
            (handler, token) => Run(new HonuaContentPublicationHttpClient(Http(handler), new HonuaContentPublicationClientOptions(BaseUri, "key")).GetAsync("pub-1", token)),
            Endpoint: "The Honua server content publication endpoint",
            Mismatch: "The Honua server content publication response did not match the expected contract: ",
            MissingBody: "null",
            Missing: "The Honua server content publication response body was empty.",
            NotFound: "The Honua server does not expose this admin API contract."),
        new(
            "temporal bare GET",
            (handler, token) => Run(new HonuaTemporalHttpClient(Http(handler), new HonuaTemporalClientOptions(BaseUri, "key")).GetCapabilityAsync("svc", 1, token)),
            Endpoint: "The Honua server temporal endpoint",
            Mismatch: "The Honua server temporal response did not match the expected contract: ",
            MissingBody: "null",
            Missing: "The Honua server temporal response body was empty.",
            NotFound: "The Honua server temporal service, layer, or replica was not found."),
        new(
            "temporal envelope GET",
            (handler, token) => Run(new HonuaTemporalHttpClient(Http(handler), new HonuaTemporalClientOptions(BaseUri, "key")).ListReplicasAsync("svc", token)),
            Endpoint: "The Honua server temporal endpoint",
            Mismatch: "The Honua server response did not match the expected admin API shape: ",
            MissingBody: EnvelopeWithoutData,
            Missing: "The Honua server response did not include data.",
            NotFound: "The Honua server temporal service, layer, or replica was not found."),
        new(
            "version management GET",
            (handler, token) => Run(new HonuaVersionManagementHttpClient(Http(handler), new HonuaVersionManagementClientOptions(BaseUri, "key")).ListVersionsAsync("svc", token)),
            Endpoint: "The Honua server endpoint",
            Mismatch: "The Honua server VersionManagementServer response did not match the expected contract: ",
            MissingBody: "",
            Missing: "The Honua server VersionManagementServer response did not include a body.",
            NotFound: "The Honua server does not expose the VersionManagementServer for this service."),
    ];

    private const string EnvelopeWithoutData = """{"success":true,"data":null}""";

    public static TheoryData<string> Clients => new(ClientCalls.Select(call => call.Name));

    [Theory]
    [MemberData(nameof(Clients))]
    public async Task TransportFailure_NamesTheEndpoint(string client)
    {
        var call = Find(client);

        var outcome = await call.Send(ScriptedHttpHandler.Throwing(new HttpRequestException("connection refused")));

        Assert.Equal("Unavailable", outcome.Issue?.State);
        Assert.Equal($"{call.Endpoint} could not be reached: connection refused", outcome.Issue?.Detail);
        Assert.Null(outcome.Issue?.StatusCode);
    }

    [Theory]
    [MemberData(nameof(Clients))]
    public async Task Timeout_IsUnavailable(string client)
    {
        var call = Find(client);

        var outcome = await call.Send(ScriptedHttpHandler.Throwing(new TaskCanceledException("timed out")));

        Assert.Equal($"{call.Endpoint} could not be reached: timed out", outcome.Issue?.Detail);
    }

    [Theory]
    [MemberData(nameof(Clients))]
    public async Task CallerCancellation_Propagates(string client)
    {
        var call = Find(client);
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            call.Send(new ScriptedHttpHandler(_ => Task.FromCanceled<HttpResponseMessage>(cancellation.Token)), cancellation.Token));
    }

    [Theory]
    [MemberData(nameof(Clients))]
    public async Task NotFound_UsesTheClientDetail(string client)
    {
        var call = Find(client);

        var outcome = await call.Send(ScriptedHttpHandler.Returning(HttpStatusCode.NotFound));

        Assert.Equal("Unsupported", outcome.Issue?.State);
        Assert.Equal(call.NotFound, outcome.Issue?.Detail);
        Assert.Equal(404, outcome.Issue?.StatusCode);
    }

    [Theory]
    [MemberData(nameof(Clients))]
    public async Task MalformedBody_IsUnsupported(string client)
    {
        var call = Find(client);

        var outcome = await call.Send(ScriptedHttpHandler.Returning(HttpStatusCode.OK, "{not json"));

        Assert.Equal("Unsupported", outcome.Issue?.State);
        Assert.StartsWith(call.Mismatch, outcome.Issue?.Detail);
        Assert.Equal(200, outcome.Issue?.StatusCode);
    }

    [Theory]
    [MemberData(nameof(Clients))]
    public async Task MissingBody_IsUnavailable(string client)
    {
        var call = Find(client);

        var outcome = await call.Send(ScriptedHttpHandler.Returning(HttpStatusCode.OK, call.MissingBody));

        Assert.Equal("Unavailable", outcome.Issue?.State);
        Assert.Equal(call.Missing, outcome.Issue?.Detail);
        Assert.Equal(200, outcome.Issue?.StatusCode);
    }

    [Theory]
    [MemberData(nameof(Clients))]
    public async Task ApiKey_IsSent(string client)
    {
        var handler = ScriptedHttpHandler.Returning(HttpStatusCode.NotFound);

        await Find(client).Send(handler);

        Assert.Equal("key", Assert.Single(handler.Requests).ApiKey);
    }

    [Theory]
    [InlineData(HttpStatusCode.Conflict, "Unavailable")]
    [InlineData(HttpStatusCode.BadRequest, "Unavailable")]
    public async Task CatalogDiscoveryRead_KeepsItsOwnStateMapping(HttpStatusCode status, string state)
    {
        var outcome = await Find("catalog discovery GET").Send(ScriptedHttpHandler.Returning(status));

        Assert.Equal(state, outcome.Issue?.State);
    }

    [Fact]
    public async Task ShareFailure_PrefersTheServerMessage()
    {
        var outcome = await Find("share GET").Send(
            ScriptedHttpHandler.Returning(HttpStatusCode.Conflict, """{"success":false,"message":"Blocked by 2 dependencies"}"""));

        Assert.Equal("Conflict", outcome.Issue?.State);
        Assert.Equal("Blocked by 2 dependencies", outcome.Issue?.Detail);
    }

    [Fact]
    public async Task ShareCommandWithoutData_IsAnAcknowledgement()
    {
        using var client = new HonuaConsoleShareHttpClient(
            Http(ScriptedHttpHandler.Returning(HttpStatusCode.OK, """{"success":true,"data":null,"message":"Revoked"}""")),
            new HonuaConsoleShareClientOptions(BaseUri, "key"));

        var result = await client.RevokePublicLinkAsync("item-1", "token-1");

        Assert.Null(result.Issue);
        Assert.Equal("Revoked", result.Data?.Message);
    }

    [Fact]
    public async Task RbacDelete_SucceedsWithoutBodyAndMapsRejection()
    {
        using var deleted = new HonuaConsoleRbacHttpClient(
            Http(ScriptedHttpHandler.Returning(HttpStatusCode.NoContent)), new HonuaConsoleRbacClientOptions(BaseUri, "key"));
        using var rejected = new HonuaConsoleRbacHttpClient(
            Http(ScriptedHttpHandler.Returning(HttpStatusCode.BadRequest)), new HonuaConsoleRbacClientOptions(BaseUri, "key"));

        var success = await deleted.DeleteRoleAsync("ws-1", "role-1");
        var failure = await rejected.DeleteRoleAsync("ws-1", "role-1");

        Assert.True(success.Data);
        Assert.Equal("Rejected", failure.Issue?.State);
        Assert.Equal(
            "The Honua server rejected the role change (e.g. blank name, or a built-in role cannot be edited or deleted).",
            failure.Issue?.Detail);
    }

    [Fact]
    public async Task FormPackagePublishRejection_CarriesTheValidationResult()
    {
        using var client = new HonuaFormPackageHttpClient(
            Http(ScriptedHttpHandler.Returning(HttpStatusCode.BadRequest, """{"isValid":false,"issues":[]}""")),
            new HonuaFormPackageClientOptions(BaseUri, "key"));

        var result = await client.PublishAsync("form-1", 2);

        Assert.Equal("Rejected", result.Issue?.State);
        Assert.False(result.Data?.Validation?.IsValid);
        Assert.Equal(2, result.Data?.Version);
    }

    [Fact]
    public async Task VersionManagementFailure_PrefersTheServerMessage()
    {
        var outcome = await Find("version management GET").Send(
            ScriptedHttpHandler.Returning(HttpStatusCode.Conflict, """{"error":{"code":409,"message":"Version is locked"}}"""));

        Assert.Equal("Rejected", outcome.Issue?.State);
        Assert.Equal("Version is locked", outcome.Issue?.Detail);
    }

    private static ClientCall Find(string name) => ClientCalls.Single(call => call.Name == name);

    private static HttpClient Http(HttpMessageHandler handler) => new(handler);

    private static async Task<Outcome> Run<T>(Task<HonuaAdminEndpointResult<T>> call)
    {
        var result = await call;
        return new Outcome(result.Data, result.Issue);
    }

    private sealed record Outcome(object? Data, HonuaAdminEndpointIssue? Issue);

    private sealed record ClientCall(
        string Name,
        Func<ScriptedHttpHandler, CancellationToken, Task<Outcome>> Invoke,
        string Endpoint,
        string Mismatch,
        string MissingBody,
        string Missing,
        string NotFound)
    {
        public Task<Outcome> Send(ScriptedHttpHandler handler) => Invoke(handler, CancellationToken.None);

        public Task<Outcome> Send(ScriptedHttpHandler handler, CancellationToken cancellationToken) =>
            Invoke(handler, cancellationToken);
    }
}
