namespace Honua.Console.Shell.Models;

/// <summary>
/// Operator intent for the connection-create operation: the connection identity, the PostGIS target
/// (host/port/database), credentials, provider, and SSL posture the Add Connection form collects. Mirrors the
/// inputs the connection wizard captures before the connection is persisted on honua-server.
/// </summary>
public sealed record ConsoleConnectionCreateCommand
{
    public required string Name { get; init; }

    public string? Description { get; init; }

    /// <summary>"password" (inline credentials) or "secret" (external secret reference holding the connection).</summary>
    public string CredentialSource { get; init; } = "password";

    public string? Host { get; init; }

    public int Port { get; init; } = 5432;

    public string? DatabaseName { get; init; }

    public string? Username { get; init; }

    public string? Password { get; init; }

    /// <summary>External secret reference (e.g. <c>env:PROD_DB_DSN</c>, <c>aws:secretsmanager:prod-db-creds</c>).</summary>
    public string? SecretReference { get; init; }

    /// <summary>Secret store kind derived from the reference prefix (env, aws, azure).</summary>
    public string? SecretType { get; init; }

    public string Provider { get; init; } = "postgis";

    public bool SslRequired { get; init; }

    public string SslMode { get; init; } = "Disable";

    /// <summary>True when the operator chose to supply an external secret reference instead of a password.</summary>
    public bool UsesSecretReference =>
        string.Equals(CredentialSource, "secret", StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// Outcome of the connection-create operation. On success, <see cref="Succeeded"/> is <c>true</c> and
/// <see cref="ConnectionId"/> identifies the persisted connection. On failure (missing binding, validation
/// rejection, transport error) <see cref="Succeeded"/> is <c>false</c>, <see cref="State"/> carries the
/// neutral state vocabulary token, and <see cref="FieldErrors"/> carries any field-addressable validation
/// errors the operator can bind onto the offending inputs.
/// </summary>
public sealed record ConsoleConnectionCreateResult
{
    public bool Succeeded { get; init; }

    /// <summary>State vocabulary token (e.g. "Created", "Missing binding", "Rejected", "Unavailable").</summary>
    public required string State { get; init; }

    public string? Detail { get; init; }

    public string? ConnectionId { get; init; }

    public string? Name { get; init; }

    public IReadOnlyList<ConsoleConnectionCreateFieldError> FieldErrors { get; init; } = [];

    public static ConsoleConnectionCreateResult MissingBinding(string detail) => new()
    {
        Succeeded = false,
        State = "Missing binding",
        Detail = detail
    };
}

/// <summary>A field-addressable validation error surfaced by the connection-create operation.</summary>
public sealed record ConsoleConnectionCreateFieldError(
    string Code,
    string Message,
    string? Path = null,
    string? FieldId = null,
    string? Severity = null);

/// <summary>
/// Outcome of the pre-save connection-test operation. <see cref="Tested"/> distinguishes a completed test
/// (healthy or not) from a request that never reached the health probe (missing binding, validation
/// rejection, transport error), in which case <see cref="State"/> carries the neutral state vocabulary token
/// and <see cref="FieldErrors"/> any field-addressable validation errors.
/// </summary>
public sealed record ConsoleConnectionTestOutcome
{
    /// <summary>True when the server actually ran the health probe (regardless of the healthy result).</summary>
    public bool Tested { get; init; }

    public bool IsHealthy { get; init; }

    /// <summary>State vocabulary token (e.g. "Healthy", "Unhealthy", "Missing binding", "Rejected", "Unavailable").</summary>
    public required string State { get; init; }

    public string? Detail { get; init; }

    public IReadOnlyList<ConsoleConnectionCreateFieldError> FieldErrors { get; init; } = [];

    public static ConsoleConnectionTestOutcome MissingBinding(string detail) => new()
    {
        Tested = false,
        IsHealthy = false,
        State = "Missing binding",
        Detail = detail
    };
}
