namespace Honua.Console.Shell.Models;

/// <summary>
/// Tenancy helpers for environment profiles. Honua 2026.1 GA is single-tenant; a tenant-scoped
/// profile is a Preview/trial context only.
/// </summary>
public static class ConsoleEnvironmentProfileTenancy
{
    /// <summary>
    /// The tenant a session on this profile actually binds to. An edge/IdP-supplied operator tenant is
    /// stored on the account binding and takes precedence over the profile-level tenant, matching
    /// sign-in and the operator session bridge.
    /// </summary>
    public static string EffectiveTenantId(this ConsoleEnvironmentProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        return string.IsNullOrWhiteSpace(profile.Account.TenantId) ? profile.TenantId : profile.Account.TenantId;
    }

    /// <summary>True when the profile (or its bound account) is tenant-scoped, i.e. a Preview/trial context.</summary>
    public static bool IsTenantScoped(this ConsoleEnvironmentProfile profile) =>
        !string.IsNullOrWhiteSpace(profile.EffectiveTenantId());
}
