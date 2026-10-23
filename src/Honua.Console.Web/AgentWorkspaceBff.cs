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
                gaSpecWorkflow = configuration.GetValue<bool>("Honua:Agent:GaSpecWorkflowEnabled"),
                maxArtifactUploadBytes = ProcessArtifactFileMaximumBytes,
                importTransportResumable = false,
                // The scoped transport key remains installed for an external MCP agent even when
                // this Console must only save human turns and must not launch a second model.
                nativeAgentAvailable = bool.TryParse(configuration["HONUA_NATIVE_MODEL_ENABLED"], out var nativeModelEnabled)
                    && nativeModelEnabled && !string.IsNullOrWhiteSpace(configuration["HONUA_AGENT_API_KEY"]),
                releaseId = configuration["Honua:Agent:ReleaseId"] ?? configuration["HONUA_AGENT_RELEASE_ID"],
                // A bounded browser transport lease; upstream credentials are checked on every request.
                expiresAt = credential.ExpiresAt ?? DateTimeOffset.UtcNow.AddMinutes(5),
                csrfToken = tokens.RequestToken,
                environmentId = profile.Id,
                environmentName = profile.DisplayName,
                tenantId = profile.TenantId,
                sourcePathBase = profile.ServerBaseUri.AbsolutePath.TrimEnd('/'),
                principalId = AgentGatewayBinding.OperatorPartition(operators.RequireOperatorKey(), profile.Id, profile.ServerBaseUri)
            });
        });

        app.MapMethods("/api/agent-workspace/ga/{**path}", ["GET", "POST"],
            (HttpContext context, string? path, IConfiguration configuration,
                IConsoleEnvironmentProfileStore profiles, IConsoleOperatorBearerProvider bearers,
                IConsoleAccountSessionStore sessions, IHttpClientFactory clients, IAntiforgery antiforgery,
                CancellationToken token) => configuration.GetValue<bool>("Honua:Agent:GaSpecWorkflowEnabled")
                    ? ForwardAsync(context, path, false, profiles, bearers, sessions, clients, antiforgery, token, ga: true)
                    : Task.FromResult<IResult>(Results.NotFound()));
        app.MapPost("/api/agent-workspace", (HttpContext context,
            IConsoleEnvironmentProfileStore profiles, IConsoleOperatorBearerProvider bearers,
            IConsoleAccountSessionStore sessions, IHttpClientFactory clients, IAntiforgery antiforgery,
            CancellationToken token) => ForwardAsync(context, "", false, profiles, bearers, sessions, clients, antiforgery, token));
        app.MapMethods("/api/agent-workspace/{**path}", ["GET", "POST", "PUT", "PATCH", "DELETE"],
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
        CancellationToken cancellationToken, bool processes = false, bool ga = false)
    {
        context.Response.Headers.CacheControl = "no-store";
        var humanGaApply = ga && path?.EndsWith("/apply", StringComparison.Ordinal) == true;
        if (humanGaApply) context.Response.Headers["X-Honua-Dispatch-Status"] = "not-dispatched";
        if (ga)
        {
            var valid = path is not null && (HttpMethods.IsGet(context.Request.Method)
                ? Regex.IsMatch(path, @"\A(?:bundle|(?:plans|runs)/[A-Za-z0-9_-]{1,128})\z")
                : Regex.IsMatch(path, @"\A(?:plans|plans/[A-Za-z0-9_-]{1,128}/apply)\z"));
            if (!valid) return Results.NotFound();
            if (context.Request.Query.Any(item => item.Key != "revision" || item.Value.Count != 1
                || item.Value.ToString().Length > 256)) return Results.BadRequest();
        }
        string? processPath = null;
        var datasetRoute = !studio && !processes ? ReadDatasetImportRoute(path) : null;
        var isPublicationReceipt = studio && path is not null && StudioPublicationReceiptPath().IsMatch(path);
        string datasetQuery = "";
        string? datasetContentHash = null;
        if (datasetRoute is not null && !datasetRoute.Allows(context.Request.Method)) return Results.StatusCode(405);
        if (datasetRoute is null && !studio && !processes && HttpMethods.IsDelete(context.Request.Method)) return Results.StatusCode(405);
        if (datasetRoute is not null && !TryDatasetImportQuery(datasetRoute, context.Request.Method, context.Request.Query,
                out datasetQuery, out datasetContentHash)) return Results.BadRequest();
        if (!ga && (path is null || (path.Length == 0 ? !HttpMethods.IsPost(context.Request.Method) : !(processes ? TryBuildProcessPath(path, out processPath) : (studio ? StudioPath() : WorkspacePath()).IsMatch(path) || datasetRoute is not null || isPublicationReceipt))))
        {
            return Results.NotFound();
        }
        path ??= "";
        if (processes && !AllowsProcessMethod(path, context.Request.Method)) return Results.StatusCode(405);
        if (isPublicationReceipt && !HttpMethods.IsGet(context.Request.Method)) return Results.StatusCode(405);
        if (!HttpMethods.IsGet(context.Request.Method))
        {
            if (processes && path == "admin/import/upload" && !HasProcessImportAntiforgeryHeader(context)) return Results.StatusCode(403);
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
        if (!AgentGatewayBinding.MatchesBrowserPartition(context, profile))
            return Results.Json(new { message = "The editor connection changed. Reconnect before retrying." }, statusCode: 409);
        var credential = await bearers.ResolveAsync(profile, cancellationToken);
        var session = await sessions.GetSessionAsync(profile.Id, cancellationToken);
        if (!credential.IsAvailable || session?.ServerBaseUri != profile.ServerBaseUri)
        {
            return Results.StatusCode(401);
        }
        var isStyleWrite = studio && Regex.IsMatch(path, @"\Av1/studio/package-drafts/[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}/style(?:/initialize)?\z");
        if (studio && Regex.IsMatch(path, @"\Av1/studio/package-drafts/[^/]+/style(?:/|$)") && (!isStyleWrite || (path.EndsWith("/initialize", StringComparison.Ordinal) ? !HttpMethods.IsPost(context.Request.Method) : !HttpMethods.IsPut(context.Request.Method)))) { return Results.NotFound(); }
        var bodyLimit = datasetRoute is not null
            ? (datasetRoute.IsChunk ? DatasetImportChunkMaximumBytes : DatasetImportJsonMaximumBytes)
            : isStyleWrite ? 1024 * 1024 : 256 * 1024;
        var isImportUpload = processes && path == "admin/import/upload";
        if (context.Request.ContentLength > (isImportUpload ? ProcessImportBodyMaximumBytes : bodyLimit)) { return Results.StatusCode(413); }
        var upstreamPath = ga ? $"v1/spec/ga/{path}" : processes ? $"api/v1/{processPath}" : studio ? (path == "health" ? "health" : $"api/{path}") : $"api/v1/agent/workspaces/{path}";
        if (ga && context.Request.Query.TryGetValue("revision", out var gaRevision))
            upstreamPath += "?revision=" + Uri.EscapeDataString(gaRevision.ToString());
        if (datasetRoute is not null) upstreamPath += datasetQuery;
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
        if (!studio && !processes && HttpMethods.IsGet(context.Request.Method)
            && path.Contains("/commands/", StringComparison.Ordinal) && context.Request.Query.Count > 0)
        {
            var query = context.Request.Query;
            if (query.Count != 2 || !query.TryGetValue("draftId", out var draftId) || draftId.Count != 1
                || !Regex.IsMatch(draftId.ToString(), @"\A[A-Za-z0-9_-]{1,128}\z")
                || !query.TryGetValue("draftRevision", out var draftRevision) || draftRevision.Count != 1
                || !Regex.IsMatch(draftRevision.ToString(), @"\A[0-9]{1,19}\z")
                || !long.TryParse(draftRevision.ToString(), out var revision) || revision < 0)
                return Results.BadRequest();
            upstreamPath += "?draftId=" + Uri.EscapeDataString(draftId.ToString())
                + "&draftRevision=" + Uri.EscapeDataString(draftRevision.ToString());
        }
        else if (!studio && !processes && HttpMethods.IsGet(context.Request.Method) && context.Request.Query.TryGetValue("viewId", out var viewId))
        {
            if (!System.Text.RegularExpressions.Regex.IsMatch(viewId.ToString(), @"\A[A-Za-z0-9_-]{1,128}\z")) { return Results.BadRequest(); }
            upstreamPath += "?viewId=" + Uri.EscapeDataString(viewId.ToString());
        }
        using var request = new HttpRequestMessage(new HttpMethod(context.Request.Method),
            new Uri(new Uri(profile.ServerBaseUri.AbsoluteUri.TrimEnd('/') + "/"), upstreamPath));
        // Publication/rollback retries must retain the caller's original operation identity.
        // Older clients may omit it; never invent a new identity in the proxy.
        var isStudioPointerWrite = studio && HttpMethods.IsPost(context.Request.Method) && StudioPointerWritePath().IsMatch(path);
        if (isStyleWrite || isStudioPointerWrite && context.Request.Headers.ContainsKey("Idempotency-Key"))
        {
            var key = context.Request.Headers["Idempotency-Key"].ToString();
            if (!Regex.IsMatch(key, @"\A[A-Za-z0-9_-]{1,128}\z")) { return Results.BadRequest(); }
            request.Headers.TryAddWithoutValidation("Idempotency-Key", key);
        }
        if (!humanGaApply)
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", credential.AccessToken);
        // Do not relay arbitrary query strings, identity headers, cookies, API keys or redirect destinations.
        byte[]? datasetRequestBody = null;
        if (datasetRoute?.IsChunk == true)
        {
            if (!string.Equals(context.Request.ContentType?.Split(';', 2)[0].Trim(), "application/octet-stream", StringComparison.OrdinalIgnoreCase))
                return Results.StatusCode(415);
            request.Content = new StreamContent(new DatasetImportBoundedStream(context.Request.Body, DatasetImportChunkMaximumBytes));
            request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
            if (context.Request.ContentLength is { } length) request.Content.Headers.ContentLength = length;
        }
        else if (datasetRoute is not null && HttpMethods.IsDelete(context.Request.Method))
        {
            var probe = new byte[1];
            if (await context.Request.Body.ReadAsync(probe, cancellationToken) != 0) return Results.BadRequest();
        }
        else if (isImportUpload)
        {
            if (!context.Request.HasFormContentType) { return Results.StatusCode(415); }
            IFormCollection form;
            var originalBody = context.Request.Body;
            try
            {
                context.Request.Body = new DatasetImportBoundedStream(originalBody, ProcessImportBodyMaximumBytes);
                form = await context.Request.ReadFormAsync(cancellationToken);
            }
            catch (Exception exception) when (IsDatasetBodyTooLarge(exception)) { return Results.StatusCode(413); }
            catch (InvalidDataException) { return Results.BadRequest(); }
            finally { context.Request.Body = originalBody; }
            if (form.Files.Count == 1 && form.Files[0].Length > ProcessArtifactFileMaximumBytes) return Results.StatusCode(413);
            if (form.Files.Count != 1 || form.Files[0].Name != "File"
                || form.Keys.Any(key => key is not ("TableName" or "UploadId")) || form["TableName"].Count != 1
                || !Regex.IsMatch(form["TableName"].ToString(), @"\A[A-Za-z_][A-Za-z0-9_]{0,62}\z")
                || form.ContainsKey("UploadId") && (form["UploadId"].Count != 1 || !Regex.IsMatch(form["UploadId"].ToString(), @"\A[A-Za-z0-9_-]{1,128}\z")))
            {
                return Results.BadRequest();
            }
            var multipart = new MultipartFormDataContent();
            var fileContent = new StreamContent(form.Files[0].OpenReadStream());
            fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
            multipart.Add(fileContent, "File", Path.GetFileName(form.Files[0].FileName));
            multipart.Add(new StringContent(form["TableName"].ToString(), System.Text.Encoding.UTF8, "text/plain"), "TableName");
            // Correlation only: this value does not make upload replay safe or imply job acceptance.
            if (form.ContainsKey("UploadId")) multipart.Add(new StringContent(form["UploadId"].ToString(), System.Text.Encoding.UTF8, "text/plain"), "UploadId");
            request.Content = multipart;
        }
        else if (!HttpMethods.IsGet(context.Request.Method))
        {
            if (datasetRoute is not null && !context.Request.HasJsonContentType()) return Results.StatusCode(415);
            using var buffer = new MemoryStream();
            var bytes = new byte[8192];
            int count;
            while ((count = await context.Request.Body.ReadAsync(bytes, cancellationToken)) != 0)
            {
                if (buffer.Length + count > bodyLimit) { return Results.StatusCode(413); }
                buffer.Write(bytes, 0, count);
            }
            var payload = buffer.ToArray();
            if (datasetRoute is not null) datasetRequestBody = payload;
            request.Content = new ByteArrayContent(payload);
            request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        }
        try
        {
            ConsoleServerSessionPartition? cookiePartition = null;
            if (humanGaApply)
            {
                var operatorKey = context.RequestServices.GetRequiredService<IConsoleOperatorContext>().RequireOperatorKey();
                if (!context.RequestServices.GetRequiredService<ConsoleServerSessionClientStore>()
                    .TryGet(operatorKey, profile, out cookiePartition) || cookiePartition is null)
                    return Results.StatusCode(401);
            }
            using var authentication = cookiePartition is null ? null
                : await cookiePartition.EnterAuthenticationAsync(cancellationToken).ConfigureAwait(false);
            if (cookiePartition is not null
                && !await context.RequestServices.GetRequiredService<ConsoleServerSessionIdentityBinding>().EnsureAsync(
                    cookiePartition, profile, cookiePartition.Key.OperatorKey, cookiePartition.AuthenticatedProviderKey,
                    cancellationToken).ConfigureAwait(false))
                return Results.StatusCode(403);
            var transport = cookiePartition?.Client ?? clients.CreateClient("honua-agent-workspace");
            // Once SendAsync starts, even a failed/lost response can have a durable outcome.
            if (humanGaApply) context.Response.Headers.Remove("X-Honua-Dispatch-Status");
            using var response = await transport.SendAsync(request,
                datasetRoute is not null || ga ? HttpCompletionOption.ResponseHeadersRead : HttpCompletionOption.ResponseContentRead, cancellationToken);
            if ((int)response.StatusCode is >= 300 and < 400) { return Results.StatusCode(502); }
            if (datasetRoute is not null)
            {
                if (!response.IsSuccessStatusCode)
                    return Results.Json(new { message = "The import request was refused or its outcome is unknown. Read the original task and upload receipt before retrying." }, statusCode: (int)response.StatusCode);
                await using var upstreamBody = await response.Content.ReadAsStreamAsync(cancellationToken);
                using var limited = new DatasetImportBoundedStream(upstreamBody, DatasetImportJsonMaximumBytes);
                using var buffer = new MemoryStream();
                try { await limited.CopyToAsync(buffer, cancellationToken); }
                catch (DatasetImportBodyTooLargeException) { return Results.StatusCode(502); }
                if (!TryProjectDatasetResponse(datasetRoute, datasetContentHash, datasetRequestBody ?? [], buffer.ToArray(), out var projected))
                    return Results.StatusCode(502);
                return Results.Content(projected, "application/json", statusCode: (int)response.StatusCode);
            }
            if (ga)
            {
                await using var gaBody = await response.Content.ReadAsStreamAsync(cancellationToken);
                using var limited = new DatasetImportBoundedStream(gaBody, 128 * 1024);
                using var buffer = new MemoryStream();
                try { await limited.CopyToAsync(buffer, cancellationToken); }
                catch (DatasetImportBodyTooLargeException) { return Results.StatusCode(502); }
                return Results.Content(System.Text.Encoding.UTF8.GetString(buffer.ToArray()), "application/json", statusCode: (int)response.StatusCode);
            }
            var body = await response.Content.ReadAsByteArrayAsync(cancellationToken);
            return Results.Content(System.Text.Encoding.UTF8.GetString(body), "application/json", statusCode: (int)response.StatusCode);
        }
        catch (Exception exception) when (datasetRoute is not null && IsDatasetBodyTooLarge(exception)) { return Results.StatusCode(413); }
        catch (HttpRequestException) { return Results.StatusCode(502); }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested) { return Results.StatusCode(504); }
    }

    [GeneratedRegex(@"\A(?:[A-Za-z0-9_-]{1,128})(?:/(?:context|conversation|turns|deploy-targets|commands/[A-Za-z0-9_-]{1,128}|drafts(?:/[A-Za-z0-9_-]{1,128}(?:/submit)?)?))?\z", RegexOptions.CultureInvariant)]
    private static partial Regex WorkspacePath();

    internal static bool TryBuildProcessPath(string path, out string? upstream)
    {
        upstream = null;
        var package = WorkflowPackagePath().Match(path);
        if (package.Success)
        {
            var id = package.Groups["packageId"].Value;
            if (id != id.Trim() || id.Any(char.IsControl) || id is "." or ".." || id.Contains('\\')) return false;
            var version = package.Groups["version"].Value;
            if (version.Length > 0 && (!int.TryParse(version, out var number) || number < 1)) return false;
            upstream = "console/workflow-packages/" + Uri.EscapeDataString(id) + "/versions";
            if (version.Length > 0) upstream += "/" + version;
            if (package.Groups["action"].Success) upstream += "/" + package.Groups["action"].Value;
            return true;
        }
        if (!ProcessPath().IsMatch(path)) return false;
        upstream = path;
        return true;
    }

    internal static bool AllowsProcessMethod(string path, string method)
    {
        if (path == "admin/import/upload" || path.EndsWith("/cancel", StringComparison.Ordinal) || path.EndsWith("/runs", StringComparison.Ordinal))
            return HttpMethods.IsPost(method);
        var package = WorkflowPackagePath().Match(path);
        if (package.Success)
            return package.Groups["action"].Success ? HttpMethods.IsPost(method)
                : package.Groups["version"].Success ? HttpMethods.IsGet(method)
                : HttpMethods.IsGet(method) || HttpMethods.IsPost(method);
        if (path == "console/workflow-packages") return HttpMethods.IsGet(method) || HttpMethods.IsPost(method);
        return HttpMethods.IsGet(method);
    }

    [GeneratedRegex(@"\Aconsole/workflow-packages/(?<packageId>[^/]{1,256})/versions(?:/(?<version>[1-9][0-9]{0,9})(?:/(?<action>validate|dry-run|publish))?)?\z", RegexOptions.CultureInvariant)]
    private static partial Regex WorkflowPackagePath();

    [GeneratedRegex(@"\A(?:admin/import/upload|admin/import/jobs/[A-Za-z0-9_-]{1,128}(?:/cancel)?|admin/connections/[A-Za-z0-9_-]{1,128}/layers/?|console/workflow-node-registry|console/workflow-packages|console/workflow-publications(?:/[A-Za-z0-9_-]{1,128}/runs)?|admin/operations/[A-Za-z0-9_-]{1,128}(?:/cancel)?|admin/proposals/[A-Za-z0-9_-]{1,128})\z", RegexOptions.CultureInvariant)]
    private static partial Regex ProcessPath();

    [GeneratedRegex(@"\A(?:health|v1/studio/(?:catalog|packages|package-families|package-drafts|content-items)(?:/[A-Za-z0-9_-]{1,128}){0,4})\z", RegexOptions.CultureInvariant)]
    private static partial Regex StudioPath();

    [GeneratedRegex(@"\Av1/studio/content-items/[A-Za-z0-9_-]{1,128}/versions/[A-Za-z0-9_-]{1,128}/publish-requests/[A-Za-z0-9_-]{1,128}\z", RegexOptions.CultureInvariant)]
    private static partial Regex StudioPublicationReceiptPath();

    [GeneratedRegex(@"\Av1/studio/content-items/[A-Za-z0-9_-]{1,128}/(?:versions/[A-Za-z0-9_-]{1,128}/publish-requests|rollback-requests)\z", RegexOptions.CultureInvariant)]
    private static partial Regex StudioPointerWritePath();
}
