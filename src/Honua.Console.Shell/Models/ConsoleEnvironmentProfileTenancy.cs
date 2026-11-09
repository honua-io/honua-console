namespace Honua.Console.Shell.Models;

public static class ConsoleEnvironmentProfileTenancy
{
    public static string EffectiveTenantId(this ConsoleEnvironmentProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        return string.IsNullOrWhiteSpace(profile.Account.TenantId) ? profile.TenantId : profile.Account.TenantId;
    }

    public static bool IsTenantScoped(this ConsoleEnvironmentProfile profile) =>
        !string.IsNullOrWhiteSpace(profile.EffectiveTenantId());
}
