using System.Security.Claims;

namespace Honua.Console.Web.Runtime;

internal static class ConsolePrincipalExtensions
{
    internal static string? AccountId(this ClaimsPrincipal principal) =>
        principal.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? principal.FindFirst("sub")?.Value;
}
