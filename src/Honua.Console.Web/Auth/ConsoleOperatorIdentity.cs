using System.Security.Claims;

namespace Honua.Console.Web.Auth;

/// <summary>
/// A <b>resolved</b> Console operator identity (honua-console#254). This type exists to make
/// "no operator" structurally unrepresentable on the server-bound call path: an instance can only be
/// produced from a genuinely-authenticated <see cref="ClaimsPrincipal"/> via
/// <see cref="FromPrincipal"/>, and there is no public constructor, no "anonymous" instance, and no
/// mutable default. Server-bound callers therefore hold either a <see cref="ConsoleOperatorIdentity"/>
/// (a real operator, with a non-empty partition <see cref="Key"/>) or <see langword="null"/> — there is
/// no third "unresolved/ambient-fallback" state for the compiler to let slip through. Contrast the legacy
/// string partition key, whose <c>__anonymous__</c> sentinel silently represented BOTH a genuinely
/// anonymous surface and an authenticated operator whose context failed to resolve (the recurring
/// fail-open class this issue closes).
/// </summary>
public sealed class ConsoleOperatorIdentity
{
    private ConsoleOperatorIdentity(string key, string? accountId, string? displayName)
    {
        Key = key;
        AccountId = accountId;
        DisplayName = displayName;
    }

    /// <summary>
    /// The stable, non-empty per-operator partition key (the same key the operator-scoped stores use).
    /// Guaranteed non-empty for any instance — an instance cannot exist without a resolved operator.
    /// </summary>
    public string Key { get; }

    /// <summary>The operator's account identifier claim, when present.</summary>
    public string? AccountId { get; }

    /// <summary>The operator's display name claim, when present.</summary>
    public string? DisplayName { get; }

    /// <summary>
    /// Produces a resolved operator identity from an authenticated principal, or <see langword="null"/>
    /// when the principal is unauthenticated / carries no stable identifier. Returning <c>null</c> (rather
    /// than a shared "anonymous" instance) is what forces callers to fail closed: there is no operator
    /// value to act with.
    /// </summary>
    public static ConsoleOperatorIdentity? FromPrincipal(ClaimsPrincipal? principal)
    {
        var key = ConsoleOperatorContext.ResolveKey(principal);
        if (key is null)
        {
            return null;
        }

        var accountId = principal!.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        var displayName = principal.FindFirst(ClaimTypes.Name)?.Value ?? principal.Identity?.Name;
        return new ConsoleOperatorIdentity(key, accountId, displayName);
    }
}
