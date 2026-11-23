using System.Net;
using System.Net.Http.Headers;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Services;
using Honua.Console.Web.Auth;

namespace Honua.Console.IntegrationTests;

public sealed class ConsoleManagerRoutingHandlerTests
{
    private static readonly Uri Serving = new("https://honua.test/");
    private static readonly Uri Manager = new("https://manager.test/");

    [Fact]
    public async Task SignedInManagementReadUsesTheManagementSessionWithoutTheServingBearer()
    {
        var serving = new Recorder();
        var session = new Recorder();
        using var client = Client(serving, (profileId, manager) =>
            profileId == "local" && manager == Manager ? new HttpClient(session) : null);

        using var response = await client.SendAsync(Get("api/v1/admin/deploy/preflight"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Null(serving.Request);
        Assert.Equal(new Uri(Manager, "api/v1/admin/deploy/preflight"), session.Request!.RequestUri);
        Assert.Null(session.Request.Headers.Authorization);
    }

    [Fact]
    public async Task ManagementReadWithoutAManagementSignInKeepsTheBoundClient()
    {
        var serving = new Recorder();
        using var client = Client(serving, (_, _) => null);

        using var response = await client.SendAsync(Get("api/v1/admin/proposals"));

        Assert.Equal(new Uri(Manager, "api/v1/admin/proposals"), serving.Request!.RequestUri);
        Assert.Equal("serving-bearer", serving.Request.Headers.Authorization!.Parameter);
    }

    [Fact]
    public async Task ManagementWriteNeverUsesTheReadSession()
    {
        var serving = new Recorder();
        var session = new Recorder();
        using var client = Client(serving, (_, _) => new HttpClient(session));
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(Serving, "api/v1/admin/proposals/p1/approve"));

        using var response = await client.SendAsync(request);

        Assert.Null(session.Request);
        Assert.Equal(new Uri(Manager, "api/v1/admin/proposals/p1/approve"), serving.Request!.RequestUri);
    }

    private static HttpRequestMessage Get(string path)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, new Uri(Serving, path));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "serving-bearer");
        return request;
    }

    private static HttpClient Client(Recorder serving, Func<string, Uri, HttpClient?> managementSession) =>
        new(new ConsoleManagerRoutingHandler(new ActiveProfile(), new FixedManager(), managementSession) { InnerHandler = serving });

    private sealed class Recorder : HttpMessageHandler
    {
        public HttpRequestMessage? Request { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Request = request;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { RequestMessage = request });
        }
    }

    private sealed class FixedManager : IConsoleManagerEndpoint
    {
        public Uri? Resolve(Uri servingBaseUri) => servingBaseUri == Serving ? Manager : null;
    }

    private sealed class ActiveProfile : IConsoleEnvironmentProfileStore
    {
        private static readonly ConsoleEnvironmentProfile Profile = new()
        {
            Id = "local",
            DisplayName = "Local",
            ServerBaseUri = Serving,
            Account = new ConsoleAccountBinding { AuthMode = ConsoleAccountAuthMode.AccountRbac, AccountId = "operator" },
        };

        public Task<ConsoleEnvironmentProfile?> GetActiveProfileAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<ConsoleEnvironmentProfile?>(Profile);
        public Task<IReadOnlyList<ConsoleEnvironmentProfile>> ListProfilesAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ConsoleEnvironmentProfile?> GetProfileAsync(string id, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task UpsertProfileAsync(ConsoleEnvironmentProfile profile, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task ActivateProfileAsync(string id, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ConsoleEnvironmentState?> GetStateAsync(string id, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task SaveStateAsync(ConsoleEnvironmentState state, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
