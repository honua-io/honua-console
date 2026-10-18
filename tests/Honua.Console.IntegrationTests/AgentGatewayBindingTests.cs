using System.Text.Json;
using Honua.Console.Web;
using Microsoft.Extensions.Configuration;

namespace Honua.Console.IntegrationTests;

public sealed class AgentGatewayBindingTests
{
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
        Assert.True(AgentSessionBff.MatchesSessionPartition(partition,"original","profile"));
        Assert.False(AgentSessionBff.MatchesSessionPartition(partition,"other","profile"));
        Assert.False(AgentSessionBff.MatchesSessionPartition(partition,"original","other-profile"));
        Assert.False(AgentSessionBff.MatchesSessionPartition(default,"original","profile"));
        Assert.False(AgentSessionBff.MatchesSessionPartition(new Microsoft.Extensions.Primitives.StringValues([partition,partition]),"original","profile"));
    }

    private static IConfiguration Configuration(string? url = null, string? key = null) => new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?> { ["HONUA_AGENT_GATEWAY_URL"] = url, ["HONUA_AGENT_GATEWAY_KEY"] = key }).Build();
}
