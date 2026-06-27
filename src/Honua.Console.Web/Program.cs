using Honua.Console.Shell.DependencyInjection;
using Honua.Console.Web;
using Honua.Console.Web.Auth;
using Honua.Console.Web.Components;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorComponents()
    // Detailed circuit errors leak exception messages and stack traces to the browser, so only enable
    // them in Development; production circuits surface a generic error and log the detail server-side.
    .AddInteractiveServerComponents(options => options.DetailedErrors = builder.Environment.IsDevelopment());

// NL->workflow generation against a local CPU model can hold a circuit for minutes and
// return a graph payload larger than SignalR's 32 KB default receive cap. Raise the cap and
// the client timeout so a slow-but-valid generation renders instead of terminating the circuit.
builder.Services.Configure<Microsoft.AspNetCore.SignalR.HubOptions>(options =>
{
    options.MaximumReceiveMessageSize = 10 * 1024 * 1024; // 10 MB
    options.ClientTimeoutInterval = TimeSpan.FromMinutes(5);
    options.KeepAliveInterval = TimeSpan.FromSeconds(15);
});
builder.Services.AddHttpClient("honua-map-proxy");
builder.Services.AddHonuaConsoleShell(
    builder.Configuration["Honua:Server:BaseUrl"] ?? builder.Configuration["HONUA_SERVER_BASE_URL"],
    builder.Configuration["Honua:Server:AdminApiKey"] ?? builder.Configuration["HONUA_ADMIN_API_KEY"],
    builder.Configuration["Honua:Server:PublicationIds"] ?? builder.Configuration["HONUA_SERVER_PUBLICATION_IDS"],
    builder.Configuration["Honua:Server:TemporalSources"] ?? builder.Configuration["HONUA_SERVER_TEMPORAL_SOURCES"],
    builder.Configuration["Honua:Support:BaseUrl"] ?? builder.Configuration["HONUA_SUPPORT_BASE_URL"],
    // L0 deflection (honua-console#165): qwen assistant endpoint + bundled KB path.
    builder.Configuration["Honua:Llm:BaseUrl"] ?? builder.Configuration["HONUA_LLM_BASE_URL"],
    builder.Configuration["Honua:Llm:Model"] ?? builder.Configuration["HONUA_LLM_MODEL"],
    builder.Configuration["Honua:Llm:ApiKey"] ?? builder.Configuration["HONUA_LLM_API_KEY"],
    builder.Configuration["Honua:Support:KbPath"] ?? builder.Configuration["HONUA_SUPPORT_KB_PATH"]);

// Operator authentication (honua-console#233): fail-closed cookie/edge auth + RequireAuthenticatedUser
// fallback policy so no Console route is reachable anonymously. See ConsoleAuthentication for the model.
builder.AddConsoleAuthentication();

var app = builder.Build();

// Development testbed convenience: the browser host cannot create environment profiles
// (profile creation runs on the native MAUI host), so seed + activate one from
// HONUA_SERVER_BASE_URL when the in-memory profile store is empty. This lets a browser-only
// testbed bind to a running honua-server without the native host. Never runs outside Development.
if (app.Environment.IsDevelopment())
{
    var seedUrl = app.Configuration["Honua:Server:BaseUrl"]
        ?? Environment.GetEnvironmentVariable("HONUA_SERVER_BASE_URL");
    if (!string.IsNullOrWhiteSpace(seedUrl) && Uri.TryCreate(seedUrl, UriKind.Absolute, out var seedUri))
    {
        using var seedScope = app.Services.CreateScope();
        var profileStore = seedScope.ServiceProvider
            .GetRequiredService<Honua.Console.Shell.Services.IConsoleEnvironmentProfileStore>();
        if ((await profileStore.ListProfilesAsync()).Count == 0)
        {
            var devProfile = new Honua.Console.Shell.Models.ConsoleEnvironmentProfile
            {
                Id = "local-dev",
                DisplayName = "Local honua-server",
                ServerBaseUri = seedUri,
                EnvironmentKind = "development",
                Account = new Honua.Console.Shell.Models.ConsoleAccountBinding
                {
                    AuthMode = Honua.Console.Shell.Models.ConsoleAccountAuthMode.AccountRbac,
                    AccountId = "console-user",
                    DisplayName = "Console User",
                },
            };
            await profileStore.UpsertProfileAsync(devProfile);
            await profileStore.ActivateProfileAsync(devProfile.Id);
        }
    }
}

