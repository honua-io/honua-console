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
/// supplied (real per-principal RBAC on honua-server); otherwise, because this runs per request, it
/// preserves an existing forwardable bearer the operator obtained out-of-band through the server-session
/// BFF (honua-console#306) rather than erasing it, and falls back to a non-forwardable session sentinel
/// only when no forwardable bearer exists (signed in for read context; human mutations require
/// exchange/reauthentication).</item>
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

        // SyncAsync runs on the sign-in HTTP request (the cookie/edge /auth/login path), where the operator
        // is authenticated on HttpContext.User. The operator-partitioned stores resolve their partition key
        // from HttpContext.User on that thread, so writes here land in THIS operator's partition without any
        // ambient plumbing. Interactive-circuit reads partition via the circuit's authentication state
        // (CircuitOperatorContextHandler), not this thread's execution context — which does not flow to the
        // circuit (honua-console#256).

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
        if (profile.Account.AuthMode != ConsoleAccountAuthMode.AccountRbac
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

        // Which credential wins on this sign-in/identity request:
        //  1. A principal-supplied bearer (an edge X-Forwarded-Access-Token, or the cookie operator's
        //     forwarded bearer) is the edge/IdP-owned credential and always takes precedence.
        //  2. Otherwise the identity source manages the operator's identity but NOT their server
        //     credentials (an edge that forwards identity headers only). SyncAsync runs per request, so
        //     erasing the session here would wipe a bearer the operator obtained out-of-band via the
        //     server-session BFF (/auth/server/login -> /admin/auth/callback) on the very next request
        //     (honua-console#306). Preserve an existing forwardable (BFF-exchanged) bearer and its expiry;
        //     the bearer provider still enforces expiry and re-exchange downstream.
        //  3. With neither a forwarded bearer nor a preserved one, write the non-forwardable session
        //     sentinel: signed in for read context, human mutations require exchange/reauthentication.
        string accessToken;
        DateTimeOffset? accessTokenExpiresAt = null;
        if (!string.IsNullOrWhiteSpace(bearer))
        {
            accessToken = bearer;
        }
        else
        {
            var existing = await _sessions.GetSessionAsync(profile.Id, cancellationToken).ConfigureAwait(false);
            if (existing is not null && !ConsoleAuthConstants.IsSessionSentinel(existing.AccessToken)
                && !string.IsNullOrWhiteSpace(existing.AccessToken))
            {
                accessToken = existing.AccessToken;
                accessTokenExpiresAt = existing.AccessTokenExpiresAt;
            }
            else
            {
                accessToken = ConsoleAuthConstants.SessionSentinelPrefix + profile.Id;
            }
        }

        await _sessions.SaveSessionAsync(new ConsoleAccountSession
        {
            ProfileId = profile.Id,
            AccountId = accountId,
            DisplayName = displayName,
            TenantId = tenantId,
            AccessToken = accessToken,
            AccessTokenExpiresAt = accessTokenExpiresAt
        }, cancellationToken).ConfigureAwait(false);
    }
}
