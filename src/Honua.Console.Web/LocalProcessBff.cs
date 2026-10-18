using System.Text.RegularExpressions;
using Honua.Console.Shell.Services;
using Microsoft.AspNetCore.Antiforgery;

namespace Honua.Console.Web;

/// <summary>Bounded browser transport to the configured loopback source/build workspace daemon.</summary>
internal static partial class LocalProcessBff
{
    public static void MapLocalProcessBff(this WebApplication app)
    {
        app.MapMethods("/api/processes/local/{**path}", ["GET", "PUT", "POST"], async (
            HttpContext context, string? path, IConfiguration configuration,
            IConsoleEnvironmentProfileStore profiles, IAntiforgery antiforgery,
            IHttpClientFactory clients, CancellationToken cancellationToken) =>
        {
            context.Response.Headers.CacheControl = "no-store";
            if (path is null || !AllowedPath().IsMatch(path) || path.Split('/').Any(part => part is "." or ".."))
            {
                return Results.NotFound();
            }
            var method = path == "project" || path.Contains("/artifacts/", StringComparison.Ordinal)
                || (path.StartsWith("runs/", StringComparison.Ordinal) && !path.EndsWith("/cancel", StringComparison.Ordinal))
                ? "GET" : path.StartsWith("sources/", StringComparison.Ordinal) ? "PUT" : "POST";
            if (context.Request.Method != method) { return Results.StatusCode(405); }
            if (context.Request.Query.Count != 0) { return Results.BadRequest(); }
            if (method != "GET")
            {
                try { await antiforgery.ValidateRequestAsync(context); }
                catch (AntiforgeryValidationException) { return Results.StatusCode(403); }
            }
            var profile = await profiles.GetActiveProfileAsync(cancellationToken);
            if (profile is null) { return Results.StatusCode(503); }
            if (context.Request.Headers["X-Honua-Environment"] != profile.Id) { return Results.StatusCode(409); }
            if (!Uri.TryCreate(configuration["HONUA_GP_WORKSPACE_URL"], UriKind.Absolute, out var daemon)
                || !daemon.IsLoopback || daemon.Scheme is not ("http" or "https")
                || daemon.UserInfo.Length != 0 || daemon.Query.Length != 0 || daemon.Fragment.Length != 0
                || string.IsNullOrWhiteSpace(configuration["HONUA_GP_WORKSPACE_KEY"]))
            {
                return Results.Json(new { message = "The local process workspace is not configured." }, statusCode: 503);
            }
            using var request = new HttpRequestMessage(new HttpMethod(method),
                new Uri(new Uri(daemon.AbsoluteUri.TrimEnd('/') + "/"), path));
            request.Headers.Add("X-Honua-Workspace-Key", configuration["HONUA_GP_WORKSPACE_KEY"]);
            if (context.Request.Headers.IfMatch is { Count: > 0 } etag)
            {
                if (etag.Count != 1 || !Revision().IsMatch(etag.ToString())) { return Results.BadRequest(); }
                request.Headers.TryAddWithoutValidation("If-Match", etag.ToString());
            }
            if (method != "GET")
            {
                using var buffer = new MemoryStream();
                var bytes = new byte[8192];
                int count;
                while ((count = await context.Request.Body.ReadAsync(bytes, cancellationToken)) != 0)
                {
                    if (buffer.Length + count > 2 * 1024 * 1024) { return Results.StatusCode(413); }
                    buffer.Write(bytes, 0, count);
                }
                request.Content = new ByteArrayContent(buffer.ToArray());
                request.Content.Headers.ContentType = new("application/json");
            }
            try
            {
                using var response = await clients.CreateClient("honua-agent-workspace").SendAsync(request,
                    HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                if ((int)response.StatusCode is >= 300 and < 400) { return Results.StatusCode(502); }
                context.Response.StatusCode = (int)response.StatusCode;
                var artifact = path.Contains("/artifacts/", StringComparison.Ordinal);
                context.Response.ContentType = artifact ? "application/octet-stream" : "application/json";
                if (artifact) { context.Response.Headers.ContentDisposition = "attachment"; }
                if (response.Headers.ETag is { } responseEtag) { context.Response.Headers.ETag = responseEtag.ToString(); }
                await response.Content.CopyToAsync(context.Response.Body, cancellationToken);
                return Results.Empty;
            }
            catch (HttpRequestException) { return Results.StatusCode(502); }
            catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested) { return Results.StatusCode(504); }
        });
    }

    [GeneratedRegex(@"\A(?:project|sources/(?:[A-Za-z0-9_-][A-Za-z0-9_.-]*/)*[A-Za-z0-9_-][A-Za-z0-9_.-]*\.(?:py|cs|csproj|json|yaml|lock)|runs|runs/[a-f0-9]{32}(?:/cancel|/artifacts/(?:[A-Za-z0-9_-][A-Za-z0-9_.-]*/)*[A-Za-z0-9_-][A-Za-z0-9_.-]*)?)\z", RegexOptions.CultureInvariant)]
    private static partial Regex AllowedPath();

    [GeneratedRegex("\\A[\\\"A-Za-z0-9:_-]{1,128}\\z", RegexOptions.CultureInvariant)]
    private static partial Regex Revision();
}
