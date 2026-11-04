namespace Honua.Console.Shell.Models;

public sealed record ConsoleConnectionCreateCommand
{
    public required string Name { get; init; }

    public string? Description { get; init; }

    // "password" sends inline credentials. "secret" sends SecretReference.
    public string CredentialSource { get; init; } = "password";

    public string? Host { get; init; }

    public int Port { get; init; } = 5432;

    public string? DatabaseName { get; init; }

    public string? Username { get; init; }

    public string? Password { get; init; }

    // A reference such as env:NAME or aws:secretsmanager:NAME. SecretType is the prefix: env, aws, or azure.
    public string? SecretReference { get; init; }

    public string? SecretType { get; init; }

    public string Provider { get; init; } = "postgis";

    public bool SslRequired { get; init; }

    public string SslMode { get; init; } = "Disable";

    public bool UsesSecretReference =>
        string.Equals(CredentialSource, "secret", StringComparison.OrdinalIgnoreCase);
}

public sealed record ConsoleConnectionCreateResult : ConsoleOperationResult<ConsoleConnectionCreateResult>
{
    public string? ConnectionId { get; init; }

    public string? Name { get; init; }

    public IReadOnlyList<ConsoleConnectionCreateFieldError> FieldErrors { get; init; } = [];
}

public sealed record ConsoleConnectionCreateFieldError(
    string Code,
    string Message,
    string? Path = null,
    string? FieldId = null,
    string? Severity = null);

public sealed record ConsoleConnectionTestOutcome
{
    public bool Tested { get; init; }

    public bool IsHealthy { get; init; }

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
