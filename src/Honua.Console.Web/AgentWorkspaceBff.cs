using System.Net.Http.Headers;
using System.Text.RegularExpressions;
using Honua.Console.Shell.Services;
using Honua.Console.Web.Auth;
using Microsoft.AspNetCore.Antiforgery;

namespace Honua.Console.Web;

/// <summary>Same-origin, operator-only transport. Never forwards browser credentials or upstream cookies.</summary>
internal static partial class AgentWorkspaceBff
{
    public static void MapAgentWorkspaceBff(this WebApplication app)
    {
        app.MapGet("/api/agent-workspace/session", async (HttpContext context,
            IConsoleEnvironmentProfileStore profiles, IConsoleOperatorBearerProvider bearers,
            IAntiforgery antiforgery, IConfiguration configuration, ConsoleOperatorSessionBridge bridge, IConsoleOperatorContext operators, CancellationToken cancellationToken) =>
        {
            context.Response.Headers.CacheControl = "no-store";
            await bridge.SyncAsync(context.User, cancellationToken);
            var profile = await profiles.GetActiveProfileAsync(cancellationToken);
            if (profile is null)
            {
                return Results.Json(new { authenticated = false, message = "Select an environment and sign in to Honua." }, statusCode: 503);
            }
            var credential = await bearers.ResolveAsync(profile, cancellationToken);
            if (!credential.IsAvailable)
            {
                return Results.Json(new { authenticated = false, message = "Sign in to Honua again." }, statusCode: 401);
            }
            var tokens = antiforgery.GetAndStoreTokens(context);
            return Results.Json(new
            {
                authenticated = true,
                nativeAgentAvailable = !string.IsNullOrWhiteSpace(configuration["HONUA_AGENT_API_KEY"]),
                releaseId = configuration["Honua:Agent:ReleaseId"] ?? configuration["HONUA_AGENT_RELEASE_ID"],
                // A bounded browser transport lease; upstream credentials are checked on every request.
                expiresAt = credential.ExpiresAt ?? DateTimeOffset.UtcNow.AddMinutes(5),
                csrfToken = tokens.RequestToken,
                environmentId = profile.Id,
                environmentName = profile.DisplayName,
                tenantId = profile.TenantId,
                principalId = AgentGatewayBinding.OperatorPartition(operators.RequireOperatorKey(), profile.Id)
            });
        });

        app.MapPost("/api/agent-workspace", (HttpContext context,
            IConsoleEnvironmentProfileStore profiles, IConsoleOperatorBearerProvider bearers,
            IConsoleAccountSessionStore sessions, IHttpClientFactory clients, IAntiforgery antiforgery,
            CancellationToken token) => ForwardAsync(context, "", false, profiles, bearers, sessions, clients, antiforgery, token));
        app.MapMethods("/api/agent-workspace/{**path}", ["GET", "POST", "PUT", "PATCH"],
            (HttpContext context, string? path, IConsoleEnvironmentProfileStore profiles,
                IConsoleOperatorBearerProvider bearers, IConsoleAccountSessionStore sessions,
                IHttpClientFactory clients, IAntiforgery antiforgery, CancellationToken token) =>
                ForwardAsync(context, path, false, profiles, bearers, sessions, clients, antiforgery, token));
        app.MapMethods("/api/processes/{**path}", ["GET", "POST"],
            (HttpContext context, string? path, IConsoleEnvironmentProfileStore profiles,
                IConsoleOperatorBearerProvider bearers, IConsoleAccountSessionStore sessions,
                IHttpClientFactory clients, IAntiforgery antiforgery, CancellationToken token) =>
                ForwardAsync(context, path, false, profiles, bearers, sessions, clients, antiforgery, token, processes: true));
        app.MapMethods("/api/studio/{**path}", ["GET", "POST", "PUT", "PATCH", "DELETE"],
            (HttpContext context, string? path, IConsoleEnvironmentProfileStore profiles,
                IConsoleOperatorBearerProvider bearers, IConsoleAccountSessionStore sessions,
                IHttpClientFactory clients, IAntiforgery antiforgery, CancellationToken token) =>
                ForwardAsync(context, path, true, profiles, bearers, sessions, clients, antiforgery, token));
    }

