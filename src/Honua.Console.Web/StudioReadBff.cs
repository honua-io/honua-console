using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;
using Honua.Console.Shell.Services;
using Honua.Console.Web.Auth;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Http.Features;

namespace Honua.Console.Web;

/// <summary>Human-only catalog and feature reads; this transport is never supplied to the agent SDK.</summary>
internal static class StudioReadBff
{
    public static void MapStudioReadBff(this WebApplication app)
    {
        app.MapPost("/api/studio/catalog-mcp", async (HttpContext context, IAntiforgery antiforgery, CancellationToken token) =>
        {
            try { await antiforgery.ValidateRequestAsync(context); }
            catch (AntiforgeryValidationException) { return Results.StatusCode(403); }
            using var buffer = new MemoryStream();
            var bytes = new byte[4096];
            int count;
            while ((count = await context.Request.Body.ReadAsync(bytes, token)) != 0)
            {
                if (buffer.Length + count > 16 * 1024) { return Results.StatusCode(413); }
                buffer.Write(bytes, 0, count);
            }
            try
            {
                using var json = JsonDocument.Parse(buffer.ToArray());
                if (!IsCatalogRead(json.RootElement)) { return Results.StatusCode(403); }
            }
            catch (JsonException) { return Results.BadRequest(); }
            return await SendAsync(context, "api/mcp", new ByteArrayContent(buffer.ToArray()), token, catalog: true);
        });
        app.MapGet("/api/studio/data/ogc/features/collections/{collectionId}/items", async (HttpContext context, string collectionId, CancellationToken token) =>
        {
            if (!IsCanonicalReadPath(context.Features.Get<IHttpRequestFeature>()?.RawTarget)) return Results.BadRequest();
            var path = OgcReadPath(collectionId, context.Request.Query);
            return path is null ? Results.BadRequest() : await SendAsync(context, path, null, token);
        });
        app.MapGet("/api/studio/data/rest/services/{**path}", async (HttpContext context, string? path, CancellationToken token) =>
        {
            if (!IsCanonicalReadPath(context.Features.Get<IHttpRequestFeature>()?.RawTarget)) return Results.BadRequest();
            var upstream = FeatureReadPath(path, context.Request.Query);
            return upstream is null ? Results.BadRequest() : await SendAsync(context, upstream, null, token);
        });
    }

    internal static bool IsCanonicalReadPath(string? rawTarget)
    {
        var path = rawTarget?.Split('?', 2)[0];
        return path is not null && !Regex.IsMatch(path, @"%(?:2f|5c|2e|25)", RegexOptions.IgnoreCase)
            && !path.Contains('\\');
    }

    internal static string? FeatureReadPath(string? path, IQueryCollection queryValues)
    {
        if (path is null || !Regex.IsMatch(path, @"\A(?:[A-Za-z0-9_-][A-Za-z0-9_.-]*/)+FeatureServer/[0-9]{1,9}(?:/query)?\z")
            || path.Split('/').Any(part => part is "." or "..")) return null;
        var isQuery = path.EndsWith("/query", StringComparison.Ordinal);
        string[] allowed = isQuery ? ["f", "where", "outFields", "outSR", "returnGeometry", "resultOffset", "resultRecordCount", "geometry", "geometryType", "spatialRel", "inSR"] : ["f"];
        var query = new List<string>();
        foreach (var item in queryValues)
        {
            var value = item.Value.ToString();
            if (!allowed.Contains(item.Key, StringComparer.Ordinal) || item.Value.Count != 1 || value.Length > 8192) return null;
            if (item.Key == "resultRecordCount" && (!int.TryParse(value, out var limit) || limit is < 1 or > 2000)) return null;
            if (item.Key == "resultOffset" && (!int.TryParse(value, out var offset) || offset is < 0 or > 10_000_000)) return null;
            if (item.Key == "returnGeometry" && value is not ("true" or "false")) return null;
            if (item.Key == "f" && (!IsFeatureFormat(value) || !isQuery && value != "json")) return null;
            query.Add(Uri.EscapeDataString(item.Key) + "=" + Uri.EscapeDataString(value));
        }
        if (!queryValues.ContainsKey("f")) query.Add("f=json");
        if (isQuery && !queryValues.ContainsKey("resultRecordCount")) query.Add("resultRecordCount=1000");
        return $"rest/services/{path}?{string.Join("&", query)}";
    }

    internal static bool IsFeatureFormat(string value) => value is "json" or "geojson";

    internal static string? OgcReadPath(string collectionId, IQueryCollection query)
    {
        if (collectionId.Length is < 1 or > 256 || collectionId is "." or ".."
            || collectionId.Any(c => char.IsControl(c) || c is '/' or '\\' or '%')) { return null; }
        var limit = 500;
        foreach (var item in query)
        {
            if (item.Value.Count != 1) { return null; }
            if (item.Key == "f" && item.Value.ToString() == "json") { continue; }
            if (item.Key == "limit" && int.TryParse(item.Value, out limit) && limit is >= 1 and <= 2000) { continue; }
            return null;
        }
        return $"ogc/features/collections/{Uri.EscapeDataString(collectionId)}/items?f=json&limit={limit}";
    }

