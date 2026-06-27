using System.Security.Claims;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Security;
using Honua.Console.Shell.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;

namespace Honua.Console.Web.Auth;

/// <summary>
/// Console operator authentication for the browser host (honua-console#233).
///
/// CHOSEN MODEL — Option A (interim) with an Option-C-ready forwarding path. honua-server today
/// authenticates operators with OIDC (cookie-session bound to the server's own admin origin,
/// <c>AdminAuthEndpoints</c>) and mints forwardable bearers only through the ArcGIS-shaped Portal
/// OAuth2 bridge — it does not yet expose a console-consumable "log operator in → forwardable
/// operator bearer" endpoint. Until it does (the honua-server follow-up for full Option C), the
/// Console authenticates the operator at its own edge and forwards the operator identity/bearer to
/// honua-server:
/// <list type="bullet">
/// <item><b>EdgeForwarded</b> — the deployment fronts the Console with an ingress/oauth2-proxy that
/// authenticates against the customer IdP and injects signed forwarded-identity headers (and,
/// ideally, <c>X-Forwarded-Access-Token</c>). The Console trusts those headers only from the
/// configured proxy and forwards the operator's bearer to honua-server, where per-principal RBAC is
/// enforced for real.</item>
/// <item><b>Dev</b> — a documented developer login (Development only, or an explicit
/// <c>Honua:Console:Auth:Mode=Dev</c> override) so local work is not blocked. The dev operator's
/// server calls fall back to the configured shared admin key.</item>
/// </list>
/// In every other case authentication is <b>fail-closed</b>: there is no anonymous access. The
/// <see cref="AuthorizationOptions.FallbackPolicy"/> requires an authenticated operator on every
/// endpoint that does not opt out with <see cref="Microsoft.AspNetCore.Authorization.AllowAnonymousAttribute"/>.
/// </summary>
public static class ConsoleAuthentication
{
    public static WebApplicationBuilder AddConsoleAuthentication(this WebApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services
            .AddAuthentication(ConsoleAuthConstants.CookieScheme)
            .AddCookie(ConsoleAuthConstants.CookieScheme, options =>
            {
                options.Cookie.Name = "honua_console_auth";
                options.Cookie.HttpOnly = true;
                options.Cookie.SameSite = SameSiteMode.Lax;
                options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
                options.LoginPath = "/auth/login";
                options.LogoutPath = "/auth/logout";
                options.SlidingExpiration = true;
                options.ExpireTimeSpan = TimeSpan.FromHours(8);
                // For XHR/fetch (e.g. the map-proxy) return 401 instead of a 302 to the login page so
                // the browser can react rather than receiving HTML.
                options.Events.OnRedirectToLogin = context =>
                {
                    if (IsApiRequest(context.Request))
                    {
                        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                        return Task.CompletedTask;
                    }

                    context.Response.Redirect(context.RedirectUri);
                    return Task.CompletedTask;
                };
            });

        // Fail-closed: every endpoint requires an authenticated operator unless it explicitly allows
        // anonymous access (the auth endpoints, version.json, static assets, error pages).
        builder.Services.AddAuthorization(options =>
        {
            options.FallbackPolicy = new AuthorizationPolicyBuilder()
                .RequireAuthenticatedUser()
                .Build();
        });

        // Flow HttpContext.User into the interactive Razor components so AuthorizeRouteView resolves.
        builder.Services.AddCascadingAuthenticationState();

        builder.Services.AddSingleton<ConsoleOperatorSessionBridge>();
        builder.Services.AddSingleton<IConfigureOptions<ConsoleEdgeAuthOptions>, ConsoleEdgeAuthOptionsSetup>();

        return builder;
    }

    /// <summary>
    /// Inserts authentication + the trusted edge-forwarded-identity middleware, then authorization.
    /// Call after <c>UseRouting</c>/<c>MapStaticAssets</c> wiring and before endpoint execution.
    /// </summary>
    public static WebApplication UseConsoleAuthentication(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        app.UseAuthentication();
        app.UseMiddleware<ConsoleEdgeIdentityMiddleware>();
        app.UseAuthorization();
        return app;
    }

