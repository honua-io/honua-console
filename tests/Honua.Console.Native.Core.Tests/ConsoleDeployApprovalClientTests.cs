using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Honua.Console.Contracts;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Services;

namespace Honua.Console.Native.Core.Tests;

public sealed class ConsoleDeployApprovalClientTests
{
    [Fact]
    public async Task SubmitPrefersSignedInOperatorBearerOverConfiguredAdminKey()
    {
        var handler = new RecordingHandler(_ => JsonResponse(new DeployOperationResponse
        {
            OperationId = "deploy-1",
            Kind = "Deploy",
            Status = "Submitted"
        }));
        var profile = new ConsoleEnvironmentProfile
        {
            Id = "live",
            DisplayName = "Live Server Alpha",
            ServerBaseUri = new Uri("https://server.example"),
            Account = new ConsoleAccountBinding
            {
                AuthMode = ConsoleAccountAuthMode.AccountRbac,
                AccountId = "operator.live"
            }
        };
        var profiles = new InMemoryConsoleEnvironmentProfileStore([profile], activeProfileId: profile.Id);
        var sessions = new InMemoryConsoleAccountSessionStore();
        await sessions.SaveSessionAsync(new ConsoleAccountSession
        {
            ProfileId = profile.Id,
            AccessToken = "operator-alice-bearer"
        });
        var client = new HttpConsoleDeployApprovalClient(
            new HttpClient(handler),
            profiles,
            sessionStore: sessions,
            adminApiKey: "shared-admin-key");

        _ = await client.SubmitAsync("deploy-1", "Reviewed by Alice");

        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal(new AuthenticationHeaderValue("Bearer", "operator-alice-bearer"), request.Headers.Authorization);
        Assert.False(request.Headers.Contains("X-API-Key"));
    }

    private static HttpResponseMessage JsonResponse(DeployOperationResponse value)
    {
        var json = JsonSerializer.Serialize(value, MetadataReleaseJsonContext.Default.DeployOperationResponse);
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
    }

    private sealed class RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Requests.Add(CloneRequest(request));
            return Task.FromResult(responder(request));
        }

        private static HttpRequestMessage CloneRequest(HttpRequestMessage request)
        {
            var clone = new HttpRequestMessage(request.Method, request.RequestUri);
            foreach (var header in request.Headers)
            {
                clone.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }

            return clone;
        }
    }
}
