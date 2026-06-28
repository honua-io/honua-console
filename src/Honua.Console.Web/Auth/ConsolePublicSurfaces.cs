using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;

namespace Honua.Console.Web.Auth;

/// <summary>
/// The legitimately-anonymous public Console surfaces (honua-console#233 S1 #2). The Share product area
/// exposes public embeds, public-link / open-data landing pages that customers publish for the open web;
/// they are NOT operator surfaces and must render for anonymous visitors. The Console is fail-closed by
/// default (every other route requires an authenticated operator), so these prefixes are the explicit,
/// audited allowlist of routes that opt out of the RequireAuthenticatedUser fallback. Per-item public
/// access is still enforced inside the surface (e.g. <c>AuthorizeEmbedAsync</c> / the open-data "public"
/// projection) — the allowlist only governs whether the route renders anonymously at all.
/// </summary>
public static class ConsolePublicSurfaces
{
    public static readonly IReadOnlyList<string> AnonymousPathPrefixes =
    [
        "/embed/maps",          // public map embeds (EmbedMapPage)
        "/share/public",        // public-link / open-data hub + items (SharePage, SharePublicItemPage)
        "/public",              // public-link / open-data short routes (SharePage, SharePublicItemPage)
    ];

    /// <summary>True when the request path targets a legitimately-anonymous public Console surface.</summary>
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

/// <summary>
/// Fallback-policy requirement that keeps the Console fail-closed (an authenticated operator is required)
/// while letting the explicitly allow-listed public surfaces in <see cref="ConsolePublicSurfaces"/>
/// render anonymously. Replaces a bare <c>RequireAuthenticatedUser</c> fallback, which blocked every
/// public embed / public-link / open-data page (honua-console#233 S1 #2).
/// </summary>
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
