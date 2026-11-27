using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Honua.Console.Contracts;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Services;
using Honua.Console.Web.Auth;
using Microsoft.AspNetCore.Antiforgery;

namespace Honua.Console.Web;

// Opt-in BFF proxy to honua-support. Injects the configured bearer key server-side only and never exposes it to browser JS.
internal static partial class SupportPortalBff
{
    internal const string HttpClientName = "honua-support-portal";
    internal const int MaximumRequestBytes = 256 * 1024;
    internal const int MaximumResponseBytes = 2 * 1024 * 1024;
    internal const string UnconfiguredCode = "support_portal_unconfigured";
    internal const string UnconfiguredMessage =
        "In-product Support Portal is not configured. Set HonuaSupport:BaseUrl (or HONUA_SUPPORT_BASE_URL) " +
        "and HonuaSupport:ApiKey (or HONUA_SUPPORT_API_KEY) on the Console host to enable Support & Security Advisories.";

    private static readonly HashSet<string> AllowedFleetQueryKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "component",
        "version"
    };

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
        var upstreamPath = BuildFleetUpstreamPath(context.Request.Query);
        if (upstreamPath is null)
        {
            return Task.FromResult<IResult>(Results.BadRequest());
        }

        return ExecuteReadAsync(context, upstreamPath, token);
    }

    internal static bool IsSafeTicketId(string? id) =>
        !string.IsNullOrWhiteSpace(id)
        && id is not ("." or "..")
        && !id.Contains("..", StringComparison.Ordinal)
        && TicketIdPattern().IsMatch(id);

    internal static string? BuildFleetUpstreamPath(IQueryCollection query)
    {
        if (query.Count == 0)
        {
            return SupportPortalRoutes.UpstreamFleetInstances;
        }

        var pairs = new List<string>();
        foreach (var item in query)
        {
            if (!AllowedFleetQueryKeys.Contains(item.Key) || item.Value.Count != 1)
            {
                return null;
            }

            var value = item.Value[0]?.Trim();
            if (string.IsNullOrWhiteSpace(value) || value.Length > 128 || value.Any(char.IsControl))
            {
                return null;
            }

            pairs.Add($"{Uri.EscapeDataString(item.Key)}={Uri.EscapeDataString(value)}");
        }

        return pairs.Count == 0
            ? SupportPortalRoutes.UpstreamFleetInstances
            : $"{SupportPortalRoutes.UpstreamFleetInstances}?{string.Join('&', pairs)}";
    }

    internal static SupportPortalBinding? TryResolveBinding(IConfiguration configuration)
    {
        var rawBase = ReadFirst(configuration, "HonuaSupport:BaseUrl", "Honua:Support:BaseUrl", "HONUA_SUPPORT_BASE_URL");
        var apiKey = ReadFirst(configuration, "HonuaSupport:ApiKey", "Honua:Support:ApiKey", "HONUA_SUPPORT_API_KEY");
        if (string.IsNullOrWhiteSpace(rawBase) || string.IsNullOrWhiteSpace(apiKey))
        {
            return null;
        }

        if (!TryParseSupportBaseUri(rawBase.Trim(), out var baseUri))
        {
            return null;
        }

        return new SupportPortalBinding(baseUri!, apiKey.Trim());
    }

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
        if (!IsSafeTicketId(mutation.TicketId))
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
            (body, bodyError) = await AttachPreflightContextAsync(context, body!, token);
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
        var binding = TryResolveBinding(configuration);
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

    private static async Task<(byte[]? Body, IResult? Error)> AttachPreflightContextAsync(
        HttpContext context,
        byte[] rawBody,
        CancellationToken token)
    {
        var root = ParsePreflightRoot(rawBody);
        if (root is null)
        {
            return (null, Results.BadRequest());
        }

        var snapshot = await CaptureConnectedContextAsync(context, token);
        ApplyPreflightDefaults(root, snapshot);
        return (Encoding.UTF8.GetBytes(root.ToJsonString()), null);
    }

    private static JsonObject? ParsePreflightRoot(byte[] rawBody)
    {
        if (rawBody.Length == 0)
        {
            return new JsonObject();
        }

        try
        {
            return JsonNode.Parse(rawBody) as JsonObject;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static async Task<ConnectedServerSnapshot> CaptureConnectedContextAsync(HttpContext context, CancellationToken token)
    {
        var profiles = context.RequestServices.GetService<IConsoleEnvironmentProfileStore>();
        var profile = profiles is null ? null : await profiles.GetActiveProfileAsync(token);
        var serverVersion = await TryReadServerVersionAsync(context.RequestServices, token);
        var build = ConsoleBuildMetadata.Create();
        return new ConnectedServerSnapshot
        {
            Profile = profile,
            ServerVersion = serverVersion,
            ConsoleVersion = build.TryGetValue("version", out var version) ? version?.ToString() ?? "unknown" : "unknown",
            ConsoleCommit = build.TryGetValue("commit", out var commit) ? commit?.ToString() ?? "unknown" : "unknown"
        };
    }

    private static async Task<string?> TryReadServerVersionAsync(IServiceProvider services, CancellationToken token)
    {
        var client = services.GetService<IConsoleServerVersionClient>();
        if (client is null)
        {
            return null;
        }

        try
        {
            var result = await client.GetServerVersionAsync(token);
            return result.IsAllowed && !string.IsNullOrWhiteSpace(result.Value?.ServerVersion)
                ? result.Value.ServerVersion.Trim()
                : null;
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or InvalidOperationException)
        {
            return null;
        }
    }

    private static void ApplyPreflightDefaults(JsonObject root, ConnectedServerSnapshot snapshot)
    {
        var envKind = NormalizeNonBlank(snapshot.Profile?.EnvironmentKind);
        SetIfMissing(root, "environment", envKind);
        SetIfMissing(root, "service", "honua-server");

        var clientContext = GetOrCreateObject(root, "clientContext");
        SetIfMissing(clientContext, "clientName", "honua-console");
        SetIfMissing(clientContext, "clientVersion", NormalizeNonBlank(snapshot.ConsoleVersion));
        SetIfMissing(clientContext, "serverVersion", NormalizeNonBlank(snapshot.ServerVersion));

        var supportContext = GetOrCreateObject(root, "context");
        SetIfMissing(supportContext, "schemaVersion", "1.0");
        SetIfMissing(supportContext, "envKind", envKind);
        SetIfMissing(supportContext, "instanceUrl", snapshot.Profile?.ServerBaseUri.AbsoluteUri);
        SetIfMissing(supportContext, "appVersion", NormalizeNonBlank(snapshot.ServerVersion) ?? NormalizeNonBlank(snapshot.ConsoleVersion));
        SetIfMissing(supportContext, "commit", NormalizeNonBlank(snapshot.ConsoleCommit));
        ApplyTenantDefault(supportContext, snapshot.Profile?.TenantId);
    }

    private static void ApplyTenantDefault(JsonObject supportContext, string? tenantId)
    {
        var cleanTenant = NormalizeNonBlank(tenantId);
        if (cleanTenant is null)
        {
            return;
        }

        var tenant = GetOrCreateObject(supportContext, "tenant");
        SetIfMissing(tenant, "id", cleanTenant);
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

    private static bool TryParseSupportBaseUri(string raw, out Uri? baseUri)
    {
        baseUri = null;
        if (!Uri.TryCreate(raw, UriKind.Absolute, out var parsed)
            || parsed.UserInfo.Length != 0 || parsed.Query.Length != 0 || parsed.Fragment.Length != 0)
        {
            return false;
        }

        var allowedScheme = string.Equals(parsed.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
            || (string.Equals(parsed.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) && parsed.IsLoopback);
        if (!allowedScheme)
        {
            return false;
        }

        baseUri = new Uri(parsed.AbsoluteUri.TrimEnd('/') + "/");
        return true;
    }

    private static string? ReadFirst(IConfiguration configuration, params string[] keys)
    {
        foreach (var key in keys)
        {
            var value = configuration[key];
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        return null;
    }

    private static JsonObject GetOrCreateObject(JsonObject parent, string key)
    {
        if (parent[key] is JsonObject existing)
        {
            return existing;
        }

        var created = new JsonObject();
        parent[key] = created;
        return created;
    }

    private static void SetIfMissing(JsonObject target, string key, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        if (target[key] is JsonValue existing
            && existing.TryGetValue<string>(out var current)
            && !string.IsNullOrWhiteSpace(current))
        {
            return;
        }

        target[key] = value;
    }

    private static string? NormalizeNonBlank(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    [GeneratedRegex("^[A-Za-z0-9._-]{1,128}$", RegexOptions.CultureInvariant)]
    private static partial Regex TicketIdPattern();

    internal sealed record SupportPortalBinding(Uri BaseUri, string ApiKey);

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

    private sealed record ConnectedServerSnapshot
    {
        public ConsoleEnvironmentProfile? Profile { get; init; }

        public string? ServerVersion { get; init; }

        public string ConsoleVersion { get; init; } = string.Empty;

        public string ConsoleCommit { get; init; } = string.Empty;
    }
}
