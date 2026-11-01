using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
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

public sealed class AgentSessionStreamingTests
{
    [Theory]
    [InlineData("ai")]
    [InlineData("mcp")]
    [InlineData("studio/ai")]
    [InlineData("studio/mcp")]
    public async Task AuthorizedProxyDeliversFirstEventBeforeCompletionAndCancelsUpstream(string endpoint)
    {
        using var upstream = new PausedEventStream();
        using var wire = new Upstream(upstream);
        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["HONUA_AGENT_API_KEY"] = endpoint.StartsWith("studio/") ? null : "scoped-agent-key",
            ["HONUA_AGENT_SERVER_BASE_URL"] = "https://upstream.test/",
            ["HONUA_AGENT_GATEWAY_URL"] = null,
            ["HONUA_AGENT_GATEWAY_KEY"] = null
        });
        builder.Services.AddAntiforgery(options => options.HeaderName = "X-CSRF-Token");
        builder.Services.AddSingleton<IConsoleEnvironmentProfileStore>(new InMemoryConsoleEnvironmentProfileStore([
            new ConsoleEnvironmentProfile {Id="environment",ServerBaseUri=new Uri("https://upstream.test/")}
        ]));
        builder.Services.AddSingleton<IConsoleOperatorBearerProvider>(new Bearer());
        builder.Services.AddSingleton<IConsoleOperatorContext>(new Operator());
        builder.Services.AddSingleton<IHttpClientFactory>(wire);
        builder.Services.AddSingleton<AgentMcpSessions>();
        await using var app = builder.Build();
        app.Use(async (context, next) =>
        {
            context.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "operator")], "test-cookie"));
            await next();
        });
        app.MapGet("/csrf", (HttpContext context, IAntiforgery csrf) => csrf.GetAndStoreTokens(context).RequestToken!);
        app.MapAgentSessionBff();
        await app.StartAsync();
        try
        {
            var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
            using var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { BaseAddress = new Uri(address) };
            var csrf = await client.GetStringAsync("/csrf");
            using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/agent-workspace/{(endpoint.StartsWith("studio/") ? endpoint : "agent/" + endpoint)}") { Content = new StringContent("{}", Encoding.UTF8, "application/json") };
            request.Headers.Add("X-CSRF-Token", csrf);
            request.Headers.Add("X-Honua-Environment", "environment");
            request.Headers.Add("X-Honua-Workspace", "workspace");
            request.Headers.Add("X-Honua-Turn", "turn");
            request.Headers.Add("X-Honua-View", "view");
            request.Headers.Add("X-Honua-Session-Partition", AgentGatewayBinding.OperatorPartition("operator", "environment", new Uri("https://upstream.test/")));
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead).WaitAsync(TimeSpan.FromSeconds(15));
            response.EnsureSuccessStatusCode();
            Assert.Equal("text/event-stream", response.Content.Headers.ContentType?.MediaType);
            Assert.True(response.Headers.CacheControl?.NoStore);
            await using var body = await response.Content.ReadAsStreamAsync();
            var bytes = new byte[Encoding.UTF8.GetByteCount("data: first\n\n")];
            await body.ReadExactlyAsync(bytes).AsTask().WaitAsync(TimeSpan.FromSeconds(15));
            Assert.Equal("data: first\n\n", Encoding.UTF8.GetString(bytes));
            Assert.False(upstream.Completed);
            using var cancelled = new CancellationTokenSource();
            var nextRead = body.ReadAsync(bytes, cancelled.Token).AsTask();
            cancelled.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await nextRead);
            response.Dispose();
            await upstream.Cancelled.Task.WaitAsync(TimeSpan.FromSeconds(15));
            if (endpoint.StartsWith("studio/"))
            {
                Assert.False(wire.HumanMembershipChecked);
                Assert.True(wire.HumanCredentialUsed);
            }
            else
            {
                Assert.True(wire.HumanMembershipChecked);
                Assert.True(wire.IndependentAgentCredentialUsed);
            }
            Assert.True(wire.NegotiatedStreaming);
        }
        finally
        {
            upstream.Release();
            await app.StopAsync();
        }
    }

    [Theory]
    [InlineData(false, "environment", "valid", "view", "", 403)]
    [InlineData(true, "other", "valid", "view", "", 403)]
    [InlineData(true, "environment", "wrong", "view", "", 403)]
    [InlineData(true, "environment", "valid", "../view", "", 400)]
    [InlineData(true, "environment", "valid", "view", "?target=other", 400)]
    public async Task StudioProxyRefusesUnboundRequestsBeforeUpstream(bool csrfEnabled, string environment, string partition, string view, string query, int expected)
    {
        using var stream = new PausedEventStream();
        using var wire = new Upstream(stream);
        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.AddAntiforgery(options => options.HeaderName = "X-CSRF-Token");
        builder.Services.AddSingleton<IConsoleEnvironmentProfileStore>(new InMemoryConsoleEnvironmentProfileStore([
            new ConsoleEnvironmentProfile { Id = "environment", ServerBaseUri = new Uri("https://upstream.test/") }
        ]));
        builder.Services.AddSingleton<IConsoleOperatorBearerProvider>(new Bearer());
        builder.Services.AddSingleton<IConsoleOperatorContext>(new Operator());
        builder.Services.AddSingleton<IHttpClientFactory>(wire);
        builder.Services.AddSingleton<AgentMcpSessions>();
        await using var app = builder.Build();
        app.Use(async (context, next) =>
        {
            context.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "operator")], "test-cookie"));
            await next();
        });
        app.MapGet("/csrf", (HttpContext context, IAntiforgery csrf) => csrf.GetAndStoreTokens(context).RequestToken!);
        app.MapAgentSessionBff();
        await app.StartAsync();
        try
        {
            var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
            using var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { BaseAddress = new Uri(address) };
            var csrf = await client.GetStringAsync("/csrf");
            using var request = new HttpRequestMessage(HttpMethod.Post, "/api/agent-workspace/studio/ai" + query)
            {
                Content = new StringContent("{}", Encoding.UTF8, "application/json")
            };
            if (csrfEnabled) request.Headers.Add("X-CSRF-Token", csrf);
            request.Headers.Add("X-Honua-Environment", environment);
            request.Headers.Add("X-Honua-Session-Partition", partition == "valid"
                ? AgentGatewayBinding.OperatorPartition("operator", "environment", new Uri("https://upstream.test/")) : partition);
            request.Headers.Add("X-Honua-View", view);
            using var response = await client.SendAsync(request);
            Assert.Equal(expected, (int)response.StatusCode);
            Assert.Equal(0, wire.RequestCount);
        }
        finally { await app.StopAsync(); }
    }

    [Fact]
    public async Task CapabilityMetadataCannotExceedBoundedResponse()
    {
        using var content = new StringContent(new string('x', 256 * 1024 + 1));
        await Assert.ThrowsAsync<InvalidDataException>(() => AgentSessionBff.ReadBoundedJsonAsync(content, CancellationToken.None));
    }

    private sealed class Operator : IConsoleOperatorContext
    {
        public string CurrentOperatorKey => "operator";
        public bool HasOperator => true;
        public string RequireOperatorKey() => CurrentOperatorKey;
    }
    private sealed class Bearer : IConsoleOperatorBearerProvider
    {
        public Task<ConsoleOperatorBearerResolution> ResolveAsync(ConsoleEnvironmentProfile profile, CancellationToken cancellationToken = default) =>
            Task.FromResult(new ConsoleOperatorBearerResolution { AccessToken = "human-bearer", HasInteractiveSession = true });
    }
    private sealed class Upstream(PausedEventStream stream) : HttpMessageHandler, IHttpClientFactory
    {
        public int RequestCount { get; private set; }
        public bool HumanMembershipChecked { get; private set; }
        public bool HumanCredentialUsed { get; private set; }
        public bool IndependentAgentCredentialUsed { get; private set; }
        public bool NegotiatedStreaming { get; private set; }
        public HttpClient CreateClient(string name) => new(this, disposeHandler: false);
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestCount++;
            if (request.RequestUri!.AbsolutePath.StartsWith("/api/v1/agent/workspaces/", StringComparison.Ordinal))
            {
                HumanMembershipChecked = request.Headers.Authorization?.Parameter == "human-bearer" && !request.Headers.Contains("X-API-Key");
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}") });
            }
            NegotiatedStreaming = request.Headers.Accept.Any(value => value.MediaType == "text/event-stream");
            HumanCredentialUsed = request.Headers.Authorization?.Parameter == "human-bearer" && !request.Headers.Contains("X-API-Key");
            IndependentAgentCredentialUsed = request.Headers.Authorization is null && request.Headers.TryGetValues("X-API-Key", out var keys) && keys.Single() == "scoped-agent-key";
            var content = new StreamContent(stream);
            content.Headers.ContentType = new MediaTypeHeaderValue("text/event-stream");
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content });
        }
    }
    private sealed class PausedEventStream : Stream
    {
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private bool _sent;
        public TaskCompletionSource Cancelled { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool Completed { get; private set; }
        public void Release() => _release.TrySetResult();
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (!_sent) { _sent = true; return Encoding.UTF8.GetBytes("data: first\n\n", buffer.Span); }
            try { await _release.Task.WaitAsync(cancellationToken); Completed = true; return 0; }
            catch (OperationCanceledException) { Cancelled.TrySetResult(); throw; }
        }
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
