using System.Reflection;
using System.Text.RegularExpressions;
using Honua.Console.Web.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Http;

namespace Honua.Console.IntegrationTests;

/// <summary>
/// Deny-by-default route authorization audit (honua-console#254).
///
/// The Console is fail-closed by construction at the HTTP host layer: the
/// <see cref="ConsoleAuthenticatedOrPublicHandler"/> fallback policy requires an authenticated
/// operator for every route except the audited public-path-prefix allow-list in
/// <see cref="ConsolePublicSurfaces"/>. The interactive Blazor render path has a SECOND gate
/// (<c>AuthorizeRouteView</c>), which only opts a page out of the fallback when the page component
/// declares <see cref="AllowAnonymousAttribute"/>.
///
/// These two layers can silently drift, which is the recurring fail-open class this issue tracks:
/// <list type="bullet">
/// <item>A new <c>@page</c> route placed under an allow-listed prefix becomes anonymous at the host
/// layer without anyone explicitly deciding it should be public.</item>
/// <item>A page that is anonymous at the host layer but is missing <c>[AllowAnonymous]</c> renders
/// inconsistently (the host serves it anonymously, then <c>AuthorizeRouteView</c> redirects the
/// anonymous visitor to sign-in) — which is exactly how the public <c>/public</c> open-data hub
/// regressed before this audit.</item>
/// </list>
///
/// This audit reflects over every routable Console component and enforces both invariants by
/// construction, so the public surface cannot expand or drift without updating the reviewed
/// inventory in this test.
/// </summary>
public sealed class ConsoleRouteAuthorizationAuditTests
{
    // The reviewed, explicit inventory of route templates that are reachable anonymously at the HTTP
    // host layer (i.e. match a ConsolePublicSurfaces prefix). Adding a new @page under an allow-listed
    // prefix — or adding a new prefix — changes the computed set and fails this test until the new
    // public route is reviewed and added here. This is the deny-by-default audit: public access must
    // be an explicit, listed decision, never an accident of prefix matching.
    private static readonly IReadOnlySet<string> ExpectedAnonymousRouteTemplates = new HashSet<string>(StringComparer.Ordinal)
    {
        "/embed/maps/{MapId}",          // EmbedMapPage — public map embeds
        "/share/public",                // SharePage — public-link / open-data hub
        "/public",                      // SharePage — public-link / open-data hub (short route)
        "/share/public/items/{IdOrSlug}", // SharePublicItemPage — public open-data item
        "/public/items/{IdOrSlug}",     // SharePublicItemPage — public open-data item (short route)
    };

    [Fact]
    public void DevelopmentOnlyManualRoutesRemainAuthenticated()
    {
        var demoPage = RoutableComponents().Single(component =>
            component.Templates.Contains("/studio/_dev/unsaved-changes-demo", StringComparer.Ordinal));

        Assert.False(demoPage.AllowsAnonymous);
    }

    [Fact]
    public void HostAnonymousRoutes_ExactlyMatchReviewedInventory()
    {
        var anonymousTemplates = RoutableComponents()
            .SelectMany(component => component.Templates)
            .Where(template => ConsolePublicSurfaces.IsPublicSurface(ToProbePath(template)))
            .ToHashSet(StringComparer.Ordinal);

        var unexpected = anonymousTemplates.Except(ExpectedAnonymousRouteTemplates).Order().ToArray();
        var missing = ExpectedAnonymousRouteTemplates.Except(anonymousTemplates).Order().ToArray();

        Assert.True(
            unexpected.Length == 0,
            "New Console route(s) are reachable anonymously at the host layer but are NOT in the reviewed "
            + "public inventory. Confirm they are intended to be public, declare [AllowAnonymous] on the page, "
            + "and add them to ExpectedAnonymousRouteTemplates: " + string.Join(", ", unexpected));

        Assert.True(
            missing.Length == 0,
            "Route(s) in the reviewed public inventory are no longer reachable anonymously at the host layer "
            + "(an allow-list prefix or a @page route was removed/renamed). Update ExpectedAnonymousRouteTemplates: "
            + string.Join(", ", missing));
    }

    [Fact]
    public void EveryHostAnonymousPage_DeclaresAllowAnonymous()
    {
        var violations = RoutableComponents()
            .Where(component => component.Templates.Any(t => ConsolePublicSurfaces.IsPublicSurface(ToProbePath(t))))
            .Where(component => !component.AllowsAnonymous)
            .Select(component => component.Type.FullName)
            .Order()
            .ToArray();

        Assert.True(
            violations.Length == 0,
            "Page component(s) are reachable anonymously at the host layer but do not declare "
            + "[AllowAnonymous], so AuthorizeRouteView will redirect anonymous visitors to sign-in "
            + "(inconsistent public surface). Add '@attribute [Microsoft.AspNetCore.Authorization.AllowAnonymous]' "
            + "to: " + string.Join(", ", violations));
    }

    [Fact]
    public void AllowAnonymousPages_AreOnlyTheReviewedPublicPages()
    {
        // The reverse guard: an [AllowAnonymous] page whose routes are NOT host-allow-listed would be a
        // page that intends to be anonymous but is still blocked by the host fallback (fail-closed by
        // accident) — or, worse, a page someone opened up without going through ConsolePublicSurfaces.
        // Every [AllowAnonymous] routable page must have at least one host-anonymous route.
        var violations = RoutableComponents()
            .Where(component => component.AllowsAnonymous)
            .Where(component => !component.Templates.Any(t => ConsolePublicSurfaces.IsPublicSurface(ToProbePath(t))))
            .Select(component => component.Type.FullName)
            .Order()
            .ToArray();

        Assert.True(
            violations.Length == 0,
            "Page component(s) declare [AllowAnonymous] but have no route allow-listed in "
            + "ConsolePublicSurfaces, so the host fallback still blocks them (the page-level intent is "
            + "silently overridden). Reconcile the page routes with ConsolePublicSurfaces: "
            + string.Join(", ", violations));
    }

    private sealed record RoutableComponent(Type Type, IReadOnlyList<string> Templates, bool AllowsAnonymous);

    private static IEnumerable<RoutableComponent> RoutableComponents()
    {
        var shellAssembly = typeof(Honua.Console.Shell.ConsoleRoutes).Assembly;
        foreach (var type in shellAssembly.GetTypes())
        {
            if (!typeof(IComponent).IsAssignableFrom(type))
            {
                continue;
            }

            var templates = type
                .GetCustomAttributes<RouteAttribute>(inherit: false)
                .Select(attribute => attribute.Template)
                .ToArray();

            if (templates.Length == 0)
            {
                continue;
            }

            var allowsAnonymous = type.GetCustomAttributes<AllowAnonymousAttribute>(inherit: true).Any();
            yield return new RoutableComponent(type, templates, allowsAnonymous);
        }
    }

    // Turns a route template into a concrete probe path by replacing every "{param}" /
    // "{param:constraint}" segment with a literal so PathString segment matching behaves like a real
    // request (e.g. "/public/items/{IdOrSlug}" -> "/public/items/x").
    private static PathString ToProbePath(string template)
    {
        var concrete = Regex.Replace(template, "{[^/]+}", "x");
        if (!concrete.StartsWith('/'))
        {
            concrete = "/" + concrete;
        }

        return new PathString(concrete);
    }
}
