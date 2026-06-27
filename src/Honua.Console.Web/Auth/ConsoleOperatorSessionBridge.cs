using System.Security.Claims;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Security;
using Honua.Console.Shell.Services;

namespace Honua.Console.Web.Auth;

/// <summary>
/// Bridges the host's authenticated operator identity (<see cref="ClaimsPrincipal"/>) into the
/// Console's profile/session model so the existing request-time server bindings forward the operator
/// (honua-console#233/#234). On sign-in it:
/// <list type="bullet">
/// <item>ensures the active environment profile is bound to a non-anonymous operator account; and</item>
/// <item>writes an account session whose access token is the operator's forwarded bearer when one was
/// supplied (real per-principal RBAC on honua-server), or a non-forwardable session sentinel
/// otherwise (signed in for read context; server calls use the admin-key fallback).</item>
/// </list>
/// When no environment profile is active (browser host first-run) there is nothing to bridge; the
/// operator is still authenticated for routing and Family-A/B surfaces render their missing-binding
/// state until an environment is connected.
/// </summary>
public sealed class ConsoleOperatorSessionBridge
{
    private readonly IConsoleEnvironmentProfileStore _profiles;
    private readonly IConsoleAccountSessionStore _sessions;

    public ConsoleOperatorSessionBridge(
        IConsoleEnvironmentProfileStore profiles,
        IConsoleAccountSessionStore sessions)
    {
        _profiles = profiles ?? throw new ArgumentNullException(nameof(profiles));
        _sessions = sessions ?? throw new ArgumentNullException(nameof(sessions));
    }

    public async Task SyncAsync(ClaimsPrincipal principal, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(principal);

        var profile = await _profiles.GetActiveProfileAsync(cancellationToken).ConfigureAwait(false);
        if (profile is null)
        {
            return;
        }

        var accountId = principal.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? principal.Identity?.Name
            ?? "operator";
        var displayName = principal.FindFirst(ClaimTypes.Name)?.Value ?? accountId;
        var tenantId = principal.FindFirst(ConsoleAuthConstants.OperatorTenantClaim)?.Value ?? profile.TenantId;
        var bearer = principal.FindFirst(ConsoleAuthConstants.OperatorBearerClaim)?.Value;

        // Bind the profile to the operator account so the binding handler treats it as authenticated
        // (non-anonymous) and forwards the operator credential.
        if (profile.Account.AuthMode == ConsoleAccountAuthMode.Anonymous
            || !string.Equals(profile.Account.AccountId, accountId, StringComparison.Ordinal))
        {
            var updated = profile with
            {
                Account = profile.Account with
                {
                    AuthMode = ConsoleAccountAuthMode.AccountRbac,
                    AccountId = accountId,
                    DisplayName = displayName,
                    TenantId = tenantId
                }
            };
            await _profiles.UpsertProfileAsync(updated, cancellationToken).ConfigureAwait(false);
            await _profiles.ActivateProfileAsync(updated.Id, cancellationToken).ConfigureAwait(false);
        }

        var accessToken = string.IsNullOrWhiteSpace(bearer)
            ? ConsoleAuthConstants.SessionSentinelPrefix + profile.Id
            : bearer;

        await _sessions.SaveSessionAsync(new ConsoleAccountSession
        {
            ProfileId = profile.Id,
            AccountId = accountId,
            DisplayName = displayName,
            TenantId = tenantId,
            AccessToken = accessToken
        }, cancellationToken).ConfigureAwait(false);
    }
}
