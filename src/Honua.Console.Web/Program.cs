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
var mapProxyServerUrl =
    (app.Configuration["Honua:Server:BaseUrl"] ?? app.Configuration["HONUA_SERVER_BASE_URL"])?.TrimEnd('/');
var mapProxyAdminKey = app.Configuration["Honua:Server:AdminApiKey"] ?? app.Configuration["HONUA_ADMIN_API_KEY"];

if (!string.IsNullOrWhiteSpace(mapProxyServerUrl))
{
    app.MapGet("/map-proxy/styles/{layerId:int}.json", async (
        int layerId,
        IHttpClientFactory httpClientFactory,
        CancellationToken cancellationToken) =>
    {
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
        // The server returns tile URLs as /tiles/{id}/... — route them back through this proxy so the browser
        // fetches tiles with the admin key injected here, not in the page.
        styleJson = styleJson.Replace("\"/tiles/", "\"/map-proxy/tiles/", StringComparison.Ordinal);
        return Results.Content(styleJson, "application/json");
    });

    app.MapGet("/map-proxy/tiles/{layerId:int}/{z:int}/{x:int}/{y:int}.mvt", async (
        int layerId,
        int z,
        int x,
        int y,
        IHttpClientFactory httpClientFactory,
        CancellationToken cancellationToken) =>
    {
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
}

app.Run();
