using System.Globalization;

namespace Honua.Console.IntegrationTests;

public sealed record LiveServerIntegrationOptions
{
    public bool Enabled { get; init; }

    public Uri? ExternalBaseUri { get; init; }

    public string? ServerImage { get; init; }

    public ushort ServerPort { get; init; } = 8080;

    public string ServerScheme { get; init; } = "https";

    public string ServerHealthPath { get; init; } = "/health";

    /// <summary>Admin API key sent as <c>X-API-Key</c> to the admin-protected Studio package endpoints.</summary>
    public string? StudioAdminApiKey { get; init; }

    /// <summary>Server env key that receives the PostgreSQL connection string (image-specific).</summary>
    public string DbConnectionEnvKey { get; init; } = "ConnectionStrings__Postgres";

    /// <summary>Extra <c>KEY=VALUE</c> server env entries (newline or semicolon separated), image-specific.</summary>
    public string? ServerEnvironment { get; init; }

    public bool HasServerTarget =>
        ExternalBaseUri is not null || !string.IsNullOrWhiteSpace(ServerImage);

    public static LiveServerIntegrationOptions Load()
    {
        // The image-based integration lane opts in with HONUA_CONSOLE_INTEGRATION; the end-to-end smoke
        // also honours HONUA_CONSOLE_RUN_LIVE_SERVER_TESTS, the opt-in shared with the Shell live-server
        // lane, so a single flag can enable every live-server lane.
        if (!ReadBoolean("HONUA_CONSOLE_INTEGRATION") && !ReadBoolean("HONUA_CONSOLE_RUN_LIVE_SERVER_TESTS"))
        {
            return new LiveServerIntegrationOptions();
        }

        return new LiveServerIntegrationOptions
        {
            Enabled = true,
            ExternalBaseUri = ReadUri("HONUA_CONSOLE_EXTERNAL_BASE_URL"),
            ServerImage = ReadString("HONUA_CONSOLE_SERVER_IMAGE"),
            ServerPort = ReadUShort("HONUA_CONSOLE_SERVER_PORT", 8080),
            ServerScheme = ReadString("HONUA_CONSOLE_SERVER_SCHEME") ?? "https",
            ServerHealthPath = ReadString("HONUA_CONSOLE_SERVER_HEALTH_PATH") ?? "/health",
            StudioAdminApiKey = ReadString("HONUA_CONSOLE_ADMIN_API_KEY"),
            DbConnectionEnvKey = ReadString("HONUA_CONSOLE_DB_CONNECTION_ENV") ?? "ConnectionStrings__Postgres",
            ServerEnvironment = ReadString("HONUA_CONSOLE_SERVER_ENV")
        };
    }

    /// <summary>Reason this suite cannot run, or <c>null</c> if it can. Drives graceful skips.</summary>
    public static string? GetSkipReason()
    {
        var options = Load();
        if (!options.Enabled)
        {
            return "Set HONUA_CONSOLE_INTEGRATION=true (or HONUA_CONSOLE_RUN_LIVE_SERVER_TESTS=true) to run the real-server integration suite.";
        }

        if (!options.HasServerTarget)
        {
            return "Set HONUA_CONSOLE_SERVER_IMAGE or HONUA_CONSOLE_EXTERNAL_BASE_URL to run the real-server integration suite.";
        }

        if (options.ExternalBaseUri is null && !DockerIsAvailable())
        {
            return "Docker is not available; cannot boot the honua-server Testcontainer.";
        }

        return null;
    }

    /// <summary>Reason the live Studio package-lifecycle facts cannot run (need an admin API key).</summary>
    public static string? GetStudioSkipReason()
    {
        var baseReason = GetSkipReason();
        if (baseReason is not null)
        {
            return baseReason;
        }

        return string.IsNullOrWhiteSpace(Load().StudioAdminApiKey)
            ? "Set HONUA_CONSOLE_ADMIN_API_KEY to exercise the admin-protected Studio package lifecycle endpoints."
            : null;
    }

    /// <summary>
    /// Reason the Console end-to-end smoke cannot run. The smoke seeds and drives the catalog, Studio
    /// package, and operate/publishing surfaces, all of which are admin-protected, so it needs the same
    /// admin API key as the Studio lane.
    /// </summary>
    public static string? GetEndToEndSkipReason() => GetStudioSkipReason();

    public IReadOnlyDictionary<string, string> BuildServerEnvironment(string postgresConnectionString)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["ASPNETCORE_URLS"] = $"{ServerScheme}://+:{ServerPort.ToString(CultureInfo.InvariantCulture)}",
            // mTLS is optional so the admin validate endpoint stays reachable with a bearer token while
            // still validating presented client certificates (honua-server#1171 options).
            ["Authentication__ClientCertificates__Mode"] = "Optional",
            [DbConnectionEnvKey] = postgresConnectionString,
            // The pinned honua-server image resolves its provider from ConnectionStrings__DefaultConnection;
            // mirror the connection string there so the secure-connection + layer-publishing admin paths boot
            // (the publish round-trip registers a connection through the encryption service, below).
            ["ConnectionStrings__DefaultConnection"] = postgresConnectionString,
            // Secure-connection creation (POST /api/v1/admin/connections) encrypts credentials with a master
            // key; without it the admin path 500s. Provide a deterministic test key so the live lane can
            // register the source connection the layer-publish round-trip needs. Real deployments set their
            // own key; ServerEnvironment overrides this default when supplied.
            ["Security__ConnectionEncryption__MasterKey"] = "console-integration-test-master-key-0123456789"
        };

        foreach (var (key, value) in ParseEnvironmentList(ServerEnvironment))
        {
            values[key] = value;
        }

        return values;
    }

    private static IEnumerable<KeyValuePair<string, string>> ParseEnvironmentList(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            yield break;
        }

        foreach (var entry in raw.Split(['\n', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var separator = entry.IndexOf('=', StringComparison.Ordinal);
            if (separator <= 0)
            {
                continue;
            }

            yield return new KeyValuePair<string, string>(entry[..separator].Trim(), entry[(separator + 1)..].Trim());
        }
    }

    private static bool DockerIsAvailable()
    {
        // Honour the standard Testcontainers/Docker host hints, then fall back to the default socket.
        if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("DOCKER_HOST"))
            || !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("TESTCONTAINERS_DOCKER_SOCKET_OVERRIDE")))
        {
            return true;
        }

        return OperatingSystem.IsWindows()
            ? File.Exists(@"\\.\pipe\docker_engine") || File.Exists("//./pipe/docker_engine")
            : File.Exists("/var/run/docker.sock");
    }

    private static string? ReadString(string name)
    {
        var value = Environment.GetEnvironmentVariable(name);
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static Uri? ReadUri(string name)
    {
        var value = ReadString(name);
        return value is null ? null : new Uri(value, UriKind.Absolute);
    }

    private static bool ReadBoolean(string name)
    {
        var value = ReadString(name);
        return value is not null
            && (string.Equals(value, "true", StringComparison.OrdinalIgnoreCase)
                || string.Equals(value, "1", StringComparison.OrdinalIgnoreCase)
                || string.Equals(value, "yes", StringComparison.OrdinalIgnoreCase));
    }

    private static ushort ReadUShort(string name, ushort defaultValue)
    {
        var value = ReadString(name);
        return value is null ? defaultValue : ushort.Parse(value, NumberStyles.Integer, CultureInfo.InvariantCulture);
    }
}
