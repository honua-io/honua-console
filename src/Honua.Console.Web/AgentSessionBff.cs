using System.Net.Http.Headers;
using System.Collections.Concurrent;
using Honua.Console.Web.Auth;
using System.Text.RegularExpressions;
using Honua.Console.Shell.Services;
using Microsoft.AspNetCore.Antiforgery;

namespace Honua.Console.Web;

internal sealed class AgentMcpSessions
{
    private readonly ConcurrentDictionary<(string Operator, string Profile, string Workspace), (string Id, DateTimeOffset Expires)> _sessions = new();
    public string? Get((string Operator, string Profile, string Workspace) partition) => _sessions.TryGetValue(partition, out var value)
        && value.Expires > DateTimeOffset.UtcNow ? value.Id : null;
    public string? ForRequest((string Operator, string Profile, string Workspace) partition, byte[] body)
    {
        try
        {
            using var document = System.Text.Json.JsonDocument.Parse(body);
            if (document.RootElement.ValueKind == System.Text.Json.JsonValueKind.Object
                && document.RootElement.TryGetProperty("method", out var method)
                && method.ValueKind == System.Text.Json.JsonValueKind.String && method.GetString() == "initialize")
            {
                _sessions.TryRemove(partition, out _);
                return null;
            }
        }
        catch (System.Text.Json.JsonException) { /* The canonical upstream validates malformed requests. */ }
        return Get(partition);
    }
    public void Put((string Operator, string Profile, string Workspace) partition, string id)
    {
        foreach (var item in _sessions.Where(item => item.Value.Expires <= DateTimeOffset.UtcNow))
        {
            _sessions.TryRemove(item.Key, out _);
        }
        if (_sessions.Count < 2048 || _sessions.ContainsKey(partition))
        {
            _sessions[partition] = (id, DateTimeOffset.UtcNow.AddMinutes(30));
        }
    }
}

internal static class AgentSessionBff
{
    internal static bool MatchesSessionPartition(Microsoft.Extensions.Primitives.StringValues partition, string operatorKey, string profileId, Uri? serverBase = null)
    {
        if (partition.Count != 1) return false;
        return string.Equals(partition[0], AgentGatewayBinding.OperatorPartition(operatorKey, profileId, serverBase), StringComparison.Ordinal);
    }

    internal static async Task<string> ReadBoundedJsonAsync(HttpContent content, CancellationToken cancellationToken)
    {
        await using var input = await content.ReadAsStreamAsync(cancellationToken);
        using var output = new MemoryStream();
        var bytes = new byte[8192];
        int count;
        while ((count = await input.ReadAsync(bytes, cancellationToken)) != 0)
        {
            if (output.Length + count > 256 * 1024) throw new InvalidDataException("Native capability response too large.");
            output.Write(bytes, 0, count);
        }
        return System.Text.Encoding.UTF8.GetString(output.ToArray());
    }

