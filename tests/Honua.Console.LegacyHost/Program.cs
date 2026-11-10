using Honua.Console.Web;
using Honua.Console.Web.Auth;
using Honua.Console.Web.Components;

var builder = WebApplication.CreateBuilder(args);

builder.AddLegacyConsoleServices();

var app = builder.Build();

app.UseForwardedHeaders();

// Keep scripts and workers on the fixture origin; map rendering requires inline styles and WASM.
var contentSecurityPolicy = string.Join("; ", new[]
{
    "default-src 'self'",
    "base-uri 'self'",
    "object-src 'none'",
    "frame-ancestors 'self'",
    "form-action 'self'",
    "script-src 'self' 'unsafe-inline' 'unsafe-eval' blob:",
    "style-src 'self' 'unsafe-inline'",
    "img-src 'self' data: blob: https://tile.openstreetmap.org",
    "font-src 'self' data:",
    "worker-src 'self' blob:",
    "child-src 'self' blob:",
    "connect-src 'self' https://tile.openstreetmap.org",
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
app.Use(async (context, next) =>
{
    if ((context.Request.Path.StartsWithSegments("/map-proxy") || context.Request.Path.StartsWithSegments("/scene-proxy")
        || context.Request.Path.StartsWithSegments("/api/studio/viewer"))
        && context.Features.Get<Microsoft.AspNetCore.Diagnostics.IStatusCodePagesFeature>() is { } statusPages)
    {
        // Fetch clients must receive the original 401/403, never a rendered not-found page.
        statusPages.Enabled = false;
    }

    await next();
});
app.UseHttpsRedirection();

app.UseConsoleAuthentication();

app.UseAntiforgery();

// Anonymous sign-in / sign-out endpoints (the cookie LoginPath target).
app.MapConsoleAuthEndpoints();
app.MapConsoleServerSessionBff();
app.MapAgentWorkspaceBff();
app.MapAgentSessionBff();
app.MapLocalProcessBff();
app.MapStudioReadBff();
app.MapStudioAudienceBff();

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

var mapProxyLogger = app.Services.GetRequiredService<ILoggerFactory>()
    .CreateLogger("Honua.Console.Web.MapProxy");

app.MapGet("/map-proxy/{environmentId}/styles/{layerId:int}.json", async (
    string environmentId,
    int layerId,
    HttpContext httpContext,
    IHttpClientFactory httpClientFactory,
    Honua.Console.Web.Auth.IConsoleOperatorScope operatorScope,
    Honua.Console.Shell.Services.IConsoleEnvironmentProfileStore profileStore,
    CancellationToken cancellationToken) =>
{
    var operatorIdentity = await operatorScope.ResolveAsync(cancellationToken);
    if (operatorIdentity is null)
    {
        return Results.StatusCode(StatusCodes.Status401Unauthorized);
    }

    var (activeProfile, pinFailure) = await Honua.Console.Web.MapProxySupport.ResolvePinnedProfileAsync(
        profileStore, environmentId, cancellationToken);
    if (activeProfile is null)
    {
        httpContext.Response.Headers.CacheControl = "no-store";
        return pinFailure!;
    }

    httpContext.Response.Headers.CacheControl = "no-store";

    var client = httpClientFactory.CreateClient("honua-map-proxy");
    using var request = new HttpRequestMessage(HttpMethod.Get,
        Honua.Console.Shell.Services.ConsoleServerHttp.BuildUri(
            activeProfile.ServerBaseUri, $"api/styles/{layerId}.json"));

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
            return Honua.Console.Web.MapProxySupport.UpstreamFailure(httpContext, response.StatusCode);
        }

        var styleJson = await response.Content.ReadAsStringAsync(cancellationToken);
        var absoluteTileBase = $"{httpContext.Request.Scheme}://{httpContext.Request.Host}"
            + Honua.Console.Shell.Services.ConsoleProxyRoutes.MapTileBase(activeProfile.Id);
        styleJson = Honua.Console.Web.MapProxySupport.RewriteTileUrls(styleJson, absoluteTileBase);
        return Results.Content(styleJson, "application/json");
    }
});

app.MapGet("/map-proxy/{environmentId}/tiles/{layerId:int}/{z:int}/{x:int}/{y:int}.mvt", async (
    string environmentId,
    int layerId,
    int z,
    int x,
    int y,
    HttpContext httpContext,
    IHttpClientFactory httpClientFactory,
    Honua.Console.Web.Auth.IConsoleOperatorScope operatorScope,
    Honua.Console.Shell.Services.IConsoleEnvironmentProfileStore profileStore,
    CancellationToken cancellationToken) =>
{
    var operatorIdentity = await operatorScope.ResolveAsync(cancellationToken);
    if (operatorIdentity is null)
    {
        return Results.StatusCode(StatusCodes.Status401Unauthorized);
    }

    var (activeProfile, pinFailure) = await Honua.Console.Web.MapProxySupport.ResolvePinnedProfileAsync(
        profileStore, environmentId, cancellationToken);
    if (activeProfile is null)
    {
        httpContext.Response.Headers.CacheControl = "no-store";
        return pinFailure!;
    }

    httpContext.Response.Headers.CacheControl = "no-store";

    var client = httpClientFactory.CreateClient("honua-map-proxy");
    using var request = new HttpRequestMessage(
        HttpMethod.Get,
        Honua.Console.Shell.Services.ConsoleServerHttp.BuildUri(
            activeProfile.ServerBaseUri, $"tiles/{layerId}/{z}/{x}/{y}.mvt"));

    Honua.Console.Web.MapProxySupport.ForwardConditionalHeaders(httpContext.Request, request);

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
        return Honua.Console.Web.MapProxySupport.UpstreamFailure(httpContext, response.StatusCode);
    }

    Honua.Console.Web.MapProxySupport.ApplyTileCacheHeaders(response, httpContext.Response);
    var contentType = response.Content.Headers.ContentType?.ToString() ?? "application/vnd.mapbox-vector-tile";
    var tileStream = await response.Content.ReadAsStreamAsync(cancellationToken);
    return Results.Stream(tileStream, contentType);
});

