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
// OpenTelemetry traces + metrics for the console host (honua-console#279 PA-234). Default-safe: only
// exports when OTEL_EXPORTER_OTLP_ENDPOINT is configured.
builder.AddConsoleObservability();

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
    builder.Configuration["Honua:Support:KbPath"] ?? builder.Configuration["HONUA_SUPPORT_KB_PATH"],
    // Deferred exotic-depth capabilities advertised for this release (first-release cut-line). Empty
    // by default: temporal / disconnected-sync / realtime-alerting / cross-environment-promotion /
    // siem-investigations render the first-class "unsupported" state until opted in here.
    builder.Configuration["Honua:Console:Capabilities"] ?? builder.Configuration["HONUA_CONSOLE_CAPABILITIES"],
    // Registry-driven Studio-AI intent resolution (honua-console#266): OFF by default. When ON (and a
    // server is bound) the Studio generate/validate/preview/publish lifecycle resolves against the live
    // capability-manifest registry and deferred/unavailable capabilities are hidden from Studio AI.
    ParseFlag(
        builder.Configuration["Studio:RegistryIntentResolution"]
        ?? builder.Configuration["HONUA_STUDIO_REGISTRY_INTENT_RESOLUTION"]),
    // Human-facing mode is the default and requires an attributable operator bearer
    // for approval/recovery mutations. API-key fallback is available only through the
    // exact HeadlessService opt-in, a ServiceApiKey profile, and no interactive session.
    builder.Configuration["Honua:Server:CredentialMode"]
        ?? builder.Configuration["HONUA_SERVER_CREDENTIAL_MODE"]);

static bool ParseFlag(string? value) =>
    bool.TryParse(value, out var parsed) && parsed;

// Operator authentication (honua-console#233): fail-closed cookie/edge auth + RequireAuthenticatedUser
// fallback policy so no Console route is reachable anonymously. See ConsoleAuthentication for the model.
builder.AddConsoleAuthentication();
builder.AddConsoleServerSessionBff();

var app = builder.Build();

// Development testbed convenience: the browser host cannot create environment profiles (profile
// creation runs on the native MAUI host), so a "Local honua-server" profile is seeded per-operator
// on first use from HONUA_SERVER_BASE_URL. That seed now lives in AddConsoleAuthentication's
// operator-partitioned profile store (per-operator so it survives the multi-operator partitioning,
// honua-console#233 S1 #1) rather than a single shared startup seed. Never runs outside Development.

