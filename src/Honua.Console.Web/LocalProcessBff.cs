using System.Net.Http.Headers;
using System.Text.RegularExpressions;
using Honua.Console.Shell.Services;
using Honua.Console.Web.Auth;
using Microsoft.AspNetCore.Antiforgery;

namespace Honua.Console.Web;

/// <summary>Human-only bounded transport to the canonical workspace process gateway.</summary>
internal static partial class LocalProcessBff
{
    private const long MaximumArtifactBytes = 128L * 1024 * 1024;

    public static void MapLocalProcessBff(this WebApplication app)
    {
        app.MapMethods("/api/processes/local/{**path}", ["GET", "PUT", "POST", "DELETE"], async (
            HttpContext context, string? path, IConsoleEnvironmentProfileStore profiles,
            IConsoleOperatorBearerProvider bearers, IConsoleAccountSessionStore sessions, IAntiforgery antiforgery,
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
            if (!AgentGatewayBinding.MatchesBrowserPartition(context, profile)) return Results.StatusCode(409);
            var credential = await bearers.ResolveAsync(profile, cancellationToken);
            if (!credential.IsAvailable) return Results.StatusCode(401);
            var isArtifact = HttpMethods.IsGet(context.Request.Method)
                && path.Contains("/artifacts/", StringComparison.Ordinal);
            var account = isArtifact ? await sessions.GetSessionAsync(profile.Id, cancellationToken) : null;
            if (isArtifact && (account is null || string.IsNullOrWhiteSpace(account.AccountId)
                || account.ServerBaseUri != profile.ServerBaseUri
                || !string.Equals(account.AccessToken, credential.AccessToken, StringComparison.Ordinal)))
                return Results.StatusCode(401);
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
            IResult BeforeStartFailure(int status)
            {
                context.Response.ContentLength = null;
                context.Response.Headers.Remove("Content-Disposition");
                context.Response.Headers.Remove("ETag");
                return Results.StatusCode(status);
            }

            try
            {
                // ResponseHeadersRead stops HttpClient.Timeout at the headers.
                // Keep the artifact deadline active throughout the response body.
                using var artifactDeadline = isArtifact
                    ? CancellationTokenSource.CreateLinkedTokenSource(cancellationToken) : null;
                artifactDeadline?.CancelAfter(TimeSpan.FromMinutes(6));
                var upstreamToken = artifactDeadline?.Token ?? cancellationToken;
                using var client = clients.CreateClient("honua-agent-workspace");
                if (isArtifact) client.Timeout = TimeSpan.FromMinutes(6);
                using var response = await client.SendAsync(request,
                    HttpCompletionOption.ResponseHeadersRead, upstreamToken);
                if ((int)response.StatusCode is >= 300 and < 400) return Results.StatusCode(502);
                var artifactLength = isArtifact && response.IsSuccessStatusCode
                    ? response.Content.Headers.ContentLength : null;
                if (isArtifact && response.IsSuccessStatusCode
                    && (artifactLength is null or < 0 or > MaximumArtifactBytes)) return Results.StatusCode(502);
                context.Response.StatusCode = (int)response.StatusCode;
                context.Response.ContentType = response.Content.Headers.ContentType?.ToString() ?? "application/json";
                if (artifactLength is not null)
                {
                    context.Response.Headers.ContentDisposition = "attachment";
                    context.Response.Headers.XContentTypeOptions = "nosniff";
                    context.Response.ContentLength = artifactLength;
                }
                if (response.Headers.ETag is { } responseEtag) context.Response.Headers.ETag = responseEtag.ToString();
                if (artifactLength is { } length)
                {
                    async Task<bool> CurrentReaderAsync(CancellationToken token)
                    {
                        var current = await profiles.GetActiveProfileAsync(token);
                        if (current?.Id != profile.Id || current.ServerBaseUri != profile.ServerBaseUri
                            || !AgentGatewayBinding.MatchesBrowserPartition(context, current)) return false;
                        var currentCredential = await bearers.ResolveAsync(current, token);
                        if (!currentCredential.IsAvailable) return false;
                        var currentAccount = await sessions.GetSessionAsync(current.Id, token);
                        if (currentAccount?.AccountId != account!.AccountId
                            || currentAccount.TenantId != account.TenantId
                            || currentAccount.ServerBaseUri != account.ServerBaseUri) return false;
                        return string.Equals(currentCredential.AccessToken, currentAccount.AccessToken,
                            StringComparison.Ordinal);
                    }

                    await using var upstream = await response.Content.ReadAsStreamAsync(upstreamToken);
                    await TransferArtifactAsync(upstream, context.Response.Body, length,
                        CurrentReaderAsync, upstreamToken);
                }
                else await response.Content.CopyToAsync(context.Response.Body, cancellationToken);
                return Results.Empty;
            }
            catch (Exception error) when (context.Response.HasStarted && error is
                HttpRequestException or OperationCanceledException or IOException or InvalidDataException or InvalidOperationException)
            { context.Abort(); return Results.Empty; }
            catch (InvalidDataException) { return BeforeStartFailure(502); }
            catch (InvalidOperationException) { return BeforeStartFailure(409); }
            catch (IOException) { return BeforeStartFailure(502); }
            catch (HttpRequestException) { return BeforeStartFailure(502); }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { return BeforeStartFailure(504); }
        });
    }

    internal static async Task TransferArtifactAsync(Stream upstream, Stream output, long expectedBytes,
        Func<CancellationToken, Task<bool>> currentReader, CancellationToken token)
    {
        if (expectedBytes is < 0 or > MaximumArtifactBytes) throw new InvalidDataException("Artifact size is unsupported.");
        var buffer = new byte[1024 * 1024];
        long copied = 0;
        while (true)
        {
            if (!await currentReader(token)) throw new InvalidOperationException("Artifact reader changed.");
            var count = await upstream.ReadAsync(buffer, token);
            if (count == 0) break;
            if (copied + count > expectedBytes) throw new InvalidDataException("Artifact exceeded its verified length.");
            if (!await currentReader(token)) throw new InvalidOperationException("Artifact reader changed.");
            await output.WriteAsync(buffer.AsMemory(0, count), token);
            copied += count;
        }
        if (copied != expectedBytes) throw new InvalidDataException("Artifact ended before its verified length.");
        if (!await currentReader(token)) throw new InvalidOperationException("Artifact reader changed.");
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