// Content-Security-Policy: this origin holds the Blazor session and the admin-keyed map BFF proxy,
// so it must constrain where executable code, styles, images, and network connections may come from
// — a tampered or injected script here would run with full session/proxy access. Scripts and styles
// are limited to this origin plus the two pinned CDNs the preview interops lazy-load (MapLibre on
// unpkg; Cesium/Vega on jsdelivr), whose exact assets are additionally pinned with Subresource
// Integrity. object-src/base-uri/frame-ancestors/form-action are locked down to block plugin,
// base-tag, clickjacking, and form-hijack vectors. 'unsafe-eval' is required by the Vega chart
// runtime (and Cesium WASM); 'unsafe-inline' (style/script) by the MapLibre/Cesium/Vega inline
// styles and the Blazor bootstrap. Set on every response (static assets and rendered pages).
var contentSecurityPolicy = string.Join("; ", new[]
{
    "default-src 'self'",
    "base-uri 'self'",
    "object-src 'none'",
    "frame-ancestors 'self'",
    "form-action 'self'",
    "script-src 'self' 'unsafe-inline' 'unsafe-eval' https://unpkg.com https://cdn.jsdelivr.net",
    "style-src 'self' 'unsafe-inline' https://unpkg.com https://cdn.jsdelivr.net",
    "img-src 'self' data: blob: https://cdn.jsdelivr.net https://tile.openstreetmap.org",
    "font-src 'self' data: https://cdn.jsdelivr.net",
    "worker-src 'self' blob:",
    "child-src 'self' blob:",
    "connect-src 'self' https://unpkg.com https://cdn.jsdelivr.net https://tile.openstreetmap.org",
});
app.Use(async (context, next) =>
{
    context.Response.Headers["Content-Security-Policy"] = contentSecurityPolicy;
    context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    await next();
});

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();

// Authentication + the trusted edge-forwarded-identity middleware + authorization. Must run before
// the antiforgery middleware and the component/map-proxy endpoints so the fail-closed fallback policy
// gates every route (honua-console#233).
app.UseConsoleAuthentication();

app.UseAntiforgery();

// Anonymous sign-in / sign-out endpoints (the cookie LoginPath target).
app.MapConsoleAuthEndpoints();

// Static assets and the build-metadata probe are public (no operator identity required).
app.MapStaticAssets().AllowAnonymous();
app.MapGet("/version.json", (HttpContext context) =>
{
    context.Response.Headers["Cache-Control"] = "no-store";
    return Results.Json(ConsoleBuildMetadata.Create());
}).AllowAnonymous();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode()
    .AddAdditionalAssemblies(typeof(Honua.Console.Shell.ConsoleRoutes).Assembly);

// Map-preview proxy: the server's MapLibre style + vector-tile endpoints require the admin key and must not be
// exposed to the browser. These same-origin endpoints stream them from honua-server with the key injected
// server-side, and rewrite the style's tile URLs to flow back through this proxy. The browser (MapLibre GL)
// only ever talks to the console origin and never sees the admin key.
//
// SECURITY (honua-console#210/#233): because these endpoints act with honua-server privileges, every one
// of them requires an authenticated operator. The host now runs real ASP.NET authentication with a
// fail-closed RequireAuthenticatedUser fallback policy, so these endpoints check HttpContext.User and
// forward the operator's identity to honua-server: when the active operator session carries a real bearer
// (e.g. an edge-forwarded access token) it is sent as Authorization: Bearer so honua-server scopes the
// read to that operator; only when no operator bearer exists does the proxy fall back to the configured
// shared admin key (documented in docs/console-authentication.md). Full per-identity scoping for every
// deployment still depends on honua-server issuing a console-consumable operator bearer (Option C).
var mapProxyServerUrl =
    (app.Configuration["Honua:Server:BaseUrl"] ?? app.Configuration["HONUA_SERVER_BASE_URL"])?.TrimEnd('/');
