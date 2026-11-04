using Honua.Console.Shell.Models;

namespace Honua.Console.Shell.Validation;

public static class EnvironmentProfileFieldKeys
{
    public const string DisplayName = "environment.displayName";
    public const string ServerBaseUri = "environment.serverBaseUri";
}

public sealed record EnvironmentProfileState(
    string? DisplayName,
    string? ServerBaseUri);

public sealed class EnvironmentProfileValidator : IFieldValidator<EnvironmentProfileState>
{
    /// <summary>Shared singleton; the validator holds no state.</summary>
    public static EnvironmentProfileValidator Instance { get; } = new();

    /// <inheritdoc />
    public IReadOnlyList<ConsoleFieldError> Evaluate(EnvironmentProfileState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        var errors = new List<ConsoleFieldError>();

        if (string.IsNullOrWhiteSpace(state.DisplayName))
        {
            errors.Add(Blocker(EnvironmentProfileFieldKeys.DisplayName, "environment.displayName.required", "Display name is required."));
        }

        if (!UrlRule.IsAbsoluteHttps(state.ServerBaseUri))
        {
            errors.Add(Blocker(
                EnvironmentProfileFieldKeys.ServerBaseUri,
                "environment.serverBaseUri.https",
                "Server URL must be an absolute https URL."));
        }

        return errors;
    }

    private static ConsoleFieldError Blocker(string key, string code, string message) =>
        new(key, code, ConsoleValidationSeverity.Blocker, message);
}
