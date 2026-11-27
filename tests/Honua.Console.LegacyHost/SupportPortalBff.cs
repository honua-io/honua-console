using System.Net.Http.Headers;
using System.Text;
using Honua.Console.Contracts;
using Honua.Console.Web.Auth;
using Microsoft.AspNetCore.Antiforgery;

namespace Honua.Console.Web;

// Opt-in BFF proxy to honua-support. Injects the configured bearer key server-side only and never exposes it to browser JS.
internal static class SupportPortalBff
{
    internal const string HttpClientName = "honua-support-portal";
    internal const int MaximumRequestBytes = 256 * 1024;
    internal const int MaximumResponseBytes = 2 * 1024 * 1024;
    internal const string UnconfiguredCode = "support_portal_unconfigured";
    internal const string UnconfiguredMessage =
        "In-product Support Portal is not configured. Set HonuaSupport:BaseUrl (or HONUA_SUPPORT_BASE_URL) " +
        "and HonuaSupport:ApiKey (or HONUA_SUPPORT_API_KEY) on the Console host to enable Support & Security Advisories.";

    public static void MapSupportPortalBff(this WebApplication app)
    {
        app.MapPost("/api/support/preflight", HandlePreflightAsync);
        app.MapGet("/api/support/tickets", HandleListTicketsAsync);
        app.MapPost("/api/support/tickets", HandleCreateTicketAsync);
        app.MapPost("/api/support/tickets/{id}/approve", HandleApproveTicketAsync);
        app.MapPost("/api/support/tickets/{id}/revoke-access", HandleRevokeAccessAsync);
        app.MapGet("/api/support/security/advisories", HandleListAdvisoriesAsync);
        app.MapGet("/api/support/fleet/instances", HandleListFleetInstancesAsync);
    }

    internal static Task<IResult> HandlePreflightAsync(HttpContext context, CancellationToken token) =>
        ExecuteMutationAsync(context, new UpstreamTarget(SupportPortalRoutes.UpstreamPreflight, MutationKind.Preflight), token);

    internal static Task<IResult> HandleListTicketsAsync(HttpContext context, CancellationToken token) =>
        ExecuteReadAsync(context, SupportPortalRoutes.UpstreamTickets, token);

    internal static Task<IResult> HandleCreateTicketAsync(HttpContext context, CancellationToken token) =>
        ExecuteMutationAsync(context, new UpstreamTarget(SupportPortalRoutes.UpstreamTickets, MutationKind.Standard), token);

    internal static Task<IResult> HandleApproveTicketAsync(HttpContext context, string id, CancellationToken token) =>
        ExecuteTicketMutationAsync(context, new TicketMutation(id, SupportPortalRoutes.UpstreamApproveTicket), token);

    internal static Task<IResult> HandleRevokeAccessAsync(HttpContext context, string id, CancellationToken token) =>
        ExecuteTicketMutationAsync(context, new TicketMutation(id, SupportPortalRoutes.UpstreamRevokeAccess), token);

    internal static Task<IResult> HandleListAdvisoriesAsync(HttpContext context, CancellationToken token) =>
        ExecuteReadAsync(context, SupportPortalRoutes.UpstreamSecurityAdvisories, token);

    internal static Task<IResult> HandleListFleetInstancesAsync(HttpContext context, CancellationToken token)
    {
        context.Response.Headers.CacheControl = "no-store";
        var upstreamPath = SupportPortalBindingResolver.BuildFleetUpstreamPath(context.Request.Query);
        if (upstreamPath is null)
        {
            return Task.FromResult<IResult>(Results.BadRequest());
        }

        return ExecuteReadAsync(context, upstreamPath, token);
    }

    internal static bool IsSafeTicketId(string? id) =>
        SupportPortalBindingResolver.IsSafeTicketId(id);

    private static async Task<IResult> ExecuteReadAsync(HttpContext context, string upstreamPath, CancellationToken token)
    {
        context.Response.Headers.CacheControl = "no-store";
        var (binding, gateFailure) = ResolveAuthorizedBinding(context);
        if (gateFailure is not null)
        {
            return gateFailure;
        }

        var forward = new ForwardRequest { Binding = binding!, Method = HttpMethod.Get, UpstreamPath = upstreamPath };
        return await ForwardAsync(context, forward, token);
    }

    private static Task<IResult> ExecuteTicketMutationAsync(HttpContext context, TicketMutation mutation, CancellationToken token)
    {
        context.Response.Headers.CacheControl = "no-store";
        if (!SupportPortalBindingResolver.IsSafeTicketId(mutation.TicketId))
        {
            return Task.FromResult<IResult>(Results.BadRequest());
        }

        var target = new UpstreamTarget(mutation.BuildPath(mutation.TicketId), MutationKind.Standard);
        return ExecuteMutationAsync(context, target, token);
    }

    private static async Task<IResult> ExecuteMutationAsync(HttpContext context, UpstreamTarget target, CancellationToken token)
    {
        context.Response.Headers.CacheControl = "no-store";
        var (binding, gateFailure) = ResolveAuthorizedBinding(context);
        if (gateFailure is not null)
        {
            return gateFailure;
        }

        if (!await ValidateAntiforgeryAsync(context))
        {
            return Results.StatusCode(StatusCodes.Status403Forbidden);
        }

        var (body, bodyError) = await ReadBoundedBodyAsync(context.Request.Body, token);
        if (bodyError is not null)
        {
            return bodyError;
        }

        if (target.Kind == MutationKind.Preflight)
        {
            (body, bodyError) = await SupportPortalPreflightEnricher.AttachContextAsync(context, body!, token);
            if (bodyError is not null)
            {
                return bodyError;
            }
        }

        var forward = new ForwardRequest { Binding = binding!, Method = HttpMethod.Post, UpstreamPath = target.Path, Body = body };
        return await ForwardAsync(context, forward, token);
    }