var mapProxyAdminKey = app.Configuration["Honua:Server:AdminApiKey"] ?? app.Configuration["HONUA_ADMIN_API_KEY"];

if (!string.IsNullOrWhiteSpace(mapProxyServerUrl))
{
    app.MapGet("/map-proxy/styles/{layerId:int}.json", async (
        int layerId,
        HttpContext httpContext,
        IHttpClientFactory httpClientFactory,
        Honua.Console.Shell.Services.IConsoleEnvironmentProfileStore profileStore,
        Honua.Console.Shell.Services.IConsoleAccountSessionStore sessionStore,
        CancellationToken cancellationToken) =>
    {
        // The endpoint acts with honua-server privileges, so it requires an authenticated operator
        // (honua-console#233/#210). The host's fail-closed fallback policy already gates this, but the
        // explicit check keeps the 401 contract for the browser fetch even if route metadata changes.
        if (httpContext.User?.Identity?.IsAuthenticated != true)
        {
            return Results.StatusCode(StatusCodes.Status401Unauthorized);
        }

        // Forward the operator's identity to honua-server when a real bearer exists; otherwise fall
        // back to the configured shared admin key (documented).
        var operatorBearer = await Honua.Console.Web.MapProxySupport.ResolveOperatorBearerAsync(
            profileStore, sessionStore, cancellationToken);

        var client = httpClientFactory.CreateClient("honua-map-proxy");
        using var request = new HttpRequestMessage(HttpMethod.Get, $"{mapProxyServerUrl}/api/styles/{layerId}.json");
        Honua.Console.Web.MapProxySupport.ApplyUpstreamCredential(request, operatorBearer, mapProxyAdminKey);

        using var response = await client.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return Results.StatusCode((int)response.StatusCode);
        }

        var styleJson = await response.Content.ReadAsStringAsync(cancellationToken);
        // Route tile URLs back through this proxy so the browser fetches tiles with the admin key injected
        // here, not in the page. The URL MUST be ABSOLUTE: MapLibre loads vector tiles in a web worker that
        // calls new Request(url) with no document base, so a root-relative "/map-proxy/tiles/..." throws
        // "Failed to parse URL" and no feature tile ever loads. Build the absolute origin from the incoming
        // request so it works behind any host/scheme. Parse the style sources rather than string-replacing a
        // single prefix, so absolute server-emitted tile URLs are rewritten too (honua-console#213).
        var absoluteTileBase = $"{httpContext.Request.Scheme}://{httpContext.Request.Host}/map-proxy/tiles/";
        styleJson = Honua.Console.Web.MapProxySupport.RewriteTileUrls(styleJson, absoluteTileBase);
        return Results.Content(styleJson, "application/json");
    });

    app.MapGet("/map-proxy/tiles/{layerId:int}/{z:int}/{x:int}/{y:int}.mvt", async (
        int layerId,
        int z,
        int x,
        int y,
        HttpContext httpContext,
        IHttpClientFactory httpClientFactory,
        Honua.Console.Shell.Services.IConsoleEnvironmentProfileStore profileStore,
        Honua.Console.Shell.Services.IConsoleAccountSessionStore sessionStore,
        CancellationToken cancellationToken) =>
    {
        // The endpoint acts with honua-server privileges, so it requires an authenticated operator
        // (honua-console#233/#210). The host's fail-closed fallback policy already gates this, but the
        // explicit check keeps the 401 contract for the browser fetch even if route metadata changes.
        if (httpContext.User?.Identity?.IsAuthenticated != true)
        {
            return Results.StatusCode(StatusCodes.Status401Unauthorized);
        }

        // Forward the operator's identity to honua-server when a real bearer exists; otherwise fall
        // back to the configured shared admin key (documented).
        var operatorBearer = await Honua.Console.Web.MapProxySupport.ResolveOperatorBearerAsync(
            profileStore, sessionStore, cancellationToken);

        var client = httpClientFactory.CreateClient("honua-map-proxy");
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"{mapProxyServerUrl}/tiles/{layerId}/{z}/{x}/{y}.mvt");
        Honua.Console.Web.MapProxySupport.ApplyUpstreamCredential(request, operatorBearer, mapProxyAdminKey);

        // Forward the browser's validators so the upstream can answer 304 and the browser revalidates cheaply.
        Honua.Console.Web.MapProxySupport.ForwardConditionalHeaders(httpContext.Request, request);

        // Tiles are the hottest path. Read only the headers first and stream the body straight to the
        // browser instead of buffering each tile into a byte[], and forward the upstream caching/validator
        // headers so the browser can cache tiles rather than re-fetching every tile through this admin-keyed
        // proxy on every view (honua-console#236). The response is disposed once the request completes.
        var response = await client.SendAsync(
            request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        httpContext.Response.RegisterForDispose(response);

        if (response.StatusCode == System.Net.HttpStatusCode.NoContent)
        {
            return Results.StatusCode(StatusCodes.Status204NoContent);
        }

        if (response.StatusCode == System.Net.HttpStatusCode.NotModified)
        {
            Honua.Console.Web.MapProxySupport.ApplyTileCacheHeaders(response, httpContext.Response);
            return Results.StatusCode(StatusCodes.Status304NotModified);
        }

        if (!response.IsSuccessStatusCode)
        {
            return Results.StatusCode((int)response.StatusCode);
        }

        Honua.Console.Web.MapProxySupport.ApplyTileCacheHeaders(response, httpContext.Response);
        var contentType = response.Content.Headers.ContentType?.ToString() ?? "application/vnd.mapbox-vector-tile";
        var tileStream = await response.Content.ReadAsStreamAsync(cancellationToken);
        return Results.Stream(tileStream, contentType);
    });

    // Real tabular feature rows for the Studio query-result table and the live chart (graphs) preview.
    // Proxies the server's Esri FeatureServer query (the only feature-row contract; it needs both the
    // serviceId and the layerId, both captured into the editor binding at generation time) with the admin
    // key injected here, never in the page. Returns the server's Esri feature JSON verbatim so the browser
    // chart/table interop maps attributes → rows. No binding → the caller never calls this (no mock rows).
    app.MapGet("/map-proxy/features/{serviceId}/{layerId:int}", async (
        string serviceId,
        int layerId,
        int? limit,
        HttpContext httpContext,
        IHttpClientFactory httpClientFactory,
        Honua.Console.Shell.Services.IConsoleEnvironmentProfileStore profileStore,
        Honua.Console.Shell.Services.IConsoleAccountSessionStore sessionStore,
        CancellationToken cancellationToken) =>
    {
        // The endpoint acts with honua-server privileges, so it requires an authenticated operator
        // (honua-console#233/#210). The host's fail-closed fallback policy already gates this, but the
        // explicit check keeps the 401 contract for the browser fetch even if route metadata changes.
        if (httpContext.User?.Identity?.IsAuthenticated != true)
        {
            return Results.StatusCode(StatusCodes.Status401Unauthorized);
        }

        // Forward the operator's identity to honua-server when a real bearer exists; otherwise fall
        // back to the configured shared admin key (documented).
        var operatorBearer = await Honua.Console.Web.MapProxySupport.ResolveOperatorBearerAsync(
            profileStore, sessionStore, cancellationToken);

        var count = limit is > 0 and <= 2000 ? limit.Value : 200;
        var client = httpClientFactory.CreateClient("honua-map-proxy");
        var url = $"{mapProxyServerUrl}/rest/services/{Uri.EscapeDataString(serviceId)}/FeatureServer/{layerId}/query"
            + $"?where=1%3D1&outFields=*&returnGeometry=false&resultRecordCount={count}&f=json";
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        Honua.Console.Web.MapProxySupport.ApplyUpstreamCredential(request, operatorBearer, mapProxyAdminKey);

        using var response = await client.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return Results.StatusCode((int)response.StatusCode);
        }

        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        return Results.Content(json, "application/json");
    });
}

app.Run();