    public static void MapAgentSessionBff(this WebApplication app)
    {
        MapStudioConversation(app);
        app.MapMethods("/api/agent-workspace/agent/{endpoint}", ["GET", "POST"], async (string endpoint, HttpContext context,
            IConsoleEnvironmentProfileStore profiles, IConsoleOperatorBearerProvider bearers,
            IAntiforgery antiforgery, IHttpClientFactory clients, IConfiguration configuration,
            IConsoleOperatorContext operators, AgentMcpSessions mcpSessions,
            CancellationToken cancellationToken) =>
        {
            context.Response.Headers.CacheControl = "no-store";
            if (endpoint is not ("mcp" or "ai" or "capabilities" or "turn" or "conversation")) { return Results.NotFound(); }
            if (endpoint != "conversation" && HttpMethods.IsGet(context.Request.Method) != (endpoint is "capabilities" or "turn"))
            {
                return Results.StatusCode(405);
            }
            try { await antiforgery.ValidateRequestAsync(context); }
            catch (AntiforgeryValidationException) { return Results.StatusCode(403); }
            var key = configuration["HONUA_AGENT_API_KEY"];
            if (string.IsNullOrWhiteSpace(key))
            {
                return Results.Json(new { message = "The native agent session is not configured. External agents remain supported." }, statusCode: 503);
            }
            AgentGatewayBinding? gateway;
            try { gateway = AgentGatewayBinding.Resolve(configuration, key); }
            catch (InvalidOperationException) { return Results.Json(new { message = "The native gateway is not configured correctly." }, statusCode: 503); }
            var environmentId = context.Request.Headers["X-Honua-Environment"].ToString();
            if (string.IsNullOrWhiteSpace(environmentId)) { return Results.BadRequest(); }
            // Resolve the original operator-owned profile, even if another view activated a new
            // environment while this captured turn was running. Never borrow the new profile.
            var profile = await profiles.GetProfileAsync(environmentId, cancellationToken);
            if (profile is null) { return Results.StatusCode(403); }
            if (!MatchesSessionPartition(context.Request.Headers["X-Honua-Session-Partition"], operators.RequireOperatorKey(), profile.Id, profile.ServerBaseUri))
            { return Results.StatusCode(403); }
            var configuredServer = configuration["HONUA_AGENT_SERVER_BASE_URL"]
                ?? configuration["Honua:Server:BaseUrl"] ?? configuration["HONUA_SERVER_BASE_URL"];
            if (!Uri.TryCreate(configuredServer, UriKind.Absolute, out var agentServer)
                || !string.Equals(agentServer.AbsoluteUri.TrimEnd('/'), profile.ServerBaseUri.AbsoluteUri.TrimEnd('/'), StringComparison.Ordinal))
            {
                // A user-created profile must never redirect the host's agent credential to another server.
                return Results.Json(new { message = "The native agent is not bound to this environment." }, statusCode: 403);
            }
            var workspaceId = context.Request.Headers["X-Honua-Workspace"].ToString();
            var turnId = context.Request.Headers["X-Honua-Turn"].ToString();
            var viewId = context.Request.Headers["X-Honua-View"].ToString();
            if (!Regex.IsMatch(viewId, "^[A-Za-z0-9_-]{1,128}$") || !Regex.IsMatch(workspaceId, "^[A-Za-z0-9_-]{1,128}$") || !Regex.IsMatch(turnId, "^[A-Za-z0-9_-]{1,128}$"))
            {
                return Results.BadRequest();
            }
            var credential = await bearers.ResolveAsync(profile, cancellationToken);
            if (!credential.IsAvailable) { return Results.StatusCode(401); }
            var serverBase = new Uri(profile.ServerBaseUri.AbsoluteUri.TrimEnd('/') + "/");
            var client = clients.CreateClient("honua-agent-workspace");
            // Membership is checked with the human's identity; execution uses only the independent agent key.
            using var membershipRequest = new HttpRequestMessage(HttpMethod.Get,
                new Uri(serverBase, $"api/v1/agent/workspaces/{workspaceId}?viewId={Uri.EscapeDataString(viewId)}"));
            membershipRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", credential.AccessToken);
            try
            {
                using var membership = await client.SendAsync(membershipRequest, cancellationToken);
                if (!membership.IsSuccessStatusCode) { return Results.StatusCode((int)membership.StatusCode); }
                using var buffer = new MemoryStream();
                var bytes = new byte[8192];
                int count;
                while ((count = await context.Request.Body.ReadAsync(bytes, cancellationToken)) != 0)
                {
                    if (buffer.Length + count > 256 * 1024) { return Results.StatusCode(413); }
                    buffer.Write(bytes, 0, count);
                }
                using var request = new HttpRequestMessage((endpoint is "capabilities" or "turn" || (endpoint == "conversation" && HttpMethods.IsGet(context.Request.Method))) ? HttpMethod.Get : HttpMethod.Post,
                    new Uri(serverBase, endpoint switch
                    {
                        "mcp" => "mcp",
                        "capabilities" => "api/v1/studio/ai/capabilities",
                        "turn" => $"api/v1/agent/workspaces/{workspaceId}/turns/{turnId}",
                        "conversation" => $"api/v1/agent/workspaces/{workspaceId}/conversation",
                        _ => "api/v1/studio/ai/chat"
                    }));
                var operatorKey = operators.RequireOperatorKey();
                var operatorPartition = AgentGatewayBinding.OperatorPartition(operatorKey, profile.Id, profile.ServerBaseUri);
                if (endpoint == "mcp" && gateway is not null)
                {
                    request.RequestUri = new Uri(gateway.BaseUri, "mcp");
                    gateway.Bind(request, operatorPartition);
                }
                var partition = (operatorPartition, profile.Id, workspaceId);
                if (endpoint == "mcp" && mcpSessions.ForRequest(partition, buffer.ToArray()) is { } mcpSession)
                {
                    request.Headers.Add("Mcp-Session-Id", mcpSession);
                }
                if (endpoint is "ai" or "mcp") request.Headers.Accept.ParseAdd("application/json, text/event-stream");
                request.Headers.Add("X-API-Key", key);
                request.Headers.Add("X-Honua-Workspace", workspaceId);
                request.Headers.Add("X-Honua-Turn", turnId);
                request.Content = new ByteArrayContent(buffer.ToArray());
                request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
                using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                if (endpoint == "mcp" && response.Headers.TryGetValues("Mcp-Session-Id", out var mcpIds)
                    && mcpIds.FirstOrDefault() is { Length: > 0 and <= 256 } negotiatedId)
                {
                    mcpSessions.Put(partition, negotiatedId);
                }
                if ((int)response.StatusCode is >= 300 and < 400) { return Results.StatusCode(502); }
                if (response.IsSuccessStatusCode && (endpoint is "ai" or "mcp")
                    && string.Equals(response.Content.Headers.ContentType?.MediaType, "text/event-stream", StringComparison.OrdinalIgnoreCase))
                {
                    context.Response.StatusCode = (int)response.StatusCode;
                    context.Response.ContentType = response.Content.Headers.ContentType!.ToString();
                    context.Response.Headers.CacheControl = "no-store, no-transform";
                    context.Response.Headers["X-Accel-Buffering"] = "no";
                    await context.Response.StartAsync(cancellationToken);
                    await response.Content.CopyToAsync(context.Response.Body, cancellationToken);
                    await context.Response.Body.FlushAsync(cancellationToken);
                    return Results.Empty;
                }
                var body = endpoint == "capabilities"
                    ? await ReadBoundedJsonAsync(response.Content, cancellationToken)
                    : await response.Content.ReadAsStringAsync(cancellationToken);
                if (endpoint == "capabilities" && gateway is not null && response.IsSuccessStatusCode)
                {
                    try { body = await gateway.MergeCapabilitiesAsync(client, serverBase, body, key, workspaceId, turnId, operatorPartition, cancellationToken); }
                    catch (Exception error) when (error is InvalidOperationException or System.Text.Json.JsonException or KeyNotFoundException)
                    { return Results.Json(new { message = "The native gateway capabilities could not be verified." }, statusCode: 502); }
                }
                return Results.Content(body,
                    (response.Content.Headers.ContentType?.MediaType == "text/event-stream" ? "text/event-stream" : "application/json"), statusCode: (int)response.StatusCode);
            }
            catch (InvalidDataException) { return Results.Json(new { message = "Native capabilities exceeded the bounded response contract." }, statusCode: 502); }
            catch (HttpRequestException) when (context.Response.HasStarted) { context.Abort(); return Results.Empty; }
            catch (HttpRequestException) { return Results.StatusCode(502); }
            catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested && context.Response.HasStarted) { context.Abort(); return Results.Empty; }
            catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested) { return Results.StatusCode(504); }
        });
    }
    private static void MapStudioConversation(WebApplication app)
    {
        app.MapMethods("/api/agent-workspace/studio/{endpoint}", ["GET", "POST"], async (string endpoint, HttpContext context,
            IConsoleEnvironmentProfileStore profiles, IConsoleOperatorBearerProvider bearers,
            IAntiforgery antiforgery, IHttpClientFactory clients, IConsoleOperatorContext operators,
            AgentMcpSessions sessions, IConsoleAccountSessionStore accountSessions, CancellationToken cancellationToken) =>
        {
            context.Response.Headers.CacheControl = "no-store";
            if (endpoint is not ("capabilities" or "ai" or "mcp")) return Results.NotFound();
            if (HttpMethods.IsGet(context.Request.Method) != (endpoint == "capabilities")) return Results.StatusCode(405);
            if (context.Request.Query.Count > 0) return Results.BadRequest();
            try { await antiforgery.ValidateRequestAsync(context); }
            catch (AntiforgeryValidationException) { return Results.StatusCode(403); }
            var profile = await profiles.GetActiveProfileAsync(cancellationToken);
            if (profile is null) return Results.StatusCode(503);
            if (context.Request.Headers["X-Honua-Environment"] != profile.Id
                || !MatchesSessionPartition(context.Request.Headers["X-Honua-Session-Partition"], operators.RequireOperatorKey(), profile.Id, profile.ServerBaseUri))
                return Results.StatusCode(403);
            var credential = await bearers.ResolveAsync(profile, cancellationToken);
            var accountSession = await accountSessions.GetSessionAsync(profile.Id, cancellationToken);
            if (!credential.IsAvailable || accountSession?.ServerBaseUri != profile.ServerBaseUri) return Results.StatusCode(401);
            var conversation = context.Request.Headers["X-Honua-View"].ToString();
            if (!Regex.IsMatch(conversation, "^[A-Za-z0-9_-]{1,128}$")) return Results.BadRequest();
            using var body = new MemoryStream();
            var bytes = new byte[8192];
            int count;
            while ((count = await context.Request.Body.ReadAsync(bytes, cancellationToken)) != 0)
            {
                if (body.Length + count > 1024 * 1024) return Results.StatusCode(413);
                body.Write(bytes, 0, count);
            }
            var partition = (AgentGatewayBinding.OperatorPartition(operators.RequireOperatorKey(), profile.Id, profile.ServerBaseUri), profile.Id, "studio-" + conversation);
            using var request = new HttpRequestMessage(endpoint == "capabilities" ? HttpMethod.Get : HttpMethod.Post,
                new Uri(new Uri(profile.ServerBaseUri.AbsoluteUri.TrimEnd('/') + "/"), endpoint switch
                {
                    "capabilities" => "api/v1/studio/ai/capabilities",
                    "ai" => "api/v1/studio/ai/chat",
                    _ => "mcp"
                }));
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", credential.AccessToken);
            request.Headers.Accept.ParseAdd("application/json, text/event-stream");
            if (endpoint == "mcp" && sessions.ForRequest(partition, body.ToArray()) is { } id)
                request.Headers.Add("Mcp-Session-Id", id);
            if (endpoint != "capabilities")
            {
                request.Content = new ByteArrayContent(body.ToArray());
                request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
            }
            try
            {
                using var client = clients.CreateClient("honua-agent-workspace");
                using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                if ((int)response.StatusCode is >= 300 and < 400) return Results.StatusCode(502);
                if (endpoint == "mcp" && response.IsSuccessStatusCode && response.Headers.TryGetValues("Mcp-Session-Id", out var ids)
                    && ids.SingleOrDefault() is { Length: > 0 and <= 256 } sessionId)
                {
                    sessions.Put(partition, sessionId);
                    context.Response.Headers["Mcp-Session-Id"] = sessionId;
                }
                if (response.Headers.RetryAfter is { } retryAfter) context.Response.Headers.RetryAfter = retryAfter.ToString();
                if (response.IsSuccessStatusCode && response.Content.Headers.ContentType?.MediaType == "text/event-stream")
                {
                    context.Response.StatusCode = (int)response.StatusCode;
                    context.Response.ContentType = "text/event-stream";
                    context.Response.Headers["X-Accel-Buffering"] = "no";
                    await context.Response.StartAsync(cancellationToken);
                    await response.Content.CopyToAsync(context.Response.Body, cancellationToken);
                    await context.Response.Body.FlushAsync(cancellationToken);
                    return Results.Empty;
                }
                var content = await ReadBoundedJsonAsync(response.Content, cancellationToken);
                return Results.Content(content, "application/json", statusCode: (int)response.StatusCode);
            }
            catch (InvalidDataException) { return Results.StatusCode(502); }
            catch (HttpRequestException) when (context.Response.HasStarted) { context.Abort(); return Results.Empty; }
            catch (HttpRequestException) { return Results.StatusCode(502); }
            catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested && context.Response.HasStarted) { context.Abort(); return Results.Empty; }
            catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested) { return Results.StatusCode(504); }
        });
    }

}
