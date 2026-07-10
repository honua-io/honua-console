using System.Security.Claims;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Security;
using Honua.Console.Shell.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection.Extensions;
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
/// <c>Honua:Console:Auth:Mode=Dev</c> override) so local reads are not blocked. Human approval
/// and recovery mutations still require a forwardable server bearer and fail closed otherwise.</item>
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
                // The framework's automatic challenge redirect and the rest of the Console
                // (RedirectToLogin, the sign-in pages, the /auth/login endpoint) must agree on the
                // return-url query key. The Console uses "returnTo" everywhere, so override the cookie
                // middleware's default ("ReturnUrl") — otherwise a challenged deep link is dropped and
                // the operator lands on "/" after sign-in instead of the page they requested.
                options.ReturnUrlParameter = "returnTo";
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
        // anonymous access (the auth endpoints, version.json, static assets, error pages) OR it targets a
        // legitimately-anonymous PUBLIC surface — the Share embeds / public-link / open-data pages, which
        // customers publish for the open web (honua-console#233 S1 #2). A bare RequireAuthenticatedUser
        // fallback blocked those public surfaces; the requirement below keeps fail-closed for operator
        // routes while allow-listing the audited public path prefixes (ConsolePublicSurfaces). Per-page
        // authorization in ConsoleRoutes is the defense-in-depth layer for the interactive render path.
        builder.Services.AddSingleton<IAuthorizationHandler, ConsoleAuthenticatedOrPublicHandler>();
        builder.Services.AddAuthorization(options =>
        {
            options.FallbackPolicy = new AuthorizationPolicyBuilder()
                .AddRequirements(new ConsoleAuthenticatedOrPublicRequirement())
                .Build();
        });

        // Flow HttpContext.User into the interactive Razor components so AuthorizeRouteView resolves.
        builder.Services.AddCascadingAuthenticationState();

        // The browser Console host serves MANY operators from one process. The profile/session stores
        // registered by AddHonuaConsoleShell are process-wide singletons, so one operator's active
        // profile/account-binding/forwarded bearer would bleed into another operator's requests
        // (honua-console#233 S1 #1). Partition that per-operator state by the authenticated operator:
        //   - IHttpContextAccessor + the operator context resolve the current operator's partition key;
        //   - the operator-scoped stores keep the singleton registration (the singleton honua-server
        //     clients resolve them at construction) but route every read/write into the current
        //     operator's own backing store, so no operator can ever observe another's profile/bearer.
        // The native single-operator host (one operator per process) keeps its persistent Json-backed
        // singletons untouched — partitioning is wired only here in the browser Web host.
        builder.Services.AddHttpContextAccessor();
        builder.Services.TryAddSingleton<IConsoleOperatorContext, ConsoleOperatorContext>();

        // Fail-closed-by-construction operator accessor (honua-console#254). Unlike the singleton
        // IConsoleOperatorContext + process-static ambient key (which represents "no operator" as the
        // shared __anonymous__ sentinel and so can silently fail open), this is a per-circuit/per-request
        // SCOPED service that reads the scope's OWN authoritative identity (HttpContext.User for requests,
        // the circuit AuthenticationStateProvider for interactive circuits). Server-bound call sites take
        // it explicitly and treat an unresolved operator as a hard deny — there is no ambient fallback.
        // The map-proxy BFF endpoints (the highest-risk "acts with honua-server privileges" surface) are
        // converted to this seam; the broad typed-client surface is tracked as honua-console#254 follow-up.
        builder.Services.AddScoped<IConsoleOperatorScope, ConsoleOperatorScope>();

        // Blazor Server interactive rendering has NO HttpContext: component code and the singleton
        // honua-server clients' binding handler run on the circuit, so the operator key cannot be resolved
        // from HttpContext.User there (honua-console#256). This circuit handler reads the circuit's
        // AuthenticationStateProvider and sets the circuit operator key as the ambient for every inbound
        // circuit activity, so interactive-circuit reads/writes partition to the authenticated operator
        // instead of collapsing to the shared anonymous partition (the #252 regression).
        builder.Services.AddScoped<
            Microsoft.AspNetCore.Components.Server.Circuits.CircuitHandler,
            CircuitOperatorContextHandler>();

        // honua-console#254: route the Family-A server-bound typed clients through IHttpClientFactory named
        // clients whose handler chain fails closed for an unresolved operator on the privileged path (and
        // keeps the /public + /ogc/styles surfaces anonymous by design). The typed-client registrations in
        // AddHonuaConsoleShell obtain their HttpClient from the IHonuaServerBoundClientFactory registered
        // here instead of building a self-contained per-client pooled handler; resolution is lazy, so this
        // running after AddHonuaConsoleShell is fine. The native single-operator host does not register the
        // factory and keeps the self-contained pooled client.
        builder.Services.AddConsoleServerBoundClients();

        // Development testbed convenience: the browser host cannot create environment profiles (profile
        // creation runs on the native host), so seed + activate one from the configured server URL for
        // EACH operator's partition on first use. Mirrors the prior startup seed, but per-operator so it
        // survives partitioning (and never seeds outside Development).
        var browserSeed = BuildBrowserDevSeed(builder);
        builder.Services.Replace(ServiceDescriptor.Singleton<IConsoleEnvironmentProfileStore>(serviceProvider =>
            new OperatorScopedEnvironmentProfileStore(
                serviceProvider.GetRequiredService<IConsoleOperatorContext>(),
                browserSeed)));
        builder.Services.Replace(ServiceDescriptor.Singleton<IConsoleAccountSessionStore>(serviceProvider =>
            new OperatorScopedAccountSessionStore(
                serviceProvider.GetRequiredService<IConsoleOperatorContext>())));

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
                // SignInAsync only writes the auth cookie to the RESPONSE; it does not re-authenticate
                // HttpContext.User for THIS request. The operator-partitioned profile write below
                // (bridge.SyncAsync -> OperatorScopedEnvironmentProfileStore.CurrentForWrite ->
                // IConsoleOperatorContext.RequireOperatorKey) resolves the operator from HttpContext.User
                // on this same request and fails closed with ConsoleOperatorContextUnresolvedException when
                // it is still anonymous. Set the principal on the request so the operator is resolvable now
                // (honua-console#256; the cookie/edge branches already arrive with User populated).
                context.User = principal;
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

    // Builds the per-operator dev-seed factory: in Development with a configured server URL, each new
    // operator partition starts with one active "Local honua-server" profile so a browser-only testbed
    // binds without the native host. Outside Development (or with no server URL) partitions start empty.
    private static Func<InMemoryConsoleEnvironmentProfileStore>? BuildBrowserDevSeed(WebApplicationBuilder builder)
    {
        if (!builder.Environment.IsDevelopment())
        {
            return null;
        }

        var seedUrl = builder.Configuration["Honua:Server:BaseUrl"]
            ?? builder.Configuration["HONUA_SERVER_BASE_URL"];
        if (string.IsNullOrWhiteSpace(seedUrl) || !Uri.TryCreate(seedUrl, UriKind.Absolute, out var seedUri))
        {
            return null;
        }

        return () =>
        {
            var devProfile = new ConsoleEnvironmentProfile
            {
                Id = "local-dev",
                DisplayName = "Local honua-server",
                ServerBaseUri = seedUri,
                EnvironmentKind = "development",
                Account = new ConsoleAccountBinding
                {
                    AuthMode = ConsoleAccountAuthMode.AccountRbac,
                    AccountId = "console-user",
                    DisplayName = "Console User",
                },
            };
            return new InMemoryConsoleEnvironmentProfileStore([devProfile], activeProfileId: devProfile.Id);
        };
    }

    private static ClaimsPrincipal BuildDevPrincipal()
    {
        var identity = new ClaimsIdentity(
            [
                new Claim(ClaimTypes.NameIdentifier, "dev-operator"),
                new Claim(ClaimTypes.Name, "Developer"),
            ],
            ConsoleAuthConstants.CookieScheme);
        // No operator bearer: reads may use their documented fallback, while human
        // approval/recovery mutations fail closed until a bearer is available.
        return new ClaimsPrincipal(identity);
    }

    private static bool IsApiRequest(HttpRequest request)
    {
        // Note: /map-proxy/ paths intentionally use the standard cookie-redirect flow (not the
        // 401-direct path) so that the dev auto-login can round-trip back to the original map-proxy
        // URL via the returnTo parameter. In production the user is already signed in before any
        // page with a map is rendered, so the redirect case is rare and handles correctly.

        var requestedWith = request.Headers["X-Requested-With"];
        if (requestedWith.Count > 0 && string.Equals(requestedWith[0], "XMLHttpRequest", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var accept = request.Headers.Accept;
        return accept.Count > 0 && accept[0] is { } value && value.Contains("application/json", StringComparison.OrdinalIgnoreCase)
            && !value.Contains("text/html", StringComparison.OrdinalIgnoreCase);
    }

    internal static string SanitizeReturnTo(string? value) => ConsoleReturnUrl.Sanitize(value);
}
