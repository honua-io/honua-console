using System.Net;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Services;
using Honua.Console.Web;
using Honua.Console.Web.Auth;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Honua.Console.IntegrationTests;

// Real Kestrel, antiforgery and bounded relay. Upstream HTTP is a controlled handler,
// so these tests do not qualify an actual Honua import or connected user journey.
public sealed class AgentWorkspaceDatasetImportBffTests
{
    private static readonly string Hash = new('a', 64);
    private const string Root = "/api/agent-workspace/workspace/dataset-import-tasks/task/versions/1";
    private static string Receipt => JsonSerializer.Serialize(new
    {
        taskId = "task", taskRevision = 1, taskContentHash = Hash, uploadId = new string('b', 32),
        version = 2, state = "sealed", acceptedBytes = AgentWorkspaceBff.DatasetImportChunkMaximumBytes, totalBytes = AgentWorkspaceBff.DatasetImportChunkMaximumBytes, chunkBytes = AgentWorkspaceBff.DatasetImportChunkMaximumBytes,
        chunkCount = 1, expiresAt = "2026-10-19T00:00:00Z", contentSha256 = Hash,
        fileId = "must-not-leak-provider-key", eTag = "must-not-leak-generation",
        sourceRetention = new { bytes = AgentWorkspaceBff.DatasetImportChunkMaximumBytes, sha256 = Hash, expiresAt = (string?)null, fileId = "nested-storage-key" }
    });

    [Fact]
    public async Task BinaryChunkUsesPinnedHumanCredentialAndExactQueryWithSafeOriginalReceipt()
    {
        await using var host = await Host.Start();
        host.Wire.Response = Receipt;
        host.Client.DefaultRequestHeaders.Authorization = new("Bearer", "browser-selected-secret");
        host.Client.DefaultRequestHeaders.Add("X-API-Key", "browser-agent-key");
        var bytes = Enumerable.Range(0, AgentWorkspaceBff.DatasetImportChunkMaximumBytes).Select(index => (byte)index).ToArray();
        using var content = new UnknownLengthContent(bytes, "application/octet-stream");
        using var response = await host.Client.PutAsync(Root + $"/upload/chunks?sha256={Hash}&offset=0&expectedVersion=1&contentHash={Hash}", content);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(bytes.Length, host.Wire.Bytes);
        Assert.True(host.Wire.Completed);
        Assert.Equal("/api/v1/agent/workspaces/workspace/dataset-import-tasks/task/versions/1/upload/chunks", host.Wire.Uri!.AbsolutePath);
        Assert.Equal($"?contentHash={Hash}&expectedVersion=1&offset=0&sha256={Hash}", host.Wire.Uri.Query);
        Assert.Equal("application/octet-stream", host.Wire.ContentType);
        Assert.True(host.Wire.HumanOnly);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("\"taskId\":\"task\"", body);
        Assert.DoesNotContain("must-not-leak", body);
        Assert.DoesNotContain("nested-storage-key", body);
        Assert.DoesNotContain("fileId", body);
        Assert.DoesNotContain("eTag", body);
    }

    [Fact]
    public async Task UnknownLengthOverflowStopsAtOneProbeWithoutCompletingUpstreamRequest()
    {
        await using var host = await Host.Start();
        host.Wire.Response = Receipt;
        var bytes = new byte[AgentWorkspaceBff.DatasetImportChunkMaximumBytes + 100_000];
        using var body = new UnknownLengthContent(bytes, "application/octet-stream");
        using var response = await host.Client.PutAsync(Root + $"/upload/chunks?contentHash={Hash}&expectedVersion=1&offset=0&sha256={Hash}", body);
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
        Assert.True(host.Wire.Bytes <= AgentWorkspaceBff.DatasetImportChunkMaximumBytes);
        Assert.False(host.Wire.Completed);
    }

