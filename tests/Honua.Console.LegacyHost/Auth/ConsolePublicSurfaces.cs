using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;

namespace Honua.Console.Web.Auth;

// These prefixes are anonymous exceptions to the fallback policy; explicit endpoint metadata still applies, and each item is authorized inside the surface.
public static class ConsolePublicSurfaces
{
    public static readonly IReadOnlyList<string> AnonymousPathPrefixes =
    [
        "/embed/maps",          // public map embeds (EmbedMapPage)
        "/share/public",        // public-link / open-data hub + items (SharePage, SharePublicItemPage)
        "/public",              // public-link / open-data short routes (SharePage, SharePublicItemPage)
    ];

    public static bool IsPublicSurface(PathString path)
    {
        foreach (var prefix in AnonymousPathPrefixes)
        {
            if (path.StartsWithSegments(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}

public sealed class ConsoleAuthenticatedOrPublicRequirement : IAuthorizationRequirement;

public sealed class ConsoleAuthenticatedOrPublicHandler
    : AuthorizationHandler<ConsoleAuthenticatedOrPublicRequirement>
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        ConsoleAuthenticatedOrPublicRequirement requirement)
    {
        if (context.User?.Identity?.IsAuthenticated == true)
        {
            context.Succeed(requirement);
            return Task.CompletedTask;
        }

        // Anonymous: allow only the explicitly allow-listed public surfaces; everything else stays
        // fail-closed and is challenged to sign in.
        if (context.Resource is HttpContext httpContext
            && ConsolePublicSurfaces.IsPublicSurface(httpContext.Request.Path))
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }
}
