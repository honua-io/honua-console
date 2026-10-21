using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Honua.Console.Shell.Services;
using Honua.Console.Web.Auth;
using Honua.Console.Web.Components;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Honua.Console.Web;

/// <summary>Published definitions and bounded data reads under the current viewer's authority.</summary>
internal static class StudioAudienceBff
{
    internal const string PartitionHeader = "X-Honua-Viewer-Partition";
    private const int MaximumResponseBytes = 16 * 1024 * 1024;
    private const int MaximumStyleResponseBytes = 256 * 1024;
    private const string MapboxStyleContentType = "application/vnd.mapbox.style+json";

    public static void MapStudioAudienceBff(this WebApplication app)
    {
        app.MapGet("/maps/published/{**route}", (HttpContext context, string? route) =>
        {
            context.Response.Headers.CacheControl = "no-store";
            return IsRoute(route)
                ? (IResult)new RazorComponentResult<PublishedMapDocument>(new { PublishedRoute = route })
                : Results.BadRequest();
        }).AllowAnonymous();
        app.MapGet("/api/studio/viewer/published/{**route}", ReadPublishedAsync).AllowAnonymous();
        app.MapGet("/api/studio/viewer/data/rest/services/{**path}", ReadFeaturesAsync).AllowAnonymous();
        app.MapGet("/api/studio/viewer/styles/{styleId}", ReadStyleAsync).AllowAnonymous();
    }

    internal static Task<IResult> ReadPublishedAsync(HttpContext context, string? route, CancellationToken token)
    {
        if (!IsRoute(route) || !CanonicalPath(context) || context.Request.Query.Count != 0)
        {
            return Task.FromResult<IResult>(Results.BadRequest());
        }

        return SendAsync(context, "api/v1/studio/published/" + string.Join('/', route!.Split('/').Select(Uri.EscapeDataString))
            + "?includeSources=true", false, token);
    }

    internal static Task<IResult> ReadFeaturesAsync(HttpContext context, string? path, CancellationToken token)
    {
        if (!CanonicalPath(context)) return Task.FromResult<IResult>(Results.BadRequest());
        var upstream = StudioReadBff.FeatureReadPath(path, context.Request.Query);
        return upstream is null
            ? Task.FromResult<IResult>(Results.BadRequest())
            : SendAsync(context, upstream, true, token);
    }

    internal static Task<IResult> ReadStyleAsync(HttpContext context, string? styleId, CancellationToken token)
    {
        if (styleId is not { Length: > 0 and <= 256 } || styleId is "." or ".."
            || styleId.Any(character => char.IsControl(character) || character is '/' or '\\' or '%')
            || context.Request.Query.Count != 0 || !CanonicalPath(context)
            || context.Features.Get<IHttpRequestFeature>()?.RawTarget !=
                "/api/studio/viewer/styles/" + Uri.EscapeDataString(styleId))
        {
            return Task.FromResult<IResult>(Results.BadRequest());
        }

        return SendAsync(context, "ogc/styles/" + Uri.EscapeDataString(styleId), true, token, style: true);
    }

    internal static bool IsRoute(string? route) => route is { Length: > 0 and <= 1024 }
        && route.Split('/').All(segment => segment.Length > 0 && segment is not ("." or "..")
            && !segment.Any(character => char.IsControl(character) || character is '\\' or '%'));

    private static bool CanonicalPath(HttpContext context)
    {
        var raw = context.Features.Get<IHttpRequestFeature>()?.RawTarget;
        return raw is { Length: > 0 and <= 32 * 1024 } && context.Request.Path.Value?.Length <= 4096
            && context.Request.Query.Count <= 16 && StudioReadBff.IsCanonicalReadPath(raw);
    }

    private sealed record ViewerBinding(Uri Target, string Partition, string? Bearer);

