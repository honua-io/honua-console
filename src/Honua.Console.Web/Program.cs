using Honua.Console.Shell.DependencyInjection;
using Honua.Console.Web;
using Honua.Console.Web.Components;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents(options => options.DetailedErrors = true);

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

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();

app.UseAntiforgery();

app.MapStaticAssets();
app.MapGet("/version.json", (HttpContext context) =>
{
    context.Response.Headers["Cache-Control"] = "no-store";
    return Results.Json(ConsoleBuildMetadata.Create());
});
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode()
    .AddAdditionalAssemblies(typeof(Honua.Console.Shell.ConsoleRoutes).Assembly);

// Map-preview proxy: the server's MapLibre style + vector-tile endpoints require the admin key and must not be
// exposed to the browser. These same-origin endpoints stream them from honua-server with the key injected
// server-side, and rewrite the style's tile URLs to flow back through this proxy. The browser (MapLibre GL)
// only ever talks to the console origin and never sees the admin key.
//
// SECURITY (honua-console#210): because these endpoints act with the server's admin privileges, every one
// of them MUST first verify an authenticated console session. The console host has no ASP.NET authentication
// middleware; "authenticated" here means an active environment profile with a non-Anonymous account and a
// valid session access token — the same definition used by ConsoleCatalogReadContextResolver. Unauthenticated
// callers receive 401. Known gap: this gates *whether* a session exists, but does not yet scope the proxied
// request to that caller's identity (the proxy still uses the shared admin key); per-identity scoping is
// tracked separately and depends on honua-server exposing a session-scoped style/tile/feature contract.
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
        if (!await Honua.Console.Web.MapProxySupport.HasAuthenticatedConsoleSessionAsync(
                profileStore, sessionStore, cancellationToken))
        {
            return Results.StatusCode(StatusCodes.Status401Unauthorized);
        }

        var client = httpClientFactory.CreateClient("honua-map-proxy");
        using var request = new HttpRequestMessage(HttpMethod.Get, $"{mapProxyServerUrl}/api/styles/{layerId}.json");
        if (!string.IsNullOrWhiteSpace(mapProxyAdminKey))
        {
            request.Headers.TryAddWithoutValidation("X-API-Key", mapProxyAdminKey);
        }

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
        IHttpClientFactory httpClientFactory,
        Honua.Console.Shell.Services.IConsoleEnvironmentProfileStore profileStore,
        Honua.Console.Shell.Services.IConsoleAccountSessionStore sessionStore,
        CancellationToken cancellationToken) =>
    {
        if (!await Honua.Console.Web.MapProxySupport.HasAuthenticatedConsoleSessionAsync(
                profileStore, sessionStore, cancellationToken))
        {
            return Results.StatusCode(StatusCodes.Status401Unauthorized);
        }

        var client = httpClientFactory.CreateClient("honua-map-proxy");
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"{mapProxyServerUrl}/tiles/{layerId}/{z}/{x}/{y}.mvt");
        if (!string.IsNullOrWhiteSpace(mapProxyAdminKey))
        {
            request.Headers.TryAddWithoutValidation("X-API-Key", mapProxyAdminKey);
        }

        using var response = await client.SendAsync(request, cancellationToken);
        if (response.StatusCode == System.Net.HttpStatusCode.NoContent)
        {
            return Results.StatusCode(StatusCodes.Status204NoContent);
        }

        if (!response.IsSuccessStatusCode)
        {
            return Results.StatusCode((int)response.StatusCode);
        }

        var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
        var contentType = response.Content.Headers.ContentType?.ToString() ?? "application/vnd.mapbox-vector-tile";
        return Results.Bytes(bytes, contentType);
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
        IHttpClientFactory httpClientFactory,
        Honua.Console.Shell.Services.IConsoleEnvironmentProfileStore profileStore,
        Honua.Console.Shell.Services.IConsoleAccountSessionStore sessionStore,
        CancellationToken cancellationToken) =>
    {
        if (!await Honua.Console.Web.MapProxySupport.HasAuthenticatedConsoleSessionAsync(
                profileStore, sessionStore, cancellationToken))
        {
            return Results.StatusCode(StatusCodes.Status401Unauthorized);
        }

        var count = limit is > 0 and <= 2000 ? limit.Value : 200;
        var client = httpClientFactory.CreateClient("honua-map-proxy");
        var url = $"{mapProxyServerUrl}/rest/services/{Uri.EscapeDataString(serviceId)}/FeatureServer/{layerId}/query"
            + $"?where=1%3D1&outFields=*&returnGeometry=false&resultRecordCount={count}&f=json";
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        if (!string.IsNullOrWhiteSpace(mapProxyAdminKey))
        {
            request.Headers.TryAddWithoutValidation("X-API-Key", mapProxyAdminKey);
        }

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
