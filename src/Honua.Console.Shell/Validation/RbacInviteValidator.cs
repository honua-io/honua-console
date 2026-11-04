using Honua.Console.Shell.Models;

namespace Honua.Console.Shell.Validation;

public static class RbacInviteFieldKeys
{
    public const string Email = "rbac.invite.email";
    public const string Scope = "rbac.invite.scope";
    public const string IpAllowlist = "rbac.invite.ipAllowlist";
}

public sealed record RbacInviteState(
    string? Email,
    bool ScopeDev,
    bool ScopeStaging,
    bool ScopeProd,
    string? IpAllowlist);

public sealed class RbacInviteValidator : IFieldValidator<RbacInviteState>
{
    public static RbacInviteValidator Instance { get; } = new();

    public IReadOnlyList<ConsoleFieldError> Evaluate(RbacInviteState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        var errors = new List<ConsoleFieldError>();

        if (string.IsNullOrWhiteSpace(state.Email))
        {
            errors.Add(Blocker(RbacInviteFieldKeys.Email, "rbac.invite.identity.required", "Enter an email or group to invite."));
        }
        else if (!IsValidIdentity(state.Email))
        {
            // The drawer accepts an email OR a group principal. An identity containing '@' is treated as an
            // email and held to the email rule; any other non-empty, whitespace-free token is a group principal.
            errors.Add(Error(RbacInviteFieldKeys.Email, "rbac.invite.email.format", "Enter a valid email address (name@example.gov) or a group identity."));
        }

        if (!state.ScopeDev && !state.ScopeStaging && !state.ScopeProd)
        {
            errors.Add(Blocker(
                RbacInviteFieldKeys.Scope,
                "rbac.invite.scope.required",
                "Select at least one environment scope (dev, staging, or prod)."));
        }

        EvaluateAllowlist(state.IpAllowlist, errors);

        return errors;
    }

    // A value without @ is a group or directory principal, not an email.
    private static bool IsValidIdentity(string? identity)
    {
        if (string.IsNullOrWhiteSpace(identity))
        {
            return false;
        }

        var trimmed = identity.Trim();
        if (trimmed.Any(char.IsWhiteSpace))
        {
            return false;
        }

        return trimmed.Contains('@', StringComparison.Ordinal) ? EmailRule.IsValid(trimmed) : true;
    }

    private static void EvaluateAllowlist(string? allowlist, List<ConsoleFieldError> errors)
    {
        if (string.IsNullOrWhiteSpace(allowlist))
        {
            // The IP allowlist is optional; blank means "global / no restriction".
            return;
        }

        var invalid = SplitEntries(allowlist)
            .Where(entry => !CidrRule.IsValid(entry))
            .ToArray();

        if (invalid.Length > 0)
        {
            errors.Add(Error(
                RbacInviteFieldKeys.IpAllowlist,
                "rbac.invite.ipAllowlist.cidr",
                $"Each IP allowlist entry must be a valid CIDR block (e.g. 10.0.0.0/8). Invalid: {string.Join(", ", invalid)}."));
        }
    }

    public static IEnumerable<string> SplitEntries(string? allowlist) =>
        string.IsNullOrWhiteSpace(allowlist)
            ? Array.Empty<string>()
            : allowlist
                .Split([',', ';', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static ConsoleFieldError Blocker(string key, string code, string message) =>
        new(key, code, ConsoleValidationSeverity.Blocker, message);

    private static ConsoleFieldError Error(string key, string code, string message) =>
        new(key, code, ConsoleValidationSeverity.Error, message);
}
