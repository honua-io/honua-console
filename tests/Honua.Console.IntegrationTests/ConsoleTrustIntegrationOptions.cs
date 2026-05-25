using System.Globalization;

namespace Honua.Console.IntegrationTests;

/// <summary>
/// Environment-driven options for the opt-in real-server trust integration suite. Mirrors the
/// honua-sdk-dotnet ProtocolIntegration options pattern. The suite is off by default and skips with a
/// clear reason unless explicitly enabled (Console Patterns Charter section 11).
/// </summary>
public sealed record ConsoleTrustIntegrationOptions
{
    public bool Enabled { get; init; }

    public Uri? ExternalBaseUri { get; init; }

    public string? ServerImage { get; init; }

    public ushort ServerPort { get; init; } = 8080;

    public string ServerScheme { get; init; } = "https";

    public string ServerHealthPath { get; init; } = "/health";

    /// <summary>Bearer token granting admin access to the client-certificate validate endpoint.</summary>
    public string? AdminToken { get; init; }

    /// <summary>Admin API key sent as <c>X-API-Key</c> to the admin-protected Studio package endpoints.</summary>
    public string? StudioAdminApiKey { get; init; }

    /// <summary>Optional expected server-side trust profile id.</summary>
    public string? TrustProfileId { get; init; }

    /// <summary>Server env key that receives the PostgreSQL connection string (image-specific).</summary>
    public string DbConnectionEnvKey { get; init; } = "ConnectionStrings__Postgres";

    /// <summary>Optional path to a PFX the operator pre-seeded as a trusted client certificate on the server.</summary>
    public string? TrustedCertificatePfxPath { get; init; }

    /// <summary>Optional password for <see cref="TrustedCertificatePfxPath"/>.</summary>
    public string? TrustedCertificatePassword { get; init; }

    /// <summary>Extra <c>KEY=VALUE</c> server env entries (newline or semicolon separated), image-specific.</summary>
    public string? ServerEnvironment { get; init; }

    public bool HasServerTarget =>
        ExternalBaseUri is not null || !string.IsNullOrWhiteSpace(ServerImage);

    public static ConsoleTrustIntegrationOptions Load()
    {
        if (!ReadBoolean("HONUA_CONSOLE_INTEGRATION"))
        {
            return new ConsoleTrustIntegrationOptions();
        }

        return new ConsoleTrustIntegrationOptions
        {
            Enabled = true,
            ExternalBaseUri = ReadUri("HONUA_CONSOLE_EXTERNAL_BASE_URL"),
            ServerImage = ReadString("HONUA_CONSOLE_SERVER_IMAGE"),
            ServerPort = ReadUShort("HONUA_CONSOLE_SERVER_PORT", 8080),
            ServerScheme = ReadString("HONUA_CONSOLE_SERVER_SCHEME") ?? "https",
            ServerHealthPath = ReadString("HONUA_CONSOLE_SERVER_HEALTH_PATH") ?? "/health",
            AdminToken = ReadString("HONUA_CONSOLE_ADMIN_TOKEN"),
            StudioAdminApiKey = ReadString("HONUA_CONSOLE_ADMIN_API_KEY"),
            TrustProfileId = ReadString("HONUA_CONSOLE_TRUST_PROFILE_ID"),
            DbConnectionEnvKey = ReadString("HONUA_CONSOLE_DB_CONNECTION_ENV") ?? "ConnectionStrings__Postgres",
            TrustedCertificatePfxPath = ReadString("HONUA_CONSOLE_TRUSTED_CERT_PFX"),
            TrustedCertificatePassword = ReadString("HONUA_CONSOLE_TRUSTED_CERT_PASSWORD"),
            ServerEnvironment = ReadString("HONUA_CONSOLE_SERVER_ENV")
        };
    }

    /// <summary>Reason this suite cannot run, or <c>null</c> if it can. Drives graceful skips.</summary>
    public static string? GetSkipReason()
    {
        var options = Load();
        if (!options.Enabled)
        {
            return "Set HONUA_CONSOLE_INTEGRATION=true to run the real-server mTLS/trust integration suite.";
        }

        if (!options.HasServerTarget)
        {
            return "Set HONUA_CONSOLE_SERVER_IMAGE or HONUA_CONSOLE_EXTERNAL_BASE_URL to run the trust integration suite.";
        }

        if (options.ExternalBaseUri is null && !DockerIsAvailable())
        {
            return "Docker is not available; cannot boot the honua-server Testcontainer.";
        }

        return null;
    }

    /// <summary>Reason the server-authenticated validate facts cannot run (need an admin token).</summary>
    public static string? GetValidateSkipReason()
    {
        var baseReason = GetSkipReason();
        if (baseReason is not null)
        {
            return baseReason;
        }

        return string.IsNullOrWhiteSpace(Load().AdminToken)
            ? "Set HONUA_CONSOLE_ADMIN_TOKEN to exercise the admin-protected client-certificate validate endpoint."
            : null;
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

    public IReadOnlyDictionary<string, string> BuildServerEnvironment(string postgresConnectionString)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["ASPNETCORE_URLS"] = $"{ServerScheme}://+:{ServerPort.ToString(CultureInfo.InvariantCulture)}",
            // mTLS is optional so the admin validate endpoint stays reachable with a bearer token while
            // still validating presented client certificates (honua-server#1171 options).
            ["Authentication__ClientCertificates__Mode"] = "Optional",
            [DbConnectionEnvKey] = postgresConnectionString
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
