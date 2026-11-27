using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Services;

namespace Honua.Console.Web;

internal static class SupportPortalPreflightEnricher
{
    internal static async Task<(byte[]? Body, IResult? Error)> AttachContextAsync(
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

    private sealed record ConnectedServerSnapshot
    {
        public ConsoleEnvironmentProfile? Profile { get; init; }

        public string? ServerVersion { get; init; }

        public string ConsoleVersion { get; init; } = string.Empty;

        public string ConsoleCommit { get; init; } = string.Empty;
    }
}