    /// <summary>Maps the anonymous sign-in / sign-out endpoints (the cookie LoginPath target).</summary>
    public static WebApplication MapConsoleAuthEndpoints(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        app.MapGet("/auth/login", async (
            HttpContext context,
            ConsoleOperatorSessionBridge bridge,
            CancellationToken cancellationToken) =>
        {
            var returnTo = SanitizeReturnTo(context.Request.Query["returnTo"]);

            // Already authenticated (cookie or edge): just (re)sync the operator session and continue.
            if (context.User?.Identity?.IsAuthenticated == true)
            {
                await bridge.SyncAsync(context.User, cancellationToken).ConfigureAwait(false);
                return Results.Redirect(returnTo);
            }

            if (DevLoginAllowed(app))
            {
                var principal = BuildDevPrincipal();
                await context.SignInAsync(ConsoleAuthConstants.CookieScheme, principal, new AuthenticationProperties
                {
                    IsPersistent = true,
                    ExpiresUtc = DateTimeOffset.UtcNow.AddHours(8)
                }).ConfigureAwait(false);
                await bridge.SyncAsync(principal, cancellationToken).ConfigureAwait(false);
                return Results.Redirect(returnTo);
            }

            // Fail-closed: no anonymous access and no local sign-in available. Operators authenticate
            // through the deployment's edge IdP (oauth2-proxy / ingress OIDC), which must inject the
            // configured forwarded-identity headers.
            return Results.Problem(
                title: "Sign-in required",
                detail: "This Console requires an operator identity from the deployment's edge identity provider. "
                    + "No forwarded operator identity was present on this request.",
                statusCode: StatusCodes.Status401Unauthorized);
        }).AllowAnonymous();

        app.MapPost("/auth/logout", async (HttpContext context) =>
        {
            await context.SignOutAsync(ConsoleAuthConstants.CookieScheme).ConfigureAwait(false);
            return Results.Redirect("/auth/login");
        }).AllowAnonymous();

        return app;
    }

    // Dev login is allowed in the Development environment, or when an operator has explicitly opted in
    // with Honua:Console:Auth:Mode=Dev (documented as insecure for production).
    private static bool DevLoginAllowed(WebApplication app)
    {
        if (app.Environment.IsDevelopment())
        {
            return true;
        }

        var mode = app.Configuration[ConsoleAuthConstants.AuthModeKey];
        return string.Equals(mode, "Dev", StringComparison.OrdinalIgnoreCase);
    }

    private static ClaimsPrincipal BuildDevPrincipal()
    {
        var identity = new ClaimsIdentity(
            [
                new Claim(ClaimTypes.NameIdentifier, "dev-operator"),
                new Claim(ClaimTypes.Name, "Developer"),
            ],
            ConsoleAuthConstants.CookieScheme);
        // No operator bearer: dev server calls use the admin-key fallback.
        return new ClaimsPrincipal(identity);
    }

    private static bool IsApiRequest(HttpRequest request)
    {
        if (request.Path.StartsWithSegments("/map-proxy", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var requestedWith = request.Headers["X-Requested-With"];
        if (requestedWith.Count > 0 && string.Equals(requestedWith[0], "XMLHttpRequest", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var accept = request.Headers.Accept;
        return accept.Count > 0 && accept[0] is { } value && value.Contains("application/json", StringComparison.OrdinalIgnoreCase)
            && !value.Contains("text/html", StringComparison.OrdinalIgnoreCase);
    }

    internal static string SanitizeReturnTo(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)
            || !value.StartsWith('/')
            || value.StartsWith("//", StringComparison.Ordinal)
            || value.Contains("://", StringComparison.Ordinal))
        {
            return "/";
        }

        return value;
    }
}
