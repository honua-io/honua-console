using Honua.Console.Shell.DependencyInjection;
using Honua.Console.Web;
using Honua.Console.Web.Components;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();
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

app.Run();