app.MapGet("/map-proxy/{environmentId}/features/{serviceId}/{layerId:int}", async (
    string environmentId,
    string serviceId,
    int layerId,
    int? limit,
    HttpContext httpContext,
    IHttpClientFactory httpClientFactory,
    Honua.Console.Web.Auth.IConsoleOperatorScope operatorScope,
    Honua.Console.Shell.Services.IConsoleEnvironmentProfileStore profileStore,
    CancellationToken cancellationToken) =>
{
    var operatorIdentity = await operatorScope.ResolveAsync(cancellationToken);
    if (operatorIdentity is null)
    {
        return Results.StatusCode(StatusCodes.Status401Unauthorized);
    }

    var (activeProfile, pinFailure) = await Honua.Console.Web.MapProxySupport.ResolvePinnedProfileAsync(
        profileStore, environmentId, cancellationToken);
    if (activeProfile is null)
    {
        httpContext.Response.Headers.CacheControl = "no-store";
        return pinFailure!;
    }

    httpContext.Response.Headers.CacheControl = "no-store";

    var count = limit is > 0 and <= 2000 ? limit.Value : 200;
    var client = httpClientFactory.CreateClient("honua-map-proxy");
    var url = Honua.Console.Shell.Services.ConsoleServerHttp.BuildUri(
        activeProfile.ServerBaseUri,
        $"rest/services/{Uri.EscapeDataString(serviceId)}/FeatureServer/{layerId}/query"
            + $"?where=1%3D1&outFields=*&returnGeometry=false&resultRecordCount={count}&f=json");
    using var request = new HttpRequestMessage(HttpMethod.Get, url);

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
            return Honua.Console.Web.MapProxySupport.UpstreamFailure(httpContext, response.StatusCode);
        }

        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        return Results.Content(json, "application/json");
    }
});

app.MapGet("/scene-proxy/{environmentId}/scenes/{sceneId}/{**assetPath}", async (
    string environmentId,
    string sceneId,
    string assetPath,
    HttpContext httpContext,
    IHttpClientFactory httpClientFactory,
    Honua.Console.Web.Auth.IConsoleOperatorScope operatorScope,
    Honua.Console.Shell.Services.IConsoleEnvironmentProfileStore profileStore,
    CancellationToken cancellationToken) =>
{
    var operatorIdentity = await operatorScope.ResolveAsync(cancellationToken);
    if (operatorIdentity is null)
    {
        return Results.StatusCode(StatusCodes.Status401Unauthorized);
    }

    var safeSceneId = Honua.Console.Web.MapProxySupport.NormalizeSceneAssetPath(sceneId);
    var safeAssetPath = Honua.Console.Web.MapProxySupport.NormalizeSceneAssetPath(assetPath);
    if (safeSceneId is null || safeSceneId.Contains('/') || safeAssetPath is null)
    {
        return Results.BadRequest();
    }

    var (activeProfile, pinFailure) = await Honua.Console.Web.MapProxySupport.ResolvePinnedProfileAsync(
        profileStore, environmentId, cancellationToken);
    if (activeProfile is null)
    {
        httpContext.Response.Headers.CacheControl = "no-store";
        return pinFailure!;
    }

    var client = httpClientFactory.CreateClient("honua-map-proxy");
    using var request = new HttpRequestMessage(
        HttpMethod.Get,
        Honua.Console.Web.MapProxySupport.BuildSceneAssetUri(
            activeProfile.ServerBaseUri, safeSceneId, safeAssetPath));
    Honua.Console.Web.MapProxySupport.ForwardConditionalHeaders(httpContext.Request, request);

    HttpResponseMessage response;
    try
    {
        response = await client.SendAsync(
            request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
    }
    catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
    {
        ConsoleMapProxyTelemetry.RecordTransportFault("scenes");
        mapProxyLogger.LogWarning(ex,
            "Scene proxy upstream request failed for scene {SceneId} asset {AssetPath}.",
            Honua.Console.Web.MapProxySupport.LogSafe(sceneId),
            Honua.Console.Web.MapProxySupport.LogSafe(assetPath));
        return Results.StatusCode(StatusCodes.Status502BadGateway);
    }

    httpContext.Response.RegisterForDispose(response);
    ConsoleMapProxyTelemetry.RecordResponse("scenes", (int)response.StatusCode);
    if (response.StatusCode == System.Net.HttpStatusCode.NotModified)
    {
        Honua.Console.Web.MapProxySupport.ApplyTileCacheHeaders(response, httpContext.Response);
        return Results.StatusCode(StatusCodes.Status304NotModified);
    }
    if (!response.IsSuccessStatusCode)
    {
        return Honua.Console.Web.MapProxySupport.UpstreamFailure(httpContext, response.StatusCode);
    }

    Honua.Console.Web.MapProxySupport.ApplyTileCacheHeaders(response, httpContext.Response);
    var contentType = response.Content.Headers.ContentType?.ToString() ?? "application/octet-stream";
    var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
    return Results.Stream(stream, contentType);
});

app.Run();
