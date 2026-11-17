using System.Net;
using System.Text;
using Honua.Console.Contracts;
using Honua.Console.Shell.Services;

namespace Honua.Console.Shell.Tests;

public sealed class RbacAccessEnvironmentTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ReadsAcceptCanonicalEnvironmentIdentity(bool members)
    {
        const string payload = """{"success":true,"data":{"environmentId":"env-west","environmentName":"Public Works","roles":[],"members":[]}}""";
        using var http = new HttpClient(new ResponseHandler(payload));
        using var client = CreateClient(http);
        var source = new HonuaServerRbacAccessDataSource(client);

        if (members)
        {
            var result = await source.LoadMembershipAsync("env-west");
            Assert.NotNull(result.Membership);
            Assert.Equal("env-west", result.Membership.EnvironmentId);
            Assert.Equal("Public Works", result.Membership.DisplayEnvironment);
            Assert.Empty(result.CapabilityStates);
        }
        else
        {
            var result = await source.LoadOverviewAsync("env-west");
            Assert.NotNull(result.Overview);
            Assert.Equal("env-west", result.Overview.EnvironmentId);
            Assert.Equal("Public Works", result.Overview.DisplayEnvironment);
            Assert.Empty(result.CapabilityStates);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RetiredWorkspaceIdentityDoesNotSatisfyEnvironmentContract(bool members)
    {
        const string payload = """{"success":true,"data":{"workspaceId":"env-west","workspaceName":"Public Works","roles":[],"members":[]}}""";
        using var http = new HttpClient(new ResponseHandler(payload));
        using var client = CreateClient(http);
        var source = new HonuaServerRbacAccessDataSource(client);
        if (members)
        {
            var result = await source.LoadMembershipAsync("env-west");
            Assert.Null(result.Membership);
            Assert.NotEmpty(result.CapabilityStates);
        }
        else
        {
            var result = await source.LoadOverviewAsync("env-west");
            Assert.Null(result.Overview);
            Assert.NotEmpty(result.CapabilityStates);
        }
    }

    [Theory]
    [InlineData("overview", "/api/v1/console/access/env%20west/roles")]
    [InlineData("members", "/api/v1/console/access/env%20west/members")]
    [InlineData("audit", "/api/v1/console/access/env%20west/roles/audit")]
    [InlineData("create", "/api/v1/console/access/env%20west/roles")]
    [InlineData("update", "/api/v1/console/access/env%20west/roles/role%20id")]
    [InlineData("delete", "/api/v1/console/access/env%20west/roles/role%20id")]
    public async Task OperationsPreserveIdentifiersAndReportCanonicalRoute(string operation, string path)
    {
        using var http = new HttpClient(new ResponseHandler("", request =>
            Assert.Equal(path, request.RequestUri!.AbsolutePath), HttpStatusCode.ServiceUnavailable));
        using var client = CreateClient(http);
        var role = new HonuaConsoleRoleWriteRequest { Name = "Existing role" };

        var issue = operation switch
        {
            "overview" => (await client.GetOverviewAsync("env west")).Issue,
            "members" => (await client.GetMembershipAsync("env west")).Issue,
            "audit" => (await client.GetRoleAuditAsync("env west")).Issue,
            "create" => (await client.CreateRoleAsync("env west", role)).Issue,
            "update" => (await client.UpdateRoleAsync("env west", "role id", role)).Issue,
            "delete" => (await client.DeleteRoleAsync("env west", "role id")).Issue,
            _ => throw new ArgumentOutOfRangeException(nameof(operation))
        };

        Assert.NotNull(issue);
        Assert.Contains("{environmentId}", issue.Contract, StringComparison.Ordinal);
        Assert.DoesNotContain("workspace", issue.Contract, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ScopeLevelsMatchCanonicalProducer()
    {
        var levels = typeof(HonuaConsoleAccessScopeLevels).GetFields()
            .Select(field => field.GetRawConstantValue()).Cast<string>().Order().ToArray();
        Assert.Equal(new[] { "content", "dataset-field", "environment", "layer" }, levels);
    }

    private static HonuaConsoleRbacHttpClient CreateClient(HttpClient http) =>
        new(http, new HonuaConsoleRbacClientOptions(new Uri("https://server.example")));

    private sealed class ResponseHandler(
        string payload,
        Action<HttpRequestMessage>? inspect = null,
        HttpStatusCode status = HttpStatusCode.OK) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            inspect?.Invoke(request);
            return Task.FromResult(new HttpResponseMessage(status)
            {
                Content = new StringContent(payload, Encoding.UTF8, "application/json")
            });
        }
    }
}
