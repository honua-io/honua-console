using System.Security.Claims;

namespace Honua.Console.Web.Auth;

// An instance exists only for an authenticated principal, so a missing operator is null rather than a shared anonymous identity.
public sealed class ConsoleOperatorIdentity
{
    private ConsoleOperatorIdentity(string key, string? accountId, string? displayName)
    {
        Key = key;
        AccountId = accountId;
        DisplayName = displayName;
    }

    public string Key { get; }

    public string? AccountId { get; }

    public string? DisplayName { get; }

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
