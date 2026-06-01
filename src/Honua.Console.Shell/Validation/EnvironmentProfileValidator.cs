using Honua.Console.Shell.Models;

namespace Honua.Console.Shell.Validation;

/// <summary>
/// Stable console-owned field keys for the native environment-profile create form
/// (<c>EnvironmentProfileNewPage</c>).
/// </summary>
public static class EnvironmentProfileFieldKeys
{
    public const string DisplayName = "environment.displayName";
    public const string ServerBaseUri = "environment.serverBaseUri";
    public const string CertValue = "environment.certValue";
}

/// <summary>
/// Console-owned snapshot of the native environment-profile create form. Migrated from the page's former
/// ad-hoc <c>CreateAsync</c> checks so the same rules render inline via the shared validation vocabulary.
/// </summary>
/// <param name="DisplayName">The profile display name (required).</param>
/// <param name="ServerBaseUri">The server base URL (must be an absolute https URL).</param>
/// <param name="MtlsEnabled">Whether native mTLS (client certificate) is required.</param>
/// <param name="CertValue">The certificate reference value (required when <paramref name="MtlsEnabled"/>).</param>
public sealed record EnvironmentProfileState(
    string? DisplayName,
    string? ServerBaseUri,
    bool MtlsEnabled,
    string? CertValue);

/// <summary>
/// Pure client-side validator for the native environment-profile create form. It migrates the page's former
/// inline <c>CreateAsync</c> presence/format checks onto the shared <see cref="ConsoleFieldError"/> vocabulary:
/// display name required, server base URI absolute https, and certificate value required-when-mTLS. Mirrors the
/// <see cref="StudioMapValidator"/> pattern; keyed by <see cref="EnvironmentProfileFieldKeys"/>.
/// </summary>
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

        if (state.MtlsEnabled && string.IsNullOrWhiteSpace(state.CertValue))
        {
            errors.Add(Blocker(
                EnvironmentProfileFieldKeys.CertValue,
                "environment.certValue.requiredWhenMtls",
                "A certificate reference value is required when native mTLS is enabled."));
        }

        return errors;
    }

    private static ConsoleFieldError Blocker(string key, string code, string message) =>
        new(key, code, ConsoleValidationSeverity.Blocker, message);
}
