using System.Net;
using System.Security.Claims;
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
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Honua.Console.IntegrationTests;

public sealed class AgentWorkspaceProcessBffTests
{
    [Theory]
    [InlineData("pkg-ʻāina", 1)]
    [InlineData("東京 #?%", int.MaxValue)]
    public async Task RoutedUnicodePackageIdentityIsReescapedWithoutChangingUpstreamTarget(string id, int version)
    {
        await using var host = await Host.Start();
        var response = await host.Client.GetAsync($"/api/processes/console/workflow-packages/{Uri.EscapeDataString(id)}/versions/{version}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal($"/api/v1/console/workflow-packages/{id}/versions/{version}", Uri.UnescapeDataString(host.Wire.Uri!.AbsolutePath));
        Assert.Equal(string.Empty, host.Wire.Uri.Query);
        Assert.Equal(string.Empty, host.Wire.Uri.Fragment);
        Assert.True(host.Wire.HumanOnly);
    }

    [Theory]
    [InlineData(".")]
    [InlineData("..")]
    [InlineData(" padded")]
    [InlineData("padded\u00a0")]
    [InlineData("control\u0085character")]
    [InlineData("back\\slash")]
    public void PackageRouteRejectsControlWhitespaceAndTraversalSegments(string id)
        => Assert.False(AgentWorkspaceBff.TryBuildProcessPath($"console/workflow-packages/{id}/versions/1", out _));

    [Theory]
    [InlineData("0")]
    [InlineData("2147483648")]
    [InlineData("9999999999")]
    public void PackageRouteRejectsVersionsOutsidePositiveInt32(string version)
        => Assert.False(AgentWorkspaceBff.TryBuildProcessPath($"console/workflow-packages/package/versions/{version}", out _));

    [Fact]
    public async Task UploadCorrelationIsForwardedWithoutGrantingReplayOrAlternateSessionAuthority()
    {
        await using var host = await Host.Start();
        var uploadId = Guid.NewGuid().ToString();
        using var accepted = await host.Client.PostAsync("/api/processes/admin/import/upload", Upload(uploadId));
        Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
        Assert.Contains(uploadId, host.Wire.Body, StringComparison.Ordinal);
        Assert.Contains("UploadId", host.Wire.Body, StringComparison.Ordinal);
        Assert.Equal(2, host.Wire.TextParts.Count);
        Assert.Equal(("fixture_table", "text/plain", "utf-8"), host.Wire.TextParts["TableName"]);
        Assert.Equal((uploadId, "text/plain", "utf-8"), host.Wire.TextParts["UploadId"]);
        Assert.True(host.Wire.HumanOnly);
        Assert.False(host.Wire.IdempotencyKey);
        var calls = host.Wire.Calls;
        host.Client.DefaultRequestHeaders.Remove("X-Honua-Session-Partition");
        host.Client.DefaultRequestHeaders.Add("X-Honua-Session-Partition", "stale-session");
        using var refused = await host.Client.PostAsync("/api/processes/admin/import/upload", Upload(Guid.NewGuid().ToString()));
        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        Assert.Equal(calls, host.Wire.Calls);
    }

    [Theory]
    [InlineData("")]
    [InlineData("../other")]
    [InlineData("two,values")]
    [InlineData("trailing\n")]
    public async Task InvalidUploadCorrelationDoesNotReachServer(string uploadId)
    {
        await using var host = await Host.Start();
        using var response = await host.Client.PostAsync("/api/processes/admin/import/upload", Upload(uploadId));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, host.Wire.Calls);
    }