    private static async Task<(ViewerBinding? Binding, int Failure)> ResolveAsync(HttpContext context, CancellationToken token)
    {
        var configuration = context.RequestServices.GetRequiredService<IConfiguration>();
        var configured = configuration["Honua:Server:BaseUrl"] ?? configuration["HONUA_SERVER_BASE_URL"];
        if (!Uri.TryCreate(configured, UriKind.Absolute, out var target)
            || target.Scheme is not ("https" or "http") || target.UserInfo.Length != 0
            || target.Query.Length != 0 || target.Fragment.Length != 0)
        {
            return (null, 503);
        }

        target = new Uri(target.AbsoluteUri.TrimEnd('/') + "/");
        // Use the verified HTTP principal only. A public read never inherits a circuit's ambient identity.
        var actor = ConsoleOperatorContext.ResolveKey(context.User);
        if (actor is null)
        {
            if (context.User.Identity?.IsAuthenticated == true) return (null, 401);
            return (new(target, Partition(target, "anonymous"), null), 0);
        }

        var profiles = context.RequestServices.GetRequiredService<IConsoleEnvironmentProfileStore>();
        var profile = await profiles.GetActiveProfileAsync(token);
        if (profile is null) return (null, 401);
        if (new Uri(profile.ServerBaseUri.AbsoluteUri.TrimEnd('/') + "/") != target) return (null, 409);
        var credentials = await context.RequestServices.GetRequiredService<IConsoleOperatorBearerProvider>()
            .ResolveAsync(profile, token);
        var session = await context.RequestServices.GetRequiredService<IConsoleAccountSessionStore>()
            .GetSessionAsync(profile.Id, token);
        if (!credentials.IsAvailable || session?.ServerBaseUri != profile.ServerBaseUri
            || string.IsNullOrWhiteSpace(session.AccountId)
            || !string.Equals(credentials.AccessToken, session.AccessToken, StringComparison.Ordinal)) return (null, 401);

        var fingerprint = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(credentials.AccessToken!)));
        return (new(target, Partition(target, actor, profile.Id, session.AccountId, session.TenantId, fingerprint), credentials.AccessToken), 0);
    }

    // This value isolates a view; it is never an authorization credential. The server authorizes every GET.
    private static string Partition(Uri target, params string[] values) => Convert.ToHexStringLower(
        SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new[] { target.AbsoluteUri }.Concat(values)))));

    private static async Task<IResult> SendAsync(HttpContext context, string path, bool requirePartition,
        CancellationToken token, bool style = false)
    {
        context.Response.Headers.CacheControl = "no-store";
        context.Response.Headers.Vary = "Cookie";
        var (viewer, failure) = await ResolveAsync(context, token);
        if (viewer is null) return Results.StatusCode(failure);
        if (requirePartition && (context.Request.Headers[PartitionHeader].Count != 1
            || !string.Equals(context.Request.Headers[PartitionHeader], viewer.Partition, StringComparison.Ordinal)))
        {
            return Results.StatusCode(409);
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(viewer.Target, path));
        if (viewer.Bearer is not null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", viewer.Bearer);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(style ? MapboxStyleContentType : "application/json"));
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(45));
        try
        {
            // Dedicated client: no configured API key, cookies, automatic redirects or agent credentials.
            using var response = await context.RequestServices.GetRequiredService<IHttpClientFactory>()
                .CreateClient("honua-studio-audience").SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            if ((int)response.StatusCode is >= 300 and < 400) return Results.StatusCode(502);
            if (!response.IsSuccessStatusCode) return Results.StatusCode((int)response.StatusCode);
            var maximumBytes = style ? MaximumStyleResponseBytes : MaximumResponseBytes;
            if (response.Content.Headers.ContentLength > maximumBytes) return Results.StatusCode(502);
            await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token);
            using var buffer = new MemoryStream();
            var bytes = new byte[16 * 1024];
            int count;
            while ((count = await stream.ReadAsync(bytes, timeout.Token)) != 0)
            {
                if (buffer.Length + count > maximumBytes) return Results.StatusCode(502);
                buffer.Write(bytes, 0, count);
            }

            // Reject a reply produced while the current account/profile/target was being changed.
            var (current, _) = await ResolveAsync(context, timeout.Token);
            if (current?.Partition != viewer.Partition) return Results.StatusCode(409);
            try { using var json = JsonDocument.Parse(buffer.ToArray()); }
            catch (JsonException) { return Results.StatusCode(502); }
            context.Response.Headers[PartitionHeader] = viewer.Partition;
            if (style && response.Headers.ETag is { } tag) context.Response.Headers.ETag = tag.ToString();
            return Results.Content(Encoding.UTF8.GetString(buffer.ToArray()),
                style ? MapboxStyleContentType : "application/json", statusCode: (int)response.StatusCode);
        }
        catch (HttpRequestException) { return Results.StatusCode(502); }
        catch (OperationCanceledException) when (!token.IsCancellationRequested) { return Results.StatusCode(504); }
    }
}