    [Theory]
    [InlineData("duplicate")]
    [InlineData("extra")]
    [InlineData("trailing-newline")]
    [InlineData("negative-offset")]
    [InlineData("overflow-version")]
    public async Task InvalidAndDuplicateQueryValuesNeverReachUpstream(string scenario)
    {
        await using var host = await Host.Start();
        var query = $"contentHash={Hash}&expectedVersion=1&offset=0&sha256={Hash}";
        query = scenario switch
        {
            "duplicate" => query + "&contentHash=" + Hash,
            "extra" => query + "&workspaceId=foreign",
            "trailing-newline" => query.Replace("sha256=" + Hash, "sha256=" + Hash + "%0A"),
            "negative-offset" => query.Replace("offset=0", "offset=-1"),
            _ => query.Replace("expectedVersion=1", "expectedVersion=9223372036854775808")
        };
        using var body = new ByteArrayContent([1, 2, 3]);
        body.Headers.ContentType = new("application/octet-stream");
        using var response = await host.Client.PutAsync(Root + "/upload/chunks?" + query, body);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, host.Wire.Calls);
    }

    [Theory]
    [InlineData("csrf")]
    [InlineData("environment")]
    [InlineData("session")]
    public async Task DeleteRequiresCurrentCsrfEnvironmentAndSessionBinding(string missing)
    {
        await using var host = await Host.Start();
        host.Client.DefaultRequestHeaders.Remove(missing switch
            { "csrf" => "X-CSRF-Token", "environment" => "X-Honua-Environment", _ => "X-Honua-Session-Partition" });
        using var response = await host.Client.DeleteAsync(Root + $"/upload?contentHash={Hash}&expectedVersion=1");
        Assert.Equal(missing == "csrf" ? HttpStatusCode.Forbidden : HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(0, host.Wire.Calls);
    }

    [Fact]
    public async Task DeleteIsBodylessOriginalVersionAndForeignResponseCannotReplaceReceipt()
    {
        await using var host = await Host.Start();
        host.Wire.Response = Receipt;
        using var accepted = await host.Client.DeleteAsync(Root + $"/upload?contentHash={Hash}&expectedVersion=1");
        Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
        Assert.Equal(HttpMethod.Delete, host.Wire.Method);
        Assert.Equal(0, host.Wire.Bytes);
        Assert.True(host.Wire.HumanOnly);
        host.Wire.Response = Receipt.Replace("\"taskId\":\"task\"", "\"taskId\":\"foreign\"");
        using var refused = await host.Client.GetAsync(Root + "/upload?contentHash=" + Hash);
        Assert.Equal(HttpStatusCode.BadGateway, refused.StatusCode);
        Assert.DoesNotContain("foreign", await refused.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task JsonAndResponseLimitsDoNotBorrowTheChunkAllowance()
    {
        await using var host = await Host.Start();
        using (var oversized = new UnknownLengthContent(Encoding.UTF8.GetBytes(new string('x', 70000)), "application/json"))
        using (var refused = await host.Client.PostAsync(Root + "/upload/seal", oversized))
            Assert.Equal(HttpStatusCode.RequestEntityTooLarge, refused.StatusCode);
        Assert.Equal(0, host.Wire.Calls);
        using (var wrongMethod = await host.Client.PostAsync(Root + "/upload/capabilities", new StringContent("{}", Encoding.UTF8, "application/json")))
            Assert.Equal(HttpStatusCode.MethodNotAllowed, wrongMethod.StatusCode);
        using (var unrelatedDelete = await host.Client.DeleteAsync("/api/agent-workspace/workspace/drafts/draft"))
            Assert.Equal(HttpStatusCode.MethodNotAllowed, unrelatedDelete.StatusCode);
        host.Wire.Response = new string('x', 70000);
        using var largeResponse = await host.Client.GetAsync(Root + "/upload?contentHash=" + Hash);
        Assert.Equal(HttpStatusCode.BadGateway, largeResponse.StatusCode);
    }

    [Fact]
    public async Task TypedTaskAndAuthorityRoutesPreserveScopeAndRemoveUnknownNestedFields()
    {
        await using var host = await Host.Start();
        host.Wire.Response = JsonSerializer.Serialize(new
        {
            taskId = "task", revision = 1, contentHash = Hash, ownerActorId = "actual-executor", targetId = "target",
            workspaceId = "workspace", destination = new { schema = "public", table = "roads", password = "not-a-destination-field" },
            source = new { kind = "browser-file", fileName = "roads.geojson", totalBytes = 3, expectedSha256 = Hash, browserSelectionId = "original-human-selection", token = "not-a-source-field" },
            options = new { loadMode = 1, targetSrid = 4326 }, uploadAdmissionDeadline = "2026-10-19T00:00:00Z", executorAuthority = "never-browser-data"
        });
        using var body = new StringContent("{}", Encoding.UTF8, "application/json");
        using var created = await host.Client.PostAsync("/api/agent-workspace/workspace/dataset-import-tasks", body);
        Assert.Equal(HttpStatusCode.OK, created.StatusCode);
        var task = await created.Content.ReadAsStringAsync();
        Assert.DoesNotContain("not-a-", task);
        Assert.Contains("original-human-selection", task);
        Assert.DoesNotContain("never-browser-data", task);
        host.Wire.Response = JsonSerializer.Serialize(new
        {
            taskId = "task", taskRevision = 1, taskContentHash = Hash, executorActorId = "actual-executor",
            originatingTurnId = "original-turn", originalTurnHash = Hash, authorityRevision = 4, authorityFingerprint = Hash,
            policyContextFingerprint = Hash, uploadAdmissionDeadline = "2026-10-19T00:00:00Z", executorAuthority = "never-browser-data"
        });
        using var descriptor = await host.Client.GetAsync(Root + "/authority?contentHash=" + Hash);
        Assert.Equal(HttpStatusCode.OK, descriptor.StatusCode);
        Assert.DoesNotContain("executorAuthority", await descriptor.Content.ReadAsStringAsync());
        host.Wire.Response = JsonSerializer.Serialize(new
            { renewalId = "original-renewal", previousAuthorityRevision = 4, authorityRevision = 5, authorityFingerprint = Hash, renewedAt = "2026-10-18T00:00:00Z" });
        using var renewalBody = new StringContent(JsonSerializer.Serialize(new
            { renewalId = "original-renewal", expectedAuthorityRevision = 4, taskContentHash = Hash }), Encoding.UTF8, "application/json");
        using var renewal = await host.Client.PostAsync(Root + "/authority-renewals", renewalBody);
        Assert.Equal(HttpStatusCode.OK, renewal.StatusCode);
    }

    [Fact]
    public async Task CapabilityProjectionRefusesNominalOneTiBAndNeverPromotesAcceptance()
    {
        await using var host = await Host.Start();
        host.Wire.Response = JsonSerializer.Serialize(new
            { chunkBytes = 4 * 1024 * 1024, maximumFileBytes = 1024L * 1024 * 1024 * 1024, importAcceptanceSupported = false });
        using var oversized = await host.Client.GetAsync(Root + "/upload/capabilities?contentHash=" + Hash);
        Assert.Equal(HttpStatusCode.BadGateway, oversized.StatusCode);
        host.Wire.Response = JsonSerializer.Serialize(new
            { chunkBytes = 4 * 1024 * 1024, maximumFileBytes = 256L * 1024 * 1024 * 1024, importAcceptanceSupported = false, storageToken = "not-browser-data" });
        using var valid = await host.Client.GetAsync(Root + "/upload/capabilities?contentHash=" + Hash);
        Assert.Equal(HttpStatusCode.OK, valid.StatusCode);
        Assert.DoesNotContain("storageToken", await valid.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task SubmissionRelaysOriginalHumanPreparationAndProjectsActualJobOutputWithoutProviderAuthority()
    {
        await using var host = await Host.Start();
        var operation = new string('e', 32);
        host.Wire.Response = JsonSerializer.Serialize(new
        {
            taskId = "task", taskRevision = 1, taskContentHash = Hash, state = "accepted", operationId = operation,
            authorityRevision = 2, authorityFingerprint = Hash, policyContextFingerprint = Hash, acceptBefore = "2026-10-19T00:00:00Z",
            executorAuthority = "must-not-leak", job = new
            {
                jobId = operation, leaseId = "actual-lease", status = "succeeded", createdAt = "2026-10-18T00:00:00Z", updatedAt = "2026-10-18T00:01:00Z",
                protectedAuthority = "must-not-leak", claims = new[] { "must-not-leak" },
                output = new { schema = "actual_schema", physicalTableName = "actual_output", featureCount = 3, detectedSrid = 4326, cloudFileId = "must-not-leak" }
            }
        });
        using var body = new StringContent(JsonSerializer.Serialize(new
        {
            taskContentHash = Hash, authorityRevision = 2, authorityFingerprint = Hash, policyContextFingerprint = Hash
        }), Encoding.UTF8, "application/json");
        using var response = await host.Client.PostAsync(Root + "/submission", body);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(host.Wire.HumanOnly);
        Assert.Equal("/api/v1/agent/workspaces/workspace/dataset-import-tasks/task/versions/1/submission", host.Wire.Uri!.AbsolutePath);
        var json = await response.Content.ReadAsStringAsync();
        Assert.Contains("actual_output", json);
        Assert.DoesNotContain("must-not-leak", json);
        Assert.DoesNotContain("protectedAuthority", json);
        using var recovered = await host.Client.GetAsync(Root + "/submission?contentHash=" + Hash);
        Assert.Equal(HttpStatusCode.OK, recovered.StatusCode);
        Assert.Equal(HttpMethod.Get, host.Wire.Method);
        Assert.Equal("?contentHash=" + Hash, host.Wire.Uri.Query);
    }

    [Theory]
    [InlineData("csrf")]
    [InlineData("environment")]
    [InlineData("session")]
    public async Task SubmissionMutationRequiresCurrentHumanCsrfAndConnectionBinding(string missing)
    {
        await using var host = await Host.Start();
        host.Client.DefaultRequestHeaders.Remove(missing switch
        {
            "csrf" => "X-CSRF-Token", "environment" => "X-Honua-Environment", _ => "X-Honua-Session-Partition"
        });
        using var body = new StringContent("{}", Encoding.UTF8, "application/json");
        using var response = await host.Client.PostAsync(Root + "/submission", body);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(0, host.Wire.Calls);
    }

    [Theory]
    [InlineData("task")]
    [InlineData("authority")]
    [InlineData("unaccepted-job")]
    public async Task SubmissionRejectsRetargetedOrInventedAcceptanceReceipt(string scenario)
    {
        await using var host = await Host.Start();
        host.Wire.Response = JsonSerializer.Serialize(new
        {
            taskId = scenario == "task" ? "foreign-task" : "task", taskRevision = 1, taskContentHash = Hash,
            state = "prepared", operationId = new string('e', 32), authorityRevision = scenario == "authority" ? 3 : 2,
            authorityFingerprint = Hash, policyContextFingerprint = Hash, acceptBefore = "2026-10-19T00:00:00Z",
            job = scenario == "unaccepted-job" ? new { jobId = "invented" } : null
        });
        using var body = new StringContent(JsonSerializer.Serialize(new
        {
            taskContentHash = Hash, authorityRevision = 2, authorityFingerprint = Hash, policyContextFingerprint = Hash
        }), Encoding.UTF8, "application/json");
        using var response = await host.Client.PostAsync(Root + "/submission", body);
        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
    }

    private sealed class Host(WebApplication app, HttpClient client, Wire wire) : IAsyncDisposable
    {
        internal HttpClient Client => client;
        internal Wire Wire => wire;
        internal static async Task<Host> Start()
        {
            var builder = WebApplication.CreateSlimBuilder();
            builder.Logging.ClearProviders();
            builder.WebHost.UseUrls("http://127.0.0.1:0");
            builder.Services.AddAntiforgery(options => options.HeaderName = "X-CSRF-Token");
            var profile = new ConsoleEnvironmentProfile { Id = "environment", ServerBaseUri = new("https://upstream.test/") };
            var profiles = new InMemoryConsoleEnvironmentProfileStore([profile]);
            var sessions = new InMemoryConsoleAccountSessionStore();
            await sessions.SaveSessionAsync(new() { ProfileId = profile.Id, ServerBaseUri = profile.ServerBaseUri });
            var wire = new Wire();
            builder.Services.AddSingleton<IConsoleEnvironmentProfileStore>(profiles);
            builder.Services.AddSingleton<IConsoleAccountSessionStore>(sessions);
            builder.Services.AddSingleton<IConsoleOperatorBearerProvider>(new Bearer());
            builder.Services.AddSingleton<IConsoleOperatorContext>(new Operator());
            builder.Services.AddSingleton(new ConsoleOperatorSessionBridge(profiles, sessions));
            builder.Services.AddSingleton<IHttpClientFactory>(wire);
            var app = builder.Build();
            app.Use(async (context, next) =>
            {
                context.User = new(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "operator")], "test-cookie"));
                await next();
            });
            app.MapGet("/csrf", (HttpContext context, IAntiforgery antiforgery) => antiforgery.GetAndStoreTokens(context).RequestToken!);
            app.MapAgentWorkspaceBff();
            await app.StartAsync();
            var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
            var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { BaseAddress = new(address) };
            client.DefaultRequestHeaders.Add("X-CSRF-Token", await client.GetStringAsync("/csrf"));
            client.DefaultRequestHeaders.Add("X-Honua-Environment", profile.Id);
            client.DefaultRequestHeaders.Add("X-Honua-Session-Partition", AgentGatewayBinding.OperatorPartition("operator", profile.Id, profile.ServerBaseUri));
            return new(app, client, wire);
        }
        public async ValueTask DisposeAsync()
        {
            client.Dispose();
            await app.StopAsync();
            await app.DisposeAsync();
            wire.Dispose();
        }
    }
    private sealed class Wire : HttpMessageHandler, IHttpClientFactory
    {
        internal Uri? Uri { get; private set; }
        internal HttpMethod? Method { get; private set; }
        internal int Calls { get; private set; }
        internal long Bytes { get; private set; }
        internal bool Completed { get; private set; }
        internal bool HumanOnly { get; private set; }
        internal string? ContentType { get; private set; }
        internal string Response { get; set; } = "{}";
        public HttpClient CreateClient(string name) => new(this, disposeHandler: false);
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            Uri = request.RequestUri;
            Method = request.Method;
            ContentType = request.Content?.Headers.ContentType?.MediaType;
            HumanOnly = request.Headers.Authorization?.Parameter == "human-bearer" &&
                !request.Headers.Contains("X-API-Key") && !request.Headers.Contains("Cookie");
            Bytes = 0;
            Completed = false;
            if (request.Content is not null)
            {
                await using var stream = await request.Content.ReadAsStreamAsync(cancellationToken);
                var buffer = new byte[32768];
                int read;
                while ((read = await stream.ReadAsync(buffer, cancellationToken)) != 0) Bytes += read;
            }
            Completed = true;
            return new(HttpStatusCode.OK) { Content = new StringContent(Response) };
        }
    }
    private sealed class Bearer : IConsoleOperatorBearerProvider
    {
        public Task<ConsoleOperatorBearerResolution> ResolveAsync(ConsoleEnvironmentProfile profile, CancellationToken cancellationToken = default)
            => Task.FromResult(new ConsoleOperatorBearerResolution { AccessToken = "human-bearer", HasInteractiveSession = true });
    }
    private sealed class Operator : IConsoleOperatorContext
    {
        public string CurrentOperatorKey => "operator";
        public bool HasOperator => true;
        public string RequireOperatorKey() => CurrentOperatorKey;
    }
    private sealed class UnknownLengthContent : HttpContent
    {
        private readonly byte[] _bytes;
        internal UnknownLengthContent(byte[] bytes, string contentType)
        {
            _bytes = bytes;
            Headers.ContentType = new(contentType);
        }
        protected override bool TryComputeLength(out long length) { length = 0; return false; }
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) => stream.WriteAsync(_bytes).AsTask();
    }
}