    private static async Task<IResult> ForwardAsync(HttpContext context, string? path, bool studio,
        IConsoleEnvironmentProfileStore profiles, IConsoleOperatorBearerProvider bearers,
        IConsoleAccountSessionStore sessions, IHttpClientFactory clients, IAntiforgery antiforgery,
        CancellationToken cancellationToken, bool processes = false)
    {
        context.Response.Headers.CacheControl = "no-store";
        if (path is null || (path.Length == 0 ? !HttpMethods.IsPost(context.Request.Method) : !(processes ? ProcessPath() : studio ? StudioPath() : WorkspacePath()).IsMatch(path)))
        {
            return Results.NotFound();
        }
        if (!HttpMethods.IsGet(context.Request.Method))
        {
            try { await antiforgery.ValidateRequestAsync(context); }
            catch (AntiforgeryValidationException) { return Results.StatusCode(403); }
        }
        var profile = await profiles.GetActiveProfileAsync(cancellationToken);
        if (profile is null) { return Results.StatusCode(503); }
        // Every browser request is pinned to the environment visible at submission. A background tab
        // whose operator changed environments fails closed instead of borrowing the new target.
        if (context.Request.Headers["X-Honua-Environment"] != profile.Id)
        {
            return Results.Json(new { message = "Environment changed. Reconnect the workspace." }, statusCode: 409);
        }
        var credential = await bearers.ResolveAsync(profile, cancellationToken);
        var session = await sessions.GetSessionAsync(profile.Id, cancellationToken);
        if (!credential.IsAvailable || session?.ServerBaseUri != profile.ServerBaseUri)
        {
            return Results.StatusCode(401);
        }
        var isImportUpload = processes && path == "admin/import/upload";
        if (context.Request.ContentLength > (isImportUpload ? 20 * 1024 * 1024 : 256 * 1024)) { return Results.StatusCode(413); }
        var upstreamPath = processes ? $"api/v1/{path}" : studio ? (path == "health" ? "health" : $"api/{path}") : $"api/v1/agent/workspaces/{path}";
        if (studio)
        {
            string[] allowedQueryKeys = ["family", "workspaceId", "owner", "state", "q", "cursor", "limit"];
            var query = new List<string>();
            foreach (var item in context.Request.Query)
            {
                if (!allowedQueryKeys.Contains(item.Key, StringComparer.Ordinal) || item.Value.Count != 1 || item.Value.ToString().Length > 1024)
                {
                    return Results.BadRequest();
                }
                query.Add(Uri.EscapeDataString(item.Key) + "=" + Uri.EscapeDataString(item.Value.ToString()));
            }
            if (query.Count > 0) { upstreamPath += "?" + string.Join("&", query); }
        }
        if (!studio && !processes && HttpMethods.IsGet(context.Request.Method) && context.Request.Query.TryGetValue("viewId", out var viewId))
        {
            if (!System.Text.RegularExpressions.Regex.IsMatch(viewId.ToString(), "^[A-Za-z0-9_-]{1,128}$")) { return Results.BadRequest(); }
            upstreamPath += "?viewId=" + Uri.EscapeDataString(viewId.ToString());
        }
        using var request = new HttpRequestMessage(new HttpMethod(context.Request.Method),
            new Uri(new Uri(profile.ServerBaseUri.AbsoluteUri.TrimEnd('/') + "/"), upstreamPath));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", credential.AccessToken);
        // Do not relay arbitrary query strings, identity headers, cookies, API keys or redirect destinations.
        if (isImportUpload)
        {
            if (!context.Request.HasFormContentType) { return Results.StatusCode(415); }
            var form = await context.Request.ReadFormAsync(cancellationToken);
            if (form.Files.Count != 1 || form.Files[0].Name != "File" || form.Files[0].Length > 20 * 1024 * 1024
                || form.Keys.Any(key => key != "TableName") || !Regex.IsMatch(form["TableName"].ToString(), "^[A-Za-z_][A-Za-z0-9_]{0,62}$"))
            {
                return Results.BadRequest();
            }
            var multipart = new MultipartFormDataContent();
            var fileContent = new StreamContent(form.Files[0].OpenReadStream());
            fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
            multipart.Add(fileContent, "File", Path.GetFileName(form.Files[0].FileName));
            multipart.Add(new StringContent(form["TableName"].ToString()), "TableName");
            request.Content = multipart;
        }
        else if (!HttpMethods.IsGet(context.Request.Method))
        {
            using var buffer = new MemoryStream();
            var bytes = new byte[8192];
            int count;
            while ((count = await context.Request.Body.ReadAsync(bytes, cancellationToken)) != 0)
            {
                if (buffer.Length + count > 256 * 1024) { return Results.StatusCode(413); }
                buffer.Write(bytes, 0, count);
            }
            request.Content = new ByteArrayContent(buffer.ToArray());
            request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        }
        try
        {
            using var response = await clients.CreateClient("honua-agent-workspace").SendAsync(request, cancellationToken);
            if ((int)response.StatusCode is >= 300 and < 400) { return Results.StatusCode(502); }
            var body = await response.Content.ReadAsByteArrayAsync(cancellationToken);
            return Results.Content(System.Text.Encoding.UTF8.GetString(body), "application/json", statusCode: (int)response.StatusCode);
        }
        catch (HttpRequestException) { return Results.StatusCode(502); }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested) { return Results.StatusCode(504); }
    }

    [GeneratedRegex(@"\A(?:[A-Za-z0-9_-]{1,128})(?:/(?:context|conversation|turns|commands/[A-Za-z0-9_-]{1,128}|drafts(?:/[A-Za-z0-9_-]{1,128}(?:/submit)?)?))?\z", RegexOptions.CultureInvariant)]
    private static partial Regex WorkspacePath();

    [GeneratedRegex(@"\A(?:admin/import/upload|admin/import/jobs/[A-Za-z0-9_-]{1,128}|admin/connections/[A-Za-z0-9_-]{1,128}/layers/?|console/workflow-node-registry|console/workflow-packages(?:/[A-Za-z0-9_-]{1,256}/versions(?:/[1-9][0-9]{0,8}(?:/(?:validate|dry-run|publish))?)?)?|console/workflow-publications(?:/[A-Za-z0-9_-]{1,128}/runs)?|admin/operations/[A-Za-z0-9_-]{1,128}(?:/cancel)?)\z", RegexOptions.CultureInvariant)]
    private static partial Regex ProcessPath();

    [GeneratedRegex(@"\A(?:health|v1/studio/(?:catalog|packages|package-families|package-drafts|content-items)(?:/[A-Za-z0-9_-]{1,128}){0,4})\z", RegexOptions.CultureInvariant)]
    private static partial Regex StudioPath();
}
