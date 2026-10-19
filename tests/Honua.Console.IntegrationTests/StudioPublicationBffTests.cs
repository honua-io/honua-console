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
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Honua.Console.IntegrationTests;

public sealed class StudioPublicationBffTests
{
    private const string Publication = "/api/studio/v1/studio/content-items/item/versions/version/publish-requests";
    private const string Rollback = "/api/studio/v1/studio/content-items/item/rollback-requests";

    [Theory]
    [InlineData(Publication)]
    [InlineData(Rollback)]
    public async Task RetainsOriginalSubmissionIdentityAndPendingApprovalReceipt(string path)
    {
        await using var host = await Host.Start();
        host.Wire.Status = HttpStatusCode.Accepted;
        host.Wire.ResponseBody = """{"data":{"status":4,"operationInstanceId":"operation","proposalId":"proposal"}}""";
        host.Client.DefaultRequestHeaders.Add("Idempotency-Key", "original-command");
        for (var attempt = 0; attempt < 2; attempt++)
        {
            using var response = await host.Client.PostAsync(path, new StringContent("{}"));
            Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
            Assert.Equal(host.Wire.ResponseBody, await response.Content.ReadAsStringAsync());
            Assert.Equal("original-command", host.Wire.IdempotencyKey);
            Assert.True(host.Wire.HumanOnly);
        }
        Assert.Equal(2, host.Wire.Calls);
    }

    [Fact]
    public async Task ReceiptPollingReachesExactReadOnlyRouteWithCurrentOperator()
    {
        await using var host = await Host.Start();
        using var response = await host.Client.GetAsync(Publication + "/request");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("/api/v1/studio/content-items/item/versions/version/publish-requests/request", host.Wire.Uri!.AbsolutePath);
        Assert.True(host.Wire.HumanOnly);
        Assert.Null(host.Wire.IdempotencyKey);
        using var mutation = await host.Client.PostAsync(Publication + "/request", new StringContent("{}"));
        Assert.Equal(HttpStatusCode.MethodNotAllowed, mutation.StatusCode);
        using var deeper = await host.Client.GetAsync(Publication + "/request/approve");
        Assert.Equal(HttpStatusCode.NotFound, deeper.StatusCode);
        Assert.Equal(1, host.Wire.Calls);
    }

    [Theory]
    [InlineData(Publication)]
    [InlineData(Rollback)]
    public async Task DuplicateOrMalformedIdempotencyKeysNeverReachServer(string path)
    {
        await using var host = await Host.Start();
        foreach (var value in new[] { "bad/key", "two,keys", new string('a', 129) })
        {
            host.Client.DefaultRequestHeaders.Remove("Idempotency-Key");
            host.Client.DefaultRequestHeaders.Add("Idempotency-Key", value);
            using var response = await host.Client.PostAsync(path, new StringContent("{}"));
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }
        host.Client.DefaultRequestHeaders.Remove("Idempotency-Key");
        host.Client.DefaultRequestHeaders.Add("Idempotency-Key", new[] { "first", "second" });
        using var duplicate = await host.Client.PostAsync(path, new StringContent("{}"));
        Assert.Equal(HttpStatusCode.BadRequest, duplicate.StatusCode);
        Assert.Equal(0, host.Wire.Calls);
    }

    [Fact]
    public async Task PublicationStillRequiresCsrfAndCurrentSessionPartition()
    {
        await using var host = await Host.Start();
        host.Client.DefaultRequestHeaders.Add("Idempotency-Key", "original-command");
        host.Client.DefaultRequestHeaders.Remove("X-CSRF-Token");
        using var missingCsrf = await host.Client.PostAsync(Publication, new StringContent("{}"));
        Assert.Equal(HttpStatusCode.Forbidden, missingCsrf.StatusCode);
        host.Client.DefaultRequestHeaders.Add("X-CSRF-Token", host.Csrf);
        host.Client.DefaultRequestHeaders.Remove("X-Honua-Session-Partition");
        host.Client.DefaultRequestHeaders.Add("X-Honua-Session-Partition", "stale-session");
        using var staleWrite = await host.Client.PostAsync(Publication, new StringContent("{}"));
        using var staleRead = await host.Client.GetAsync(Publication + "/request");
        Assert.Equal(HttpStatusCode.Conflict, staleWrite.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, staleRead.StatusCode);
        Assert.Equal(0, host.Wire.Calls);
    }

    [Fact]
    public async Task AbsentKeysRemainAbsentAndUnrelatedRoutesDoNotAcquireReplayAuthority()
    {
        await using var host = await Host.Start();
        using var publication = await host.Client.PostAsync(Publication, new StringContent("{}"));
        Assert.Equal(HttpStatusCode.OK, publication.StatusCode);
        Assert.Null(host.Wire.IdempotencyKey);
        host.Client.DefaultRequestHeaders.Add("Idempotency-Key", "original-command");
        using var unrelated = await host.Client.PostAsync("/api/agent-workspace/workspace/drafts", new StringContent("{}"));
        Assert.Equal(HttpStatusCode.OK, unrelated.StatusCode);
        Assert.Null(host.Wire.IdempotencyKey);
    }

    private sealed class Host(WebApplication app, HttpClient client, Wire wire, string csrf) : IAsyncDisposable
    {
        public HttpClient Client => client;
        public Wire Wire => wire;
        public string Csrf => csrf;
        public static async Task<Host> Start()
        {
            var builder = WebApplication.CreateSlimBuilder();
            builder.Logging.ClearProviders();
            builder.WebHost.UseUrls("http://127.0.0.1:0");
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
        public string? IdempotencyKey { get; private set; }
        public HttpStatusCode Status { get; set; } = HttpStatusCode.OK;
        public string ResponseBody { get; set; } = "{}";
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
            IdempotencyKey = request.Headers.TryGetValues("Idempotency-Key", out var keys) ? keys.Single() : null;
            return new(Status) { Content = new StringContent(ResponseBody) };
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
