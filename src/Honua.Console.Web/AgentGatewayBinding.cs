using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Honua.Console.Web;

/// <summary>Host-pinned transport to the same canonical gateway used by external clients.</summary>
internal sealed record AgentGatewayBinding(Uri BaseUri, string Key)
{
    internal static AgentGatewayBinding? Resolve(IConfiguration configuration, string agentKey)
    {
        var address = configuration["HONUA_AGENT_GATEWAY_URL"];
        var bridgeKey = configuration["HONUA_AGENT_GATEWAY_KEY"];
        if (string.IsNullOrWhiteSpace(address) && string.IsNullOrWhiteSpace(bridgeKey)) return null;
        if (!Uri.TryCreate(address, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https") ||
            uri.UserInfo.Length != 0 || uri.Query.Length != 0 || uri.Fragment.Length != 0 || uri.AbsolutePath != "/" ||
            string.IsNullOrWhiteSpace(bridgeKey) || Encoding.UTF8.GetByteCount(bridgeKey) is < 32 or > 512 || bridgeKey.Any(char.IsWhiteSpace) ||
            bridgeKey == agentKey || bridgeKey == configuration["HONUA_LOCAL_HUMAN_BRIDGE_KEY"])
            throw new InvalidOperationException("The native gateway binding is incomplete or invalid.");
        return new AgentGatewayBinding(uri, bridgeKey);
    }

    internal static string OperatorPartition(string operatorKey, string profileId, Uri? serverBase = null) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(operatorKey.Length + ":" + operatorKey + profileId.Length + ":" + profileId + ":" + (serverBase?.GetComponents(UriComponents.SchemeAndServer | UriComponents.Path, UriFormat.UriEscaped).TrimEnd('/') ?? ""))));

    internal static bool MatchesBrowserPartition(HttpContext context, Honua.Console.Shell.Models.ConsoleEnvironmentProfile profile)
    {
        var operators = context.RequestServices.GetRequiredService<Honua.Console.Web.Auth.IConsoleOperatorContext>();
        return operators.HasOperator && AgentSessionBff.MatchesSessionPartition(context.Request.Headers["X-Honua-Session-Partition"],
            operators.RequireOperatorKey(), profile.Id, profile.ServerBaseUri);
    }

    internal void Bind(HttpRequestMessage request, string operatorPartition)
    {
        request.Headers.Add("X-Honua-Gateway-Key", Key);
        request.Headers.Add("X-Honua-Operator-Partition", operatorPartition);
    }

    internal static bool MatchesManifest(JsonElement manifest, string targetId, string workspaceId, string turnId)
    {
        string[] fields = ["schemaVersion", "targetId", "workspaceId", "turnId", "toolFamilies"];
        if (manifest.ValueKind != JsonValueKind.Object || manifest.EnumerateObject().Count() != fields.Length ||
            manifest.EnumerateObject().Any(property => !fields.Contains(property.Name, StringComparer.Ordinal)) ||
            manifest.EnumerateObject().Select(property => property.Name).Distinct(StringComparer.Ordinal).Count() != fields.Length)
            return false;
        return Match("schemaVersion", "honua.agent-gateway.capabilities.v1") && Match("targetId", targetId) &&
            Match("workspaceId", workspaceId) && Match("turnId", turnId) &&
            manifest.GetProperty("toolFamilies") is { ValueKind: JsonValueKind.Array } families &&
            families.GetArrayLength() <= 1 && families.EnumerateArray().All(family =>
                family.ValueKind == JsonValueKind.String && family.GetString() == "honua.workspace.devops");

        bool Match(string property, string expected) => manifest.GetProperty(property) is { ValueKind: JsonValueKind.String } value && value.GetString() == expected;
    }

    internal async Task<string> MergeCapabilitiesAsync(HttpClient client, Uri serverBase, string serverCapabilities,
        string agentKey, string workspaceId, string turnId, string operatorPartition, CancellationToken token)
    {
        // This separately authorized agent projection is the comparison authority, never the human view.
        using var turnRequest = new HttpRequestMessage(HttpMethod.Get,
            new Uri(serverBase, $"api/v1/agent/workspaces/{workspaceId}/turns/{turnId}"));
        AddAgentBinding(turnRequest);
        using var turnResponse = await client.SendAsync(turnRequest, token);
        if (!turnResponse.IsSuccessStatusCode) throw new InvalidOperationException("The agent turn is unavailable.");
        using var turn = JsonDocument.Parse(await AgentSessionBff.ReadBoundedJsonAsync(turnResponse.Content, token));
        var targetId = turn.RootElement.GetProperty("context").GetProperty("targetId").GetString();
        if (string.IsNullOrWhiteSpace(targetId)) throw new InvalidOperationException("The agent target is unavailable.");
        using var capabilitiesRequest = new HttpRequestMessage(HttpMethod.Get, new Uri(BaseUri, "capabilities"));
        AddAgentBinding(capabilitiesRequest); Bind(capabilitiesRequest, operatorPartition);
        using var response = await client.SendAsync(capabilitiesRequest, token);
        if (!response.IsSuccessStatusCode) throw new InvalidOperationException("The native gateway is unavailable.");
        using var manifest = JsonDocument.Parse(await AgentSessionBff.ReadBoundedJsonAsync(response.Content, token));
        if (!MatchesManifest(manifest.RootElement, targetId, workspaceId, turnId))
            throw new InvalidOperationException("The native gateway capability binding changed.");
        var result = JsonNode.Parse(serverCapabilities) ?? throw new JsonException();
        var data = result["data"] ?? result;
        data["workspaceGateway"] = JsonNode.Parse(manifest.RootElement.GetRawText());
        return result.ToJsonString();

        void AddAgentBinding(HttpRequestMessage request)
        {
            request.Headers.Add("X-API-Key", agentKey);
            request.Headers.Add("X-Honua-Workspace", workspaceId);
            request.Headers.Add("X-Honua-Turn", turnId);
        }
    }
}