    private static (SupportPortalBinding? Binding, IResult? Failure) ResolveAuthorizedBinding(HttpContext context)
    {
        if (ConsoleOperatorContext.ResolveKey(context.User) is null)
        {
            return (null, Results.StatusCode(StatusCodes.Status401Unauthorized));
        }

        var configuration = context.RequestServices.GetRequiredService<IConfiguration>();
        var binding = SupportPortalBindingResolver.TryResolve(configuration);
        if (binding is null)
        {
            return (null, Results.Json(
                new { error = UnconfiguredCode, message = UnconfiguredMessage },
                statusCode: StatusCodes.Status503ServiceUnavailable));
        }

        return (binding, null);
    }

    private static async Task<bool> ValidateAntiforgeryAsync(HttpContext context)
    {
        var antiforgery = context.RequestServices.GetService<IAntiforgery>();
        if (antiforgery is null)
        {
            return true;
        }

        try
        {
            await antiforgery.ValidateRequestAsync(context);
            return true;
        }
        catch (AntiforgeryValidationException)
        {
            return false;
        }
    }

    private static async Task<(byte[]? Body, IResult? Error)> ReadBoundedBodyAsync(Stream source, CancellationToken token)
    {
        using var buffer = new MemoryStream();
        var chunk = new byte[8192];
        int read;
        while ((read = await source.ReadAsync(chunk, token)) != 0)
        {
            if (buffer.Length + read > MaximumRequestBytes)
            {
                return (null, Results.StatusCode(StatusCodes.Status413PayloadTooLarge));
            }

            buffer.Write(chunk, 0, read);
        }

        return (buffer.ToArray(), null);
    }

    private static async Task<IResult> ForwardAsync(
        HttpContext context,
        ForwardRequest forward,
        CancellationToken token)
    {
        var client = context.RequestServices.GetRequiredService<IHttpClientFactory>().CreateClient(HttpClientName);
        using var request = BuildUpstreamRequest(forward);
        try
        {
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
            if ((int)response.StatusCode is >= 300 and < 400)
            {
                return Results.StatusCode(StatusCodes.Status502BadGateway);
            }

            var content = await ReadBoundedResponseAsync(response.Content, token);
            if (content is null)
            {
                return Results.StatusCode(StatusCodes.Status502BadGateway);
            }

            var safeBody = ScrubConfiguredSecret(content, forward.Binding.ApiKey);
            var mediaType = ResolveJsonMediaType(response.Content.Headers.ContentType?.MediaType);
            return Results.Content(safeBody, mediaType, statusCode: (int)response.StatusCode);
        }
        catch (HttpRequestException)
        {
            return Results.StatusCode(StatusCodes.Status502BadGateway);
        }
        catch (TaskCanceledException) when (!token.IsCancellationRequested)
        {
            return Results.StatusCode(StatusCodes.Status504GatewayTimeout);
        }
    }

    private static HttpRequestMessage BuildUpstreamRequest(ForwardRequest forward)
    {
        var uri = new Uri(forward.Binding.BaseUri, forward.UpstreamPath);
        var request = new HttpRequestMessage(forward.Method, uri);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", forward.Binding.ApiKey);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        if (forward.Body is not null)
        {
            var payload = forward.Body.Length == 0 ? "{}"u8.ToArray() : forward.Body;
            request.Content = new ByteArrayContent(payload);
            request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        }

        return request;
    }

    private static async Task<string?> ReadBoundedResponseAsync(HttpContent content, CancellationToken token)
    {
        await using var stream = await content.ReadAsStreamAsync(token);
        using var output = new MemoryStream();
        var chunk = new byte[8192];
        int read;
        while ((read = await stream.ReadAsync(chunk, token)) != 0)
        {
            if (output.Length + read > MaximumResponseBytes)
            {
                return null;
            }

            output.Write(chunk, 0, read);
        }

        return Encoding.UTF8.GetString(output.ToArray());
    }

    private static string ScrubConfiguredSecret(string body, string apiKey) =>
        string.IsNullOrEmpty(apiKey) || !body.Contains(apiKey, StringComparison.Ordinal)
            ? body
            : body.Replace(apiKey, "[redacted]", StringComparison.Ordinal);

    private static string ResolveJsonMediaType(string? mediaType) =>
        string.Equals(mediaType, "application/problem+json", StringComparison.OrdinalIgnoreCase)
            ? "application/problem+json"
            : "application/json";

    private enum MutationKind
    {
        Standard,
        Preflight
    }

    private sealed record UpstreamTarget(string Path, MutationKind Kind);

    private sealed record TicketMutation(string TicketId, Func<string, string> BuildPath);

    private sealed record ForwardRequest
    {
        public required SupportPortalBinding Binding { get; init; }

        public required HttpMethod Method { get; init; }

        public required string UpstreamPath { get; init; }

        public byte[]? Body { get; init; }
    }
}
