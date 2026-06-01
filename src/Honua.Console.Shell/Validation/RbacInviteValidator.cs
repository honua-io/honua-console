using Honua.Console.Shell.Models;

namespace Honua.Console.Shell.Validation;

/// <summary>
/// Stable console-owned field keys for the RBAC scoped-invite drawer
/// (<c>OperateAccessMembersPage</c>). The client validator (<see cref="RbacInviteValidator"/>) and the
/// inline render surfaces share these so a finding lands on the offending input.
/// </summary>
public static class RbacInviteFieldKeys
{
    public const string Email = "rbac.invite.email";
    public const string Scope = "rbac.invite.scope";
    public const string IpAllowlist = "rbac.invite.ipAllowlist";
}

/// <summary>
/// Console-owned snapshot of the RBAC scoped-invite drawer the client validator evaluates. The page binds
/// its drawer inputs onto this and re-runs <see cref="RbacInviteValidator"/> on every edit. There is no
/// server body-validator for invite today (catalog "RBAC / share: no dedicated body validators"); this is
/// purely client-side form validation surfaced inline.
/// </summary>
/// <param name="Email">The invitee email / group identity (<c>InviteEmail</c>).</param>
/// <param name="ScopeDev">Whether the dev environment scope is selected.</param>
/// <param name="ScopeStaging">Whether the staging environment scope is selected.</param>
/// <param name="ScopeProd">Whether the prod environment scope is selected.</param>
/// <param name="IpAllowlist">The optional IP allowlist (one CIDR per line / comma-separated).</param>
public sealed record RbacInviteState(
    string? Email,
    bool ScopeDev,
    bool ScopeStaging,
    bool ScopeProd,
    string? IpAllowlist);

/// <summary>
/// Pure client-side validator for the RBAC scoped-invite drawer, mirroring the <see cref="StudioMapValidator"/>
/// pattern: it examines the console-owned <see cref="RbacInviteState"/> and emits field-addressable
/// <see cref="ConsoleFieldError"/> findings keyed by <see cref="RbacInviteFieldKeys"/>. It covers the rules the
/// catalog flags for the invite form: a valid invite email, at least one environment scope selected, and a valid
/// CIDR per IP-allowlist entry (the allowlist itself is optional). Console-only — no server body-validator exists.
/// </summary>
public sealed class RbacInviteValidator : IFieldValidator<RbacInviteState>
{
    /// <summary>Shared singleton; the validator holds no state.</summary>
    public static RbacInviteValidator Instance { get; } = new();

    /// <inheritdoc />
    public IReadOnlyList<ConsoleFieldError> Evaluate(RbacInviteState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        var errors = new List<ConsoleFieldError>();

        if (string.IsNullOrWhiteSpace(state.Email))
        {
            errors.Add(Blocker(RbacInviteFieldKeys.Email, "rbac.invite.email.required", "Enter an email or group to invite."));
        }
        else if (!EmailRule.IsValid(state.Email))
        {
            errors.Add(Error(RbacInviteFieldKeys.Email, "rbac.invite.email.format", "Enter a valid email address (name@example.gov)."));
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

    /// <summary>Splits the allowlist on commas, semicolons, and newlines, trimming each entry.</summary>
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
