using System.Security.Claims;
using Honua.Console.Web.Auth;
using Honua.Console.Shell.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using System.Text.Json;
using Honua.Console.Web;
using Microsoft.Extensions.Configuration;

namespace Honua.Console.IntegrationTests;

public sealed class AgentGatewayBindingTests
{
    [Fact]
    public void ExplicitInitializeDropsOnlyItsOwnNegotiatedSession()
    {
        var sessions = new AgentMcpSessions();
        var partition = ("operator", "profile", "workspace");
        var other = ("other", "profile", "workspace");
        sessions.Put(partition, "old-session"); sessions.Put(other, "other-session");
        Assert.Equal("old-session", sessions.ForRequest(partition, System.Text.Encoding.UTF8.GetBytes("{\"method\":\"tools/list\"}")));
        Assert.Null(sessions.ForRequest(partition, System.Text.Encoding.UTF8.GetBytes("{\"method\":\"initialize\"}")));
        Assert.Null(sessions.Get(partition)); Assert.Equal("other-session", sessions.Get(other));
        sessions.Put(partition, "new-session");
        Assert.Equal("new-session", sessions.ForRequest(partition, System.Text.Encoding.UTF8.GetBytes("{\"method\":\"tools/call\"}")));
    }

    [Fact]
    public void ProfileAuthorityAndPathAreOpaquePartitionBoundaries()
    {
        var first = new Uri("https://server.test/honua/");
        var partition = AgentGatewayBinding.OperatorPartition("operator", "profile", first);
        Assert.True(AgentSessionBff.MatchesSessionPartition(partition, "operator", "profile", first));
        Assert.False(AgentSessionBff.MatchesSessionPartition(partition, "operator", "profile", new Uri("https://server.test/other/")));
        Assert.False(AgentSessionBff.MatchesSessionPartition(partition, "operator", "profile", new Uri("https://different.test/honua/")));
        Assert.DoesNotContain("server.test", partition, StringComparison.Ordinal);
        Assert.Equal(partition, AgentGatewayBinding.OperatorPartition("operator", "profile", new Uri("https://server.test/honua")));
    }

    [Fact]
    public void OnlyAbsentConfigurationUsesDirectServerTransport()
    {
        Assert.Null(AgentGatewayBinding.Resolve(Configuration(), "agent-key"));
        Assert.Throws<InvalidOperationException>(() => AgentGatewayBinding.Resolve(Configuration("http://gateway/"), "agent-key"));
        Assert.Throws<InvalidOperationException>(() => AgentGatewayBinding.Resolve(Configuration(null, new string('k', 40)), "agent-key"));
    }

    [Theory]
    [InlineData("http://user:password@gateway/")]
    [InlineData("http://gateway/?target=other")]
    [InlineData("http://gateway/#other")]
    [InlineData("http://gateway/arbitrary-path")]
    [InlineData("file:///etc/passwd")]
    public void UnsafeGatewayBindingsFailClosed(string address) => Assert.Throws<InvalidOperationException>(() =>
        AgentGatewayBinding.Resolve(Configuration(address, new string('k', 40)), "agent-key"));

    [Fact]
    public void HostOperatorPartitionCannotCollideThroughDelimiterValues()
    {
        Assert.NotEqual(AgentGatewayBinding.OperatorPartition("a\nb", "c"), AgentGatewayBinding.OperatorPartition("a", "b\nc"));
        Assert.Equal(64, AgentGatewayBinding.OperatorPartition("operator", "profile").Length);
    }

    [Theory]
    [InlineData("target", "workspace", "turn", "honua.workspace.devops", true)]
    [InlineData("other", "workspace", "turn", "honua.workspace.devops", false)]
    [InlineData("target", "other", "turn", "honua.workspace.devops", false)]
    [InlineData("target", "workspace", "old-turn", "honua.workspace.devops", false)]
    [InlineData("target", "workspace", "turn", "invented.family", false)]
    public void CapabilityManifestRequiresExactFrozenBindingAndKnownFamily(string target, string workspace, string turn, string family, bool expected)
    {
        using var manifest = JsonDocument.Parse(JsonSerializer.Serialize(new { schemaVersion = "honua.agent-gateway.capabilities.v1", targetId = target, workspaceId = workspace, turnId = turn, toolFamilies = new[] { family } }));
        Assert.Equal(expected, AgentGatewayBinding.MatchesManifest(manifest.RootElement, "target", "workspace", "turn"));
    }

    [Fact]
    public void NativeBrowserSessionCannotBorrowAnotherOperatorOrProfile()
    {
        var partition = AgentGatewayBinding.OperatorPartition("original", "profile");
        Assert.True(AgentSessionBff.MatchesSessionPartition(partition, "original", "profile"));
        Assert.False(AgentSessionBff.MatchesSessionPartition(partition, "other", "profile"));
        Assert.False(AgentSessionBff.MatchesSessionPartition(partition, "original", "other-profile"));
        Assert.False(AgentSessionBff.MatchesSessionPartition(default, "original", "profile"));
        Assert.False(AgentSessionBff.MatchesSessionPartition(new Microsoft.Extensions.Primitives.StringValues([partition, partition]), "original", "profile"));
    }

    [Theory]
    [InlineData("same", true)]
    [InlineData("uri", false)]
    [InlineData("path", false)]
    [InlineData("profile", false)]
    [InlineData("principal", false)]
    [InlineData("missing", false)]
    [InlineData("duplicate", false)]
    [InlineData("anonymous", false)]
    public void HumanCookieTransportRequiresExactCapturedAuthority(string changed, bool accepted)
    {
        var context = new DefaultHttpContext();
        context.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "human")], "cookie"));
        var operators = new ConsoleOperatorContext(new HttpContextAccessor { HttpContext = context });
        using var services = new ServiceCollection().AddSingleton<IConsoleOperatorContext>(operators).BuildServiceProvider();
        context.RequestServices = services;
        var profile = new ConsoleEnvironmentProfile { Id = "profile", ServerBaseUri = new Uri("https://honua.test/base/") };
        var partition = AgentGatewayBinding.OperatorPartition(operators.RequireOperatorKey(), profile.Id, profile.ServerBaseUri);
        context.Request.Headers["X-Honua-Session-Partition"] = partition;
        if (changed == "uri") profile = new() { Id = "profile", ServerBaseUri = new Uri("https://other.test/base/") };
        if (changed == "path") profile = new() { Id = "profile", ServerBaseUri = new Uri("https://honua.test/other/") };
        if (changed == "profile") profile = new() { Id = "other", ServerBaseUri = profile.ServerBaseUri };
        if (changed == "principal") context.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "other")], "cookie"));
        if (changed == "anonymous") context.User = new ClaimsPrincipal(new ClaimsIdentity());
        if (changed == "missing") context.Request.Headers.Remove("X-Honua-Session-Partition");
        if (changed == "duplicate") context.Request.Headers["X-Honua-Session-Partition"] = new Microsoft.Extensions.Primitives.StringValues([partition, partition]);
        Assert.Equal(accepted, AgentGatewayBinding.MatchesBrowserPartition(context, profile));
    }

    private static IConfiguration Configuration(string? url = null, string? key = null) => new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?> { ["HONUA_AGENT_GATEWAY_URL"] = url, ["HONUA_AGENT_GATEWAY_KEY"] = key }).Build();
}