// Content-Security-Policy: this origin holds the Blazor session and the admin-keyed map BFF proxy,
// so it must constrain where executable code, styles, images, and network connections may come from
// — a tampered or injected script here would run with full session/proxy access. MapLibre is served
// from this origin as a committed, version-pinned asset (honua-console#333), so unpkg.com is gone
// from the policy entirely. jsdelivr remains only for the two interops still loading their runtime
// from a CDN — Cesium in scene-viewer.js and Vega in chart-preview.js, tracked in honua-console#334
// to be vendored the same way — whose exact assets are meanwhile pinned with Subresource
// Integrity. object-src/base-uri/frame-ancestors/form-action are locked down to block plugin,
// base-tag, clickjacking, and form-hijack vectors. 'unsafe-eval' is required by the Vega chart
// runtime (and Cesium WASM); 'unsafe-inline' (style/script) by the MapLibre/Cesium/Vega inline
// styles and the Blazor bootstrap. 'blob:' in script-src lets same-origin code execute module bytes
// pulled from this origin under a guaranteed MIME (the map-preview interop is loaded this way, and
// MapLibre/Cesium also spin up blob-backed workers); given 'unsafe-inline'/'unsafe-eval' are already
// permitted, allowing same-origin blob scripts adds no meaningful capability and introduces no new
// remote code origin. Set on every response (static assets and rendered pages).
var contentSecurityPolicy = string.Join("; ", new[]
{
    "default-src 'self'",
    "base-uri 'self'",
    "object-src 'none'",
    "frame-ancestors 'self'",
    "form-action 'self'",
    "script-src 'self' 'unsafe-inline' 'unsafe-eval' blob: https://cdn.jsdelivr.net",
    "style-src 'self' 'unsafe-inline' https://cdn.jsdelivr.net",
    "img-src 'self' data: blob: https://cdn.jsdelivr.net https://tile.openstreetmap.org",
    "font-src 'self' data: https://cdn.jsdelivr.net",
    "worker-src 'self' blob:",
    "child-src 'self' blob:",
    "connect-src 'self' https://cdn.jsdelivr.net https://tile.openstreetmap.org",
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
app.MapConsoleServerSessionBff();

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

// The map proxy is the hottest console path but had no error logging, metrics, or trace on upstream
// failure (honua-console#279 PA-235). A single category logger is captured by the endpoint closures so
// an upstream honua-server fault is recorded here (with layerId / z,x,y / status) instead of vanishing
// behind the browser's opaque status code, and ConsoleMapProxyTelemetry counts requests/failures.
var mapProxyLogger = app.Services.GetRequiredService<ILoggerFactory>()
    .CreateLogger("Honua.Console.Web.MapProxy");

if (!string.IsNullOrWhiteSpace(mapProxyServerUrl))
{
    app.MapGet("/map-proxy/styles/{layerId:int}.json", async (
        int layerId,
        HttpContext httpContext,
        IHttpClientFactory httpClientFactory,
        Honua.Console.Web.Auth.IConsoleOperatorScope operatorScope,
        Honua.Console.Shell.Services.IConsoleEnvironmentProfileStore profileStore,
        Honua.Console.Shell.Services.IConsoleAccountSessionStore sessionStore,
        CancellationToken cancellationToken) =>
    {
        // The endpoint acts with honua-server privileges, so it requires an authenticated operator
        // (honua-console#233/#210/#254). Fail-closed BY CONSTRUCTION: the operator is resolved from the
        // request's scoped operator accessor, which reads HttpContext.User directly and has no shared
        // ambient/anonymous fallback. A scope with no resolved operator yields null and cannot proceed
        // past this point (the host fail-closed fallback policy is the defense-in-depth layer); the
        // explicit deny keeps the 401 contract for the browser fetch even if route metadata changes.
        var operatorIdentity = await operatorScope.ResolveAsync(cancellationToken);
        if (operatorIdentity is null)
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

        HttpResponseMessage response;
        try
        {
            response = await client.SendAsync(request, cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            ConsoleMapProxyTelemetry.RecordTransportFault("styles");
            mapProxyLogger.LogWarning(ex,
                "Map-proxy styles upstream request failed for layer {LayerId}: honua-server was unreachable or timed out.",
                layerId);
            return Results.StatusCode(StatusCodes.Status502BadGateway);
        }

        using (response)
        {
            ConsoleMapProxyTelemetry.RecordResponse("styles", (int)response.StatusCode);
            if (!response.IsSuccessStatusCode)
            {
                mapProxyLogger.LogWarning(
                    "Map-proxy styles upstream returned {StatusCode} for layer {LayerId}.",
                    (int)response.StatusCode, layerId);
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
        }
    });

    app.MapGet("/map-proxy/tiles/{layerId:int}/{z:int}/{x:int}/{y:int}.mvt", async (
        int layerId,
        int z,
        int x,
        int y,
        HttpContext httpContext,
        IHttpClientFactory httpClientFactory,
        Honua.Console.Web.Auth.IConsoleOperatorScope operatorScope,
        Honua.Console.Shell.Services.IConsoleEnvironmentProfileStore profileStore,
        Honua.Console.Shell.Services.IConsoleAccountSessionStore sessionStore,
        CancellationToken cancellationToken) =>
    {
        // The endpoint acts with honua-server privileges, so it requires an authenticated operator
        // (honua-console#233/#210/#254). Fail-closed BY CONSTRUCTION: the operator is resolved from the
        // request's scoped operator accessor, which reads HttpContext.User directly and has no shared
        // ambient/anonymous fallback. A scope with no resolved operator yields null and cannot proceed
        // past this point (the host fail-closed fallback policy is the defense-in-depth layer); the
        // explicit deny keeps the 401 contract for the browser fetch even if route metadata changes.
        var operatorIdentity = await operatorScope.ResolveAsync(cancellationToken);
        if (operatorIdentity is null)
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
        HttpResponseMessage response;
        try
        {
            response = await client.SendAsync(
                request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            ConsoleMapProxyTelemetry.RecordTransportFault("tiles");
            mapProxyLogger.LogWarning(ex,
                "Map-proxy tile upstream request failed for layer {LayerId} z/x/y {Z}/{X}/{Y}: honua-server was unreachable or timed out.",
                layerId, z, x, y);
            return Results.StatusCode(StatusCodes.Status502BadGateway);
        }

        httpContext.Response.RegisterForDispose(response);
        ConsoleMapProxyTelemetry.RecordResponse("tiles", (int)response.StatusCode);

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
            mapProxyLogger.LogWarning(
                "Map-proxy tile upstream returned {StatusCode} for layer {LayerId} z/x/y {Z}/{X}/{Y}.",
                (int)response.StatusCode, layerId, z, x, y);
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
        Honua.Console.Web.Auth.IConsoleOperatorScope operatorScope,
        Honua.Console.Shell.Services.IConsoleEnvironmentProfileStore profileStore,
        Honua.Console.Shell.Services.IConsoleAccountSessionStore sessionStore,
        CancellationToken cancellationToken) =>
    {
        // The endpoint acts with honua-server privileges, so it requires an authenticated operator
        // (honua-console#233/#210/#254). Fail-closed BY CONSTRUCTION: the operator is resolved from the
        // request's scoped operator accessor, which reads HttpContext.User directly and has no shared
        // ambient/anonymous fallback. A scope with no resolved operator yields null and cannot proceed
        // past this point (the host fail-closed fallback policy is the defense-in-depth layer); the
        // explicit deny keeps the 401 contract for the browser fetch even if route metadata changes.
        var operatorIdentity = await operatorScope.ResolveAsync(cancellationToken);
        if (operatorIdentity is null)
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

        HttpResponseMessage response;
        try
        {
            response = await client.SendAsync(request, cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            ConsoleMapProxyTelemetry.RecordTransportFault("features");
            mapProxyLogger.LogWarning(ex,
                "Map-proxy features upstream request failed for service {ServiceId} layer {LayerId}: honua-server was unreachable or timed out.",
                Honua.Console.Web.MapProxySupport.LogSafe(serviceId), layerId);
            return Results.StatusCode(StatusCodes.Status502BadGateway);
        }

        using (response)
        {
            ConsoleMapProxyTelemetry.RecordResponse("features", (int)response.StatusCode);
            if (!response.IsSuccessStatusCode)
            {
                mapProxyLogger.LogWarning(
                    "Map-proxy features upstream returned {StatusCode} for service {ServiceId} layer {LayerId}.",
                    (int)response.StatusCode, Honua.Console.Web.MapProxySupport.LogSafe(serviceId), layerId);
                return Results.StatusCode((int)response.StatusCode);
            }

            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            return Results.Content(json, "application/json");
        }
    });
}

app.Run();