    internal static bool IsCatalogRead(JsonElement root)
    {
        if (HasDuplicateProperties(root)) { return false; }
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("method", out var method) || method.ValueKind != JsonValueKind.String) { return false; }
        if (method.GetString() is "initialize" or "notifications/initialized") { return true; }
        if (method.GetString() != "tools/call" || !root.TryGetProperty("params", out var parameters)
            || parameters.ValueKind != JsonValueKind.Object || !parameters.TryGetProperty("name", out var name)
            || name.ValueKind != JsonValueKind.String) { return false; }
        var tool = name.GetString();
        if (tool is not ("honua_list_layers" or "honua_get_style" or "honua_describe_layer")) { return false; }
        if (!parameters.TryGetProperty("arguments", out var args)) { return tool != "honua_describe_layer"; }
        if (args.ValueKind != JsonValueKind.Object) { return false; }
        foreach (var property in args.EnumerateObject())
        {
            if (tool == "honua_list_layers")
            {
                if (property.Name is not ("offset" or "limit") || property.Value.ValueKind != JsonValueKind.Number
                    || !property.Value.TryGetInt32(out var value) || value < 0
                    || (property.Name == "limit" ? value is < 1 or > 1000 : value > 1_000_000)) { return false; }
            }
            else if (tool == "honua_get_style")
            {
                if (property.Name == "styleId" && IsIdentifier(property.Value)) { continue; }
                if (property.Name == "includeStylesheet" && property.Value.ValueKind == JsonValueKind.True) { continue; }
                if (property.Name == "encoding" && property.Value.ValueKind == JsonValueKind.String
                    && property.Value.GetString() == "mapbox-style") { continue; }
                return false;
            }
            else
            {
                if (property.Name == "serviceId" && IsIdentifier(property.Value)) { continue; }
                if (property.Name == "layerId" && property.Value.ValueKind == JsonValueKind.Number
                    && property.Value.TryGetInt32(out var layerId) && layerId >= 0) { continue; }
                return false;
            }
        }
        return tool != "honua_describe_layer" || (args.TryGetProperty("serviceId", out _) && args.TryGetProperty("layerId", out _));
    }

    private static bool IsIdentifier(JsonElement value) => value.ValueKind == JsonValueKind.String
        && value.GetString() is { Length: > 0 and <= 256 } text && !text.Any(char.IsControl);

    private static bool HasDuplicateProperties(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in value.EnumerateObject())
            {
                if (!names.Add(property.Name) || HasDuplicateProperties(property.Value)) { return true; }
            }
        }
        else if (value.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in value.EnumerateArray())
            {
                if (HasDuplicateProperties(item)) { return true; }
            }
        }
        return false;
    }

    private static async Task<IResult> SendAsync(HttpContext context, string path, HttpContent? body,
        CancellationToken token, bool catalog = false)
    {
        context.Response.Headers.CacheControl = "no-store";
        var profiles = context.RequestServices.GetRequiredService<IConsoleEnvironmentProfileStore>();
        var sessions = context.RequestServices.GetRequiredService<IConsoleAccountSessionStore>();
        var bearers = context.RequestServices.GetRequiredService<IConsoleOperatorBearerProvider>();
        var profile = await profiles.GetActiveProfileAsync(token);
        if (profile is null) { return Results.StatusCode(503); }
        if (context.Request.Headers["X-Honua-Environment"] != profile.Id) { return Results.StatusCode(409); }
        if (!AgentGatewayBinding.MatchesBrowserPartition(context, profile)) return Results.StatusCode(409);
        var credential = await bearers.ResolveAsync(profile, token);
        var session = await sessions.GetSessionAsync(profile.Id, token);
        if (!credential.IsAvailable || session?.ServerBaseUri != profile.ServerBaseUri) { return Results.StatusCode(401); }
        using var request = new HttpRequestMessage(body is null ? HttpMethod.Get : HttpMethod.Post,
            new Uri(new Uri(profile.ServerBaseUri.AbsoluteUri.TrimEnd('/') + "/"), path))
        { Content = body };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", credential.AccessToken);
        if (body is not null) { body.Headers.ContentType = new("application/json"); }
        var mcpSessions = context.RequestServices.GetRequiredService<AgentMcpSessions>();
        var operators = context.RequestServices.GetRequiredService<IConsoleOperatorContext>();
        var partition = (AgentGatewayBinding.OperatorPartition(operators.RequireOperatorKey(), profile.Id, profile.ServerBaseUri), profile.Id, "catalog:");
        if (catalog && body is not null && mcpSessions.ForRequest(partition, await body.ReadAsByteArrayAsync(token)) is { } id) { request.Headers.Add("Mcp-Session-Id", id); }
        try
        {
            using var response = await context.RequestServices.GetRequiredService<IHttpClientFactory>()
                .CreateClient("honua-agent-workspace").SendAsync(request, token);
            if (catalog && response.Headers.TryGetValues("Mcp-Session-Id", out var ids)
                && ids.FirstOrDefault() is { Length: > 0 and <= 256 } negotiatedId)
            { mcpSessions.Put(partition, negotiatedId); }
            if ((int)response.StatusCode is >= 300 and < 400) { return Results.StatusCode(502); }
            return Results.Content(await response.Content.ReadAsStringAsync(token),
                "application/json", statusCode: (int)response.StatusCode);
        }
        catch (HttpRequestException) { return Results.StatusCode(502); }
        catch (TaskCanceledException) when (!token.IsCancellationRequested) { return Results.StatusCode(504); }
    }
}
