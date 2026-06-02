using Honua.Console.Shell.Models;

namespace Honua.Console.Shell.Validation;

/// <summary>
/// Stable console-owned field keys for the Add Connection form (<c>OperateConnectionNewPage</c>). Used to key
/// both client-computed findings and server <c>errors[]</c> back onto the offending inputs.
/// </summary>
public static class ConnectionFieldKeys
{
    public const string Name = "connection.name";
    public const string Host = "connection.host";
    public const string Port = "connection.port";
    public const string Database = "connection.database";
    public const string Username = "connection.username";
    public const string Password = "connection.password";
    public const string Provider = "connection.provider";
    public const string SecretReference = "connection.secretReference";
}

/// <summary>
/// Console-owned snapshot of the Add Connection form. <see cref="ExistingNames"/> is the set of connection
/// names already on the server (case-insensitive) so the validator can block a duplicate before POSTing —
/// honua-server enforces a unique-name DB constraint but surfaces a violation as a generic 400.
/// </summary>
public sealed record ConnectionDraftState(
    string? Name,
    string? Host,
    int Port,
    string? Database,
    string? Username,
    string? Password,
    string? Provider,
    IReadOnlySet<string> ExistingNames,
    bool UsesSecretReference = false,
    string? SecretReference = null);

/// <summary>
/// Pure client-side validator for the Add Connection form. Presence/format/duplicate rules rendered inline via
/// the shared <see cref="ConsoleFieldError"/> vocabulary, mirroring <see cref="EnvironmentProfileValidator"/>;
/// keyed by <see cref="ConnectionFieldKeys"/>.
/// </summary>
public sealed class ConnectionDraftValidator : IFieldValidator<ConnectionDraftState>
{
    /// <summary>Shared singleton; the validator holds no state.</summary>
    public static ConnectionDraftValidator Instance { get; } = new();

    /// <inheritdoc />
    public IReadOnlyList<ConsoleFieldError> Evaluate(ConnectionDraftState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        var errors = new List<ConsoleFieldError>();

        var name = state.Name?.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            errors.Add(Blocker(ConnectionFieldKeys.Name, "connection.name.required", "Connection name is required."));
        }
        else if (state.ExistingNames.Contains(name))
        {
            errors.Add(Blocker(
                ConnectionFieldKeys.Name,
                "connection.name.duplicate",
                $"A connection named '{name}' already exists. Choose a different name."));
        }

        if (state.UsesSecretReference)
        {
            // The secret reference holds the full connection string; host/port/database/username/password are
            // supplied inside the resolved secret, so only the reference itself is validated here.
            var reference = state.SecretReference?.Trim();
            if (string.IsNullOrWhiteSpace(reference))
            {
                errors.Add(Blocker(
                    ConnectionFieldKeys.SecretReference,
                    "connection.secretReference.required",
                    "A secret reference is required (e.g. env:PROD_DB_DSN)."));
            }
            else if (!IsWellFormedSecretReference(reference))
            {
                errors.Add(Blocker(
                    ConnectionFieldKeys.SecretReference,
                    "connection.secretReference.format",
                    "Use the form provider:path, e.g. env:PROD_DB_DSN or aws:secretsmanager:prod-db-creds."));
            }

            return errors;
        }

        if (string.IsNullOrWhiteSpace(state.Host))
        {
            errors.Add(Blocker(ConnectionFieldKeys.Host, "connection.host.required", "Host is required."));
        }

        if (state.Port is < 1 or > 65535)
        {
            errors.Add(Blocker(ConnectionFieldKeys.Port, "connection.port.range", "Port must be between 1 and 65535."));
        }

        if (string.IsNullOrWhiteSpace(state.Database))
        {
            errors.Add(Blocker(ConnectionFieldKeys.Database, "connection.database.required", "Database is required."));
        }

        if (string.IsNullOrWhiteSpace(state.Username))
        {
            errors.Add(Blocker(ConnectionFieldKeys.Username, "connection.username.required", "Username is required."));
        }

        if (string.IsNullOrWhiteSpace(state.Password))
        {
            errors.Add(Blocker(ConnectionFieldKeys.Password, "connection.password.required", "Password is required."));
        }

        return errors;
    }

    /// <summary>A reference is well-formed when it is <c>provider:path</c> with non-empty provider and path.</summary>
    private static bool IsWellFormedSecretReference(string reference)
    {
        var colon = reference.IndexOf(':');
        return colon > 0 && colon < reference.Length - 1;
    }

    private static ConsoleFieldError Blocker(string key, string code, string message) =>
        new(key, code, ConsoleValidationSeverity.Blocker, message);
}
