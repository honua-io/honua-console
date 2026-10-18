using System.Net.Http.Headers;
using System.Text.RegularExpressions;
using Honua.Console.Shell.Services;
using Honua.Console.Web.Auth;
using Microsoft.AspNetCore.Antiforgery;

namespace Honua.Console.Web;

/// <summary>Human-only bounded transport to the canonical workspace process gateway.</summary>
internal static partial class LocalProcessBff
{
    public static void MapLocalProcessBff(this WebApplication app)
    {
        app.MapMethods("/api/processes/local/{**path}", ["GET", "PUT", "POST", "DELETE"], async (
            HttpContext context, string? path, IConsoleEnvironmentProfileStore profiles,
            IConsoleOperatorBearerProvider bearers, IAntiforgery antiforgery,
            IHttpClientFactory clients, CancellationToken cancellationToken) =>
        {
            context.Response.Headers.CacheControl = "no-store";
            if (path is null || !AllowedPath().IsMatch(path) || path.Split('/').Any(part => part is "." or ".."))
                return Results.NotFound();
            var workspaceId = context.Request.Headers["X-Honua-Workspace"].ToString();
            var environmentId = context.Request.Headers["X-Honua-Environment"].ToString();
            if (!Identity().IsMatch(workspaceId) || string.IsNullOrWhiteSpace(environmentId)) return Results.BadRequest();
            if (path.StartsWith("project/bindings/", StringComparison.Ordinal) && path != $"project/bindings/{workspaceId}")
                return Results.BadRequest();
            if (!AllowsMethod(path, context.Request.Method)) return Results.StatusCode(405);
            if (context.Request.Query.Count != 0) return Results.BadRequest();
            if (!HttpMethods.IsGet(context.Request.Method))
            {
                try { await antiforgery.ValidateRequestAsync(context); }
                catch (AntiforgeryValidationException) { return Results.StatusCode(403); }
            }
            var profile = await profiles.GetProfileAsync(environmentId, cancellationToken);
            if (profile is null) return Results.StatusCode(403);
            var credential = await bearers.ResolveAsync(profile, cancellationToken);
            if (!credential.IsAvailable) return Results.StatusCode(401);
            using var request = new HttpRequestMessage(new HttpMethod(context.Request.Method),
                new Uri(new Uri(profile.ServerBaseUri.AbsoluteUri.TrimEnd('/') + "/"),
                    $"api/v1/agent/workspaces/{workspaceId}/processes/local/{path}"));
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", credential.AccessToken);
            request.Headers.Add("X-Honua-Workspace", workspaceId);
            if (context.Request.Headers.IfMatch is { Count: > 0 } etag)
            {
                if (etag.Count != 1 || !Revision().IsMatch(etag.ToString())) return Results.BadRequest();
                request.Headers.TryAddWithoutValidation("If-Match", etag.ToString());
            }
            if (HttpMethods.IsPost(context.Request.Method) || HttpMethods.IsPut(context.Request.Method))
            {
                using var buffer = new MemoryStream();
                var bytes = new byte[8192];
                int count;
                while ((count = await context.Request.Body.ReadAsync(bytes, cancellationToken)) != 0)
                {
                    if (buffer.Length + count > 2 * 1024 * 1024) return Results.StatusCode(413);
                    buffer.Write(bytes, 0, count);
                }
                request.Content = new ByteArrayContent(buffer.ToArray());
                request.Content.Headers.ContentType = new("application/json");
            }
            try
            {
                using var response = await clients.CreateClient("honua-agent-workspace").SendAsync(request,
                    HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                if ((int)response.StatusCode is >= 300 and < 400) return Results.StatusCode(502);
                context.Response.StatusCode = (int)response.StatusCode;
                context.Response.ContentType = response.Content.Headers.ContentType?.ToString() ?? "application/json";
                if (response.IsSuccessStatusCode && path.Contains("/artifacts/", StringComparison.Ordinal))
                    context.Response.Headers.ContentDisposition = "attachment";
                if (response.Headers.ETag is { } responseEtag) context.Response.Headers.ETag = responseEtag.ToString();
                await response.Content.CopyToAsync(context.Response.Body, cancellationToken);
                return Results.Empty;
            }
            catch (HttpRequestException) { return Results.StatusCode(502); }
            catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested) { return Results.StatusCode(504); }
        });
    }

    internal static bool AllowsMethod(string path, string method) =>
        path.StartsWith("sources/", StringComparison.Ordinal) ? method is "GET" or "PUT" :
        path.StartsWith("project/bindings/", StringComparison.Ordinal) ? method == "DELETE" :
        path == "runs" ? method is "GET" or "POST" :
        path is "snapshots" or "project/bind" || path.EndsWith("/cancel", StringComparison.Ordinal) ? method == "POST" : method == "GET";

    [GeneratedRegex(@"\A(?:project(?:/identity|/bind|/bindings/[A-Za-z0-9_-]{1,128})?|sources/(?:[A-Za-z0-9_-][A-Za-z0-9_.-]*/)*[A-Za-z0-9_-][A-Za-z0-9_.-]*\.(?:py|cs|csproj|json|yaml|lock)|snapshots(?:/[A-Za-z0-9_-]{1,128}(?:/sources/(?:[A-Za-z0-9_-][A-Za-z0-9_.-]*/)*[A-Za-z0-9_-][A-Za-z0-9_.-]*\.(?:py|cs|csproj|json|yaml|lock))?)?|commands/[A-Za-z0-9_-]{1,128}|runs|runs/[a-f0-9]{32}(?:/cancel|/artifacts/(?:[A-Za-z0-9_-][A-Za-z0-9_.-]*/)*[A-Za-z0-9_-][A-Za-z0-9_.-]*)?)\z", RegexOptions.CultureInvariant)]
    internal static partial Regex AllowedPath();

    [GeneratedRegex("\\A[A-Za-z0-9_-]{1,128}\\z", RegexOptions.CultureInvariant)]
    private static partial Regex Identity();

    [GeneratedRegex("\\A[\\\"A-Za-z0-9:_-]{1,128}\\z", RegexOptions.CultureInvariant)]
    private static partial Regex Revision();
}
