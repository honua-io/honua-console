using Honua.Console.Shell.DependencyInjection;
using Honua.Console.Web;
using Honua.Console.Web.Components;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();
builder.Services.AddHttpClient("honua-map-proxy");
builder.Services.AddHonuaConsoleShell(
    builder.Configuration["Honua:Server:BaseUrl"] ?? builder.Configuration["HONUA_SERVER_BASE_URL"],
    builder.Configuration["Honua:Server:AdminApiKey"] ?? builder.Configuration["HONUA_ADMIN_API_KEY"],
    builder.Configuration["Honua:Server:PublicationIds"] ?? builder.Configuration["HONUA_SERVER_PUBLICATION_IDS"],
    builder.Configuration["Honua:Server:TemporalSources"] ?? builder.Configuration["HONUA_SERVER_TEMPORAL_SOURCES"]);

var app = builder.Build();

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