    [Fact]
    public async Task TableNameWithFinalNewlineDoesNotReachServer()
    {
        await using var host = await Host.Start();
        using var body = new MultipartFormDataContent();
        body.Add(new ByteArrayContent([1, 2, 3]), "File", "fixture.geojson");
        body.Add(new StringContent("fixture_table\n"), "TableName");
        using var response = await host.Client.PostAsync("/api/processes/admin/import/upload", body);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, host.Wire.Calls);
    }

    [Fact]
    public async Task UploadCorrelationIsOptionalAndDuplicateOrUnboundedFieldsAreRefused()
    {
        await using var host = await Host.Start();
        using var absent = await host.Client.PostAsync("/api/processes/admin/import/upload", Upload(null));
        Assert.Equal(HttpStatusCode.OK, absent.StatusCode);
        Assert.DoesNotContain("UploadId", host.Wire.Body, StringComparison.Ordinal);
        var calls = host.Wire.Calls;
        using var duplicate = Upload("first");
        duplicate.Add(new StringContent("second"), "UploadId");
        using var refused = await host.Client.PostAsync("/api/processes/admin/import/upload", duplicate);
        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        using var oversized = await host.Client.PostAsync("/api/processes/admin/import/upload", Upload(new string('x', 129)));
        Assert.Equal(HttpStatusCode.BadRequest, oversized.StatusCode);
        Assert.Equal(calls, host.Wire.Calls);
    }

    [Fact]
    public async Task KnownJobCancelRequiresPostAndAntiforgeryAndPreservesOperatorBinding()
    {
        await using var host = await Host.Start();
        using var read = await host.Client.GetAsync("/api/processes/admin/import/jobs/job-id/cancel");
        Assert.Equal(HttpStatusCode.MethodNotAllowed, read.StatusCode);
        host.Client.DefaultRequestHeaders.Remove("X-CSRF-Token");
        using var missingCsrf = await host.Client.PostAsync("/api/processes/admin/import/jobs/job-id/cancel", null);
        Assert.Equal(HttpStatusCode.Forbidden, missingCsrf.StatusCode);
        host.Client.DefaultRequestHeaders.Add("X-CSRF-Token", host.Csrf);
        using var cancelled = await host.Client.PostAsync("/api/processes/admin/import/jobs/job-id/cancel", null);
        Assert.Equal(HttpStatusCode.OK, cancelled.StatusCode);
        Assert.Equal("/api/v1/admin/import/jobs/job-id/cancel", host.Wire.Uri!.AbsolutePath);
        Assert.True(host.Wire.HumanOnly);
        Assert.Equal(1, host.Wire.Calls);
    }

    [Fact]
    public async Task CommandRecoveryForwardsOnlyPinnedDraftIdentityAndRevision()
    {
        await using var host = await Host.Start();
        using var accepted = await host.Client.GetAsync("/api/agent-workspace/workspace/commands/original?draftId=draft&draftRevision=3");
        Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
        Assert.Equal("?draftId=draft&draftRevision=3", host.Wire.Uri!.Query);
        Assert.True(host.Wire.HumanOnly);
        Assert.Equal(1, host.Wire.Calls);
    }

    [Theory]
    [InlineData("draftId=draft")]
    [InlineData("draftRevision=3")]
    [InlineData("draftId=draft&draftRevision=-1")]
    [InlineData("draftId=draft&draftRevision=9223372036854775808")]
    [InlineData("draftId=draft&draftRevision=3&draftRevision=4")]
    [InlineData("draftId=draft&draftRevision=3&targetId=other")]
    public async Task InvalidCommandRecoveryBindingDoesNotReachServer(string query)
    {
        await using var host = await Host.Start();
        using var refused = await host.Client.GetAsync("/api/agent-workspace/workspace/commands/original?" + query);
        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        Assert.Equal(0, host.Wire.Calls);
    }

    [Fact]
    public async Task AuthenticatedSessionAdvertisesEffectiveFileBytesWithFramingHeadroom()
    {
        await using var host = await Host.Start();
        using var response = await host.Client.GetAsync("/api/agent-workspace/session");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.True(document.RootElement.GetProperty("authenticated").GetBoolean());
        Assert.Equal(AgentWorkspaceBff.ProcessArtifactFileMaximumBytes, document.RootElement.GetProperty("maxArtifactUploadBytes").GetInt32());
        Assert.False(document.RootElement.GetProperty("importTransportResumable").GetBoolean());
        Assert.True(AgentWorkspaceBff.ProcessArtifactFileMaximumBytes < AgentWorkspaceBff.ProcessImportBodyMaximumBytes);
    }

    [Theory]
    [InlineData("true", "scoped-agent-key", true)]
    [InlineData("false", "scoped-agent-key", false)]
    [InlineData(null, "scoped-agent-key", false)]
    [InlineData("unknown", "scoped-agent-key", false)]
    [InlineData("true", null, false)]
    public async Task SessionOffersNativeChatOnlyWithExplicitModelAndTransportKey(
        string? modelEnabled, string? agentKey, bool expected)
    {
        await using var host = await Host.Start(modelEnabled, agentKey);
        using var response = await host.Client.GetAsync("/api/agent-workspace/session");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.True(document.RootElement.GetProperty("authenticated").GetBoolean());
        Assert.Equal(expected, document.RootElement.GetProperty("nativeAgentAvailable").GetBoolean());
    }

    [Fact]
    public async Task CanonicalUnknownLengthMultipartAtEffectiveFileLimitReachesServer()
    {
        await using var host = await Host.Start();
        using var body = SizedUpload(AgentWorkspaceBff.ProcessArtifactFileMaximumBytes);
        using var response = await host.Client.PostAsync("/api/processes/admin/import/upload", body);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(1, host.Wire.Calls);
        Assert.True(host.Wire.HumanOnly);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(165536)]
    public async Task UnknownLengthFileAboveEffectiveOrRawBodyLimitCannotReachServer(int excess)
    {
        await using var host = await Host.Start();
        using var body = SizedUpload(AgentWorkspaceBff.ProcessArtifactFileMaximumBytes + excess);
        using var response = await host.Client.PostAsync("/api/processes/admin/import/upload", body);
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
        Assert.Equal(0, host.Wire.Calls);
    }

    [Fact]
    public async Task FormTokenCannotTriggerMultipartParsingInsteadOfRequiredHeader()
    {
        await using var host = await Host.Start();
        host.Client.DefaultRequestHeaders.Remove("X-CSRF-Token");
        using var body = Upload(null);
        body.Add(new StringContent(host.Csrf), "__RequestVerificationToken");
        using var response = await host.Client.PostAsync("/api/processes/admin/import/upload", body);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(0, host.Wire.Calls);
    }

    private static MultipartFormDataContent SizedUpload(int bytes)
    {
        var body = new MultipartFormDataContent();
        body.Add(new UnknownLengthRepeatedContent(bytes), "File", "fixture.geojson");
        body.Add(new StringContent("fixture_table"), "TableName");
        body.Add(new StringContent(Guid.NewGuid().ToString()), "UploadId");
        return body;
    }

    private sealed class UnknownLengthRepeatedContent(int bytes) : HttpContent
    {
        protected override bool TryComputeLength(out long length) { length = 0; return false; }
        protected override async Task SerializeToStreamAsync(Stream stream, TransportContext? context)
        {
            var buffer = new byte[65536];
            for (var offset = 0; offset < bytes; offset += buffer.Length)
                await stream.WriteAsync(buffer.AsMemory(0, Math.Min(buffer.Length, bytes - offset)));
        }
    }

    private static MultipartFormDataContent Upload(string? uploadId)
    {
        var body = new MultipartFormDataContent();
        body.Add(new ByteArrayContent([1, 2, 3]), "File", "fixture.geojson");
        body.Add(new StringContent("fixture_table"), "TableName");
        if (uploadId is not null) body.Add(new StringContent(uploadId), "UploadId");
        return body;
    }

    private sealed class Host(WebApplication app, HttpClient client, Wire wire, string csrf) : IAsyncDisposable
    {
        public HttpClient Client => client;
        public Wire Wire => wire;
        public string Csrf => csrf;
        public static async Task<Host> Start(string? nativeModelEnabled = null, string? agentKey = null)
        {
            var builder = WebApplication.CreateSlimBuilder();
            builder.Logging.ClearProviders();
            builder.WebHost.UseUrls("http://127.0.0.1:0");
            builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["HONUA_NATIVE_MODEL_ENABLED"] = nativeModelEnabled,
                ["HONUA_AGENT_API_KEY"] = agentKey
            });
            builder.Services.AddAntiforgery(options => options.HeaderName = "X-CSRF-Token");
            var profile = new ConsoleEnvironmentProfile { Id = "environment", ServerBaseUri = new Uri("https://upstream.test/") };
            var profiles = new InMemoryConsoleEnvironmentProfileStore([profile]);
            var sessions = new InMemoryConsoleAccountSessionStore();
            await sessions.SaveSessionAsync(new ConsoleAccountSession { ProfileId = profile.Id, ServerBaseUri = profile.ServerBaseUri });
            var wire = new Wire();
            builder.Services.AddSingleton<IConsoleEnvironmentProfileStore>(profiles);
            builder.Services.AddSingleton<IConsoleAccountSessionStore>(sessions);
            builder.Services.AddSingleton(new ConsoleOperatorSessionBridge(profiles, sessions));
            builder.Services.AddSingleton<IConsoleOperatorBearerProvider>(new Bearer());
            builder.Services.AddSingleton<IConsoleOperatorContext>(new Operator());
            builder.Services.AddSingleton<IHttpClientFactory>(wire);
            var app = builder.Build();
            app.Use(async (context, next) =>
            {
                context.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "operator")], "test-cookie"));
                await next();
            });
            app.MapGet("/csrf", (HttpContext context, IAntiforgery antiforgery) => antiforgery.GetAndStoreTokens(context).RequestToken!);
            app.MapAgentWorkspaceBff();
            await app.StartAsync();
            var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
            var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { BaseAddress = new Uri(address) };
            var csrf = await client.GetStringAsync("/csrf");
            client.DefaultRequestHeaders.Add("X-CSRF-Token", csrf);
            client.DefaultRequestHeaders.Add("X-Honua-Environment", profile.Id);
            client.DefaultRequestHeaders.Add("X-Honua-Session-Partition", AgentGatewayBinding.OperatorPartition("operator", profile.Id, profile.ServerBaseUri));
            return new(app, client, wire, csrf);
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
        public Uri? Uri { get; private set; }
        public string Body { get; private set; } = string.Empty;
        public Dictionary<string, (string Value, string? MediaType, string? Charset)> TextParts { get; } = [];
        public bool HumanOnly { get; private set; }
        public bool IdempotencyKey { get; private set; }
        public int Calls { get; private set; }
        public HttpClient CreateClient(string name) => new(this, disposeHandler: false);
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            Uri = request.RequestUri;
            Body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
            TextParts.Clear();
            if (request.Content is MultipartFormDataContent multipart)
            {
                foreach (var part in multipart)
                {
                    var name = part.Headers.ContentDisposition?.Name?.Trim('"');
                    if (name is "TableName" or "UploadId")
                        TextParts.Add(name, (await part.ReadAsStringAsync(cancellationToken),
                            part.Headers.ContentType?.MediaType, part.Headers.ContentType?.CharSet));
                }
            }
            HumanOnly = request.Headers.Authorization?.Scheme == "Bearer" && request.Headers.Authorization.Parameter == "human-bearer"
                && !request.Headers.Contains("X-API-Key") && !request.Headers.Contains("Cookie");
            IdempotencyKey = request.Headers.Contains("Idempotency-Key");
            return new(HttpStatusCode.OK) { Content = new StringContent("{}") };
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
}
